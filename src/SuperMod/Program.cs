using System.Net.Http.Headers;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SuperMod.Ai;
using SuperMod.Configuration;
using SuperMod.Discord;
using SuperMod.Moderation;

var builder = Host.CreateApplicationBuilder(args);

// Configuration: appsettings.json + environment variables (e.g. SuperMod__DiscordToken).
builder.Services
    .AddOptions<SuperModOptions>()
    .Bind(builder.Configuration.GetSection(SuperModOptions.SectionName));

// Discord gateway client. MessageContent is a privileged intent and must also
// be enabled for the bot in the Discord Developer Portal.
builder.Services.AddSingleton(_ => new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
    MessageCacheSize = 0,
    LogLevel = LogSeverity.Info,
    AlwaysDownloadUsers = false
}));

// One OpenAI-compatible HTTP client, configured from the chosen AI provider.
builder.Services.AddHttpClient<IChatClient, OpenAiChatClient>((serviceProvider, http) =>
{
    var ai = serviceProvider.GetRequiredService<IOptions<SuperModOptions>>().Value.Ai;

    var baseUrl = ai.ResolveBaseUrl();
    if (!baseUrl.EndsWith('/'))
        baseUrl += "/";

    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(Math.Max(10, ai.RequestTimeoutSeconds));

    if (!string.IsNullOrWhiteSpace(ai.ApiKey))
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ai.ApiKey);
});

builder.Services.AddSingleton<MessageBufferStore>();
builder.Services.AddSingleton<IModerationActions, DiscordModerationActions>();
builder.Services.AddSingleton<ModerationService>();
builder.Services.AddHostedService<DiscordBotService>();

var host = builder.Build();

// Fail fast with a clear message if the bot is misconfigured. Written straight
// to stderr so it is never lost to the console logger's background flush.
var options = host.Services.GetRequiredService<IOptions<SuperModOptions>>().Value;
try
{
    options.Validate();
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync(ex.Message);
    return 1;
}

await host.RunAsync();
return 0;
