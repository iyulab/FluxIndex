using FluxIndex.Service.Application.Interfaces.Services;
using FluxIndex.Service.Shared.DTOs.Auth;

namespace FluxIndex.Service.Api.Middleware;

/// <summary>
/// Middleware for API key authentication.
/// </summary>
public class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private const string ApiKeyHeaderName = "X-API-Key";

    // Endpoints that don't require authentication
    private static readonly string[] PublicEndpoints =
    [
        "/health",
        "/swagger",
        "/api/v1/auth/validate"
    ];

    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IApiKeyService apiKeyService)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Skip authentication for public endpoints
        if (PublicEndpoints.Any(e => path.StartsWith(e)))
        {
            await _next(context);
            return;
        }

        // Check for API key header
        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyValue))
        {
            _logger.LogWarning("API key not provided for request to {Path}", path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "API key required" });
            return;
        }

        var apiKey = apiKeyValue.ToString();
        var validatedKey = await apiKeyService.ValidateAsync(apiKey);

        if (validatedKey == null)
        {
            _logger.LogWarning("Invalid API key used for request to {Path}", path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
            return;
        }

        // Store API key info in HttpContext for use in controllers
        context.Items["ApiKey"] = validatedKey;
        context.Items["ApiKeyRole"] = validatedKey.Role;

        await _next(context);
    }
}

/// <summary>
/// Extension methods for getting API key info from HttpContext.
/// </summary>
public static class HttpContextExtensions
{
    public static ApiKeyDto? GetApiKey(this HttpContext context)
    {
        return context.Items["ApiKey"] as ApiKeyDto;
    }

    public static string? GetApiKeyRole(this HttpContext context)
    {
        return context.Items["ApiKeyRole"] as string;
    }

    public static bool IsAdmin(this HttpContext context)
    {
        return context.GetApiKeyRole() == "Admin";
    }

    public static bool IsWriter(this HttpContext context)
    {
        var role = context.GetApiKeyRole();
        return role == "Admin" || role == "Writer";
    }
}
