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

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Parses a JSON file, reporting syntax errors with their line and column.
    /// Returns null when the file can't be read or parsed.
    /// </summary>
    public static JsonDocument? Parse(string path, string? module, List<ModuleDiagnostic> diagnostics)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ModuleDiagnostic("file.read", $"Can't read the file: {exception.Message}", module, path));
            return null;
        }

        return Parse(bytes, path, module, diagnostics);
    }

    /// <summary>Parses one file of a module source; see <see cref="Parse(string, string?, List{ModuleDiagnostic})"/>.</summary>
    public static JsonDocument? Parse(ModuleSource source, string relativePath, string? module, List<ModuleDiagnostic> diagnostics)
    {
        byte[] bytes;
        try
        {
            bytes = source.Read(relativePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ModuleDiagnostic("file.read", $"Can't read the file: {exception.Message}", module, source.PathOf(relativePath)));
            return null;
        }

        return Parse(bytes, source.PathOf(relativePath), module, diagnostics);
    }

    /// <summary>Parses JSON text that came from <paramref name="file"/> (a path or other location, for diagnostics).</summary>
    public static JsonDocument? Parse(ReadOnlyMemory<byte> utf8, string file, string? module, List<ModuleDiagnostic> diagnostics)
    {
        if (utf8.Span.StartsWith(Utf8Bom))
        {
            utf8 = utf8[Utf8Bom.Length..];
        }

        try
        {
            return JsonDocument.Parse(utf8, Options);
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
                file));
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
