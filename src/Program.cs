using Microsoft.AspNetCore.SignalR;
using WebhookReceiver.Hubs;
using WebhookReceiver.Models;
using WebhookReceiver.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSignalR();
builder.Services.AddSingleton<WebhookStore>();
builder.Services.AddHostedService<CleanupService>();

// Configure CORS for SignalR
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true);
    });
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// Map SignalR hub
app.MapHub<WebhookHub>("/webhookhub");

// Health check endpoint
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Get all webhooks
app.MapGet("/api/webhooks", (WebhookStore store) => Results.Ok(store.GetAll()));

// Get single webhook
app.MapGet("/api/webhooks/{id}", (string id, WebhookStore store) =>
{
    var entry = store.Get(id);
    return entry is not null ? Results.Ok(entry) : Results.NotFound();
});

// Delete single webhook
app.MapDelete("/api/webhooks/{id}", async (string id, WebhookStore store, IHubContext<WebhookHub> hub) =>
{
    if (store.Delete(id))
    {
        await hub.Clients.All.SendAsync("EntryDeleted", id);
        return Results.Ok();
    }
    return Results.NotFound();
});

// Clear all webhooks
app.MapDelete("/api/webhooks", async (WebhookStore store, IHubContext<WebhookHub> hub) =>
{
    var count = store.Clear();
    await hub.Clients.All.SendAsync("AllCleared");
    return Results.Ok(new { deleted = count });
});

// Webhook receiver endpoint - catches all methods and paths under /api/webhook
app.Map("/api/webhook/{*path}", async (HttpContext context, WebhookStore store, IHubContext<WebhookHub> hub) =>
{
    var entry = await CaptureWebhook(context);
    store.Add(entry);
    await hub.Clients.All.SendAsync("NewWebhook", entry);
    
    return Results.Ok(new 
    { 
        message = "Webhook received", 
        id = entry.Id,
        receivedAt = entry.ReceivedAt 
    });
});

// Also handle /api/webhook without trailing path
app.Map("/api/webhook", async (HttpContext context, WebhookStore store, IHubContext<WebhookHub> hub) =>
{
    var entry = await CaptureWebhook(context);
    store.Add(entry);
    await hub.Clients.All.SendAsync("NewWebhook", entry);
    
    return Results.Ok(new 
    { 
        message = "Webhook received", 
        id = entry.Id,
        receivedAt = entry.ReceivedAt 
    });
});

app.Run();

// Helper function to capture webhook details
static async Task<WebhookEntry> CaptureWebhook(HttpContext context)
{
    var request = context.Request;
    
    // Read body
    string? body = null;
    if (request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding"))
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
    }

    // Extract path after /api/webhook/
    var fullPath = request.Path.Value ?? "";
    var channel = fullPath.Replace("/api/webhook", "").TrimStart('/');

    // Capture headers (exclude some internal ones)
    var headers = request.Headers
        .Where(h => !h.Key.StartsWith(":", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(h => h.Key, h => h.Value.ToString());

    return new WebhookEntry
    {
        Method = request.Method,
        Path = fullPath,
        Channel = string.IsNullOrEmpty(channel) ? null : channel,
        QueryString = request.QueryString.HasValue ? request.QueryString.Value : null,
        Headers = headers,
        ContentType = request.ContentType,
        Body = body,
        SourceIp = context.Connection.RemoteIpAddress?.ToString(),
        ContentLength = request.ContentLength ?? body?.Length ?? 0
    };
}
