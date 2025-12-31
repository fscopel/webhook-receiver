using System.Collections.Concurrent;
using Google.Cloud.Firestore;
using WebhookReceiver.Models;

namespace WebhookReceiver.Services;

/// <summary>
/// Webhook storage using Firestore with a master list and per-user views.
/// 
/// Master Store: Firestore collection "webhooks" - source of truth, 24hr TTL via scheduled cleanup.
/// User Stores: Firestore subcollection "user_webhooks/{email}/webhooks" - personal views users can delete from.
/// 
/// When webhooks arrive: Added to master + all active users.
/// When users log in: They get all webhooks from master (synced to their store).
/// When users delete: Only their store is affected.
/// When cleanup runs: Firebase scheduled function removes expired webhooks.
/// </summary>
public class WebhookStore
{
    private readonly FirestoreDb _db;
    private readonly ILogger<WebhookStore> _logger;
    
    // Collection names
    private const string MasterCollection = "webhooks";
    private const string UserWebhooksCollection = "user_webhooks";
    private const string WebhooksSubcollection = "webhooks";
    
    // Track active users in memory (users with at least one SignalR connection)
    // This doesn't need persistence - it's runtime state only
    private readonly ConcurrentDictionary<string, int> _activeUsers = new();
    
    private readonly TimeSpan _ttl = TimeSpan.FromHours(24);

    public WebhookStore(IConfiguration configuration, ILogger<WebhookStore> logger)
    {
        var projectId = configuration["Firebase:ProjectId"] ?? "webhook-receiver-ldeat";
        _db = FirestoreDb.Create(projectId);
        _logger = logger;
    }

    /// <summary>
    /// Register a user as active (called when SignalR connects)
    /// </summary>
    public void RegisterUser(string userEmail)
    {
        var email = NormalizeEmail(userEmail);
        _activeUsers.AddOrUpdate(email, 1, (_, count) => count + 1);
    }

    /// <summary>
    /// Unregister a user connection (called when SignalR disconnects)
    /// </summary>
    public void UnregisterUser(string userEmail)
    {
        var email = NormalizeEmail(userEmail);
        _activeUsers.AddOrUpdate(email, 0, (_, count) => Math.Max(0, count - 1));
    }

    /// <summary>
    /// Check if a user is currently active (has at least one connection)
    /// </summary>
    public bool IsUserActive(string userEmail)
    {
        var email = NormalizeEmail(userEmail);
        return _activeUsers.TryGetValue(email, out var count) && count > 0;
    }

    /// <summary>
    /// Get all currently active user emails
    /// </summary>
    public IEnumerable<string> GetActiveUsers()
    {
        return _activeUsers
            .Where(kvp => kvp.Value > 0)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// Add a webhook to the master store AND all active users' stores.
    /// This is called when a new webhook arrives from an external source.
    /// </summary>
    public async Task AddAsync(WebhookEntry entry)
    {
        // Set expiry time
        entry.ExpiresAt = DateTime.UtcNow.Add(_ttl);
        
        // Add to master collection
        var masterDoc = _db.Collection(MasterCollection).Document(entry.Id);
        await masterDoc.SetAsync(entry);
        
        _logger.LogDebug("Added webhook {Id} to master collection", entry.Id);
        
        // Add to all active users' stores
        var activeUsers = GetActiveUsers();
        var tasks = activeUsers.Select(userEmail => AddToUserStoreAsync(userEmail, entry));
        await Task.WhenAll(tasks);
        
        _logger.LogDebug("Added webhook {Id} to {Count} active users", entry.Id, activeUsers.Count());
    }

    /// <summary>
    /// Add a webhook to a specific user's store
    /// </summary>
    private async Task AddToUserStoreAsync(string userEmail, WebhookEntry entry)
    {
        var email = NormalizeEmail(userEmail);
        var userDoc = _db.Collection(UserWebhooksCollection)
            .Document(email)
            .Collection(WebhooksSubcollection)
            .Document(entry.Id);
        await userDoc.SetAsync(entry);
    }

    /// <summary>
    /// Sync a user's store from the master list.
    /// Called when a user logs in to ensure they have all current webhooks.
    /// Only adds webhooks the user doesn't already have (preserves their deletions).
    /// </summary>
    public async Task<int> SyncUserFromMasterAsync(string userEmail)
    {
        var email = NormalizeEmail(userEmail);
        
        // Get all webhooks from master that haven't expired
        var masterQuery = _db.Collection(MasterCollection)
            .WhereGreaterThan("ExpiresAt", DateTime.UtcNow);
        var masterSnapshot = await masterQuery.GetSnapshotAsync();
        
        // Get user's existing webhook IDs
        var userCollection = _db.Collection(UserWebhooksCollection)
            .Document(email)
            .Collection(WebhooksSubcollection);
        var userSnapshot = await userCollection.GetSnapshotAsync();
        var existingIds = userSnapshot.Documents.Select(d => d.Id).ToHashSet();
        
        // Add missing webhooks to user's store
        var added = 0;
        var batch = _db.StartBatch();
        
        foreach (var doc in masterSnapshot.Documents)
        {
            if (!existingIds.Contains(doc.Id))
            {
                var entry = doc.ConvertTo<WebhookEntry>();
                var userDoc = userCollection.Document(entry.Id);
                batch.Set(userDoc, entry);
                added++;
            }
        }
        
        if (added > 0)
        {
            await batch.CommitAsync();
        }
        
        _logger.LogDebug("Synced {Count} webhooks to user {Email}", added, email);
        return added;
    }

    /// <summary>
    /// Get all webhooks from the master store
    /// </summary>
    public async Task<IEnumerable<WebhookEntry>> GetMasterListAsync()
    {
        var query = _db.Collection(MasterCollection)
            .WhereGreaterThan("ExpiresAt", DateTime.UtcNow)
            .OrderByDescending("ReceivedAt");
        var snapshot = await query.GetSnapshotAsync();
        
        return snapshot.Documents
            .Select(d => d.ConvertTo<WebhookEntry>())
            .ToList();
    }

    /// <summary>
    /// Get all webhooks for a specific user
    /// </summary>
    public async Task<IEnumerable<WebhookEntry>> GetAllAsync(string userEmail)
    {
        var email = NormalizeEmail(userEmail);
        // Simple query - just order by ReceivedAt, filter expired in memory
        // This avoids needing a composite index
        var query = _db.Collection(UserWebhooksCollection)
            .Document(email)
            .Collection(WebhooksSubcollection)
            .OrderByDescending("ReceivedAt");
        var snapshot = await query.GetSnapshotAsync();
        
        var now = DateTime.UtcNow;
        return snapshot.Documents
            .Select(d => d.ConvertTo<WebhookEntry>())
            .Where(e => e.ExpiresAt > now) // Filter expired in memory
            .ToList();
    }

    /// <summary>
    /// Get a specific webhook from a user's store
    /// </summary>
    public async Task<WebhookEntry?> GetAsync(string userEmail, string id)
    {
        var email = NormalizeEmail(userEmail);
        var doc = await _db.Collection(UserWebhooksCollection)
            .Document(email)
            .Collection(WebhooksSubcollection)
            .Document(id)
            .GetSnapshotAsync();
        
        return doc.Exists ? doc.ConvertTo<WebhookEntry>() : null;
    }

    /// <summary>
    /// Delete a webhook from a specific user's store only.
    /// Does NOT affect the master store or other users.
    /// </summary>
    public async Task<bool> DeleteAsync(string userEmail, string id)
    {
        var email = NormalizeEmail(userEmail);
        var docRef = _db.Collection(UserWebhooksCollection)
            .Document(email)
            .Collection(WebhooksSubcollection)
            .Document(id);
        
        var doc = await docRef.GetSnapshotAsync();
        if (doc.Exists)
        {
            await docRef.DeleteAsync();
            _logger.LogDebug("User {Email} deleted webhook {Id}", email, id);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clear all webhooks for a specific user only.
    /// Does NOT affect the master store or other users.
    /// </summary>
    public async Task<int> ClearAsync(string userEmail)
    {
        var email = NormalizeEmail(userEmail);
        var collection = _db.Collection(UserWebhooksCollection)
            .Document(email)
            .Collection(WebhooksSubcollection);
        
        var snapshot = await collection.GetSnapshotAsync();
        var count = snapshot.Count;
        
        // Delete in batches (Firestore limit is 500 per batch)
        var batch = _db.StartBatch();
        var batchCount = 0;
        
        foreach (var doc in snapshot.Documents)
        {
            batch.Delete(doc.Reference);
            batchCount++;
            
            if (batchCount >= 450) // Leave some headroom
            {
                await batch.CommitAsync();
                batch = _db.StartBatch();
                batchCount = 0;
            }
        }
        
        if (batchCount > 0)
        {
            await batch.CommitAsync();
        }
        
        _logger.LogInformation("User {Email} cleared {Count} webhooks", email, count);
        return count;
    }

    /// <summary>
    /// Restore all webhooks from master to a user's store.
    /// Clears the user's store and copies everything from master.
    /// Returns the list of restored entries for the client to display.
    /// </summary>
    public async Task<IEnumerable<WebhookEntry>> RestoreFromMasterAsync(string userEmail)
    {
        var email = NormalizeEmail(userEmail);
        
        // Clear user's store first
        await ClearAsync(userEmail);
        
        // Get all webhooks from master
        var masterEntries = await GetMasterListAsync();
        
        // Copy all from master to user's store
        var batch = _db.StartBatch();
        var batchCount = 0;
        var userCollection = _db.Collection(UserWebhooksCollection)
            .Document(email)
            .Collection(WebhooksSubcollection);
        
        foreach (var entry in masterEntries)
        {
            var userDoc = userCollection.Document(entry.Id);
            batch.Set(userDoc, entry);
            batchCount++;
            
            if (batchCount >= 450)
            {
                await batch.CommitAsync();
                batch = _db.StartBatch();
                batchCount = 0;
            }
        }
        
        if (batchCount > 0)
        {
            await batch.CommitAsync();
        }
        
        _logger.LogInformation("User {Email} restored {Count} webhooks from master", email, masterEntries.Count());
        return masterEntries;
    }

    /// <summary>
    /// Get count of webhooks for a specific user
    /// </summary>
    public async Task<int> GetCountAsync(string userEmail)
    {
        var email = NormalizeEmail(userEmail);
        var snapshot = await _db.Collection(UserWebhooksCollection)
            .Document(email)
            .Collection(WebhooksSubcollection)
            .WhereGreaterThan("ExpiresAt", DateTime.UtcNow)
            .GetSnapshotAsync();
        
        return snapshot.Count;
    }

    /// <summary>
    /// Get count of webhooks in master store
    /// </summary>
    public async Task<int> GetMasterCountAsync()
    {
        var snapshot = await _db.Collection(MasterCollection)
            .WhereGreaterThan("ExpiresAt", DateTime.UtcNow)
            .GetSnapshotAsync();
        return snapshot.Count;
    }

    /// <summary>
    /// Get count of active users (in-memory only)
    /// </summary>
    public int ActiveUserCount => _activeUsers.Count(kvp => kvp.Value > 0);

    private static string NormalizeEmail(string email)
    {
        return email?.ToLowerInvariant().Trim() ?? string.Empty;
    }
}
