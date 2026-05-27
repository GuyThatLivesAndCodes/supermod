using System.IO;
using System.Text.Json;
using SuperMod.Configuration;

namespace SuperMod.App.Services;

/// <summary>
/// Persists configuration as JSON under the user's application data directory
/// (e.g. ~/.config/SuperMod/config.json on Linux,
/// %APPDATA%\SuperMod\config.json on Windows).
/// </summary>
public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Path { get; }

    public JsonConfigStore()
    {
        var directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SuperMod");
        Path = System.IO.Path.Combine(directory, "config.json");
    }

    public SuperModOptions Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<SuperModOptions>(File.ReadAllText(Path), Json) ?? new SuperModOptions();
        }
        catch
        {
            // Corrupt or unreadable config: fall back to defaults rather than crashing.
        }
        return new SuperModOptions();
    }

    public void Save(SuperModOptions options)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(options, Json));
    }
}
