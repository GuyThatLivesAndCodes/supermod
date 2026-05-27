using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SuperMod.Configuration;

namespace SuperMod.Ai;

/// <summary>
/// Talks to any OpenAI-compatible chat-completions endpoint. The HttpClient's
/// BaseAddress, timeout and Authorization header are configured by the DI
/// registration in Program.cs.
/// </summary>
public sealed class OpenAiChatClient : IChatClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly AiOptions _ai;
    private readonly ILogger<OpenAiChatClient> _log;

    public OpenAiChatClient(HttpClient http, IOptions<SuperModOptions> options, ILogger<OpenAiChatClient> log)
    {
        _http = http;
        _ai = options.Value.Ai;
        _log = log;
    }

    public async Task<ChatMessage> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatTool> tools,
        CancellationToken cancellationToken)
    {
        var request = new ChatCompletionRequest
        {
            Model = _ai.Model,
            Temperature = _ai.Temperature,
            Messages = messages.ToList(),
            Tools = tools.Count > 0 ? tools.ToList() : null,
            ToolChoice = tools.Count > 0 ? "auto" : null
        };

        using var response = await _http.PostAsJsonAsync("chat/completions", request, Json, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"AI request to '{_http.BaseAddress}chat/completions' failed ({(int)response.StatusCode} {response.ReasonPhrase}): {Truncate(body, 500)}");
        }

        var parsed = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(Json, cancellationToken);
        var message = parsed?.Choices.FirstOrDefault()?.Message;

        if (message is null)
        {
            _log.LogWarning("AI response contained no choices.");
            return new ChatMessage { Role = "assistant", Content = "" };
        }

        return message;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}
