using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SuperMod.Ai;
using SuperMod.Configuration;
using SuperMod.Moderation;
using Xunit;

namespace SuperMod.Tests;

public class ModerationServiceTests
{
    // Message [1] = id 100 by author 200; message [2] = id 101 by author 201.
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

    private static ChatMessage Reply(params ToolCall[] calls) =>
        new() { Role = "assistant", ToolCalls = calls.ToList() };

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
    public async Task Timeout_targets_the_author_of_the_referenced_message()
    {
        var reply = Reply(ToolCallFactory.FromObject(ToolSchemas.TimeoutUsers, new
        {
            message_numbers = new[] { 2 },
            duration_minutes = 30,
            reason = "harassment",
            notice = "Per rule 1, be respectful."
        }));
        var (service, actions) = Build(reply);

        var outcome = await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.True(outcome.ActionTaken);
        var call = Assert.Single(actions.Timeouts);
        Assert.Equal(1ul, call.GuildId);
        Assert.Equal(new ulong[] { 201 }, call.UserIds); // author of message [2]
        Assert.Equal(TimeSpan.FromMinutes(30), call.Duration);
        Assert.Equal("harassment", call.Reason);
    }

    [Fact]
    public async Task Delete_maps_numbers_to_message_ids()
    {
        var reply = Reply(ToolCallFactory.FromObject(ToolSchemas.DeleteMessages, new
        {
            message_numbers = new[] { 1, 2 },
            reason = "spam",
            notice = "No spam please."
        }));
        var (service, actions) = Build(reply);

        await service.ModerateAsync(Context(), CancellationToken.None);

        var call = Assert.Single(actions.Deletes);
        Assert.Equal(2ul, call.ChannelId);
        Assert.Equal(new ulong[] { 100, 101 }, call.MessageIds);
        Assert.Equal("spam", call.Reason);
    }

    [Fact]
    public async Task Out_of_range_numbers_are_ignored()
    {
        var reply = Reply(ToolCallFactory.FromObject(ToolSchemas.DeleteMessages, new
        {
            message_numbers = new[] { 2, 99 }, // 99 doesn't exist
            reason = "spam",
            notice = "x"
        }));
        var (service, actions) = Build(reply);

        await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.Equal(new ulong[] { 101 }, actions.Deletes.Single().MessageIds);
    }

    [Fact]
    public async Task Notice_is_dmed_to_the_affected_user_on_timeout()
    {
        var reply = Reply(ToolCallFactory.FromObject(ToolSchemas.TimeoutUsers, new
        {
            message_numbers = new[] { 2 },
            duration_minutes = 30,
            reason = "harassment",
            notice = "Per rule 3.4, no harassment — please review the rules!"
        }));
        var (service, actions) = Build(reply);

        await service.ModerateAsync(Context(), CancellationToken.None);

        var notify = Assert.Single(actions.Notifications);
        Assert.Equal(new ulong[] { 201 }, notify.UserIds);
        Assert.Equal("Per rule 3.4, no harassment — please review the rules!", notify.Message);
    }

    [Fact]
    public async Task Notice_on_delete_is_dmed_to_the_message_authors()
    {
        var reply = Reply(ToolCallFactory.FromObject(ToolSchemas.DeleteMessages, new
        {
            message_numbers = new[] { 2 },
            reason = "nsfw",
            notice = "Per the no-NSFW rule, that's not allowed here."
        }));
        var (service, actions) = Build(reply);

        await service.ModerateAsync(Context(), CancellationToken.None);

        var notify = Assert.Single(actions.Notifications);
        Assert.Equal(new ulong[] { 201 }, notify.UserIds); // author of message [2]
        Assert.Contains("no-NSFW", notify.Message);
    }

    [Fact]
    public async Task Falls_back_to_a_default_notice_when_none_supplied()
    {
        var reply = Reply(ToolCallFactory.FromJsonString(ToolSchemas.DeleteMessages,
            """{ "message_numbers": [2], "reason": "spam" }"""));
        var (service, actions) = Build(reply);

        await service.ModerateAsync(Context(), CancellationToken.None);

        var notify = Assert.Single(actions.Notifications);
        Assert.Contains("removed", notify.Message);
        Assert.Contains("spam", notify.Message);
    }

    [Fact]
    public async Task No_dm_when_notifications_disabled()
    {
        var reply = Reply(ToolCallFactory.FromObject(ToolSchemas.DeleteMessages, new
        {
            message_numbers = new[] { 2 },
            reason = "spam",
            notice = "hello"
        }));
        var (service, actions) = Build(reply, new ModerationOptions { NotifyUsers = false });

        await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.Single(actions.Deletes);
        Assert.Empty(actions.Notifications);
    }

    [Fact]
    public async Task Executes_both_tools_in_one_pass()
    {
        var reply = Reply(
            ToolCallFactory.FromObject(ToolSchemas.DeleteMessages, new { message_numbers = new[] { 2 }, reason = "rule 1", notice = "x" }),
            ToolCallFactory.FromObject(ToolSchemas.TimeoutUsers, new { message_numbers = new[] { 2 }, duration_minutes = 60, reason = "rule 1", notice = "y" }));
        var (service, actions) = Build(reply);

        await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.Single(actions.Deletes);
        Assert.Single(actions.Timeouts);
    }

    [Fact]
    public async Task Clamps_timeout_to_configured_maximum()
    {
        var reply = Reply(ToolCallFactory.FromObject(ToolSchemas.TimeoutUsers, new
        {
            message_numbers = new[] { 2 },
            duration_minutes = 999999,
            reason = "extreme",
            notice = "x"
        }));
        var (service, actions) = Build(reply, new ModerationOptions { MaxTimeoutMinutes = 60 });

        await service.ModerateAsync(Context(), CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(60), actions.Timeouts.Single().Duration);
    }

    [Fact]
    public async Task Parses_arguments_supplied_as_json_string()
    {
        // The OpenAI spec encodes arguments as a JSON string rather than an object.
        var reply = Reply(ToolCallFactory.FromJsonString(ToolSchemas.TimeoutUsers,
            """{ "message_numbers": [2], "duration_minutes": 15, "reason": "spam", "notice": "x" }"""));
        var (service, actions) = Build(reply);

        await service.ModerateAsync(Context(), CancellationToken.None);

        var call = actions.Timeouts.Single();
        Assert.Equal(new ulong[] { 201 }, call.UserIds);
        Assert.Equal(TimeSpan.FromMinutes(15), call.Duration);
    }

    [Fact]
    public async Task Empty_number_list_is_skipped()
    {
        var reply = Reply(ToolCallFactory.FromObject(ToolSchemas.DeleteMessages,
            new { message_numbers = Array.Empty<int>(), reason = "x", notice = "y" }));
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
