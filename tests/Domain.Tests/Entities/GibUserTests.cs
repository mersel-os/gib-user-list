using FluentAssertions;
using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Domain.Tests.Entities;

public class GibUserTests
{
    [Fact]
    public void EInvoiceGibUser_ShouldInheritFromGibUser()
    {
        var user = new EInvoiceGibUser();
        user.Should().BeAssignableTo<GibUser>();
    }

    [Fact]
    public void EDespatchGibUser_ShouldInheritFromGibUser()
    {
        var user = new EDespatchGibUser();
        user.Should().BeAssignableTo<GibUser>();
    }

    [Fact]
    public void GibUser_ShouldStoreAllProperties()
    {
        var id = Guid.NewGuid();
        var now = DateTime.Now;

        var user = new EInvoiceGibUser
        {
            Id = id,
            Identifier = "1234567890",
            Title = "MERSEL YAZILIM A.S.",
            TitleLower = "mersel yazilim a.s.",
            AccountType = "Ozel",
            Type = "Tuzel",
            FirstCreationTime = now.AddYears(-2),
            AliasesJson = "[{\"Alias\":\"urn:mail:defaultpk\",\"Type\":\"PK\"}]"
        };

        user.Id.Should().Be(id);
        user.Identifier.Should().Be("1234567890");
        user.Title.Should().Be("MERSEL YAZILIM A.S.");
        user.TitleLower.Should().Be("mersel yazilim a.s.");
        user.AccountType.Should().Be("Ozel");
        user.Type.Should().Be("Tuzel");
        user.FirstCreationTime.Should().BeBefore(now);
        user.AliasesJson.Should().Contain("defaultpk");
    }
}
