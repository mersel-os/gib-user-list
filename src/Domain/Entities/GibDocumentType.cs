namespace MERSEL.Services.GibUserList.Domain.Entities;

/// <summary>
/// GIB belge türü — changelog tablosunda document_type kolonu olarak kullanılır.
/// </summary>
public enum GibDocumentType : short
{
    EInvoice = 1,
    EDespatch = 2
}
