using FluentAssertions;
using MERSEL.Services.GibUserList.Domain.ValueObjects;

namespace MERSEL.Services.GibUserList.Domain.Tests.ValueObjects;

public class GibUserAliasTests
{
    [Fact]
    public void TwoAliases_WithSameValues_ShouldBeEqual()
    {
        var creationTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var alias1 = new GibUserAlias
        {
            Name = "urn:mail:defaultpk",
            Type = "PK",
            CreationTime = creationTime
        };

        var alias2 = new GibUserAlias
        {
            Name = "urn:mail:defaultpk",
            Type = "PK",
            CreationTime = creationTime
        };

        alias1.Should().Be(alias2);
    }

    [Fact]
    public void TwoAliases_WithDifferentNames_ShouldNotBeEqual()
    {
        var alias1 = new GibUserAlias
        {
            Name = "urn:mail:defaultpk",
            Type = "PK",
            CreationTime = DateTime.UtcNow
        };

        var alias2 = new GibUserAlias
        {
            Name = "urn:mail:otherpk",
            Type = "PK",
            CreationTime = DateTime.UtcNow
        };

        alias1.Should().NotBe(alias2);
    }
}
