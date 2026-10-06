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
        IReadOnlyList<string> files = module.Source.ListFiles(module.Id, diagnostics);
        foreach (string file in files)
        {
            CheckPortableName(module, file, diagnostics);
        }

        foreach (string file in files.Where(IsDefinitionFile))
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

    /// <summary>
    /// Windows can't create a file or directory named after a device, whatever
    /// its extension, so a module holding one loses that file when it's checked
    /// out or unpacked there.
    /// </summary>
    private static void CheckPortableName(ModuleManifest module, string file, List<ModuleDiagnostic> diagnostics)
    {
        foreach (string part in file.Split('/'))
        {
            string stem = part.Split('.')[0].TrimEnd(' ');
            if (ReservedOnWindows.Contains(stem))
            {
                diagnostics.Add(new ModuleDiagnostic(
                    "module.file-name",
                    $"'{part}' is a device name on Windows, so the file can't exist there and the module loses it. Rename it, for example to '{part.Insert(stem.Length, "_")}'; a definition's id comes from its \"id\" field, not the file name.",
                    module.Id,
                    module.Source.PathOf(file)));
                return;
            }
        }
    }

    private static readonly HashSet<string> ReservedOnWindows = new(
        ["con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"],
        StringComparer.OrdinalIgnoreCase);

    private static bool IsDefinitionFile(string file)
    {
        string[] parts = file.Split('/');
        return file.EndsWith(".json", StringComparison.Ordinal)
            && file != ManifestReader.FileName
            && !parts[..^1].Any(part => part.StartsWith('.'));
    }
}
