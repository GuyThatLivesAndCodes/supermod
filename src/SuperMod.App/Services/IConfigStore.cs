using SuperMod.Configuration;

namespace SuperMod.App.Services;

/// <summary>Loads and saves the bot configuration.</summary>
public interface IConfigStore
{
    string Path { get; }
    SuperModOptions Load();
    void Save(SuperModOptions options);
}
