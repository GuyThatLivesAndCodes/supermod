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

    /// <summary>
    /// Reads an array of small message numbers (the [n] labels), accepting numeric
    /// or numeric-string elements. Invalid entries are skipped.
    /// </summary>
    public IReadOnlyList<int> GetInts(string property)
    {
        var numbers = new List<int>();
        if (_root.ValueKind != JsonValueKind.Object)
            return numbers;

        if (!_root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return numbers;

        foreach (var element in array.EnumerateArray())
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number when element.TryGetInt32(out var fromNumber):
                    numbers.Add(fromNumber);
                    break;
                case JsonValueKind.String when int.TryParse(element.GetString(), out var fromString):
                    numbers.Add(fromString);
                    break;
            }
        }
        return numbers;
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
