using SuperMod.Moderation;
using Xunit;

namespace SuperMod.Tests;

public class PromptBuilderTests
{
    [Fact]
    public void System_prompt_includes_rules_channel_and_tool_guidance()
    {
        var prompt = PromptBuilder.BuildSystemPrompt("No spam allowed.", "general");

        Assert.Contains("No spam allowed.", prompt);
        Assert.Contains("#general", prompt);
        Assert.Contains("timeout_users", prompt);
        Assert.Contains("delete_messages", prompt);
        Assert.Contains("notice", prompt); // instructs the model to provide a user notice
        Assert.Contains("NO_ACTION", prompt);
    }

    [Fact]
    public void Transcript_numbers_messages_and_includes_authors_and_content()
    {
        var messages = new List<BufferedMessage>
        {
            new(111, 222, "alice", "hello world", DateTimeOffset.UnixEpoch),
            new(333, 444, "bob", "spam spam spam", DateTimeOffset.UnixEpoch)
        };

        var transcript = PromptBuilder.BuildTranscript(messages);

        Assert.Contains("[1] alice: hello world", transcript);
        Assert.Contains("[2] bob: spam spam spam", transcript);
        // Raw snowflake ids must NOT be exposed to the model anymore.
        Assert.DoesNotContain("111", transcript);
        Assert.DoesNotContain("222", transcript);
    }

    [Fact]
    public void Transcript_collapses_newlines_to_keep_one_line_per_message()
    {
        var messages = new List<BufferedMessage>
        {
            new(1, 2, "carol", "line one\nline two", DateTimeOffset.UnixEpoch)
        };

        var transcript = PromptBuilder.BuildTranscript(messages);

        Assert.DoesNotContain("line one\nline two", transcript);
        Assert.Contains("line one ⏎ line two", transcript);
    }

    [Fact]
    public void Empty_message_list_yields_placeholder()
    {
        Assert.Equal("(no messages)", PromptBuilder.BuildTranscript(Array.Empty<BufferedMessage>()));
    }
}
