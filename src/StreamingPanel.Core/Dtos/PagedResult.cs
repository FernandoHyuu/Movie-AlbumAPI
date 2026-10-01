using System.Text.Json.Serialization;

namespace StreamingPanel.Core.Dtos;

/// <summary>
/// Pagination envelope returned by every paginated list endpoint.
/// </summary>
public sealed class PagedResult<T>
{
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; }

    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; }

    // Guard against divide-by-zero: a non-positive page size yields zero pages.
    [JsonPropertyName("totalPages")]
    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling((double)TotalCount / PageSize)
        : 0;

    public PagedResult(IReadOnlyList<T> items, int pageNumber, int pageSize, int totalCount)
    {
        Items = items ?? Array.Empty<T>();
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalCount = totalCount;
    }
}
