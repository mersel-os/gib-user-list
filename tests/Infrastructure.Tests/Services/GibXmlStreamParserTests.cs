using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MERSEL.Services.GibUserList.Infrastructure.Services;

namespace MERSEL.Services.GibUserList.Infrastructure.Tests.Services;

public class GibXmlStreamParserTests : IDisposable
{
    private readonly GibXmlStreamParser _sut;
    private readonly string _tempDir;

    public GibXmlStreamParserTests()
    {
        _sut = new GibXmlStreamParser(NullLogger<GibXmlStreamParser>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), $"gib_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ParseUsers_WithValidXml_ShouldParseAllUsers()
    {
        var xmlPath = CreateTempXml("""
            <?xml version="1.0" encoding="UTF-8"?>
            <UserList>
                <User>
                    <Identifier>1111111111</Identifier>
                    <Title>FIRST COMPANY</Title>
                    <FirstCreationTime>2022-01-01T00:00:00</FirstCreationTime>
                </User>
                <User>
                    <Identifier>2222222222</Identifier>
                    <Title>SECOND COMPANY</Title>
                    <FirstCreationTime>2023-06-15T00:00:00</FirstCreationTime>
                </User>
            </UserList>
            """);

        var users = _sut.ParseUsers(xmlPath).ToList();

        users.Should().HaveCount(2);
        users[0].Identifier.Should().Be("1111111111");
        users[0].Title.Should().Be("FIRST COMPANY");
        users[1].Identifier.Should().Be("2222222222");
    }

    [Fact]
    public void ParseUsers_WithDocuments_ShouldParseDocumentsAndAliases()
    {
        var xmlPath = CreateTempXml("""
            <?xml version="1.0" encoding="UTF-8"?>
            <UserList>
                <User>
                    <Identifier>3333333333</Identifier>
                    <Title>WITH DOCS</Title>
                    <FirstCreationTime>2022-01-01T00:00:00</FirstCreationTime>
                    <Documents>
                        <Document type="Invoice">
                            <Alias>
                                <Name>urn:mail:default</Name>
                                <CreationTime>2022-01-01T00:00:00</CreationTime>
                            </Alias>
                        </Document>
                    </Documents>
                </User>
            </UserList>
            """);

        var users = _sut.ParseUsers(xmlPath).ToList();

        users.Should().HaveCount(1);
        users[0].Documents.Should().NotBeNull();
        users[0].Documents!.Document.Should().NotBeNull().And.HaveCount(1);

        var doc = users[0].Documents!.Document![0];
        doc.Type.Should().Be("Invoice");
        doc.Aliases.Should().NotBeNull().And.HaveCount(1);
        doc.Aliases![0].Names.Should().NotBeNull().And.Contain("urn:mail:default");
    }

    [Fact]
    public void ParseUsers_WithEmptyFile_ShouldReturnEmpty()
    {
        var xmlPath = CreateTempXml("""
            <?xml version="1.0" encoding="UTF-8"?>
            <UserList></UserList>
            """);

        var users = _sut.ParseUsers(xmlPath).ToList();

        users.Should().BeEmpty();
    }

    private string CreateTempXml(string content)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
