using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.Intermediate.Assets;

public class IntermediateAssetCatalog
{
    [JsonPropertyName("assets")]
    public List<IntermediateAsset> Assets { get; set; } = [];

    public IntermediateAsset Add(string role)
    {
        IntermediateAsset asset = new() { Role = role };
        Assets.Add(asset);
        return asset;
    }
}

public class IntermediateAsset
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("sourcePath")]
    public string? SourcePath { get; set; }

    [JsonPropertyName("generatedPath")]
    public string? GeneratedPath { get; set; }

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("variant")]
    public string? Variant { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, string> Attributes { get; set; } = new();

    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;
}
