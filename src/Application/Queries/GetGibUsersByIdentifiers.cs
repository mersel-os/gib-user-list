using System.Diagnostics;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Queries;

/// <summary>
/// Birden fazla VKN/TCKN kimlik numarası ile toplu mükellef sorgulama.
/// En fazla 100 kayıt sorgulanabilir. Sonuçlar cache'lenmez.
/// </summary>
public sealed record GetGibUsersByIdentifiersQuery(
    IReadOnlyList<string> Identifiers,
    GibUserDocumentType DocumentType) : IQuery<GibUserBatchResult>;

public sealed class GetGibUsersByIdentifiersQueryValidator
    : AbstractValidator<GetGibUsersByIdentifiersQuery>
{
    private const int MaxBatchSize = 100;

    public GetGibUsersByIdentifiersQueryValidator()
    {
        RuleFor(x => x.Identifiers)
            .NotEmpty()
            .WithMessage("Kimlik numarası listesi boş olamaz.")
            .Must(ids => ids.Count <= MaxBatchSize)
            .WithMessage($"Tek seferde en fazla {MaxBatchSize} kimlik numarası sorgulanabilir.");

        RuleForEach(x => x.Identifiers)
            .NotEmpty()
            .WithMessage("Kimlik numarası boş olamaz.")
            .Matches(@"^\d{10,11}$")
            .WithMessage("Her kimlik numarası (VKN/TCKN) tam olarak 10 veya 11 haneli rakam olmalıdır.");

        RuleFor(x => x.DocumentType)
            .IsInEnum()
            .WithMessage("Geçersiz belge türü.");
    }
}

public sealed class GetGibUsersByIdentifiersQueryHandler(
    IGibUserListReadDbContext dbContext,
    IAppMetrics metrics)
    : IQueryHandler<GetGibUsersByIdentifiersQuery, GibUserBatchResult>
{
    public async ValueTask<GibUserBatchResult> Handle(
        GetGibUsersByIdentifiersQuery request, CancellationToken ct)
    {
        using var activity = GibUserListActivitySource.Source.StartActivity("Query.BatchGetByIdentifiers");
        activity?.SetTag("query.type", "batch");
        activity?.SetTag("query.document_type", request.DocumentType.ToString());
        activity?.SetTag("query.batch_size", request.Identifiers.Count);

        var sw = Stopwatch.StartNew();
        var docType = request.DocumentType == GibUserDocumentType.EInvoice ? "einvoice" : "edespatch";

        try
        {
            var identifiers = request.Identifiers.Distinct().ToList();

            var dbSet = request.DocumentType == GibUserDocumentType.EInvoice
                ? (IQueryable<GibUser>)dbContext.EInvoiceGibUsers
                : dbContext.EDespatchGibUsers;

            var users = await dbSet.AsNoTracking()
                .Where(u => identifiers.Contains(u.Identifier))
                .Select(u => new GibUserDto
                {
                    Identifier = u.Identifier,
                    Title = u.Title,
                    AccountType = u.AccountType,
                    Type = u.Type,
                    FirstCreationTime = u.FirstCreationTime,
                    Aliases = AliasJsonHelper.ParseAliases(u.AliasesJson)
                })
                .ToListAsync(ct);

            var foundIdentifiers = users.Select(u => u.Identifier).ToHashSet();
            var notFound = identifiers.Where(id => !foundIdentifiers.Contains(id)).ToList();

            sw.Stop();
            metrics.RecordQuery("batch", docType, sw.Elapsed.TotalMilliseconds);

            return new GibUserBatchResult
            {
                Items = users,
                NotFound = notFound,
                TotalRequested = identifiers.Count,
                TotalFound = users.Count
            };
        }
        catch
        {
            metrics.RecordQueryError("batch", docType);
            throw;
        }
    }
}
