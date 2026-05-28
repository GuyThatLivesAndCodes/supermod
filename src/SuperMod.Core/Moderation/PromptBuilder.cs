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

            You will be given the most recent messages from this channel. Each message is labelled
            with a number in square brackets, like this:
            [3] <author_name>: <message content>

            Carefully review EVERY message against the rules. Find ALL messages that break the rules —
            do not stop after the first one. If several different messages break the rules, you must
            include all of their numbers.

            To act, call these tools (you may call both, and pass several numbers at once):
            - delete_messages: remove rule-breaking messages. Put their numbers in "message_numbers".
            - timeout_users: temporarily mute the AUTHORS of rule-breaking messages. Put the offending
              message numbers in "message_numbers" and set "duration_minutes".

            For every tool call you MUST also provide:
            - reason: a short reason for the audit log.
            - notice: a short, friendly message that will be sent privately to the affected user(s),
              telling them which rule they broke and to review the rules. Cite the rule, for example:
              "Per the no-NSFW rule, that isn't allowed in this channel — please review the rules and be careful!"

            Guidelines:
            - Act on every clear violation in the list, not just one. Group messages that share the same
              reason and (for timeouts) the same duration into a single call with multiple numbers.
            - Match severity: short timeouts for minor repeat issues, longer ones for serious violations
              such as hate speech, threats or NSFW content.
            - Only use message numbers that appear in the list. When a message is borderline, leave it.

            If nothing breaks the rules, reply with exactly: NO_ACTION
            Do not add commentary when you take action; just call the tools.
            """;
    }

    public static string BuildTranscript(IReadOnlyList<BufferedMessage> messages)
    {
        if (messages.Count == 0)
            return "(no messages)";

        var builder = new StringBuilder();
        builder.AppendLine("Recent messages (oldest first). Each is labelled with its number:");
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            builder.Append('[')
                   .Append(i + 1)
                   .Append("] ")
                   .Append(message.AuthorName)
                   .Append(": ")
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
