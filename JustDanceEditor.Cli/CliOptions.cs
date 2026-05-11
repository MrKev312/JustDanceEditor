namespace JustDanceEditor.Cli;

internal sealed class CliOptions(bool headless)
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);

    public bool Headless { get; } = headless;

    public void Set(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!_values.TryGetValue(key, out List<string>? values))
        {
            values = [];
            _values[key] = values;
        }

        values.Add(value);
    }

    public void Set(string key, IEnumerable<string>? values)
    {
        if (values is null)
            return;

        foreach (string value in values)
            Set(key, value);
    }

    public void SetFlag(string key, bool value)
    {
        if (value)
            Set(key, "true");
    }

    public bool HasFlag(string key) => _values.ContainsKey(key);

    public string? Get(string key) => _values.TryGetValue(key, out List<string>? values) && values.Count > 0
        ? values[^1]
        : null;

    public string Require(string key)
    {
        string? value = Get(key);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Missing required option '--{key}'.");

        return value;
    }

    public IEnumerable<string> GetMany(string key) => _values.TryGetValue(key, out List<string>? values)
        ? values
        : [];
}
