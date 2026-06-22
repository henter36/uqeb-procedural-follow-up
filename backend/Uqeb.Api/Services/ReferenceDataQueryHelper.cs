using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.DTOs.Common;
using Uqeb.Api.DTOs.Transactions;

namespace Uqeb.Api.Services;

public static class ReferenceDataQueryHelper
{
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
        var page = Math.Max(1, request.Page);
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);

        var total = await query.CountAsync(cancellationToken);
        query = applySort(query, request.SortBy, request.SortDesc);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(selector)
            .ToListAsync(cancellationToken);

        return PagedResult<TDto>.Create(items, total, page, pageSize);
    }
}
