using FluentAssertions;
using NSubstitute;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Application.Queries;
using MERSEL.Services.GibUserList.Application.Tests.Helpers;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Tests.Queries;

public class GetGibUserByIdentifierQueryHandlerTests : IDisposable
{
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly IAppMetrics _metrics = Substitute.For<IAppMetrics>();
    private readonly TestGibUserListDbContext _dbContext;

    private static readonly EInvoiceGibUser TestEInvoiceUser = new()
    {
        Id = Guid.NewGuid(),
        Identifier = "1234567890",
        Title = "MERSEL YAZILIM A.S.",
        TitleLower = "mersel yazılım a.s.",
        AccountType = "Özel",
        Type = "Mükellef",
        FirstCreationTime = new DateTime(2024, 1, 15, 10, 0, 0),
        AliasesJson = """[{"alias": "urn:mail:defaultpk", "type": "PK", "creationTime": "2024-01-15T10:00:00"}]"""
    };

    private static readonly EDespatchGibUser TestEDespatchUser = new()
    {
        Id = Guid.NewGuid(),
        Identifier = "9876543210",
        Title = "TEST LOJISTIK A.S.",
        TitleLower = "test lojistik a.s.",
        FirstCreationTime = new DateTime(2024, 3, 1, 8, 0, 0),
    };

    public GetGibUserByIdentifierQueryHandlerTests()
    {
        _dbContext = TestGibUserListDbContext.Create(
            eInvoiceUsers: [TestEInvoiceUser],
            eDespatchUsers: [TestEDespatchUser]);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Handle_WhenCacheHit_ShouldReturnCachedValueWithoutDbQuery()
    {
        var cachedDto = new GibUserDto
        {
            Identifier = "1234567890",
            Title = "CACHED VALUE",
            FirstCreationTime = DateTime.Now
        };

        _cache.GetAsync<GibUserDto>("einvoice:id:1234567890", Arg.Any<CancellationToken>())
            .Returns(cachedDto);

        var handler = new GetGibUserByIdentifierQueryHandler(_dbContext, _cache, _metrics);
        var query = new GetGibUserByIdentifierQuery("1234567890", GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Title.Should().Be("CACHED VALUE");
        _metrics.Received(1).RecordCacheHit();
        _metrics.DidNotReceive().RecordCacheMiss();
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldQueryDbAndCacheResult()
    {
        _cache.GetAsync<GibUserDto>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((GibUserDto?)null);

        var handler = new GetGibUserByIdentifierQueryHandler(_dbContext, _cache, _metrics);
        var query = new GetGibUserByIdentifierQuery("1234567890", GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Identifier.Should().Be("1234567890");
        result.Title.Should().Be("MERSEL YAZILIM A.S.");
        result.Aliases.Should().HaveCount(1);

        _metrics.Received(1).RecordCacheMiss();
        await _cache.Received(1).SetAsync(
            "einvoice:id:1234567890",
            Arg.Is<GibUserDto>(d => d.Identifier == "1234567890"),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNotFound_ShouldReturnNull()
    {
        _cache.GetAsync<GibUserDto>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((GibUserDto?)null);

        var handler = new GetGibUserByIdentifierQueryHandler(_dbContext, _cache, _metrics);
        var query = new GetGibUserByIdentifierQuery("0000000000", GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().BeNull();
        await _cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<GibUserDto>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithFirstCreationTime_ShouldBypassCache()
    {
        var handler = new GetGibUserByIdentifierQueryHandler(_dbContext, _cache, _metrics);
        var query = new GetGibUserByIdentifierQuery(
            "1234567890",
            GibUserDocumentType.EInvoice,
            FirstCreationTime: new DateTime(2025, 1, 1));

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        await _cache.DidNotReceive().GetAsync<GibUserDto>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<GibUserDto>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithFirstCreationTime_WhenUserCreatedAfter_ShouldReturnNull()
    {
        var handler = new GetGibUserByIdentifierQueryHandler(_dbContext, _cache, _metrics);
        var query = new GetGibUserByIdentifierQuery(
            "1234567890",
            GibUserDocumentType.EInvoice,
            FirstCreationTime: new DateTime(2023, 1, 1)); // Kullanıcı 2024'te oluşturuldu

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_EDespatchType_ShouldQueryEDespatchDbSet()
    {
        _cache.GetAsync<GibUserDto>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((GibUserDto?)null);

        var handler = new GetGibUserByIdentifierQueryHandler(_dbContext, _cache, _metrics);
        var query = new GetGibUserByIdentifierQuery("9876543210", GibUserDocumentType.EDespatch);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Identifier.Should().Be("9876543210");
        result.Title.Should().Be("TEST LOJISTIK A.S.");
    }

    [Fact]
    public async Task Handle_ShouldRecordQueryMetric()
    {
        _cache.GetAsync<GibUserDto>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((GibUserDto?)null);

        var handler = new GetGibUserByIdentifierQueryHandler(_dbContext, _cache, _metrics);
        var query = new GetGibUserByIdentifierQuery("1234567890", GibUserDocumentType.EInvoice);

        await handler.Handle(query, CancellationToken.None);

        _metrics.Received(1).RecordQuery("identifier", "einvoice", Arg.Any<double>());
    }

    [Fact]
    public async Task Handle_WhenDbThrows_ShouldRecordErrorMetricAndRethrow()
    {
        var faultyContext = Substitute.For<IGibUserListReadDbContext>();
        faultyContext.EInvoiceGibUsers.Returns(_ => throw new InvalidOperationException("DB hatası"));

        var handler = new GetGibUserByIdentifierQueryHandler(faultyContext, _cache, _metrics);
        var query = new GetGibUserByIdentifierQuery("1234567890", GibUserDocumentType.EInvoice);

        var act = () => handler.Handle(query, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        _metrics.Received(1).RecordQueryError("identifier", "einvoice");
    }
}
