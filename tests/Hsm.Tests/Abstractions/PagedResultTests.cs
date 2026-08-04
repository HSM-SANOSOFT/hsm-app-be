using Hsm.Contracts;

namespace Hsm.Tests.Abstractions;

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(100, 7, 15)]
    public void TotalPages_is_the_ceiling_of_total_over_page_size(int totalItems, int pageSize, int expected)
    {
        var result = new PagedResult<string>([], Page: 1, PageSize: pageSize, TotalItems: totalItems);

        Assert.Equal(expected, result.TotalPages);
    }

    [Fact]
    public void A_zero_page_size_reports_no_pages_rather_than_dividing_by_zero()
    {
        var result = new PagedResult<string>([], Page: 1, PageSize: 0, TotalItems: 5);

        Assert.Equal(0, result.TotalPages);
    }
}
