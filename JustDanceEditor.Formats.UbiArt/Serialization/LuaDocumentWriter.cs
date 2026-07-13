using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Serialization;

/// <summary>
/// Serializes C# object graphs as UbiArt Lua data files.
/// </summary>
public static class LuaDocumentWriter
{
    public static string Write(
        object? value,
        string assignment = "params",
        IEnumerable<string>? includes = null,
        IEnumerable<string>? trailingStatements = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignment);

        StringBuilder builder = new();
        foreach (string include in includes ?? [])
            builder.Append("includeReference(\"").Append(Escape(include)).AppendLine("\")");

        if (builder.Length > 0)
            builder.AppendLine();

        builder.Append(assignment).AppendLine(" =");
        WriteValue(builder, value, 0);
        builder.AppendLine();
        foreach (string statement in trailingStatements ?? [])
            builder.AppendLine(statement);
        return builder.ToString();
    }

    private static void WriteValue(StringBuilder builder, object? value, int depth)
    {
        switch (value)
        {
            case null:
                builder.Append("nil");
                return;
            case LuaExpression expression:
                builder.Append(expression.Value);
                return;
            case string text:
                builder.Append('"').Append(Escape(text)).Append('"');
                return;
            case char character:
                builder.Append('"').Append(Escape(character.ToString())).Append('"');
                return;
            case bool boolean:
                builder.Append(boolean ? "true" : "false");
                return;
            case Enum enumValue:
                builder.Append(Convert.ToInt64(enumValue, CultureInfo.InvariantCulture));
                return;
            case IFormattable formattable when IsNumber(value):
                builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            case IDictionary dictionary:
                WriteDictionary(builder, dictionary, depth);
                return;
            case IEnumerable sequence:
                WriteSequence(builder, sequence, depth);
                return;
            default:
                WriteProperties(builder, value, depth);
                return;
        }
    }

    private static void WriteDictionary(StringBuilder builder, IDictionary dictionary, int depth)
    {
        List<LuaField> fields = [];
        IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
        while (enumerator.MoveNext())
            fields.Add(new LuaField(FormatKey(enumerator.Key), enumerator.Value));
        WriteTable(builder, fields, depth);
    }

    private static void WriteSequence(StringBuilder builder, IEnumerable sequence, int depth)
    {
        WriteTable(builder, sequence.Cast<object?>().Select(value => new LuaField(null, value)), depth);
    }

    private static void WriteProperties(StringBuilder builder, object value, int depth)
    {
        IEnumerable<LuaField> fields = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Select(property => new LuaField(property.Name, property.GetValue(value)));
        WriteTable(builder, fields, depth);
    }

    private static void WriteTable(StringBuilder builder, IEnumerable<LuaField> sourceFields, int depth)
    {
        LuaField[] fields = [.. sourceFields];
        if (fields.Length == 0)
        {
            builder.Append("{}");
            return;
        }

        builder.AppendLine("{");
        foreach (LuaField field in fields)
        {
            builder.Append(' ', (depth + 1) * 2);
            if (field.Key != null)
                builder.Append(field.Key).Append(" = ");
            WriteValue(builder, field.Value, depth + 1);
            builder.AppendLine(",");
        }

        builder.Append(' ', depth * 2).Append('}');
    }

    private static string FormatKey(object? key)
    {
        string text = Convert.ToString(key, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("Lua table keys cannot be null.");
        return IsIdentifier(text) ? text : $"[\"{Escape(text)}\"]";
    }

    private static bool IsIdentifier(string value) =>
        value.Length > 0 &&
        (char.IsLetter(value[0]) || value[0] == '_') &&
        value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static bool IsNumber(object value) => value is
        byte or sbyte or short or ushort or int or uint or long or ulong or
        float or double or decimal;

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    private readonly record struct LuaField(string? Key, object? Value);
}

/// <summary>
/// A Lua expression that must be emitted without string quoting, such as an included table name.
/// </summary>
public readonly record struct LuaExpression(string Value);