using SuperMod.Moderation;
using Xunit;

namespace SuperMod.Tests;

public class ToolArgumentsTests
{
    [Fact]
    public void Reads_numbers_from_numeric_array()
    {
        var args = ToolArguments.Parse("""{ "message_numbers": [1, 2, 3] }""");
        Assert.Equal(new[] { 1, 2, 3 }, args.GetInts("message_numbers"));
    }

    [Fact]
    public void Reads_numbers_from_string_array()
    {
        // Some models quote the numbers.
        var args = ToolArguments.Parse("""{ "message_numbers": ["1", "2"] }""");
        Assert.Equal(new[] { 1, 2 }, args.GetInts("message_numbers"));
    }

    [Fact]
    public void Skips_unparseable_number_entries()
    {
        var args = ToolArguments.Parse("""{ "message_numbers": [1, "nope", true, null] }""");
        Assert.Equal(new[] { 1 }, args.GetInts("message_numbers"));
    }

    [Fact]
    public void Missing_number_property_returns_empty()
    {
        var args = ToolArguments.Parse("""{ "reason": "x" }""");
        Assert.Empty(args.GetInts("message_numbers"));
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
        Assert.Empty(args.GetInts("message_numbers"));
        Assert.Equal(5, args.GetInt("duration_minutes", 5));
        Assert.Equal("fallback", args.GetString("reason", "fallback"));
    }
}
