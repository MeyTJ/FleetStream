namespace FleetStream.Application.Shared.Pagination;

public sealed class Page<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public string? NextCursor { get; init; }
    public int PageSize { get; init; }
    public bool HasMore { get; init; }
}
