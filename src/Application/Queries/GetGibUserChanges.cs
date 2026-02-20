using System.Diagnostics;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Application.Configuration;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Queries;

/// <summary>
/// Belirli bir tarihten sonraki mükellef değişikliklerini (eklenen/güncellenen/silinen) sorgular.
/// Changelog tablosundan okur. Retention süresi dışındaki istekler için null döner (410 Gone).
/// </summary>
public sealed record GetGibUserChangesQuery : IQuery<GibUserChangesResult?>
{
    public DateTime Since { get; }
    public GibUserDocumentType DocumentType { get; }
    public int Page { get; }
    public int PageSize { get; }
    public DateTime? Until { get; }

    public GetGibUserChangesQuery(
        DateTime since,
        GibUserDocumentType documentType,
        int page = 1,
        int pageSize = 100,
        DateTime? until = null)
    {
        Since = DateTime.SpecifyKind(since, DateTimeKind.Unspecified);
        DocumentType = documentType;
        Page = page;
        PageSize = pageSize;
        Until = until.HasValue ? DateTime.SpecifyKind(until.Value, DateTimeKind.Unspecified) : null;
    }
}

public sealed class GetGibUserChangesQueryValidator : AbstractValidator<GetGibUserChangesQuery>
{
    public GetGibUserChangesQueryValidator()
    {
        RuleFor(x => x.Since)
            .Must(d => d != default)
            .WithMessage("since parametresi zorunludur.");

        RuleFor(x => x.DocumentType)
            .IsInEnum()
            .WithMessage("Geçersiz belge türü.");

        RuleFor(x => x.Page)
            .GreaterThan(0)
            .WithMessage("Sayfa numarası 1 veya daha büyük olmalıdır.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 1000)
            .WithMessage("Sayfa boyutu 1 ile 1000 arasında olmalıdır.");

        RuleFor(x => x.Until)
            .Must((q, until) => !until.HasValue || until.Value > q.Since)
            .WithMessage("until parametresi since'den büyük olmalıdır.");
    }
}

public sealed class GetGibUserChangesQueryHandler(
    IGibUserListReadDbContext dbContext,
    IOptions<GibEndpointOptions> endpointOptions,
    IAppMetrics metrics)
    : IQueryHandler<GetGibUserChangesQuery, GibUserChangesResult?>
{
    private static readonly Dictionary<GibChangeType, string> ChangeTypeNames = new()
    {
        [GibChangeType.Added] = "added",
        [GibChangeType.Modified] = "modified",
        [GibChangeType.Removed] = "removed"
    };

    public async ValueTask<GibUserChangesResult?> Handle(
        GetGibUserChangesQuery request, CancellationToken ct)
    {
        using var activity = GibUserListActivitySource.Source.StartActivity("Query.GetChanges");
        activity?.SetTag("query.type", "changes");
        activity?.SetTag("query.document_type", request.DocumentType.ToString());

        var sw = Stopwatch.StartNew();
        var docType = request.DocumentType == GibUserDocumentType.EInvoice ? "einvoice" : "edespatch";
        var gibDocType = request.DocumentType == GibUserDocumentType.EInvoice
            ? GibDocumentType.EInvoice
            : GibDocumentType.EDespatch;

        try
        {
            var now = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);

            var retentionCutoff = now.AddDays(-endpointOptions.Value.ChangeRetentionDays);
            if (request.Since < retentionCutoff)
                return null;
            var effectiveUntil = request.Until.HasValue && request.Until.Value <= now
                ? request.Until.Value
                : now;

            var baseQuery = dbContext.GibUserChangeLogs
                .AsNoTracking()
                .Where(c => c.DocumentType == gibDocType
                    && c.ChangedAt > request.Since
                    && c.ChangedAt <= effectiveUntil);

            var totalCount = await baseQuery.CountAsync(ct);

            var rawChanges = await baseQuery
                .OrderBy(c => c.ChangedAt)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToListAsync(ct);

            var changes = rawChanges.Select(c => new GibUserChangeDto
            {
                Identifier = c.Identifier,
                ChangeType = ChangeTypeNames[c.ChangeType],
                ChangedAt = c.ChangedAt,
                Title = c.Title,
                AccountType = c.AccountType,
                Type = c.Type,
                FirstCreationTime = c.FirstCreationTime,
                Aliases = c.AliasesJson != null
                    ? AliasJsonHelper.ParseAliases(c.AliasesJson)
                    : null
            }).ToList();

            sw.Stop();
            metrics.RecordQuery("changes", docType, sw.Elapsed.TotalMilliseconds);

            return new GibUserChangesResult
            {
                Changes = changes,
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize,
                Until = effectiveUntil
            };
        }
        catch
        {
            metrics.RecordQueryError("changes", docType);
            throw;
        }
    }
}
