using System.Xml.Serialization;

namespace MERSEL.Services.GibUserList.Application.Models;

/// <summary>
/// GIB User öğesi için XML deserializasyon modeli.
/// </summary>
[XmlRoot("User")]
public sealed class GibXmlUser
{
    [XmlElement("Identifier")]
    public string Identifier { get; set; } = default!;

    [XmlElement("Title")]
    public string Title { get; set; } = default!;

    [XmlElement("Type")]
    public string? Type { get; set; }

    [XmlElement("AccountType")]
    public string? AccountType { get; set; }

    [XmlElement("FirstCreationTime")]
    public DateTime FirstCreationTime { get; set; }

    [XmlElement("Documents")]
    public GibXmlDocuments? Documents { get; set; }
}

/// <summary>
/// Documents öğesi için XML deserializasyon modeli.
/// </summary>
public sealed class GibXmlDocuments
{
    [XmlElement("Document", Type = typeof(GibXmlDocument))]
    public List<GibXmlDocument>? Document { get; set; }
}

/// <summary>
/// Document öğesi için XML deserializasyon modeli.
/// </summary>
public sealed class GibXmlDocument
{
    [XmlAttribute("type")]
    public string Type { get; set; } = default!;

    [XmlElement("Alias", Type = typeof(GibXmlAlias))]
    public List<GibXmlAlias>? Aliases { get; set; }
}

/// <summary>
/// Alias öğesi için XML deserializasyon modeli.
/// </summary>
public sealed class GibXmlAlias
{
    [XmlElement("Name", Type = typeof(string))]
    public List<string>? Names { get; set; }

    [XmlElement("CreationTime")]
    public DateTime CreationTime { get; set; }

    [XmlElement("DeletionTime")]
    public DateTime? DeletionTime { get; set; }

    [XmlElement("AliasType")]
    public string? AliasType { get; set; }
}
