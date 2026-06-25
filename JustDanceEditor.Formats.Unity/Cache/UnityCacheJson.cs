using System.Text.Encodings.Web;
using System.Text.Json;

namespace JustDanceEditor.Formats.Unity.Cache;

internal static class UnityCacheJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}