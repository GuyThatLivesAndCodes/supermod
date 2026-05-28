using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SuperMod.Ai;
using SuperMod.Configuration;

namespace SuperMod.Moderation;

/// <summary>
/// Runs one moderation pass: build the prompt, ask the AI, then execute any
/// tool calls it returns. This class is deliberately free of Discord types so
/// it can be exercised end-to-end in unit tests.
/// </summary>
public sealed class ModerationService
{
    // Discord caps timeouts at 28 days.
    private const int DiscordMaxTimeoutMinutes = 28 * 24 * 60;

    private readonly IChatClient _chat;
    private readonly IModerationActions _actions;
    private readonly SuperModOptions _options;
    private readonly ILogger<ModerationService> _log;

    public ModerationService(
        IChatClient chat,
        IModerationActions actions,
        IOptions<SuperModOptions> options,
        ILogger<ModerationService> log)
    {
        _chat = chat;
        _actions = actions;
        _options = options.Value;
        _log = log;
    }

    public async Task<ModerationOutcome> ModerateAsync(ModerationContext context, CancellationToken cancellationToken)
    {
        if (context.Messages.Count == 0)
            return ModerationOutcome.NoAction;

        var prompt = new[]
        {
            ChatMessage.System(PromptBuilder.BuildSystemPrompt(_options.Rules, context.ChannelName)),
            ChatMessage.User(PromptBuilder.BuildTranscript(context.Messages))
        };

        ChatMessage reply;
        try
        {
            reply = await _chat.CompleteAsync(prompt, ToolSchemas.All, cancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AI completion failed for channel #{Channel} ({ChannelId}).", context.ChannelName, context.ChannelId);
            return ModerationOutcome.Failed(ex.Message);
        }

        if (reply.ToolCalls is not { Count: > 0 })
        {
            _log.LogInformation("No action for #{Channel}: {Reply}", context.ChannelName,
                string.IsNullOrWhiteSpace(reply.Content) ? "NO_ACTION" : reply.Content!.Trim());
            return ModerationOutcome.NoAction;
        }

        var results = new List<string>();
        foreach (var call in reply.ToolCalls)
        {
            var result = await DispatchAsync(context, call, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result))
            {
                results.Add(result!);
                _log.LogInformation("Moderation action in #{Channel}: {Result}", context.ChannelName, result);
            }
        }

        return ModerationOutcome.Acted(results);
    }

    private async Task<string?> DispatchAsync(ModerationContext context, ToolCall call, CancellationToken cancellationToken)
    {
        var args = ToolArguments.Parse(call.Function.ArgumentsJson);

        switch (call.Function.Name)
        {
            case ToolSchemas.TimeoutUsers:
            {
                var targets = Resolve(context, args.GetInts("message_numbers"));
                var userIds = targets.Select(m => m.AuthorId).Distinct().ToList();
                if (userIds.Count == 0)
                    return null;

                var minutes = Math.Clamp(
                    args.GetInt("duration_minutes", 10),
                    1,
                    Math.Min(_options.Moderation.MaxTimeoutMinutes, DiscordMaxTimeoutMinutes));
                var reason = args.GetString("reason", "Violated server rules.");
                var notice = args.GetString("notice", "");

                var result = await _actions.TimeoutUsersAsync(
                    context.GuildId, userIds, TimeSpan.FromMinutes(minutes), reason, cancellationToken);

                if (_options.Moderation.NotifyUsers && result.AffectedIds.Count > 0)
                {
                    var message = string.IsNullOrWhiteSpace(notice)
                        ? $"You have been timed out for {minutes} minute(s). Reason: {reason}"
                        : notice;
                    await _actions.NotifyUsersAsync(context.GuildId, result.AffectedIds, message, cancellationToken);
                }

                return result.Summary;
            }

            case ToolSchemas.DeleteMessages:
            {
                var targets = Resolve(context, args.GetInts("message_numbers"));
                if (targets.Count == 0)
                    return null;

                var messageIds = targets.Select(m => m.MessageId).Distinct().ToList();
                var reason = args.GetString("reason", "Violated server rules.");
                var notice = args.GetString("notice", "");

                var result = await _actions.DeleteMessagesAsync(
                    context.GuildId, context.ChannelId, messageIds, reason, cancellationToken);

                if (_options.Moderation.NotifyUsers && result.AffectedIds.Count > 0)
                {
                    var deleted = result.AffectedIds.ToHashSet();
                    var authorIds = targets
                        .Where(m => deleted.Contains(m.MessageId))
                        .Select(m => m.AuthorId)
                        .Distinct()
                        .ToList();

                    if (authorIds.Count > 0)
                    {
                        var message = string.IsNullOrWhiteSpace(notice)
                            ? $"A message you posted was removed by moderation. Reason: {reason}"
                            : notice;
                        await _actions.NotifyUsersAsync(context.GuildId, authorIds, message, cancellationToken);
                    }
                }

                return result.Summary;
            }

            default:
                _log.LogWarning("AI requested unknown tool '{Tool}'.", call.Function.Name);
                return null;
        }
    }

    /// <summary>Maps 1-based message numbers from the transcript back to buffered messages.</summary>
    private static List<BufferedMessage> Resolve(ModerationContext context, IReadOnlyList<int> numbers)
    {
        var resolved = new List<BufferedMessage>();
        foreach (var number in numbers)
        {
            if (number >= 1 && number <= context.Messages.Count)
                resolved.Add(context.Messages[number - 1]);
        }
        return resolved;
    }
}
