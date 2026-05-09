namespace JustDanceEditor.Conversion.Abstractions;

public class MultipleConversionItemsFoundException(IEnumerable<string> availableItems, string? message = null) : Exception(message ?? CreateMessage(availableItems ?? []))
{
    public string[] AvailableItems { get; } = availableItems?.ToArray() ?? [];

    private static string CreateMessage(IEnumerable<string> availableItems)
    {
        string[] items = [.. availableItems];
        return items.Length == 0
            ? "Multiple conversion items were found."
            : $"Multiple conversion items were found: {string.Join(", ", items)}";
    }
}
