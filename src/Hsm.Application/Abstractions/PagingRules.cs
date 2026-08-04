using FluentValidation;

namespace Hsm.Application.Abstractions;

/// <summary>
/// The paging contract, stated once. Every paged query's validator calls these
/// two rules, so "page starts at 1" and "pageSize is capped at 100" cannot
/// drift between modules — and an oversized pageSize is REFUSED rather than
/// silently clamped, because a caller who asked for 5000 rows and got 100
/// without being told will page incorrectly.
/// </summary>
public static class PagingRules
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static IRuleBuilderOptions<T, int> ValidPage<T>(this IRuleBuilder<T, int> rule) =>
        rule.GreaterThanOrEqualTo(1).WithMessage("page must be 1 or greater.");

    public static IRuleBuilderOptions<T, int> ValidPageSize<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, MaxPageSize)
            .WithMessage($"pageSize must be between 1 and {MaxPageSize}.");
}
