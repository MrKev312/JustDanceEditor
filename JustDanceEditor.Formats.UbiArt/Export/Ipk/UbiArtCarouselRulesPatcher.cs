using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JustDanceEditor.Formats.UbiArt.Export.Ipk;

internal static class UbiArtCarouselRulesPatcher
{
    private static readonly string[] MapRoutes =
    [
        "/party",
        "/create-playlist",
        "/partycoop",
        "/sweat"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static bool TryPatch(byte[] originalBytes, int originalJDVersion, out byte[] updatedBytes)
    {
        updatedBytes = originalBytes;
        if (originalJDVersion <= 0 || originalBytes.Length == 0)
            return false;

        bool hasNullTerminator = originalBytes[^1] == 0;
        string text = Encoding.UTF8.GetString(originalBytes).TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(text))
            return false;

        JsonNode? root = JsonNode.Parse(text);
        if (root is not JsonObject rootObject ||
            rootObject["rules"] is not JsonObject rulesObject)
        {
            return false;
        }

        bool changed = false;
        foreach (string route in MapRoutes)
        {
            if (rulesObject[route] is JsonObject ruleObject)
                changed |= TryAddVersionCategory(ruleObject, originalJDVersion);
        }

        if (!changed)
            return false;

        byte[] jsonBytes = Encoding.UTF8.GetBytes(rootObject.ToJsonString(JsonOptions));
        if (!hasNullTerminator)
        {
            updatedBytes = jsonBytes;
            return true;
        }

        updatedBytes = new byte[jsonBytes.Length + 1];
        Buffer.BlockCopy(jsonBytes, 0, updatedBytes, 0, jsonBytes.Length);
        return true;
    }

    private static bool TryAddVersionCategory(JsonObject ruleObject, int originalJDVersion)
    {
        if (ruleObject["categories"] is not JsonArray categories)
            return false;

        if (HasVersionCategory(categories, originalJDVersion))
            return false;

        List<VersionCategory> templates = [];
        for (int index = 0; index < categories.Count; index++)
        {
            if (categories[index] is not JsonObject category)
                continue;

            int? version = GetCategoryOriginalVersion(category);
            if (version is > 0)
                templates.Add(new VersionCategory(category, index, version.Value));
        }

        if (templates.Count == 0)
            return false;

        VersionCategory template = templates
            .OrderByDescending(candidate => IsJustDanceVersionCategory(candidate.Category))
            .ThenByDescending(candidate => candidate.Version)
            .ThenBy(candidate => candidate.Index)
            .First();

        JsonObject newCategory = (JsonObject)template.Category.DeepClone();
        UpdateCategoryVersion(newCategory, originalJDVersion);

        int insertIndex = originalJDVersion > template.Version
            ? template.Index
            : template.Index + 1;
        categories.Insert(insertIndex, newCategory);
        return true;
    }

    private static bool HasVersionCategory(JsonArray categories, int originalJDVersion)
    {
        foreach (JsonNode? categoryNode in categories)
        {
            if (categoryNode is JsonObject category &&
                GetCategoryOriginalVersion(category) == originalJDVersion)
            {
                return true;
            }
        }

        return false;
    }

    private static int? GetCategoryOriginalVersion(JsonObject category)
    {
        if (category["requests"] is not JsonArray requests)
            return null;

        foreach (JsonNode? requestNode in requests)
        {
            if (requestNode is not JsonObject request ||
                !IsMapRequest(request) ||
                !TryGetInt(request["originalJDVersion"], out int version))
            {
                continue;
            }

            if (version > 0)
                return version;
        }

        return null;
    }

    private static void UpdateCategoryVersion(JsonObject category, int originalJDVersion)
    {
        string? templateTitle = category["title"]?.GetValue<string>();
        category["title"] = BuildVersionTitle(templateTitle, originalJDVersion);
        category["titleId"] = uint.MaxValue;

        if (category["requests"] is not JsonArray requests)
            return;

        foreach (JsonNode? requestNode in requests)
        {
            if (requestNode is JsonObject request && IsMapRequest(request))
                request["originalJDVersion"] = originalJDVersion;
        }
    }

    private static string BuildVersionTitle(string? templateTitle, int originalJDVersion)
    {
        if (!string.IsNullOrWhiteSpace(templateTitle) &&
            !templateTitle.Any(char.IsLower) &&
            templateTitle.Any(char.IsLetter))
        {
            return $"JUST DANCE {originalJDVersion}";
        }

        return $"Just Dance {originalJDVersion}";
    }

    private static bool IsJustDanceVersionCategory(JsonObject category)
    {
        string? title = category["title"]?.GetValue<string>();
        return title?.Contains("Just Dance", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsMapRequest(JsonObject request)
    {
        string? className = request["__class"]?.GetValue<string>();
        return string.Equals(className, "JD_CarouselMapRequestDesc", StringComparison.Ordinal);
    }

    private static bool TryGetInt(JsonNode? node, out int value)
    {
        value = 0;
        if (node is null)
            return false;

        try
        {
            value = node.GetValue<int>();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record VersionCategory(JsonObject Category, int Index, int Version);
}
