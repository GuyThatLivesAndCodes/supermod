using SuperMod.Moderation;
using Xunit;

namespace SuperMod.Tests;

public class ToolArgumentsTests
{
    [Fact]
    public void Reads_ids_from_string_array()
    {
        var args = ToolArguments.Parse("""{ "user_ids": ["123", "456"] }""");
        Assert.Equal(new ulong[] { 123, 456 }, args.GetIds("user_ids"));
    }

    [Fact]
    public void Reads_ids_from_numeric_array()
    {
        // Snowflakes exceed JS safe-int range; ensure 64-bit numbers survive.
        var args = ToolArguments.Parse("""{ "message_ids": [123456789012345678, 987654321098765432] }""");
        Assert.Equal(new ulong[] { 123456789012345678, 987654321098765432 }, args.GetIds("message_ids"));
    }

    [Fact]
    public void Skips_unparseable_id_entries()
    {
        var args = ToolArguments.Parse("""{ "user_ids": ["123", "not-a-number", true, null] }""");
        Assert.Equal(new ulong[] { 123 }, args.GetIds("user_ids"));
    }

    [Fact]
    public void Missing_id_property_returns_empty()
    {
        var args = ToolArguments.Parse("""{ "reason": "x" }""");
        Assert.Empty(args.GetIds("user_ids"));
    }

    [Theory]
    [InlineData("""{ "duration_minutes": 30 }""", 30)]
    [InlineData("""{ "duration_minutes": "45" }""", 45)]
    [InlineData("""{ "duration_minutes": 12.7 }""", 13)]
    [InlineData("""{ }""", 99)]
    public void Reads_int_flexibly(string json, int expected)
    {
        var args = ToolArguments.Parse(json);
        Assert.Equal(expected, args.GetInt("duration_minutes", 99));
    }

    [Fact]
    public void Reads_string_with_fallback()
    {
        var args = ToolArguments.Parse("""{ "reason": "spam" }""");
        Assert.Equal("spam", args.GetString("reason", "default"));
        Assert.Equal("default", args.GetString("missing", "default"));
    }

    [Fact]
    public void Invalid_json_is_handled_gracefully()
    {
        var args = ToolArguments.Parse("this is not json");
        Assert.Empty(args.GetIds("user_ids"));
        Assert.Equal(5, args.GetInt("duration_minutes", 5));
        Assert.Equal("fallback", args.GetString("reason", "fallback"));
    }
}
