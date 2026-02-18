using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.DTOs.Auth;

namespace FluxIndex.Stack.Api.Middleware;

/// <summary>
/// Middleware for API key authentication.
/// </summary>
public partial class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly IHostEnvironment _environment;
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
        ILogger<ApiKeyAuthenticationMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context, IApiKeyService apiKeyService)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Skip authentication for public endpoints
        if (PublicEndpoints.Any(e => path.StartsWith(e, StringComparison.Ordinal)))
        {
            await _next(context);
            return;
        }

        // Skip authentication in Development environment when no API key is provided
        if (_environment.IsDevelopment() &&
            !context.Request.Headers.ContainsKey(ApiKeyHeaderName))
        {
            LogBypassingAuthentication(_logger, path);

            // Set a mock admin context for development
            context.Items["ApiKey"] = new ApiKeyDto
            {
                Id = Guid.Empty,
                Name = "Development",
                Role = "Admin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            context.Items["ApiKeyRole"] = "Admin";

            await _next(context);
            return;
        }

        // Check for API key header
        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyValue))
        {
            LogApiKeyNotProvided(_logger, path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "API key required" });
            return;
        }

        var apiKey = apiKeyValue.ToString();
        var validatedKey = await apiKeyService.ValidateAsync(apiKey);

        if (validatedKey == null)
        {
            LogInvalidApiKey(_logger, path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
            return;
        }

        // Store API key info in HttpContext for use in controllers
        context.Items["ApiKey"] = validatedKey;
        context.Items["ApiKeyRole"] = validatedKey.Role;

        await _next(context);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Development mode: bypassing API key authentication for {Path}")]
    private static partial void LogBypassingAuthentication(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API key not provided for request to {Path}")]
    private static partial void LogApiKeyNotProvided(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid API key used for request to {Path}")]
    private static partial void LogInvalidApiKey(ILogger logger, string path);

    #endregion
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
