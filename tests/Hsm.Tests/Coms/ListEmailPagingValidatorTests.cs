using Hsm.Application.Coms;
using Hsm.Application.Coms.Queries.ListEmailBatches;
using Hsm.Application.Coms.Queries.ListEmailRecipients;

namespace Hsm.Tests.Coms;

/// <summary>
/// Regression: ComsEndpoints previously capped page/limit at the HTTP edge
/// (BodyValidator/QueryValidator); Task 3's sweep replaced that with a bare
/// default-or-parsed int and no upper bound. These two validators restore the
/// frozen paging contract in the pipeline.
/// </summary>
public class ListEmailPagingValidatorTests
{
    [Fact]
    public void Batches_oversized_page_size_is_refused()
    {
        var validator = new ListEmailBatchesValidator();
        var query = new ListEmailBatchesQuery(
            new BatchListFilter(null, null, null, null, null, Page: 1, Limit: 5000));

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Batches_valid_paging_passes()
    {
        var validator = new ListEmailBatchesValidator();
        var query = new ListEmailBatchesQuery(
            new BatchListFilter(null, null, null, null, null, Page: 1, Limit: 20));

        var result = validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Recipients_oversized_page_size_is_refused()
    {
        var validator = new ListEmailRecipientsValidator();
        var query = new ListEmailRecipientsQuery(new RecipientListFilter(null, null, null, Page: 1, Limit: 5000));

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Recipients_valid_paging_passes()
    {
        var validator = new ListEmailRecipientsValidator();
        var query = new ListEmailRecipientsQuery(new RecipientListFilter(null, null, null, Page: 1, Limit: 20));

        var result = validator.Validate(query);

        Assert.True(result.IsValid);
    }
}
