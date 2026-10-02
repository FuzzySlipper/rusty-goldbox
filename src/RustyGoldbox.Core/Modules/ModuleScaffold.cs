using System.Text.Encodings.Web;
using System.Text.Json;

namespace RustyGoldbox.Core.Modules;

/// <summary>Creates a new module directory with a starting <c>module.json</c>.</summary>
public static class ModuleScaffold
{
    public const string InitialVersion = "0.1.0";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Writes <c>&lt;parentDirectory&gt;/&lt;id&gt;/module.json</c>. Returns the
    /// module directory, or null with a diagnostic when it already exists.
    /// </summary>
    public static string? Create(
        string parentDirectory,
        ModuleKind kind,
        string id,
        string title,
        string provenance,
        IReadOnlyList<(string Id, VersionRange Range)> requires,
        List<ModuleDiagnostic> diagnostics)
    {
        string directory = Path.GetFullPath(Path.Combine(parentDirectory, id));
        try
        {
            if (File.Exists(directory) || (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()))
            {
                diagnostics.Add(new ModuleDiagnostic(
                    "module.exists",
                    $"{directory} already exists and is not an empty directory. Pick another ID or parent directory (--dir).",
                    id,
                    directory));
                return null;
            }

            Write(directory, kind, id, title, provenance, requires);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "module.create",
                $"Can't create the module: {exception.Message} Check that the parent directory exists and is writable, or pick another with --dir.",
                id,
                directory));
            return null;
        }

        return directory;
    }

    private static void Write(
        string directory,
        ModuleKind kind,
        string id,
        string title,
        string provenance,
        IReadOnlyList<(string Id, VersionRange Range)> requires)
    {
        Directory.CreateDirectory(directory);
        using (FileStream stream = File.Create(Path.Combine(directory, ManifestReader.FileName)))
        using (Utf8JsonWriter writer = new(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", ManifestReader.CurrentFormat);
            writer.WriteString("id", id);
            writer.WriteString("kind", ModuleKinds.Name(kind));
            writer.WriteString("version", InitialVersion);
            writer.WriteString("title", title);
            writer.WriteStartArray("requires");
            foreach ((string requiredId, VersionRange range) in requires)
            {
                writer.WriteStartObject();
                writer.WriteString("id", requiredId);
                writer.WriteString("version", range.Text);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("provenance", provenance);
            writer.WriteEndObject();
            writer.Flush();
            stream.Write("\n"u8);
        }

        if (kind == ModuleKind.Campaign)
        {
            WriteCampaignStart(directory, title);
        }
    }

    /// <summary>A campaign needs a campaign definition and a starting area; write the smallest valid pair.</summary>
    private static void WriteCampaignStart(string directory, string title)
    {
        Directory.CreateDirectory(Path.Combine(directory, "areas"));
        File.WriteAllText(Path.Combine(directory, "areas", "start.json"), """
            {
              "type": "area",
              "id": "start",
              "name": "Start",
              "map": [
                "+--+",
                "|  |",
                "+--+"
              ],
              "entries": { "start": { "at": [0, 0], "facing": "north" } }
            }

            """);
        File.WriteAllText(Path.Combine(directory, "campaign.json"), $$"""
            {
              "type": "campaign",
              "id": "campaign",
              "name": {{JsonSerializer.Serialize(title)}},
              "start": { "area": "start", "entry": "start" },
              "party": { "min": 1, "max": 6 }
            }

            """);
    }
}
