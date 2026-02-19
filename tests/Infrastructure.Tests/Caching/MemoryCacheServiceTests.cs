using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Infrastructure.Caching;

namespace MERSEL.Services.GibUserList.Infrastructure.Tests.Caching;

public class MemoryCacheServiceTests
{
    private readonly MemoryCacheService _sut;

    public MemoryCacheServiceTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new CachingOptions { DefaultTtlMinutes = 60 });
        _sut = new MemoryCacheService(memoryCache, options);
    }

    [Fact]
    public async Task GetAsync_WhenNotCached_ShouldReturnNull()
    {
        var result = await _sut.GetAsync<TestDto>("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAndGet_ShouldReturnCachedValue()
    {
        var dto = new TestDto { Name = "Test" };

        await _sut.SetAsync("key1", dto);
        var result = await _sut.GetAsync<TestDto>("key1");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
    }

    [Fact]
    public async Task RemoveAsync_ShouldRemoveCachedValue()
    {
        var dto = new TestDto { Name = "Test" };
        await _sut.SetAsync("key2", dto);

        await _sut.RemoveAsync("key2");
        var result = await _sut.GetAsync<TestDto>("key2");

        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_ShouldRemoveMatchingKeys()
    {
        await _sut.SetAsync("einvoice:id:111", new TestDto { Name = "A" });
        await _sut.SetAsync("einvoice:id:222", new TestDto { Name = "B" });
        await _sut.SetAsync("edespatch:id:111", new TestDto { Name = "C" });

        await _sut.RemoveByPrefixAsync("einvoice:");

        (await _sut.GetAsync<TestDto>("einvoice:id:111")).Should().BeNull();
        (await _sut.GetAsync<TestDto>("einvoice:id:222")).Should().BeNull();
        (await _sut.GetAsync<TestDto>("edespatch:id:111")).Should().NotBeNull();
    }

    private sealed class TestDto
    {
        public string Name { get; set; } = default!;
    }
}
