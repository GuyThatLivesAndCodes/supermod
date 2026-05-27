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
            Description = "Temporarily mute (time out) one or more users who broke the rules. " +
                          "Use for repeat or serious offenders.",
            Parameters = Schema(new
            {
                type = "object",
                properties = new
                {
                    user_ids = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Discord user ids (the user=... value) to time out."
                    },
                    duration_minutes = new
                    {
                        type = "integer",
                        description = "How long to mute the user(s), in minutes. Scale with severity."
                    },
                    reason = new
                    {
                        type = "string",
                        description = "Short reason recorded in the audit log."
                    }
                },
                required = new[] { "user_ids", "duration_minutes", "reason" }
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
                    message_ids = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Discord message ids (the msg=... value) to delete."
                    },
                    reason = new
                    {
                        type = "string",
                        description = "Short reason recorded in the audit log."
                    }
                },
                required = new[] { "message_ids", "reason" }
            })
        }
    };

    private static JsonElement Schema(object schema) => JsonSerializer.SerializeToElement(schema);
}
