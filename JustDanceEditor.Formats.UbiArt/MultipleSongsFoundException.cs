using JustDanceEditor.Conversion.Abstractions;

namespace JustDanceEditor.Formats.UbiArt;

public sealed class MultipleSongsFoundException(IEnumerable<string> availableSongs)
    : MultipleConversionItemsFoundException(availableSongs ?? [], CreateMessage(availableSongs ?? []))
{
    public string[] AvailableSongs { get; } = availableSongs?.ToArray() ?? [];

    private static string CreateMessage(IEnumerable<string>? availableSongs)
    {
        if (availableSongs == null)
            return "Multiple songs found in the bundle.";

        string list = string.Join(", ", availableSongs);
        return $"Multiple songs found in the bundle. Available songs: {list}";
    }
}