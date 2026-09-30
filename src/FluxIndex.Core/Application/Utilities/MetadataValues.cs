using System.Text.Json;

namespace FluxIndex.Core.Application.Utilities;

/// <summary>
/// Metadata and property values as plain .NET values, whichever store returned them. Stores that keep
/// <c>Dictionary&lt;string, object&gt;</c> in a JSON column get <see cref="JsonElement"/> values back from
/// <see cref="JsonSerializer"/>, while other stores return what was written. Every read converts through here, so a
/// consumer reads <c>value is string</c> the same way on every store and on every hybrid leg.
/// </summary>
/// <remarks>
/// A JSON string is a <see cref="string"/>, an integral number a <see cref="long"/>, any other number a
/// <see cref="double"/>, <c>true</c>/<c>false</c> a <see cref="bool"/>, an array a <c>List&lt;object?&gt;</c> and an
/// object a <c>Dictionary&lt;string, object&gt;</c> (converted the same way, recursively). A <c>null</c> member is
/// left out, as a key that was never written.
/// </remarks>
public static class MetadataValues
{
    /// <summary>Deserializes a JSON object into plain values (see the type remarks). Null, blank or JSON <c>null</c> is empty.</summary>
    /// <exception cref="JsonException">The text is not a JSON object.</exception>
    public static Dictionary<string, object> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, object>();

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Null)
            return new Dictionary<string, object>();
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException($"Expected a JSON object, found {document.RootElement.ValueKind}.");

        return ToPlainObject(document.RootElement);
    }

    /// <summary>
    /// Replaces every <see cref="JsonElement"/> value of <paramref name="values"/> with its plain value, in place, and
    /// returns the same dictionary (a new empty one for null). A null value is removed, as JSON <c>null</c> is by
    /// <see cref="Deserialize"/>; other values are left as they are.
    /// </summary>
    public static Dictionary<string, object> ToPlain(Dictionary<string, object>? values)
    {
        if (values is null)
            return new Dictionary<string, object>();

        List<string>? keys = null;
        foreach (var (key, value) in values)
        {
            if (value is JsonElement or null)
                (keys ??= []).Add(key);
        }

        if (keys is null)
            return values;

        foreach (var key in keys)
        {
            if (ToPlain(values[key]) is { } plain)
                values[key] = plain;
            else
                values.Remove(key);
        }

        return values;
    }

    /// <summary>The plain value of <paramref name="value"/>: converted when it is a <see cref="JsonElement"/>, else itself.</summary>
    public static object? ToPlain(object? value) => value is JsonElement element ? ToPlain(element) : value;

    /// <summary>The plain value of one JSON element (see the type remarks); null for JSON <c>null</c>.</summary>
    public static object? ToPlain(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? (object)integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(ToPlain).ToList(),
        JsonValueKind.Object => ToPlainObject(element),
        _ => null,
    };

    private static Dictionary<string, object> ToPlainObject(JsonElement element)
    {
        var result = new Dictionary<string, object>();
        foreach (var property in element.EnumerateObject())
        {
            if (ToPlain(property.Value) is { } plain)
                result[property.Name] = plain;
        }
        return result;
    }
}
