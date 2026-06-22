using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.DTOs.Common;
using Uqeb.Api.DTOs.Transactions;

namespace Uqeb.Api.Services;

public static class ReferenceDataQueryHelper
{
    public static ReferenceDataListRequest NormalizeListRequest(ReferenceDataListRequest request) => new()
    {
        Search = request.Search,
        Status = request.Status,
        SortBy = string.IsNullOrWhiteSpace(request.SortBy) ? "name" : request.SortBy,
        SortDesc = request.SortDesc ?? false,
        Page = Math.Max(1, request.Page ?? 1),
        PageSize = request.PageSize is null or <= 0 ? 20 : Math.Min(request.PageSize.Value, 100),
    };

    public static LookupRequest NormalizeLookupRequest(LookupRequest request) => new()
    {
        Search = request.Search,
        ActiveOnly = request.ActiveOnly ?? true,
        Limit = request.Limit is null or <= 0 ? 50 : Math.Min(request.Limit.Value, 100),
    };

    public static IQueryable<T> ApplyStatusFilter<T>(IQueryable<T> query, string? status, Func<IQueryable<T>, IQueryable<T>> activeFilter, Func<IQueryable<T>, IQueryable<T>> inactiveFilter)
    {
        if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            return activeFilter(query);
        if (string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase))
            return inactiveFilter(query);
        return query;
    }

    public static async Task<PagedResult<TDto>> ToPagedAsync<TEntity, TDto>(
        IQueryable<TEntity> query,
        ReferenceDataListRequest request,
        Func<IQueryable<TEntity>, string, bool, IQueryable<TEntity>> applySort,
        Expression<Func<TEntity, TDto>> selector,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeListRequest(request);
        var page = normalized.Page!.Value;
        var pageSize = normalized.PageSize!.Value;
        var sortBy = normalized.SortBy!;
        var sortDesc = normalized.SortDesc!.Value;

        var total = await query.CountAsync(cancellationToken);
        query = applySort(query, sortBy, sortDesc);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(selector)
            .ToListAsync(cancellationToken);

        return PagedResult<TDto>.Create(items, total, page, pageSize);
    }
}
