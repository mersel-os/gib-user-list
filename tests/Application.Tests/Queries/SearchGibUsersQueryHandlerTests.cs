using FluentAssertions;
using NSubstitute;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Application.Queries;
using MERSEL.Services.GibUserList.Application.Tests.Helpers;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Tests.Queries;

public class SearchGibUsersQueryHandlerTests : IDisposable
{
    private readonly IAppMetrics _metrics = Substitute.For<IAppMetrics>();
    private readonly TestGibUserListDbContext _dbContext;

    private static readonly List<EInvoiceGibUser> EInvoiceUsers =
    [
        new()
        {
            Id = Guid.NewGuid(),
            Identifier = "1111111111",
            Title = "MERSEL YAZILIM A.S.",
            TitleLower = "mersel yazılım a.s.",
            FirstCreationTime = DateTime.Now,
        },
        new()
        {
            Id = Guid.NewGuid(),
            Identifier = "2222222222",
            Title = "ABC TICARET LTD. STI.",
            TitleLower = "abc ticaret ltd. sti.",
            FirstCreationTime = DateTime.Now,
        },
        new()
        {
            Id = Guid.NewGuid(),
            Identifier = "3333333333",
            Title = "XYZ YAZILIM VE DANISMANLIK",
            TitleLower = "xyz yazılım ve danışmanlık",
            FirstCreationTime = DateTime.Now,
        }
    ];

    public SearchGibUsersQueryHandlerTests()
    {
        _dbContext = TestGibUserListDbContext.Create(eInvoiceUsers: EInvoiceUsers);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Handle_ByTitle_ShouldReturnMatchingResults()
    {
        var handler = new SearchGibUsersQueryHandler(_dbContext, _metrics);
        var query = new SearchGibUsersQuery("yazılım", GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(2); // MERSEL YAZILIM + XYZ YAZILIM
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ByIdentifier_ShouldReturnMatchingResults()
    {
        var handler = new SearchGibUsersQueryHandler(_dbContext, _metrics);
        var query = new SearchGibUsersQuery("1111111111", GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].Identifier.Should().Be("1111111111");
    }

    [Fact]
    public async Task Handle_NoMatch_ShouldReturnEmptyResult()
    {
        var handler = new SearchGibUsersQueryHandler(_dbContext, _metrics);
        var query = new SearchGibUsersQuery("bulunamayacak", GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithPagination_ShouldReturnCorrectPage()
    {
        var handler = new SearchGibUsersQueryHandler(_dbContext, _metrics);
        var query = new SearchGibUsersQuery("yazılım", GibUserDocumentType.EInvoice, Page: 1, PageSize: 1);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(1);
    }

    [Fact]
    public async Task Handle_SecondPage_ShouldReturnRemainingResults()
    {
        var handler = new SearchGibUsersQueryHandler(_dbContext, _metrics);
        var query = new SearchGibUsersQuery("yazılım", GibUserDocumentType.EInvoice, Page: 2, PageSize: 1);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(2);
        result.Page.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ShouldRecordSearchQueryMetric()
    {
        var handler = new SearchGibUsersQueryHandler(_dbContext, _metrics);
        var query = new SearchGibUsersQuery("test", GibUserDocumentType.EInvoice);

        await handler.Handle(query, CancellationToken.None);

        _metrics.Received(1).RecordQuery("search", "einvoice", Arg.Any<double>());
    }

    [Fact]
    public async Task Handle_WhenDbThrows_ShouldRecordErrorMetricAndRethrow()
    {
        var faultyContext = Substitute.For<IGibUserListReadDbContext>();
        faultyContext.EInvoiceGibUsers.Returns(_ => throw new InvalidOperationException("DB hatası"));

        var handler = new SearchGibUsersQueryHandler(faultyContext, _metrics);
        var query = new SearchGibUsersQuery("test", GibUserDocumentType.EInvoice);

        var act = () => handler.Handle(query, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        _metrics.Received(1).RecordQueryError("search", "einvoice");
    }
}
