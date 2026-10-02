using System.Text.Json;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Core.Modules;

/// <summary>
/// Reads a module's definition files: every <c>.json</c> file other than
/// <c>module.json</c>, each holding one definition whose <c>type</c> field
/// names its definition type.
/// </summary>
internal static class DefinitionFiles
{
    public static List<Definition> Read(ModuleManifest module, List<ModuleDiagnostic> diagnostics)
    {
        List<Definition> definitions = [];
        foreach (string path in Find(module.Directory, module.Id, diagnostics))
        {
            using JsonDocument? document = JsonFiles.Parse(path, module.Id, diagnostics);
            if (document is null)
            {
                continue;
            }

            Definition? definition = DefinitionReader.Read(document.RootElement, module.Id, path, diagnostics);
            if (definition is not null)
            {
                definitions.Add(definition);
            }
        }

        return definitions;
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
}
