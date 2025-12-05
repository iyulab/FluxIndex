namespace FluxIndex.Service.Shared.Common;

/// <summary>
/// Base class for paginated requests.
/// </summary>
public class PagedRequest
{
    private int _page = 1;
    private int _pageSize = 20;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 1,
            > 100 => 100,
            _ => value
        };
    }

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>
/// Paginated result wrapper.
/// </summary>
/// <typeparam name="T">The type of items in the result.</typeparam>
public class PagedResult<T>
{
    public List<T> Items { get; init; } = new();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public static PagedResult<T> Create(List<T> items, int page, int pageSize, int totalCount)
    {
        return new PagedResult<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public ApiMetadata ToMetadata()
    {
        return new ApiMetadata
        {
            Page = Page,
            PageSize = PageSize,
            TotalCount = TotalCount,
            TotalPages = TotalPages,
            HasNextPage = HasNextPage,
            HasPreviousPage = HasPreviousPage
        };
    }
}
