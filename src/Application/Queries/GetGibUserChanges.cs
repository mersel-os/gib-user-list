using System.Diagnostics;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Queries;

/// <summary>
/// Belirli bir tarihten sonraki mükellef değişikliklerini (eklenen/güncellenen/silinen) sorgular.
/// Changelog tablosundan okur. Retention süresi dışındaki istekler için null döner (410 Gone).
/// </summary>
public sealed record GetGibUserChangesQuery(
    DateTime Since,
    GibUserDocumentType DocumentType,
    int Page = 1,
    int PageSize = 100,
    DateTime? Until = null) : IQuery<GibUserChangesResult?>;

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
            // Retention window kontrolü: en eski changelog kaydı since'den yeniyse → 410 Gone
            var oldestEntry = await dbContext.GibUserChangeLogs
                .AsNoTracking()
                .Where(c => c.DocumentType == gibDocType)
                .OrderBy(c => c.ChangedAt)
                .Select(c => c.ChangedAt)
                .FirstOrDefaultAsync(ct);

            if (oldestEntry != default && request.Since < oldestEntry)
                return null; // API katmanında 410 Gone'a çevrilir

            var now = DateTime.Now;
            var effectiveUntil = request.Until.HasValue && request.Until.Value <= now
                ? request.Until.Value
                : now;

            var baseQuery = dbContext.GibUserChangeLogs
                .AsNoTracking()
                .Where(c => c.DocumentType == gibDocType
                    && c.ChangedAt > request.Since
                    && c.ChangedAt <= effectiveUntil);

            var totalCount = await baseQuery.CountAsync(ct);

            var changes = await baseQuery
                .OrderBy(c => c.ChangedAt)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(c => new GibUserChangeDto
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
                })
                .ToListAsync(ct);

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
