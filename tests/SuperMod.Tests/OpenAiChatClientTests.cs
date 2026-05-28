using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SuperMod.Ai;
using SuperMod.Configuration;
using SuperMod.Moderation;
using Xunit;

namespace SuperMod.Tests;

public class OpenAiChatClientTests
{
    /// <summary>Captures the outgoing request and returns a scripted response.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _responseBody;

        public StubHandler(HttpStatusCode status, string responseBody)
        {
            _status = status;
            _responseBody = responseBody;
        }

        public string? CapturedBody { get; private set; }
        public Uri? CapturedUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedUri = request.RequestUri;
            CapturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private static OpenAiChatClient Build(StubHandler handler, string model = "test-model")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1234/v1/") };
        var options = Options.Create(new SuperModOptions { Ai = new AiOptions { Model = model, Temperature = 0.3 } });
        return new OpenAiChatClient(http, options, NullLogger<OpenAiChatClient>.Instance);
    }

    [Fact]
    public async Task Posts_openai_shaped_request_to_chat_completions()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            { "choices": [ { "message": { "role": "assistant", "content": "NO_ACTION" } } ] }
            """);
        var client = Build(handler, model: "grok-2-latest");

        await client.CompleteAsync(
            new[] { ChatMessage.System("sys"), ChatMessage.User("hello") },
            ToolSchemas.All,
            CancellationToken.None);

        Assert.Equal("http://localhost:1234/v1/chat/completions", handler.CapturedUri!.ToString());

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var root = doc.RootElement;
        Assert.Equal("grok-2-latest", root.GetProperty("model").GetString());
        Assert.Equal("auto", root.GetProperty("tool_choice").GetString());
        Assert.Equal(2, root.GetProperty("messages").GetArrayLength());
        Assert.Equal(2, root.GetProperty("tools").GetArrayLength());

        // Tool schema must be a real JSON object, not a serialized string.
        var firstTool = root.GetProperty("tools")[0].GetProperty("function");
        Assert.False(string.IsNullOrEmpty(firstTool.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Object, firstTool.GetProperty("parameters").ValueKind);
    }

    [Fact]
    public async Task Parses_tool_calls_with_string_encoded_arguments()
    {
        // This mirrors exactly what OpenAI / xAI return on the wire.
        var handler = new StubHandler(HttpStatusCode.OK, """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": null,
                    "tool_calls": [
                      {
                        "id": "call_1",
                        "type": "function",
                        "function": {
                          "name": "delete_messages",
                          "arguments": "{\"message_numbers\":[1,2],\"reason\":\"spam\"}"
                        }
                      }
                    ]
                  }
                }
              ]
            }
            """);
        var client = Build(handler);

        var reply = await client.CompleteAsync(
            new[] { ChatMessage.User("x") }, ToolSchemas.All, CancellationToken.None);

        var call = Assert.Single(reply.ToolCalls!);
        Assert.Equal("delete_messages", call.Function.Name);

        var args = ToolArguments.Parse(call.Function.ArgumentsJson);
        Assert.Equal(new[] { 1, 2 }, args.GetInts("message_numbers"));
        Assert.Equal("spam", args.GetString("reason", ""));
    }

    [Fact]
    public async Task Non_success_response_throws_with_body()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, """{ "error": "model not loaded" }""");
        var client = Build(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CompleteAsync(new[] { ChatMessage.User("x") }, ToolSchemas.All, CancellationToken.None));

        Assert.Contains("500", ex.Message);
        Assert.Contains("model not loaded", ex.Message);
    }
}
