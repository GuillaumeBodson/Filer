namespace Filer.SharedKernel.Paging;

/// <summary>
/// Standard paged envelope returned by list endpoints
/// (03-api-specification.md): items plus pagination metadata.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount)
{
    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
