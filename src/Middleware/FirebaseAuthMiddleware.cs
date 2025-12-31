using WebhookReceiver.Services;

namespace WebhookReceiver.Middleware;

/// <summary>
/// Middleware that validates Firebase tokens for protected routes.
/// The webhook receiver endpoints remain open (they need to receive external webhooks).
/// </summary>
public class FirebaseAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FirebaseAuthMiddleware> _logger;

    // Paths that require authentication (dashboard access)
    private static readonly string[] ProtectedPaths = 
    {
        "/api/webhooks",  // Viewing webhooks requires auth
        "/webhookhub"     // SignalR hub requires auth
    };

    // Paths that should NEVER require auth (incoming webhooks from external systems)
    private static readonly string[] PublicPaths = 
    {
        "/api/webhook/",  // Webhook receiver with path - must be open!
        "/api/health",    // Health check
        "/api/auth/"      // Auth endpoints (email validation before login)
    };
    
    // Exact public paths (not prefix matching)
    private static readonly string[] ExactPublicPaths = 
    {
        "/api/webhook"    // Webhook receiver root - must be open!
    };

    public FirebaseAuthMiddleware(RequestDelegate next, ILogger<FirebaseAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, FirebaseAuthService authService)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Always allow public paths (webhook receiver, health check)
        if (PublicPaths.Any(p => path.StartsWith(p)) || ExactPublicPaths.Contains(path))
        {
            await _next(context);
            return;
        }

        // Check if path requires authentication
        var requiresAuth = ProtectedPaths.Any(p => path.StartsWith(p));
        
        if (!requiresAuth)
        {
            // Static files and other non-protected paths
            await _next(context);
            return;
        }

        // Extract token from Authorization header or query string (for SignalR)
        var token = ExtractToken(context);

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("No token provided for protected path: {Path}", path);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Authentication required" });
            return;
        }

        // Validate token and check email authorization
        var principal = await authService.ValidateTokenAsync(token);

        if (principal == null)
        {
            _logger.LogWarning("Invalid or unauthorized token for path: {Path}", path);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "Access denied. Only @ldeat.com emails are authorized." });
            return;
        }

        // Set the user principal for downstream middleware/handlers
        context.User = principal;
        
        await _next(context);
    }

    private static string? ExtractToken(HttpContext context)
    {
        // Try Authorization header first
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }

        // Try query string (for SignalR WebSocket connections)
        if (context.Request.Query.TryGetValue("access_token", out var queryToken))
        {
            return queryToken.FirstOrDefault();
        }

        return null;
    }
}

public static class FirebaseAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseFirebaseAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<FirebaseAuthMiddleware>();
    }
}

