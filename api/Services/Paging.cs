using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Services;

public record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);

public static class PagingExtensions
{
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;

    /// <summary>Counts, then fetches one page. Page size is capped so nobody can ask for the whole table.</summary>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<T>(items, total, page, pageSize);
    }

    public static PagedResult<U> Map<T, U>(this PagedResult<T> page, Func<T, U> map) =>
        new(page.Items.Select(map).ToList(), page.Total, page.Page, page.PageSize);

    /// <summary>Trims and length-limits a search term; returns null when there is nothing to search for.</summary>
    public static string? CleanSearch(string? q)
    {
        q = q?.Trim();
        if (string.IsNullOrEmpty(q)) return null;
        return q.Length > MaxSearchLength ? q[..MaxSearchLength] : q;
    }
}
