using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MERSEL.Services.GibUserList.Client.Interfaces;

namespace MERSEL.Services.GibUserList.Client.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddGibUserListClient_ShouldRegisterClient()
    {
        var services = new ServiceCollection();

        services.AddGibUserListClient(options =>
        {
            options.BaseUrl = "http://localhost:8080";
        });

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IGibUserListClient>();

        client.Should().NotBeNull();
        client.Should().BeOfType<GibUserListClient>();
    }

    [Fact]
    public void AddGibUserListClient_WithHmac_ShouldRegisterClient()
    {
        var services = new ServiceCollection();

        services.AddGibUserListClient(options =>
        {
            options.BaseUrl = "http://localhost:8080";
            options.AccessKey = "test-access-key";
            options.SecretKey = "test-secret-key";
            options.Timeout = TimeSpan.FromSeconds(10);
        });

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IGibUserListClient>();

        client.Should().NotBeNull();
    }
}
