using System.Diagnostics;
using System.Globalization;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Queries;

/// <summary>
/// Ünvan veya kimlik numarasına göre mükellef arama sorgusu (büyük/küçük harf duyarsız, Türkçe destekli).
/// Arama sonuçları cache'lenmez, her zaman veritabanından gelir.
/// </summary>
public sealed record SearchGibUsersQuery(
    string Search,
    GibUserDocumentType DocumentType,
    int Page = 1,
    int PageSize = 20) : IQuery<GibUserSearchResult>;

public sealed class SearchGibUsersQueryValidator
    : AbstractValidator<SearchGibUsersQuery>
{
    private const int MaxSearchLength = 200;

    public SearchGibUsersQueryValidator()
    {
        RuleFor(x => x.Search)
            .NotEmpty()
            .WithMessage("Arama parametresi boş olamaz.")
            .MaximumLength(MaxSearchLength)
            .WithMessage($"Arama parametresi en fazla {MaxSearchLength} karakter olabilir.");

        RuleFor(x => x.DocumentType)
            .IsInEnum()
            .WithMessage("Geçersiz belge türü.");

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Sayfa numarası en az 1 olmalıdır.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("Sayfa boyutu 1 ile 100 arasında olmalıdır.");
    }
}

public sealed class SearchGibUsersQueryHandler(
    IGibUserListReadDbContext dbContext,
    IAppMetrics metrics)
    : IQueryHandler<SearchGibUsersQuery, GibUserSearchResult>
{
    private static readonly CultureInfo TurkishCulture = new("tr-TR", false);

    public async ValueTask<GibUserSearchResult> Handle(
        SearchGibUsersQuery request, CancellationToken ct)
    {
        using var activity = GibUserListActivitySource.Source.StartActivity("Query.SearchGibUsers");
        activity?.SetTag("query.type", "search");
        activity?.SetTag("query.document_type", request.DocumentType.ToString());

        var sw = Stopwatch.StartNew();
        var docType = request.DocumentType == GibUserDocumentType.EInvoice ? "einvoice" : "edespatch";

        try
        {
            var dbSet = request.DocumentType == GibUserDocumentType.EInvoice
                ? (IQueryable<GibUser>)dbContext.EInvoiceGibUsers
                : dbContext.EDespatchGibUsers;

            var searchLower = request.Search.ToLower(TurkishCulture);

            var query = dbSet.AsNoTracking()
                .Where(u => u.TitleLower.Contains(searchLower) || u.Identifier.Contains(request.Search));

            var totalCount = await query.CountAsync(ct);

            var items = await query
                .OrderBy(u => u.TitleLower)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
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

            sw.Stop();
            metrics.RecordQuery("search", docType, sw.Elapsed.TotalMilliseconds);

            return new GibUserSearchResult
            {
                Items = items,
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize
            };
        }
        catch
        {
            metrics.RecordQueryError("search", docType);
            throw;
        }
    }
}
