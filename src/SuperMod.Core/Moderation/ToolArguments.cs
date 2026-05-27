using System.Text.Json;

namespace SuperMod.Moderation;

/// <summary>
/// Forgiving reader over a tool call's JSON arguments. Models are inconsistent
/// about whether ids arrive as strings or numbers and whether numbers arrive as
/// strings, so every accessor copes with both.
/// </summary>
public sealed class ToolArguments
{
    private readonly JsonElement _root;

    private ToolArguments(JsonElement root) => _root = root;

    public static ToolArguments Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ToolArguments(default);

        try
        {
            using var doc = JsonDocument.Parse(json);
            return new ToolArguments(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            return new ToolArguments(default);
        }
    }

    /// <summary>Reads an array of snowflake ids, accepting string or numeric elements.</summary>
    public IReadOnlyList<ulong> GetIds(string property)
    {
        var ids = new List<ulong>();
        if (_root.ValueKind != JsonValueKind.Object)
            return ids;

        if (!_root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return ids;

        foreach (var element in array.EnumerateArray())
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String when ulong.TryParse(element.GetString(), out var fromString):
                    ids.Add(fromString);
                    break;
                case JsonValueKind.Number when element.TryGetUInt64(out var fromNumber):
                    ids.Add(fromNumber);
                    break;
            }
        }
        return ids;
    }

    /// <summary>Reads an integer property, accepting numeric or numeric-string values.</summary>
    public int GetInt(string property, int fallback)
    {
        if (_root.ValueKind != JsonValueKind.Object || !_root.TryGetProperty(property, out var value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.Number when value.TryGetDouble(out var dbl) => (int)Math.Round(dbl),
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => fallback
        };
    }

    public string GetString(string property, string fallback)
    {
        if (_root.ValueKind != JsonValueKind.Object || !_root.TryGetProperty(property, out var value))
            return fallback;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }
}
