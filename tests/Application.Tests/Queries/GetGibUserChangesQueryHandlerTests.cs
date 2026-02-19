using FluentAssertions;
using NSubstitute;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Application.Queries;
using MERSEL.Services.GibUserList.Application.Tests.Helpers;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Tests.Queries;

public class GetGibUserChangesQueryHandlerTests : IDisposable
{
    private readonly IAppMetrics _metrics = Substitute.For<IAppMetrics>();
    private readonly TestGibUserListDbContext _dbContext;

    // Retention sınırı temsili — oldest entry. since değeri bundan eski olursa 410 döner.
    private static readonly GibUserChangeLog RetentionBoundaryEntry = new()
    {
        Id = Guid.NewGuid(),
        DocumentType = GibDocumentType.EInvoice,
        Identifier = "0000000001",
        ChangeType = GibChangeType.Added,
        ChangedAt = new DateTime(2026, 2, 17, 3, 0, 0),
        Title = "ESKI KAYIT"
    };

    private static readonly GibUserChangeLog AddedEntry = new()
    {
        Id = Guid.NewGuid(),
        DocumentType = GibDocumentType.EInvoice,
        Identifier = "1234567890",
        ChangeType = GibChangeType.Added,
        ChangedAt = new DateTime(2026, 2, 18, 3, 0, 0),
        Title = "YENI FIRMA A.S.",
        AccountType = "Ozel",
        Type = "Kagit",
        FirstCreationTime = new DateTime(2026, 2, 15),
        AliasesJson = """[{"Alias": "urn:mail:pk1", "Type": "PK", "CreationTime": "2026-02-15T00:00:00"}]"""
    };

    private static readonly GibUserChangeLog ModifiedEntry = new()
    {
        Id = Guid.NewGuid(),
        DocumentType = GibDocumentType.EInvoice,
        Identifier = "9876543210",
        ChangeType = GibChangeType.Modified,
        ChangedAt = new DateTime(2026, 2, 18, 3, 0, 1),
        Title = "MEVCUT FIRMA LTD.",
        AccountType = "Ozel",
        Type = "Kagit"
    };

    private static readonly GibUserChangeLog RemovedEntry = new()
    {
        Id = Guid.NewGuid(),
        DocumentType = GibDocumentType.EInvoice,
        Identifier = "5555555555",
        ChangeType = GibChangeType.Removed,
        ChangedAt = new DateTime(2026, 2, 18, 3, 0, 2)
    };

    private static readonly GibUserChangeLog EDespatchEntry = new()
    {
        Id = Guid.NewGuid(),
        DocumentType = GibDocumentType.EDespatch,
        Identifier = "1111111111",
        ChangeType = GibChangeType.Added,
        ChangedAt = new DateTime(2026, 2, 18, 3, 0, 0),
        Title = "IRSALIYE FIRMA"
    };

    // EDespatch retention sınırı
    private static readonly GibUserChangeLog EDespatchRetentionEntry = new()
    {
        Id = Guid.NewGuid(),
        DocumentType = GibDocumentType.EDespatch,
        Identifier = "0000000002",
        ChangeType = GibChangeType.Added,
        ChangedAt = new DateTime(2026, 2, 17, 3, 0, 0),
        Title = "ESKI IRSALIYE"
    };

    public GetGibUserChangesQueryHandlerTests()
    {
        _dbContext = TestGibUserListDbContext.CreateWithChangeLogs(
            [RetentionBoundaryEntry, AddedEntry, ModifiedEntry, RemovedEntry,
             EDespatchRetentionEntry, EDespatchEntry]);
    }

    [Fact]
    public async Task Handle_WhenChangesExist_ShouldReturnPaginatedResult()
    {
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _metrics);
        // since = retention boundary → passes retention check, returns entries with ChangedAt > since
        var query = new GetGibUserChangesQuery(
            new DateTime(2026, 2, 17, 3, 0, 0), GibUserDocumentType.EInvoice);

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
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _metrics);
        var query = new GetGibUserChangesQuery(
            new DateTime(2026, 2, 19), GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(0);
        result.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldFilterByDocumentType()
    {
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _metrics);
        var query = new GetGibUserChangesQuery(
            new DateTime(2026, 2, 17, 3, 0, 0), GibUserDocumentType.EDespatch);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(1);
        result.Changes[0].Identifier.Should().Be("1111111111");
    }

    [Fact]
    public async Task Handle_WhenSinceBeforeOldestEntry_ShouldReturnNull()
    {
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _metrics);
        var query = new GetGibUserChangesQuery(
            new DateTime(2026, 1, 1), GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldRespectPagination()
    {
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _metrics);
        var query = new GetGibUserChangesQuery(
            new DateTime(2026, 2, 17, 3, 0, 0), GibUserDocumentType.EInvoice, Page: 1, PageSize: 2);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(3);
        result.Changes.Should().HaveCount(2);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task Handle_RemovedEntry_ShouldHaveNullAliases()
    {
        var handler = new GetGibUserChangesQueryHandler(_dbContext, _metrics);
        var query = new GetGibUserChangesQuery(
            new DateTime(2026, 2, 17, 3, 0, 0), GibUserDocumentType.EInvoice);

        var result = await handler.Handle(query, CancellationToken.None);

        var removed = result!.Changes.Single(c => c.ChangeType == "removed");
        removed.Title.Should().BeNull();
        removed.Aliases.Should().BeNull();
    }

    public void Dispose() => _dbContext.Dispose();
}
