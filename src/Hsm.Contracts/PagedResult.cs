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

    // CA1000 (no static members on generic types) fires because T cannot be
    // inferred from (page, pageSize) alone, so every call site names it
    // explicitly (PagedResult<Foo>.Empty(...)) — exactly the shape the rule
    // warns about, and exactly the shape a "no rows yet" factory needs.
#pragma warning disable CA1000
    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
#pragma warning restore CA1000

    /// <summary>Projects the items, keeping the paging numbers — endpoints use this
    /// to turn a page of entities into a page of resources.</summary>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new PagedResult<TOut>([.. Items.Select(selector)], Page, PageSize, TotalItems);
    }
}
