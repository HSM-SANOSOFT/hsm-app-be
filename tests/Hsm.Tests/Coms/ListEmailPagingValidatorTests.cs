using Hsm.Application.Coms;
using Hsm.Application.Coms.Queries.ListEmails;

namespace Hsm.Tests.Coms;

/// <summary>
/// Regression: ComsEndpoints previously capped page/limit at the HTTP edge
/// (BodyValidator/QueryValidator); Task 3's sweep replaced that with a bare
/// default-or-parsed int and no upper bound. Task 4 moved paging off the
/// filter and onto the query itself (<see cref="ListEmailsQuery"/>); this
/// validator restores the frozen paging contract in the pipeline.
/// </summary>
public class ListEmailPagingValidatorTests
{
    private static readonly EmailListFilter EmptyFilter = new(null, null, null, null, null);

    [Fact]
    public void Oversized_page_size_is_refused()
    {
        var validator = new ListEmailsValidator();
        var query = new ListEmailsQuery(EmptyFilter, Page: 1, PageSize: 5000);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Valid_paging_passes()
    {
        var validator = new ListEmailsValidator();
        var query = new ListEmailsQuery(EmptyFilter, Page: 1, PageSize: 20);

        var result = validator.Validate(query);

        Assert.True(result.IsValid);
    }
}
