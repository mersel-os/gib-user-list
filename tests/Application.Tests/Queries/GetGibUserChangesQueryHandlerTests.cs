using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using MERSEL.Services.GibUserList.Application.Configuration;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Application.Queries;
using MERSEL.Services.GibUserList.Application.Tests.Helpers;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Tests.Queries;

public class GetGibUserChangesQueryHandlerTests : IDisposable
{
    private readonly IAppMetrics _metrics = Substitute.For<IAppMetrics>();
    private readonly IOptions<GibEndpointOptions> _endpointOptions;
    private readonly TestGibUserListDbContext _dbContext;

    private static DateTime Now => DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);

    private readonly GibUserChangeLog _olderEntry;
    private readonly GibUserChangeLog _addedEntry;
    private readonly GibUserChangeLog _modifiedEntry;
    private readonly GibUserChangeLog _removedEntry;
    private readonly GibUserChangeLog _eDespatchOlderEntry;
    private readonly GibUserChangeLog _eDespatchEntry;

    public GetGibUserChangesQueryHandlerTests()
    {
        _endpointOptions = Options.Create(new GibEndpointOptions { ChangeRetentionDays = 30 });

        _olderEntry = new GibUserChangeLog
        {
            Id = Guid.NewGuid(),
            DocumentType = GibDocumentType.EInvoice,
            Identifier = "0000000001",
            ChangeType = GibChangeType.Added,
            ChangedAt = Now.AddHours(-24),
            Title = "ESKI KAYIT"
        };

        _addedEntry = new GibUserChangeLog
        {
            Id = Guid.NewGuid(),
            DocumentType = GibDocumentType.EInvoice,
            Identifier = "1234567890",
            ChangeType = GibChangeType.Added,
            ChangedAt = Now.AddHours(-1),
            Title = "YENI FIRMA A.S.",
            AccountType = "Ozel",
            Type = "Kagit",
            FirstCreationTime = Now.AddDays(-5),
            AliasesJson = """[{"Alias": "urn:mail:pk1", "Type": "PK", "CreationTime": "2026-02-15T00:00:00"}]"""
        };

        _modifiedEntry = new GibUserChangeLog
        {
            Id = Guid.NewGuid(),
            DocumentType = GibDocumentType.EInvoice,
            Identifier = "9876543210",
            ChangeType = GibChangeType.Modified,
            ChangedAt = Now.AddMinutes(-59),
            Title = "MEVCUT FIRMA LTD.",
            AccountType = "Ozel",
            Type = "Kagit"
        };

        _removedEntry = new GibUserChangeLog
        {
            Id = Guid.NewGuid(),
            DocumentType = GibDocumentType.EInvoice,
            Identifier = "5555555555",
            ChangeType = GibChangeType.Removed,
            ChangedAt = Now.AddMinutes(-58)
        };

        _eDespatchOlderEntry = new GibUserChangeLog
        {
            Id = Guid.NewGuid(),
            DocumentType = GibDocumentType.EDespatch,
            Identifier = "0000000002",
            ChangeType = GibChangeType.Added,
            ChangedAt = Now.AddHours(-24),
            Title = "ESKI IRSALIYE"
        };

        _eDespatchEntry = new GibUserChangeLog
        {
            Id = Guid.NewGuid(),
            DocumentType = GibDocumentType.EDespatch,
            Identifier = "1111111111",
            ChangeType = GibChangeType.Added,
            ChangedAt = Now.AddHours(-1),
            Title = "IRSALIYE FIRMA"
        };

        _dbContext = TestGibUserListDbContext.CreateWithChangeLogs(
            [_olderEntry, _addedEntry, _modifiedEntry, _removedEntry,
             _eDespatchOlderEntry, _eDespatchEntry]);
    }

    [Fact]
    public async Task Handle_WhenChangesExist_ShouldReturnPaginatedResult()
    {
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _endpointOptions, _metrics);
        var query = new GetGibUserChangesQuery(Now.AddHours(-24), GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(3);
        result.Changes.Should().HaveCount(3);
        result.Changes[0].Identifier.Should().Be("1234567890");
        result.Changes[0].ChangeType.Should().Be("added");
        result.Changes[1].ChangeType.Should().Be("modified");
        result.Changes[2].ChangeType.Should().Be("removed");
        result.Changes[2].Title.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenNoChanges_ShouldReturnEmptyResult()
    {
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _endpointOptions, _metrics);
        var query = new GetGibUserChangesQuery(Now.AddMinutes(10), GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(0);
        result.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldFilterByDocumentType()
    {
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _endpointOptions, _metrics);
        var query = new GetGibUserChangesQuery(Now.AddHours(-24), GibUserDocumentType.EDespatch);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(1);
        result.Changes[0].Identifier.Should().Be("1111111111");
    }

    [Fact]
    public async Task Handle_WhenSinceOutsideRetentionWindow_ShouldReturnNull()
    {
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _endpointOptions, _metrics);
        var query = new GetGibUserChangesQuery(Now.AddDays(-60), GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenSinceWithinRetentionWindow_ShouldReturnResults()
    {
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _endpointOptions, _metrics);
        var query = new GetGibUserChangesQuery(Now.AddDays(-5), GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.TotalCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Handle_WhenChangelogEmpty_ShouldReturnEmptyNotNull()
    {
        using var emptyDb = TestGibUserListDbContext.CreateWithChangeLogs([]);
        var handler = new GetGibUserChangesQueryHandler(emptyDb, _endpointOptions, _metrics);
        var query = new GetGibUserChangesQuery(Now.AddHours(-2), GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(0);
        result.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldRespectPagination()
    {
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _endpointOptions, _metrics);
        var query = new GetGibUserChangesQuery(Now.AddHours(-24), GibUserDocumentType.EInvoice, page: 1, pageSize: 2);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(3);
        result.Changes.Should().HaveCount(2);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task Handle_RemovedEntry_ShouldHaveNullAliases()
    {
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _endpointOptions, _metrics);
        var query = new GetGibUserChangesQuery(Now.AddHours(-24), GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        var removed = result!.Changes.Single(c => c.ChangeType == "removed");
        removed.Title.Should().BeNull();
        removed.Aliases.Should().BeNull();
    }

    public void Dispose() => _dbContext.Dispose();
}
