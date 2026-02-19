using FluentAssertions;
using MERSEL.Services.GibUserList.Application.Queries;

namespace MERSEL.Services.GibUserList.Application.Tests.Queries;

public class AliasJsonHelperTests
{
    [Fact]
    public void ParseAliases_WithValidJson_ShouldReturnAliases()
    {
        var json = """
        [
            {"alias": "urn:mail:defaultpk", "type": "PK", "creationTime": "2024-01-15T10:00:00"},
            {"alias": "urn:mail:secondpk", "type": "GB", "creationTime": "2024-06-20T14:30:00"}
        ]
        """;

        var result = AliasJsonHelper.ParseAliases(json);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("urn:mail:defaultpk");
        result[0].Type.Should().Be("PK");
        result[1].Name.Should().Be("urn:mail:secondpk");
        result[1].Type.Should().Be("GB");
    }

    [Fact]
    public void ParseAliases_WithNullInput_ShouldReturnEmpty()
    {
        var result = AliasJsonHelper.ParseAliases(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseAliases_WithEmptyString_ShouldReturnEmpty()
    {
        var result = AliasJsonHelper.ParseAliases(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseAliases_WithMalformedJson_ShouldReturnEmpty()
    {
        var result = AliasJsonHelper.ParseAliases("{invalid json!!!}");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseAliases_WithEmptyArray_ShouldReturnEmpty()
    {
        var result = AliasJsonHelper.ParseAliases("[]");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseAliases_WithMissingProperties_ShouldUseDefaults()
    {
        var json = """[{"creationTime": "2024-01-01T00:00:00"}]""";

        var result = AliasJsonHelper.ParseAliases(json);

        result.Should().HaveCount(1);
        result[0].Name.Should().BeEmpty();
        result[0].Type.Should().BeEmpty();
    }

    [Fact]
    public void ParseAliases_ShouldBeCaseInsensitive()
    {
        var json = """[{"Alias": "urn:mail:test", "Type": "PK", "CreationTime": "2024-01-01T00:00:00"}]""";

        var result = AliasJsonHelper.ParseAliases(json);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("urn:mail:test");
        result[0].Type.Should().Be("PK");
    }

    [Fact]
    public void ParseAliases_WithNullAliasInArray_ShouldReturnEmptyString()
    {
        var json = """[{"alias": null, "type": null, "creationTime": "2024-01-01T00:00:00"}]""";

        var result = AliasJsonHelper.ParseAliases(json);

        result.Should().HaveCount(1);
        result[0].Name.Should().BeEmpty();
        result[0].Type.Should().BeEmpty();
    }
}
