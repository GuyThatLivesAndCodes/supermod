using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuperMod.Ai;

// Minimal DTOs for the OpenAI-compatible /v1/chat/completions API with tool calling.
// Used unchanged against LM Studio, Ollama, xAI and OpenAI.

public sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
    [JsonPropertyName("tools")] public List<ChatTool>? Tools { get; set; }
    [JsonPropertyName("tool_choice")] public string? ToolChoice { get; set; }
    [JsonPropertyName("temperature")] public double Temperature { get; set; } = 0.2;
}

public sealed class ChatMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("tool_calls")] public List<ToolCall>? ToolCalls { get; set; }
    [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; set; }

    public static ChatMessage System(string content) => new() { Role = "system", Content = content };
    public static ChatMessage User(string content) => new() { Role = "user", Content = content };
}

public sealed class ChatTool
{
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public ChatFunction Function { get; set; } = new();
}

public sealed class ChatFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("parameters")] public JsonElement Parameters { get; set; }
}

public sealed class ToolCall
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public ToolCallFunction Function { get; set; } = new();
}

public sealed class ToolCallFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    // The OpenAI spec sends arguments as a JSON-encoded string, but some
    // backends emit a raw object. Accept both and normalise via ArgumentsJson.
    [JsonPropertyName("arguments")] public JsonElement Arguments { get; set; }

    [JsonIgnore]
    public string ArgumentsJson => Arguments.ValueKind switch
    {
        JsonValueKind.String => Arguments.GetString() ?? "{}",
        JsonValueKind.Undefined or JsonValueKind.Null => "{}",
        _ => Arguments.GetRawText()
    };
}

public sealed class ChatCompletionResponse
{
    [JsonPropertyName("choices")] public List<ChatChoice> Choices { get; set; } = new();
}

public sealed class ChatChoice
{
    [JsonPropertyName("message")] public ChatMessage Message { get; set; } = new();
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
}
