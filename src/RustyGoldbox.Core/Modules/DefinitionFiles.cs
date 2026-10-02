using System.Text.Json;

namespace RustyGoldbox.Core.Modules;

/// <summary>
/// Checks a module's definition files: every <c>.json</c> file other than
/// <c>module.json</c> is a JSON object whose <c>type</c> field names its
/// definition type.
/// </summary>
internal static class DefinitionFiles
{
    // No definition types exist yet; ruleset types add themselves here.
    private static readonly HashSet<string> KnownTypes = [];

    public static void Check(ModuleManifest module, List<ModuleDiagnostic> diagnostics)
    {
        foreach (string path in Find(module.Directory, module.Id, diagnostics))
        {
            CheckFile(module, path, diagnostics);
        }
    }

    private static IEnumerable<string> Find(string directory, string module, List<ModuleDiagnostic> diagnostics)
    {
        List<string>? files = DirectoryListing.List(directory, directories: false, module, diagnostics, "*.json");
        List<string>? children = files is null ? null : DirectoryListing.List(directory, directories: true, module, diagnostics);
        if (files is null || children is null)
        {
            yield break;
        }

        foreach (string file in files)
        {
            if (Path.GetFileName(file) != ManifestReader.FileName)
            {
                yield return file;
            }
        }

        foreach (string child in children)
        {
            if (Path.GetFileName(child).StartsWith('.'))
            {
                continue;
            }

            foreach (string file in Find(child, module, diagnostics))
            {
                yield return file;
            }
        }
    }

    private static void CheckFile(ModuleManifest module, string path, List<ModuleDiagnostic> diagnostics)
    {
        using JsonDocument? document = JsonFiles.Parse(path, module.Id, diagnostics);
        if (document is null)
        {
            return;
        }

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out JsonElement type)
            || type.ValueKind != JsonValueKind.String)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "definition.type-missing",
                "A definition file must be a JSON object with a string \"type\" field naming its definition type.",
                module.Id,
                path,
                "$.type"));
            return;
        }

        string name = type.GetString()!;
        if (!KnownTypes.Contains(name))
        {
            string known = KnownTypes.Count == 0 ? "none yet" : string.Join(", ", KnownTypes.Order(StringComparer.Ordinal));
            diagnostics.Add(new ModuleDiagnostic(
                "definition.type-unknown",
                $"'{name}' is not a definition type. Known types: {known}.",
                module.Id,
                path,
                "$.type"));
        }
    }
}
