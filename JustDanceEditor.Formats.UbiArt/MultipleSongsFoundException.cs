namespace JustDanceEditor.Formats.UbiArt;

public sealed class MultipleSongsFoundException(IEnumerable<string> availableSongs) : Exception(CreateMessage(availableSongs))
{
    public string[] AvailableSongs { get; } = availableSongs?.ToArray() ?? [];

    private static string CreateMessage(IEnumerable<string> availableSongs)
    {
        if (availableSongs == null)
            return "Multiple songs found in the bundle.";

        var list = string.Join(", ", availableSongs);
        return $"Multiple songs found in the bundle. Available songs: {list}";
    }
}
