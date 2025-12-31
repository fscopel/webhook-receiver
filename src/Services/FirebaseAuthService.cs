using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace WebhookReceiver.Services;

/// <summary>
/// Server-side Firebase token validation with email domain restrictions.
/// This CANNOT be bypassed by client-side manipulation.
/// </summary>
public class FirebaseAuthService
{
    // Server-side configuration - CANNOT be modified by clients
    private static readonly string[] AllowedDomains = { "ldeat.com" };
    private static readonly string[] AllowedEmails = 
    { 
        "fabioscopel99@gmail.com", 
        "fabiob.scopel@gmail.com" 
    };
    
    private readonly string _projectId;
    private readonly HttpClient _httpClient;
    private readonly ILogger<FirebaseAuthService> _logger;
    
    // Cache for Google's public keys
    private static JsonWebKeySet? _googleKeys;
    private static DateTime _keysExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim _keyLock = new(1, 1);

    public FirebaseAuthService(IConfiguration configuration, ILogger<FirebaseAuthService> logger)
    {
        _projectId = configuration["Firebase:ProjectId"] ?? "webhook-receiver-ldeat";
        _httpClient = new HttpClient();
        _logger = logger;
    }

    /// <summary>
    /// Validates a Firebase ID token and checks if the email is authorized.
    /// Returns the validated claims if successful, null if validation fails.
    /// </summary>
    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string idToken)
    {
        try
        {
            var keys = await GetGooglePublicKeysAsync();
            
            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = new[] 
                { 
                    $"https://securetoken.google.com/{_projectId}" 
                },
                ValidateAudience = true,
                ValidAudience = _projectId,
                ValidateLifetime = true,
                IssuerSigningKeys = keys.GetSigningKeys(),
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var principal = tokenHandler.ValidateToken(idToken, validationParameters, out _);
            
            // Get email from claims
            var email = principal.FindFirst(ClaimTypes.Email)?.Value 
                     ?? principal.FindFirst("email")?.Value;
            
            if (string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("Token valid but no email claim found");
                return null;
            }

            // SERVER-SIDE EMAIL VALIDATION - Cannot be bypassed!
            if (!IsEmailAuthorized(email))
            {
                _logger.LogWarning("Unauthorized email attempted access: {Email}", email);
                return null;
            }

            _logger.LogInformation("Authorized user signed in: {Email}", email);
            return principal;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("Token validation failed: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Firebase token");
            return null;
        }
    }

    /// <summary>
    /// Server-side email authorization check.
    /// </summary>
    public static bool IsEmailAuthorized(string? email)
    {
        if (string.IsNullOrEmpty(email)) return false;
        
        var emailLower = email.ToLowerInvariant().Trim();
        
        // Check whitelist
        if (AllowedEmails.Contains(emailLower))
            return true;
        
        // Check domain
        var atIndex = emailLower.LastIndexOf('@');
        if (atIndex < 0) return false;
        
        var domain = emailLower[(atIndex + 1)..];
        return AllowedDomains.Contains(domain);
    }

    /// <summary>
    /// Fetches Google's public keys for JWT validation (cached).
    /// </summary>
    private async Task<JsonWebKeySet> GetGooglePublicKeysAsync()
    {
        await _keyLock.WaitAsync();
        try
        {
            if (_googleKeys != null && DateTime.UtcNow < _keysExpiry)
            {
                return _googleKeys;
            }

            var response = await _httpClient.GetAsync(
                "https://www.googleapis.com/service_accounts/v1/jwk/securetoken@system.gserviceaccount.com");
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            _googleKeys = new JsonWebKeySet(json);
            
            // Cache for 1 hour (Google recommends checking cache-control header)
            _keysExpiry = DateTime.UtcNow.AddHours(1);
            
            return _googleKeys;
        }
        finally
        {
            _keyLock.Release();
        }
    }
}

