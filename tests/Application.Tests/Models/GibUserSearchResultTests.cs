using FluentAssertions;
using MERSEL.Services.GibUserList.Application.Models;

namespace MERSEL.Services.GibUserList.Application.Tests.Models;

public class GibUserSearchResultTests
{
    [Theory]
    [InlineData(100, 20, 5)]
    [InlineData(101, 20, 6)]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(50, 10, 5)]
    public void TotalPages_ShouldCalculateCorrectly(int totalCount, int pageSize, int expectedPages)
    {
        var result = new GibUserSearchResult
        {
            Items = [],
            TotalCount = totalCount,
            Page = 1,
            PageSize = pageSize
        };

        result.TotalPages.Should().Be(expectedPages);
    }

    [Fact]
    public void TotalPages_WithZeroPageSize_ShouldReturnZero()
    {
        var result = new GibUserSearchResult
        {
            Items = [],
            TotalCount = 100,
            Page = 1,
            PageSize = 0
        };

        result.TotalPages.Should().Be(0);
    }
}
