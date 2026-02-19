namespace MERSEL.Services.GibUserList.Domain.Entities;

/// <summary>
/// GİB'e kayıtlı e-Fatura mükellefi.
/// Okumalar için mv_e_invoice_gib_users materialized view'e eşlenir.
/// </summary>
public sealed class EInvoiceGibUser : GibUser;
