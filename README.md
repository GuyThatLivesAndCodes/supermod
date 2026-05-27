# SuperMod

An AI-powered moderator for Discord that follows the rules **you** write.

You create a Discord bot, give SuperMod a set of rules in plain English, and
point it at a language model (local or hosted). SuperMod watches every channel,
and on a rolling basis sends recent messages to the model. The model decides —
using real moderation tools — whether to **delete messages** and/or **time out
users**, then SuperMod carries those actions out.

## How it works

```
Discord message ──► per-channel buffer ──(every 10 messages)──► last 20 messages
                                                                      │
                                                                      ▼
                              system prompt (your rules) + transcript ──► AI model
                                                                      │
                                              tool calls: delete_messages / timeout_users
                                                                      ▼
                                                      SuperMod executes them on Discord
```

- **Rolling review.** Each channel keeps a buffer of the **20** most recent
  messages. Every **10** new messages, SuperMod sends the current window to the
  model. Because the window (20) is twice the step (10), each pass overlaps the
  previous one by 10 messages — the model always sees the 10 newest messages
  plus the 10 it saw last time, so it keeps context across passes. Both numbers
  are configurable.
- **Tools, not guesswork.** The model is given two functions —
  `delete_messages` and `timeout_users` — each of which accepts multiple ids, so
  a single decision can clean up several messages and mute several users at once.
- **One client, three backends.** LM Studio, Ollama and xAI all speak the
  OpenAI-compatible `/v1/chat/completions` API with tool calling, so SuperMod
  uses a single HTTP client for all of them.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- A Discord bot (see below)
- One AI backend: **LM Studio**, **Ollama**, or an **xAI API key**

## 1. Create the Discord bot

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications) → **New Application**.
2. Open **Bot** → **Reset Token** and copy the token (this is your `DiscordToken`).
3. Under **Privileged Gateway Intents**, enable **Message Content Intent**
   (SuperMod needs to read message text).
4. Invite the bot with the **Moderate Members** and **Manage Messages**
   permissions (OAuth2 → URL Generator → scopes `bot`, then tick those two).
   Make sure the bot's role sits **above** the members it should be able to moderate.

## 2. Pick and start an AI backend

| Provider  | `Provider` value | Default base URL                | API key  | Example model      |
|-----------|------------------|---------------------------------|----------|--------------------|
| Ollama    | `ollama`         | `http://localhost:11434/v1`     | not used | `llama3.1`         |
| LM Studio | `lmstudio`       | `http://localhost:1234/v1`      | not used | (loaded model)     |
| xAI       | `xai`            | `https://api.x.ai/v1`           | required | `grok-2-latest`    |

- **Ollama:** `ollama pull llama3.1` then `ollama serve`. Use a model that
  supports tool calling (e.g. `llama3.1`, `qwen2.5`, `mistral-nemo`).
- **LM Studio:** load a tool-capable model and start the **Local Server**.
- **xAI:** create an API key at <https://console.x.ai> and set it as the API key.

> Tool calling quality depends on the model. Small local models can be hit or
> miss; larger instruct models follow the rules far more reliably.

## 3. Configure

Edit `src/SuperMod/appsettings.json`, or override any value with an environment
variable (double underscores map to nested keys):

```jsonc
{
  "SuperMod": {
    "DiscordToken": "",                  // your bot token
    "Rules": "Be respectful. No spam ...", // the rules the AI enforces
    "Ai": {
      "Provider": "ollama",              // ollama | lmstudio | xai | openai
      "BaseUrl": "",                     // optional; overrides the provider default
      "ApiKey": "",                      // required for xai
      "Model": "llama3.1",
      "Temperature": 0.2,
      "RequestTimeoutSeconds": 120
    },
    "Moderation": {
      "MessagesPerBatch": 10,            // run a pass every N messages
      "ContextWindow": 20,               // messages sent to the AI per pass
      "MaxTimeoutMinutes": 1440,         // cap on any timeout (Discord max is 28 days)
      "DryRun": false,                   // log actions instead of performing them
      "ProtectModerators": true          // never time out owner/admins/mods
    }
  }
}
```

Environment variable examples (useful for secrets and containers):

```bash
export SuperMod__DiscordToken="your-bot-token"
export SuperMod__Ai__Provider="xai"
export SuperMod__Ai__ApiKey="your-xai-key"
export SuperMod__Ai__Model="grok-2-latest"
```

Tip: set `"DryRun": true` the first time. SuperMod will log exactly what it
*would* delete or time out without touching anyone, so you can sanity-check your
rules and model.

## 4. Run

```bash
dotnet run --project src/SuperMod
```

Or with Docker:

```bash
docker build -t supermod .
docker run --rm \
  -e SuperMod__DiscordToken="your-bot-token" \
  -e SuperMod__Ai__Provider="ollama" \
  -e SuperMod__Ai__Model="llama3.1" \
  --network host \
  supermod
```

On a valid configuration you'll see `SuperMod connected ...` and
`Logged in as <bot> in N guild(s)`. Misconfiguration fails fast with a clear
message instead of starting.

## Safety

- **Protected members:** with `ProtectModerators` on (default), SuperMod never
  times out the guild owner or anyone with Administrator, Manage Messages or
  Moderate Members permissions. It also never times out itself.
- **Timeout cap:** every timeout is clamped to `MaxTimeoutMinutes` (and Discord's
  hard 28-day limit).
- **Dry run:** `DryRun` mode performs no destructive actions.
- **Self-throttling:** only one moderation pass runs per channel at a time;
  overlapping triggers are skipped rather than queued.

## Tests

```bash
dotnet test
```

Covers the rolling-window/overlap logic, prompt building, tolerant tool-argument
parsing (string *or* numeric ids, string-encoded arguments), the moderation
dispatch pipeline (timeout/delete/clamping/failure handling), and the
OpenAI-compatible HTTP request/response wire format — all without needing a live
Discord connection or model.

## Project layout

```
src/SuperMod/
  Program.cs                     Host setup + dependency injection
  appsettings.json               Default configuration
  Configuration/                 Strongly-typed options + validation
  Ai/                            OpenAI-compatible chat client + DTOs
  Moderation/                    Buffer, prompt, tools, dispatch (Discord-free core)
  Discord/                       Gateway service + action implementation
tests/SuperMod.Tests/            xUnit test suite
```

The moderation core in `Moderation/` depends only on the `IChatClient` and
`IModerationActions` abstractions, never on Discord types — which is what makes
the whole pipeline testable in isolation.
