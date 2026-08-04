namespace Hsm.Contracts;

/// <summary>
/// One page of a collection, and the shape every paged endpoint returns.
///
/// <para><see cref="TotalPages"/> is computed rather than carried: it is a pure
/// function of the other two numbers, and a stored copy is a field that can
/// disagree with them. It lives in Hsm.Contracts because both the REST door and
/// the Blazor UI services hand it to callers, and Hsm.Contracts is the only
/// assembly both can see.</para>
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalItems / (double)PageSize) : 0;

    /// <summary>Projects the items, keeping the paging numbers — endpoints use this
    /// to turn a page of entities into a page of resources.</summary>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new PagedResult<TOut>([.. Items.Select(selector)], Page, PageSize, TotalItems);
    }
}
