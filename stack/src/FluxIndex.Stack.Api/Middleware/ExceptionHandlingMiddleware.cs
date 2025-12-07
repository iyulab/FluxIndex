using FluxIndex.Stack.Shared.Common;

namespace FluxIndex.Stack.Api.Middleware;

/// <summary>
/// Middleware for global exception handling.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "An unhandled exception occurred: {Message}", exception.Message);

        var (statusCode, response) = exception switch
        {
            KeyNotFoundException ex => (
                StatusCodes.Status404NotFound,
                ApiResponse<object>.Fail(ex.Message, new List<ApiError>
                {
                    new() { Code = "NOT_FOUND", Message = ex.Message }
                })),

            InvalidOperationException ex => (
                StatusCodes.Status400BadRequest,
                ApiResponse<object>.Fail(ex.Message, new List<ApiError>
                {
                    new() { Code = "INVALID_OPERATION", Message = ex.Message }
                })),

            ArgumentException ex => (
                StatusCodes.Status400BadRequest,
                ApiResponse<object>.Fail(ex.Message, new List<ApiError>
                {
                    new() { Code = "INVALID_ARGUMENT", Message = ex.Message, Field = ex.ParamName }
                })),

            UnauthorizedAccessException ex => (
                StatusCodes.Status403Forbidden,
                ApiResponse<object>.Fail(ex.Message, new List<ApiError>
                {
                    new() { Code = "FORBIDDEN", Message = ex.Message }
                })),

            _ => (
                StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail("An unexpected error occurred.", new List<ApiError>
                {
                    new() { Code = "INTERNAL_ERROR", Message = "An unexpected error occurred." }
                }))
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(response);
    }
}
