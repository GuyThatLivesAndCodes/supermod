using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SuperMod.Ai;
using SuperMod.Configuration;
using SuperMod.Moderation;
using Xunit;

namespace SuperMod.Tests;

public class ModerationServiceTests
{
    private static ModerationContext Context() => new(
        GuildId: 1,
        ChannelId: 2,
        ChannelName: "general",
        Messages: new List<BufferedMessage>
        {
            new(100, 200, "alice", "hi", DateTimeOffset.UnixEpoch),
            new(101, 201, "troll", "you are all idiots", DateTimeOffset.UnixEpoch)
        });

    private static (ModerationService Service, RecordingActions Actions) Build(
        ChatMessage reply, ModerationOptions? moderation = null)
    {
        var actions = new RecordingActions();
        var options = Options.Create(new SuperModOptions
        {
            Rules = "Be nice.",
            Moderation = moderation ?? new ModerationOptions()
        });
        var service = new ModerationService(
            new FakeChatClient(reply), actions, options, NullLogger<ModerationService>.Instance);
        return (service, actions);
    }

    [Fact]
    public async Task No_tool_calls_means_no_actions()
    {
        var (service, actions) = Build(new ChatMessage { Role = "assistant", Content = "NO_ACTION" });

        var outcome = await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.False(outcome.ActionTaken);
        Assert.Empty(actions.Timeouts);
        Assert.Empty(actions.Deletes);
    }

    [Fact]
    public async Task Executes_timeout_tool_call()
    {
        var reply = new ChatMessage
        {
            Role = "assistant",
            ToolCalls = new List<ToolCall>
            {
                ToolCallFactory.FromObject(ToolSchemas.TimeoutUsers, new
                {
                    user_ids = new[] { "201" },
                    duration_minutes = 30,
                    reason = "harassment"
                })
            }
        };
        var (service, actions) = Build(reply);

        var outcome = await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.True(outcome.ActionTaken);
        var call = Assert.Single(actions.Timeouts);
        Assert.Equal(1ul, call.GuildId);
        Assert.Equal(new ulong[] { 201 }, call.UserIds);
        Assert.Equal(TimeSpan.FromMinutes(30), call.Duration);
        Assert.Equal("harassment", call.Reason);
    }

    [Fact]
    public async Task Executes_delete_tool_call_with_multiple_ids()
    {
        var reply = new ChatMessage
        {
            Role = "assistant",
            ToolCalls = new List<ToolCall>
            {
                ToolCallFactory.FromObject(ToolSchemas.DeleteMessages, new
                {
                    message_ids = new[] { "100", "101" },
                    reason = "spam"
                })
            }
        };
        var (service, actions) = Build(reply);

        await service.ModerateAsync(Context(), CancellationToken.None);

        var call = Assert.Single(actions.Deletes);
        Assert.Equal(2ul, call.ChannelId);
        Assert.Equal(new ulong[] { 100, 101 }, call.MessageIds);
        Assert.Equal("spam", call.Reason);
    }

    [Fact]
    public async Task Executes_both_tools_in_one_pass()
    {
        var reply = new ChatMessage
        {
            Role = "assistant",
            ToolCalls = new List<ToolCall>
            {
                ToolCallFactory.FromObject(ToolSchemas.DeleteMessages, new { message_ids = new[] { "101" }, reason = "rule 1" }),
                ToolCallFactory.FromObject(ToolSchemas.TimeoutUsers, new { user_ids = new[] { "201" }, duration_minutes = 60, reason = "rule 1" })
            }
        };
        var (service, actions) = Build(reply);

        await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.Single(actions.Deletes);
        Assert.Single(actions.Timeouts);
    }

    [Fact]
    public async Task Clamps_timeout_to_configured_maximum()
    {
        var reply = new ChatMessage
        {
            Role = "assistant",
            ToolCalls = new List<ToolCall>
            {
                ToolCallFactory.FromObject(ToolSchemas.TimeoutUsers, new
                {
                    user_ids = new[] { "201" },
                    duration_minutes = 999999,
                    reason = "extreme"
                })
            }
        };
        var (service, actions) = Build(reply, new ModerationOptions { MaxTimeoutMinutes = 60 });

        await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(60), actions.Timeouts.Single().Duration);
    }

    [Fact]
    public async Task Parses_arguments_supplied_as_json_string()
    {
        // The OpenAI spec encodes arguments as a JSON string rather than an object.
        var reply = new ChatMessage
        {
            Role = "assistant",
            ToolCalls = new List<ToolCall>
            {
                ToolCallFactory.FromJsonString(ToolSchemas.TimeoutUsers,
                    """{ "user_ids": ["201"], "duration_minutes": 15, "reason": "spam" }""")
            }
        };
        var (service, actions) = Build(reply);

        await service.ModerateAsync(Context(), CancellationToken.None);

        var call = actions.Timeouts.Single();
        Assert.Equal(new ulong[] { 201 }, call.UserIds);
        Assert.Equal(TimeSpan.FromMinutes(15), call.Duration);
    }

    [Fact]
    public async Task Handles_numeric_ids()
    {
        var reply = new ChatMessage
        {
            Role = "assistant",
            ToolCalls = new List<ToolCall>
            {
                ToolCallFactory.FromObject(ToolSchemas.DeleteMessages, new
                {
                    message_ids = new ulong[] { 100, 101 },
                    reason = "spam"
                })
            }
        };
        var (service, actions) = Build(reply);

        await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.Equal(new ulong[] { 100, 101 }, actions.Deletes.Single().MessageIds);
    }

    [Fact]
    public async Task Empty_id_list_is_skipped()
    {
        var reply = new ChatMessage
        {
            Role = "assistant",
            ToolCalls = new List<ToolCall>
            {
                ToolCallFactory.FromObject(ToolSchemas.DeleteMessages, new { message_ids = Array.Empty<string>(), reason = "x" })
            }
        };
        var (service, actions) = Build(reply);

        await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.Empty(actions.Deletes);
    }

    [Fact]
    public async Task Ai_failure_is_reported_without_actions()
    {
        var actions = new RecordingActions();
        var options = Options.Create(new SuperModOptions { Rules = "Be nice." });
        var service = new ModerationService(
            new FakeChatClient(new InvalidOperationException("backend down")),
            actions, options, NullLogger<ModerationService>.Instance);

        var outcome = await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal("backend down", outcome.Error);
        Assert.Empty(actions.Timeouts);
        Assert.Empty(actions.Deletes);
    }

    [Fact]
    public async Task Forwards_tool_definitions_to_the_ai()
    {
        var chat = new FakeChatClient(new ChatMessage { Role = "assistant", Content = "NO_ACTION" });
        var service = new ModerationService(
            chat, new RecordingActions(), Options.Create(new SuperModOptions()), NullLogger<ModerationService>.Instance);

        await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.NotNull(chat.LastTools);
        Assert.Contains(chat.LastTools!, t => t.Function.Name == ToolSchemas.TimeoutUsers);
        Assert.Contains(chat.LastTools!, t => t.Function.Name == ToolSchemas.DeleteMessages);
        Assert.Equal(2, chat.LastMessages!.Count); // system + user
    }
}
