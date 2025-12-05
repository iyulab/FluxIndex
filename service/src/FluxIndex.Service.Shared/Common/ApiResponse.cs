namespace FluxIndex.Service.Shared.Common;

/// <summary>
/// Standard API response wrapper for consistent response format.
/// </summary>
/// <typeparam name="T">The type of the response data.</typeparam>
public class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public List<ApiError>? Errors { get; init; }
    public ApiMetadata? Metadata { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }

    public static ApiResponse<T> Ok(T data, ApiMetadata metadata, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message,
            Metadata = metadata
        };
    }

    public static ApiResponse<T> Fail(string message, List<ApiError>? errors = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Errors = errors
        };
    }

    public static ApiResponse<T> Fail(List<ApiError> errors)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = "One or more errors occurred.",
            Errors = errors
        };
    }
}

/// <summary>
/// Represents an individual API error.
/// </summary>
public class ApiError
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Field { get; init; }
    public object? Details { get; init; }
}

/// <summary>
/// Metadata for API responses, typically for pagination.
/// </summary>
public class ApiMetadata
{
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public int? TotalCount { get; init; }
    public int? TotalPages { get; init; }
    public bool? HasNextPage { get; init; }
    public bool? HasPreviousPage { get; init; }
    public double? ExecutionTimeMs { get; init; }
}
