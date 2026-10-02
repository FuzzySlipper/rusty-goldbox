using System.Text.Json;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Core.Modules;

/// <summary>
/// Reads a module's definition files: every <c>.json</c> file other than
/// <c>module.json</c>, outside hidden directories, each holding one
/// definition whose <c>type</c> field names its definition type.
/// </summary>
internal static class DefinitionFiles
{
    public static List<Definition> Read(ModuleManifest module, List<ModuleDiagnostic> diagnostics)
    {
        List<Definition> definitions = [];
        foreach (string file in module.Source.ListFiles(module.Id, diagnostics).Where(IsDefinitionFile))
        {
            using JsonDocument? document = JsonFiles.Parse(module.Source, file, module.Id, diagnostics);
            if (document is null)
            {
                continue;
            }

            Definition? definition = DefinitionReader.Read(document.RootElement, module.Id, module.Source.PathOf(file), diagnostics);
            if (definition is not null)
            {
                definitions.Add(definition);
            }
        }

        return definitions;
    }

    private static bool IsDefinitionFile(string file)
    {
        string[] parts = file.Split('/');
        return file.EndsWith(".json", StringComparison.Ordinal)
            && file != ManifestReader.FileName
            && !parts[..^1].Any(part => part.StartsWith('.'));
    }
}
