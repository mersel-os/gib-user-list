using System.Xml;
using System.Xml.Serialization;
using FluentAssertions;
using MERSEL.Services.GibUserList.Application.Models;

namespace MERSEL.Services.GibUserList.Application.Tests.Models;

public class GibXmlModelsTests
{
    private const string SampleUserXml = """
        <User>
            <Identifier>1234567890</Identifier>
            <Title>MERSEL YAZILIM A.S.</Title>
            <Type>Tuzel</Type>
            <AccountType>Ozel</AccountType>
            <FirstCreationTime>2022-06-15T00:00:00</FirstCreationTime>
            <Documents>
                <Document type="Invoice">
                    <Alias>
                        <Name>urn:mail:defaultpk</Name>
                        <Name>urn:mail:mersel</Name>
                        <CreationTime>2022-06-15T10:30:00</CreationTime>
                    </Alias>
                </Document>
                <Document type="DespatchAdvice">
                    <Alias>
                        <Name>urn:mail:defaultpk</Name>
                        <CreationTime>2023-01-10T08:00:00</CreationTime>
                    </Alias>
                </Document>
            </Documents>
        </User>
        """;

    [Fact]
    public void Deserialize_ShouldParseIdentifierAndTitle()
    {
        var user = DeserializeUser(SampleUserXml);

        user.Should().NotBeNull();
        user!.Identifier.Should().Be("1234567890");
        user.Title.Should().Be("MERSEL YAZILIM A.S.");
        user.Type.Should().Be("Tuzel");
        user.AccountType.Should().Be("Ozel");
    }

    [Fact]
    public void Deserialize_ShouldParseFirstCreationTime()
    {
        var user = DeserializeUser(SampleUserXml);

        user!.FirstCreationTime.Should().Be(new DateTime(2022, 6, 15));
    }

    [Fact]
    public void Deserialize_ShouldParseDocuments()
    {
        var user = DeserializeUser(SampleUserXml);

        user!.Documents.Should().NotBeNull();
        user.Documents!.Document.Should().HaveCount(2);
        user.Documents.Document![0].Type.Should().Be("Invoice");
        user.Documents.Document[1].Type.Should().Be("DespatchAdvice");
    }

    [Fact]
    public void Deserialize_ShouldParseAliases()
    {
        var user = DeserializeUser(SampleUserXml);

        var invoiceDoc = user!.Documents!.Document![0];
        invoiceDoc.Aliases.Should().NotBeNull().And.HaveCount(1);
        invoiceDoc.Aliases![0].Names.Should().NotBeNull().And.HaveCount(2);
        invoiceDoc.Aliases[0].Names!.Should().Contain("urn:mail:defaultpk");
        invoiceDoc.Aliases[0].Names.Should().Contain("urn:mail:mersel");
    }

    [Fact]
    public void Deserialize_ShouldHandleDeletedAlias()
    {
        var xml = """
            <User>
                <Identifier>9999999999</Identifier>
                <Title>TEST</Title>
                <FirstCreationTime>2024-01-01T00:00:00</FirstCreationTime>
                <Documents>
                    <Document type="Invoice">
                        <Alias>
                            <Name>urn:mail:deleted</Name>
                            <CreationTime>2024-01-01T00:00:00</CreationTime>
                            <DeletionTime>2024-06-01T00:00:00</DeletionTime>
                        </Alias>
                    </Document>
                </Documents>
            </User>
            """;

        var user = DeserializeUser(xml);

        var alias = user!.Documents!.Document![0].Aliases![0];
        alias.DeletionTime.Should().NotBeNull();
        alias.DeletionTime!.Value.Year.Should().Be(2024);
    }

    [Fact]
    public void Deserialize_ShouldHandleNoDocuments()
    {
        var xml = """
            <User>
                <Identifier>1111111111</Identifier>
                <Title>MINIMAL USER</Title>
                <FirstCreationTime>2024-01-01T00:00:00</FirstCreationTime>
            </User>
            """;

        var user = DeserializeUser(xml);

        user!.Identifier.Should().Be("1111111111");
        user.Documents.Should().BeNull();
    }

    private static GibXmlUser? DeserializeUser(string xml)
    {
        var serializer = new XmlSerializer(typeof(GibXmlUser));
        using var reader = new StringReader(xml);
        return (GibXmlUser?)serializer.Deserialize(reader);
    }
}
