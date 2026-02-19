using System.Diagnostics;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using MERSEL.Services.GibUserList.Application.Interfaces;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Application.Queries;

/// <summary>
/// VKN/TCKN kimlik numarasına göre mükellef bulma sorgusu.
/// Doğrulanmış identifier sorguları cache'lenir.
/// Opsiyonel firstCreationTime ile belirtilen tarihten önce kayıtlı olma kontrolü yapılabilir.
/// </summary>
public sealed record GetGibUserByIdentifierQuery(
    string Identifier,
    GibUserDocumentType DocumentType,
    DateTime? FirstCreationTime = null) : IQuery<GibUserDto?>;

public sealed class GetGibUserByIdentifierQueryValidator
    : AbstractValidator<GetGibUserByIdentifierQuery>
{
    public GetGibUserByIdentifierQueryValidator()
    {
        RuleFor(x => x.Identifier)
            .NotEmpty()
            .WithMessage("Kimlik numarası (VKN/TCKN) boş olamaz.")
            .Matches(@"^\d{10,11}$")
            .WithMessage("Kimlik numarası (VKN/TCKN) tam olarak 10 veya 11 haneli rakam olmalıdır.");

        RuleFor(x => x.DocumentType)
            .IsInEnum()
            .WithMessage("Geçersiz belge türü.");
    }
}

public sealed class GetGibUserByIdentifierQueryHandler(
    IGibUserListReadDbContext dbContext,
    ICacheService cache,
    IAppMetrics metrics)
    : IQueryHandler<GetGibUserByIdentifierQuery, GibUserDto?>
{
    // Redis modunda Worker sync sonrası değişen key'leri hedefli siler → 1 saat TTL güvenli.
    // Memory modunda Worker cache'e erişemez → TTL tabanlı doğal expiry ile yenilenir.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(60);

    public async ValueTask<GibUserDto?> Handle(
        GetGibUserByIdentifierQuery request, CancellationToken ct)
    {
        using var activity = GibUserListActivitySource.Source.StartActivity("Query.GetByIdentifier");
        activity?.SetTag("query.type", "identifier");
        activity?.SetTag("query.document_type", request.DocumentType.ToString());

        var sw = Stopwatch.StartNew();
        var docType = request.DocumentType == GibUserDocumentType.EInvoice ? "einvoice" : "edespatch";

        try
        {
            // firstCreationTime filtresi varsa cache atlanır — tarih bazlı sorgu her zaman DB'den gelir
            var useCache = request.FirstCreationTime is null;
            var cacheKey = $"{docType}:id:{request.Identifier}";

            if (useCache)
            {
                var cached = await cache.GetAsync<GibUserDto>(cacheKey, ct);
                if (cached is not null)
                {
                    metrics.RecordCacheHit();
                    sw.Stop();
                    metrics.RecordQuery("identifier", docType, sw.Elapsed.TotalMilliseconds);
                    return cached;
                }

                metrics.RecordCacheMiss();
            }

            var dbSet = request.DocumentType == GibUserDocumentType.EInvoice
                ? (IQueryable<GibUser>)dbContext.EInvoiceGibUsers
                : dbContext.EDespatchGibUsers;

            var query = dbSet.AsNoTracking()
                .Where(u => u.Identifier == request.Identifier);

            if (request.FirstCreationTime.HasValue)
                query = query.Where(u => u.FirstCreationTime <= request.FirstCreationTime.Value);

            var user = await query.FirstOrDefaultAsync(ct);

            if (user is null)
            {
                sw.Stop();
                metrics.RecordQuery("identifier", docType, sw.Elapsed.TotalMilliseconds);
                return null;
            }

            var dto = MapToDto(user);

            if (useCache)
                await cache.SetAsync(cacheKey, dto, CacheTtl, ct);

            sw.Stop();
            metrics.RecordQuery("identifier", docType, sw.Elapsed.TotalMilliseconds);
            return dto;
        }
        catch
        {
            metrics.RecordQueryError("identifier", docType);
            throw;
        }
    }

    private static GibUserDto MapToDto(GibUser user) => new()
    {
        Identifier = user.Identifier,
        Title = user.Title,
        AccountType = user.AccountType,
        Type = user.Type,
        FirstCreationTime = user.FirstCreationTime,
        Aliases = AliasJsonHelper.ParseAliases(user.AliasesJson)
    };
}
