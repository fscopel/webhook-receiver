using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using WebhookReceiver.Models;
using WebhookReceiver.Services;

namespace WebhookReceiver.Hubs;

/// <summary>
/// SignalR hub for real-time webhook updates.
/// Each user has their own isolated inbox - delete/clear operations only affect that user.
/// </summary>
public class WebhookHub : Hub
{
    private readonly WebhookStore _store;
    private readonly ILogger<WebhookHub> _logger;

    public WebhookHub(WebhookStore store, ILogger<WebhookHub> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Get the current user's email from the authenticated context
    /// </summary>
    private string? GetUserEmail()
    {
        var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value 
                 ?? Context.User?.FindFirst("email")?.Value;
        return email?.ToLowerInvariant().Trim();
    }

    public override async Task OnConnectedAsync()
    {
        var userEmail = GetUserEmail();
        
        if (string.IsNullOrEmpty(userEmail))
        {
            _logger.LogWarning("User connected without email claim, connection: {ConnectionId}", Context.ConnectionId);
            await base.OnConnectedAsync();
            return;
        }

        // Register user as active and add to their personal group
        _store.RegisterUser(userEmail);
        await Groups.AddToGroupAsync(Context.ConnectionId, userEmail);
        
        // Sync user's store from master list - ensures they see all current webhooks
        // (including ones that arrived before they logged in)
        var synced = await _store.SyncUserFromMasterAsync(userEmail);
        
        _logger.LogInformation("User connected: {Email}, Connection: {ConnectionId}, Synced {SyncedCount} webhooks from master", 
            userEmail, Context.ConnectionId, synced);

        // Send user's complete webhook list
        var entries = await _store.GetAllAsync(userEmail);
        await Clients.Caller.SendAsync("InitialData", entries);
        
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userEmail = GetUserEmail();
        
        if (!string.IsNullOrEmpty(userEmail))
        {
            // Unregister user connection (but keep their data)
            _store.UnregisterUser(userEmail);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userEmail);
            
            _logger.LogInformation("User disconnected: {Email}, Connection: {ConnectionId}", userEmail, Context.ConnectionId);
        }
        
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Delete a webhook - only affects the current user's store
    /// </summary>
    public async Task DeleteEntry(string id)
    {
        var userEmail = GetUserEmail();
        
        if (string.IsNullOrEmpty(userEmail))
        {
            _logger.LogWarning("DeleteEntry called without user context");
            return;
        }

        if (await _store.DeleteAsync(userEmail, id))
        {
            // Only notify the user who deleted (all their connections)
            await Clients.Group(userEmail).SendAsync("EntryDeleted", id);
            _logger.LogDebug("User {Email} deleted webhook {Id}", userEmail, id);
        }
    }

    /// <summary>
    /// Clear all webhooks - only affects the current user's store
    /// </summary>
    public async Task ClearAll()
    {
        var userEmail = GetUserEmail();
        
        if (string.IsNullOrEmpty(userEmail))
        {
            _logger.LogWarning("ClearAll called without user context");
            return;
        }

        var count = await _store.ClearAsync(userEmail);
        
        // Only notify the user who cleared (all their connections)
        await Clients.Group(userEmail).SendAsync("AllCleared");
        _logger.LogInformation("User {Email} cleared {Count} webhooks", userEmail, count);
    }

    /// <summary>
    /// Restore all webhooks from master list - resets the user's view
    /// </summary>
    public async Task RestoreAll()
    {
        var userEmail = GetUserEmail();
        
        if (string.IsNullOrEmpty(userEmail))
        {
            _logger.LogWarning("RestoreAll called without user context");
            return;
        }

        var entries = await _store.RestoreFromMasterAsync(userEmail);
        
        // Send the restored data to the user (all their connections)
        await Clients.Group(userEmail).SendAsync("AllRestored", entries);
        _logger.LogInformation("User {Email} restored {Count} webhooks from master", userEmail, entries.Count());
    }
}
