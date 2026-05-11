namespace JustDanceEditor.Cli;

internal sealed class CliArguments
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        CliArguments parsed = new();

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{arg}'. Options must start with '--'.");

            string key;
            string? value = null;
            int equals = arg.IndexOf('=');
            if (equals >= 0)
            {
                key = arg[2..equals];
                value = arg[(equals + 1)..];
            }
            else
            {
                key = arg[2..];
                if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    value = args[++i];
            }

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Option name cannot be empty.");

            if (value is null)
            {
                parsed._flags.Add(key);
                continue;
            }

            if (!parsed._values.TryGetValue(key, out List<string>? values))
            {
                values = [];
                parsed._values[key] = values;
            }

            values.Add(value);
        }

        return parsed;
    }

    public bool HasFlag(string key) => _flags.Contains(key) || _values.ContainsKey(key);

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
