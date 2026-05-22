namespace JustDanceEditor.Conversion.Abstractions.Prompts;

public sealed class PromptAnswerSet
{
    private readonly Dictionary<string, string> _answers;

    public PromptAnswerSet()
    {
        _answers = new(StringComparer.OrdinalIgnoreCase);
    }

    public PromptAnswerSet(IEnumerable<KeyValuePair<string, string>> answers)
    {
        _answers = new(answers, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string> Answers => _answers;

    public void Set(string id, string value) => _answers[id] = value;

    public bool TryGetString(string id, out string? value) => _answers.TryGetValue(id, out value);

    public string GetString(string id)
    {
        if (_answers.TryGetValue(id, out string? value))
            return value;

        throw new KeyNotFoundException($"No prompt answer was provided for '{id}'.");
    }
}
