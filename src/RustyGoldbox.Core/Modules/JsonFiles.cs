using System.Globalization;
using System.Text.Json;

namespace RustyGoldbox.Core.Modules;

internal static class JsonFiles
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = false,
        AllowDuplicateProperties = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    /// <summary>
    /// Parses a JSON file, reporting syntax errors with their line and column.
    /// Returns null when the file can't be read or parsed.
    /// </summary>
    public static JsonDocument? Parse(string path, string? module, List<ModuleDiagnostic> diagnostics)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ModuleDiagnostic("file.read", $"Can't read the file: {exception.Message}", module, path));
            return null;
        }

        try
        {
            return JsonDocument.Parse(text, Options);
        }
        catch (JsonException exception)
        {
            string location = exception.LineNumber is long line
                ? string.Create(CultureInfo.InvariantCulture, $" at line {line + 1}, column {(exception.BytePositionInLine ?? 0) + 1}")
                : "";
            diagnostics.Add(new ModuleDiagnostic(
                "json.syntax",
                $"The file is not valid JSON{location}: {FirstSentence(exception.Message)} Comments and trailing commas are not allowed.",
                module,
                path));
            return null;
        }
    }

    public static string Describe(JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.Object => "an object",
            JsonValueKind.Array => "an array",
            JsonValueKind.String => "a string",
            JsonValueKind.Number => "a number",
            JsonValueKind.True or JsonValueKind.False => "a boolean",
            JsonValueKind.Null => "null",
            _ => "nothing",
        };
    }

    private static string FirstSentence(string message)
    {
        int end = message.IndexOf(". ", StringComparison.Ordinal);
        return end < 0 ? message : message[..(end + 1)];
    }
}
