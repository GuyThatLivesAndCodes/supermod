namespace SuperMod.Configuration;

/// <summary>
/// Root configuration for SuperMod, bound from the "SuperMod" config section
/// (appsettings.json) and overridable via environment variables such as
/// SuperMod__DiscordToken or SuperMod__Ai__ApiKey.
/// </summary>
public sealed class SuperModOptions
{
    public const string SectionName = "SuperMod";

    /// <summary>Discord bot token from the Discord Developer Portal.</summary>
    public string DiscordToken { get; set; } = "";

    /// <summary>Free-form moderation rules the AI must enforce.</summary>
    public string Rules { get; set; } =
        "Be respectful. No harassment, hate speech, slurs, threats, or NSFW content. " +
        "No spam or flooding. No advertising or scam links.";

    public AiOptions Ai { get; set; } = new();

    public ModerationOptions Moderation { get; set; } = new();

    /// <summary>Throws if required settings are missing.</summary>
    public void Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(DiscordToken))
            errors.Add("SuperMod:DiscordToken is required (set it in appsettings.json or the SuperMod__DiscordToken env var).");
        if (string.IsNullOrWhiteSpace(Ai.Model))
            errors.Add("SuperMod:Ai:Model is required.");
        if (string.IsNullOrWhiteSpace(Ai.ResolveBaseUrl()))
            errors.Add("SuperMod:Ai:BaseUrl could not be resolved; set Provider to ollama/lmstudio/xai/openai or provide an explicit BaseUrl.");
        if (string.Equals(Ai.Provider, "xai", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(Ai.ApiKey))
            errors.Add("SuperMod:Ai:ApiKey is required when Provider is 'xai'.");

        if (errors.Count > 0)
            throw new InvalidOperationException("Invalid SuperMod configuration:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
}

/// <summary>
/// AI backend settings. All supported backends speak the OpenAI-compatible
/// /v1/chat/completions API with tool calling, so one client serves them all.
/// </summary>
public sealed class AiOptions
{
    /// <summary>One of: ollama, lmstudio, xai, openai. Selects the default BaseUrl.</summary>
    public string Provider { get; set; } = "ollama";

    /// <summary>Explicit base URL (e.g. http://localhost:11434/v1). Overrides Provider's default when set.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>API key. Required for xAI/OpenAI; usually blank for local LM Studio / Ollama.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Model name (e.g. llama3.1, grok-2-latest, qwen2.5).</summary>
    public string Model { get; set; } = "llama3.1";

    public double Temperature { get; set; } = 0.2;

    public int RequestTimeoutSeconds { get; set; } = 120;

    /// <summary>Resolves the effective base URL from BaseUrl (if set) or the Provider default.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.Trim();

        return Provider?.Trim().ToLowerInvariant() switch
        {
            "ollama" => "http://localhost:11434/v1",
            "lmstudio" or "lm-studio" or "lm_studio" => "http://localhost:1234/v1",
            "xai" or "grok" => "https://api.x.ai/v1",
            "openai" => "https://api.openai.com/v1",
            _ => ""
        };
    }
}

/// <summary>Behavioural knobs for the moderation pipeline.</summary>
public sealed class ModerationOptions
{
    /// <summary>Run a moderation pass after this many new messages in a channel.</summary>
    public int MessagesPerBatch { get; set; } = 10;

    /// <summary>How many recent messages to send the AI on each pass (the context window).</summary>
    public int ContextWindow { get; set; } = 20;

    /// <summary>Upper bound (minutes) for any timeout the AI requests. Hard-capped at Discord's 28-day max.</summary>
    public int MaxTimeoutMinutes { get; set; } = 1440;

    /// <summary>When true, log intended actions instead of performing them. Handy for testing.</summary>
    public bool DryRun { get; set; } = false;

    /// <summary>When true, never time out the guild owner or members with admin/manage/moderate permissions.</summary>
    public bool ProtectModerators { get; set; } = true;
}
