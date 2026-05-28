using System.Text.Json;
using SuperMod.Ai;

namespace SuperMod.Moderation;

/// <summary>Defines the moderation tools advertised to the AI.</summary>
public static class ToolSchemas
{
    public const string TimeoutUsers = "timeout_users";
    public const string DeleteMessages = "delete_messages";

    public static IReadOnlyList<ChatTool> All { get; } = new[]
    {
        BuildTimeoutUsers(),
        BuildDeleteMessages()
    };

    private static ChatTool BuildTimeoutUsers() => new()
    {
        Function = new ChatFunction
        {
            Name = TimeoutUsers,
            Description = "Temporarily mute (time out) the authors of rule-breaking messages. " +
                          "Use for repeat or serious offenders.",
            Parameters = Schema(new
            {
                type = "object",
                properties = new
                {
                    message_numbers = new
                    {
                        type = "array",
                        items = new { type = "integer" },
                        description = "The [n] numbers of the offending messages. Their authors will be timed out."
                    },
                    duration_minutes = new
                    {
                        type = "integer",
                        description = "How long to mute the user(s), in minutes. Scale with severity."
                    },
                    reason = new
                    {
                        type = "string",
                        description = "Short reason recorded in the Discord audit log."
                    },
                    notice = new
                    {
                        type = "string",
                        description = "A short, friendly message sent privately to the affected user(s) explaining " +
                                      "which rule was broken and to review the rules. Cite the rule."
                    }
                },
                required = new[] { "message_numbers", "duration_minutes", "reason", "notice" }
            })
        }
    };

    private static ChatTool BuildDeleteMessages() => new()
    {
        Function = new ChatFunction
        {
            Name = DeleteMessages,
            Description = "Delete one or more rule-breaking messages from the channel.",
            Parameters = Schema(new
            {
                type = "object",
                properties = new
                {
                    message_numbers = new
                    {
                        type = "array",
                        items = new { type = "integer" },
                        description = "The [n] numbers of the messages to delete. Include every offending message."
                    },
                    reason = new
                    {
                        type = "string",
                        description = "Short reason recorded in the Discord audit log."
                    },
                    notice = new
                    {
                        type = "string",
                        description = "A short, friendly message sent privately to the affected author(s) explaining " +
                                      "which rule was broken and to review the rules. Cite the rule."
                    }
                },
                required = new[] { "message_numbers", "reason", "notice" }
            })
        }
    };

    private static JsonElement Schema(object schema) => JsonSerializer.SerializeToElement(schema);
}
