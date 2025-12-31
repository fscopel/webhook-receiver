using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using WebhookReceiver.Hubs;
using WebhookReceiver.Middleware;
using WebhookReceiver.Models;
using WebhookReceiver.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSignalR();
builder.Services.AddSingleton<WebhookStore>();
builder.Services.AddSingleton<FirebaseAuthService>();

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

// Serve static files - prefer minified/obfuscated files from wwwroot-dist if available
// In Development mode, always serve readable source files from wwwroot
var distPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot-dist");
var useMinified = !app.Environment.IsDevelopment() && Directory.Exists(distPath);

if (useMinified)
{
    app.Logger.LogInformation("Serving minified files from wwwroot-dist (Environment: {Env})", app.Environment.EnvironmentName);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(distPath)
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(distPath)
    });
}
else
{
    app.Logger.LogInformation("Serving source files from wwwroot (Environment: {Env}, DistExists: {DistExists})", 
        app.Environment.EnvironmentName, Directory.Exists(distPath));
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// Firebase authentication middleware - validates tokens and enforces email domain restrictions
app.UseFirebaseAuth();

// Map SignalR hub
app.MapHub<WebhookHub>("/webhookhub");

// Health check endpoint
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Email validation endpoint - called BEFORE sending Firebase sign-in link
// This is a PUBLIC endpoint (no auth required) - validates if email is allowed
app.MapPost("/api/auth/validate-email", (EmailValidationRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return Results.BadRequest(new { valid = false, error = "Email is required" });
    }
    
    var email = request.Email.Trim().ToLowerInvariant();
    
    if (!IsValidEmailFormat(email))
    {
        return Results.BadRequest(new { valid = false, error = "Please enter a valid email address" });
    }
    
    if (FirebaseAuthService.IsEmailAuthorized(email))
    {
        return Results.Ok(new { valid = true });
    }
    
    var domain = email.Split('@').LastOrDefault() ?? "";
    return Results.Ok(new { 
        valid = false, 
        error = $"Access restricted to @ldeat.com emails. \"{domain}\" is not authorized."
    });
});

static bool IsValidEmailFormat(string email)
{
    try
    {
        var addr = new System.Net.Mail.MailAddress(email);
        return addr.Address == email;
    }
    catch
    {
        return false;
    }
}

record EmailValidationRequest(string Email);

// Get all webhooks - returns only the requesting user's webhooks
app.MapGet("/api/webhooks", async (HttpContext context, WebhookStore store) =>
{
    var userEmail = GetUserEmail(context);
    if (string.IsNullOrEmpty(userEmail))
    {
        return Results.Unauthorized();
    }
    var entries = await store.GetAllAsync(userEmail);
    return Results.Ok(entries);
});

// Get single webhook - from the requesting user's store
app.MapGet("/api/webhooks/{id}", async (string id, HttpContext context, WebhookStore store) =>
{
    var userEmail = GetUserEmail(context);
    if (string.IsNullOrEmpty(userEmail))
    {
        return Results.Unauthorized();
    }
    
    var entry = await store.GetAsync(userEmail, id);
    return entry is not null ? Results.Ok(entry) : Results.NotFound();
});

// Delete single webhook - only from the requesting user's store
app.MapDelete("/api/webhooks/{id}", async (string id, HttpContext context, WebhookStore store, IHubContext<WebhookHub> hub) =>
{
    var userEmail = GetUserEmail(context);
    if (string.IsNullOrEmpty(userEmail))
    {
        return Results.Unauthorized();
    }
    
    if (await store.DeleteAsync(userEmail, id))
    {
        // Notify only this user's connections
        await hub.Clients.Group(userEmail).SendAsync("EntryDeleted", id);
        return Results.Ok();
    }
    return Results.NotFound();
});

// Clear all webhooks - only from the requesting user's store
app.MapDelete("/api/webhooks", async (HttpContext context, WebhookStore store, IHubContext<WebhookHub> hub) =>
{
    var userEmail = GetUserEmail(context);
    if (string.IsNullOrEmpty(userEmail))
    {
        return Results.Unauthorized();
    }
    
    var count = await store.ClearAsync(userEmail);
    // Notify only this user's connections
    await hub.Clients.Group(userEmail).SendAsync("AllCleared");
    return Results.Ok(new { deleted = count });
});

// Webhook receiver endpoint - catches all methods and paths under /api/webhook
// This is a PUBLIC endpoint - webhooks are added to master store AND all active users
app.Map("/api/webhook/{*path}", async (HttpContext context, WebhookStore store, IHubContext<WebhookHub> hub) =>
{
    var entry = await CaptureWebhook(context);
    
    // Add to master store AND all active users' stores (Firestore)
    await store.AddAsync(entry);
    
    // Broadcast to all connected clients
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
    
    // Add to master store AND all active users' stores (Firestore)
    await store.AddAsync(entry);
    
    // Broadcast to all connected clients
    await hub.Clients.All.SendAsync("NewWebhook", entry);
    
    return Results.Ok(new 
    { 
        message = "Webhook received", 
        id = entry.Id,
        receivedAt = entry.ReceivedAt 
    });
});

app.Run();

// Helper function to get user email from HttpContext
static string? GetUserEmail(HttpContext context)
{
    var email = context.User?.FindFirst(ClaimTypes.Email)?.Value 
             ?? context.User?.FindFirst("email")?.Value;
    return email?.ToLowerInvariant().Trim();
}

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
