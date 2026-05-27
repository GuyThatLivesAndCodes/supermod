using SuperMod.Configuration;
using Xunit;

namespace SuperMod.Tests;

public class AiOptionsTests
{
    [Theory]
    [InlineData("ollama", "http://localhost:11434/v1")]
    [InlineData("lmstudio", "http://localhost:1234/v1")]
    [InlineData("xai", "https://api.x.ai/v1")]
    [InlineData("openai", "https://api.openai.com/v1")]
    public void ResolveBaseUrl_maps_known_providers(string provider, string expected)
    {
        var options = new AiOptions { Provider = provider };
        Assert.Equal(expected, options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_prefers_explicit_base_url()
    {
        var options = new AiOptions { Provider = "ollama", BaseUrl = "http://example.com:9000/v1" };
        Assert.Equal("http://example.com:9000/v1", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_is_empty_for_unknown_provider_without_base_url()
    {
        var options = new AiOptions { Provider = "mystery" };
        Assert.Equal("", options.ResolveBaseUrl());
    }

    [Fact]
    public void Validate_throws_when_token_missing()
    {
        var options = new SuperModOptions { DiscordToken = "" };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_requires_api_key_for_xai()
    {
        var options = new SuperModOptions
        {
            DiscordToken = "token",
            Ai = new AiOptions { Provider = "xai", Model = "grok-2-latest", ApiKey = "" }
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_passes_for_valid_local_config()
    {
        var options = new SuperModOptions
        {
            DiscordToken = "token",
            Ai = new AiOptions { Provider = "ollama", Model = "llama3.1" }
        };
        options.Validate(); // should not throw
    }
}
