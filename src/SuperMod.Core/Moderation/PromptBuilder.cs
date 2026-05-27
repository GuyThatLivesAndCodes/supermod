using System.Text;

namespace SuperMod.Moderation;

/// <summary>Builds the system prompt and message transcript sent to the AI.</summary>
public static class PromptBuilder
{
    private const int MaxContentLength = 600;

    public static string BuildSystemPrompt(string rules, string channelName)
    {
        var trimmedRules = string.IsNullOrWhiteSpace(rules) ? "(no rules provided)" : rules.Trim();

        return $"""
            You are SuperMod, an automated moderation assistant for the Discord channel #{channelName}.
            Your job is to enforce the server rules below strictly, consistently and fairly.

            === SERVER RULES ===
            {trimmedRules}
            ====================

            You will be given the most recent messages from this channel. Each line is formatted as:
            [msg=<message_id>] <author_name> (user=<user_id>): <message content>

            Review every message against the rules. For any clear violations:
            - Call delete_messages with the offending message_id value(s) to remove them.
            - Call timeout_users with the offending user_id value(s) to mute repeat or serious offenders.
            You may call both tools, and you may include multiple ids in a single call. Group ids that
            share the same reason and (for timeouts) the same duration into one call.

            Guidelines:
            - Only act on clear, rule-breaking content. When a message is borderline or ambiguous, do nothing.
            - Match the punishment to the severity. Use short timeouts for minor repeat offenses and longer
              ones for serious violations such as hate speech, threats or NSFW content.
            - Never invent message_id or user_id values; only use the ids shown in the transcript.
            - Some messages may already have been handled in a previous pass; do not re-punish the same content.

            If nothing violates the rules, reply with exactly: NO_ACTION
            Do not add commentary when you take action; just call the tools.
            """;
    }

    public static string BuildTranscript(IReadOnlyList<BufferedMessage> messages)
    {
        if (messages.Count == 0)
            return "(no messages)";

        var builder = new StringBuilder();
        builder.AppendLine("Most recent messages (oldest first):");
        foreach (var message in messages)
        {
            builder.Append("[msg=")
                   .Append(message.MessageId)
                   .Append("] ")
                   .Append(message.AuthorName)
                   .Append(" (user=")
                   .Append(message.AuthorId)
                   .Append("): ")
                   .AppendLine(Sanitize(message.Content));
        }
        return builder.ToString().TrimEnd();
    }

    private static string Sanitize(string content)
    {
        if (string.IsNullOrEmpty(content))
            return "(empty)";

        // Keep each message on one line so the model can map lines to ids cleanly.
        var collapsed = content.Replace("\r", " ").Replace("\n", " ⏎ ");
        return collapsed.Length <= MaxContentLength
            ? collapsed
            : collapsed[..MaxContentLength] + "…";
    }
}
