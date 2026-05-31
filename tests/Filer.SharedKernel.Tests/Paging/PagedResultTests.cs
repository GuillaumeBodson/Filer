using FluentAssertions;
using Filer.SharedKernel.Paging;
using Xunit;

namespace Filer.SharedKernel.Tests.Paging;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(0, 10, 0)]    // no items, no pages
    [InlineData(10, 10, 1)]   // exactly one full page
    [InlineData(9, 3, 3)]     // exact multiple
    [InlineData(10, 3, 4)]    // remainder rounds up
    [InlineData(1, 10, 1)]    // a single item still makes one page
    public void TotalPages_DividesCountByPageSizeRoundingUp(long totalCount, int pageSize, int expected)
    {
        var paged = new PagedResult<string>([], Page: 1, PageSize: pageSize, TotalCount: totalCount);

        paged.TotalPages.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TotalPages_WhenPageSizeNotPositive_ReturnsZeroInsteadOfDividingByZero(int pageSize)
    {
        var paged = new PagedResult<string>([], Page: 1, PageSize: pageSize, TotalCount: 100);

        paged.TotalPages.Should().Be(0);
    }
}
