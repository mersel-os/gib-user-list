using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Infrastructure.Data.Configurations;

/// <summary>
/// EInvoiceGibUser varlığı için yapılandırma.
/// mv_e_invoice_gib_users materialized view'ına eşlenir.
/// </summary>
public sealed class EInvoiceGibUserConfiguration : GibUserConfigurationBase<EInvoiceGibUser>
{
    protected override string ViewName => "mv_e_invoice_gib_users";
}
