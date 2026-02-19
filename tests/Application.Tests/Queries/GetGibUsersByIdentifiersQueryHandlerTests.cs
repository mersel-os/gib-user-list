using FluentAssertions;
using NSubstitute;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Application.Queries;
using MERSEL.Services.GibUserList.Application.Tests.Helpers;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Tests.Queries;

public class GetGibUsersByIdentifiersQueryHandlerTests : IDisposable
{
    private readonly IAppMetrics _metrics = Substitute.For<IAppMetrics>();
    private readonly TestGibUserListDbContext _dbContext;

    private static readonly List<EInvoiceGibUser> EInvoiceUsers =
    [
        new()
        {
            Id = Guid.NewGuid(),
            Identifier = "1111111111",
            Title = "FIRMA A",
            TitleLower = "firma a",
            FirstCreationTime = DateTime.Now.AddYears(-1),
            AliasesJson = """[{"alias": "urn:mail:pk1", "type": "PK", "creationTime": "2024-01-01T00:00:00"}]"""
        },
        new()
        {
            Id = Guid.NewGuid(),
            Identifier = "2222222222",
            Title = "FIRMA B",
            TitleLower = "firma b",
            FirstCreationTime = DateTime.Now.AddYears(-2),
        },
        new()
        {
            Id = Guid.NewGuid(),
            Identifier = "3333333333",
            Title = "FIRMA C",
            TitleLower = "firma c",
            FirstCreationTime = DateTime.Now.AddMonths(-6),
        }
    ];

    public GetGibUsersByIdentifiersQueryHandlerTests()
    {
        _dbContext = TestGibUserListDbContext.Create(eInvoiceUsers: EInvoiceUsers);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Handle_WithExistingIdentifiers_ShouldReturnAllFound()
    {
        var handler = new GetGibUsersByIdentifiersQueryHandler(_dbContext, _metrics);
        var query = new GetGibUsersByIdentifiersQuery(
            ["1111111111", "2222222222"],
            GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.NotFound.Should().BeEmpty();
        result.TotalRequested.Should().Be(2);
        result.TotalFound.Should().Be(2);
    }

    [Fact]
    public async Task Handle_WithSomeMissing_ShouldReturnFoundAndNotFound()
    {
        var handler = new GetGibUsersByIdentifiersQueryHandler(_dbContext, _metrics);
        var query = new GetGibUsersByIdentifiersQuery(
            ["1111111111", "9999999999"],
            GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].Identifier.Should().Be("1111111111");
        result.NotFound.Should().ContainSingle().Which.Should().Be("9999999999");
        result.TotalRequested.Should().Be(2);
        result.TotalFound.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithDuplicateIdentifiers_ShouldDeduplicateAndQueryOnce()
    {
        var handler = new GetGibUsersByIdentifiersQueryHandler(_dbContext, _metrics);
        var query = new GetGibUsersByIdentifiersQuery(
            ["1111111111", "1111111111", "2222222222"],
            GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.TotalRequested.Should().Be(2); // Tekrarsız sayı
        result.TotalFound.Should().Be(2);
        result.NotFound.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithAllMissing_ShouldReturnEmptyItemsAndAllNotFound()
    {
        var handler = new GetGibUsersByIdentifiersQueryHandler(_dbContext, _metrics);
        var query = new GetGibUsersByIdentifiersQuery(
            ["8888888888", "9999999999"],
            GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.NotFound.Should().HaveCount(2);
        result.TotalRequested.Should().Be(2);
        result.TotalFound.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ShouldRecordBatchQueryMetric()
    {
        var handler = new GetGibUsersByIdentifiersQueryHandler(_dbContext, _metrics);
        var query = new GetGibUsersByIdentifiersQuery(
            ["1111111111"],
            GibUserDocumentType.EInvoice);

        await handler.Handle(query, CancellationToken.None);

        _metrics.Received(1).RecordQuery("batch", "einvoice", Arg.Any<double>());
    }

    [Fact]
    public async Task Handle_EDespatchType_ShouldQueryEDespatchDbSet()
    {
        var eDespatchUsers = new List<EDespatchGibUser>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Identifier = "5555555555",
                Title = "KARGO FIRMA",
                TitleLower = "kargo firma",
                FirstCreationTime = DateTime.Now,
            }
        };
        using var context = TestGibUserListDbContext.Create(eDespatchUsers: eDespatchUsers);
        var handler = new GetGibUsersByIdentifiersQueryHandler(context, _metrics);
        var query = new GetGibUsersByIdentifiersQuery(
            ["5555555555"],
            GibUserDocumentType.EDespatch);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].Title.Should().Be("KARGO FIRMA");
    }

    [Fact]
    public async Task Handle_WhenDbThrows_ShouldRecordErrorMetricAndRethrow()
    {
        var faultyContext = Substitute.For<IGibUserListReadDbContext>();
        faultyContext.EInvoiceGibUsers.Returns(_ => throw new InvalidOperationException("DB hatası"));

        var handler = new GetGibUsersByIdentifiersQueryHandler(faultyContext, _metrics);
        var query = new GetGibUsersByIdentifiersQuery(
            ["1111111111"],
            GibUserDocumentType.EInvoice);

        var act = () => handler.Handle(query, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        _metrics.Received(1).RecordQueryError("batch", "einvoice");
    }
}
