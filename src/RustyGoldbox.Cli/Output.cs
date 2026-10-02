using System.Text.Encodings.Web;
using System.Text.Json;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Cli;

/// <summary>Prints command results as text or, with --json, as one JSON object.</summary>
internal sealed class Output(TextWriter writer, string workingDirectory, bool json)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public int UsageError(string message)
    {
        if (json)
        {
            WriteJson(new { ok = false, diagnostics = new[] { new { rule = "usage", message } } });
        }
        else
        {
            writer.WriteLine($"error[usage] {message}");
        }

        return GoldboxCli.Usage;
    }

    public int Created(string? directory, string id, ModuleKind kind, List<ModuleDiagnostic> diagnostics)
    {
        bool ok = directory is not null;
        if (json)
        {
            WriteJson(new
            {
                ok,
                module = ok ? new { id, kind = ModuleKinds.Name(kind), path = Display(directory!) } : null,
                diagnostics = diagnostics.Select(ToJson),
            });
        }
        else if (ok)
        {
            writer.WriteLine($"Created {ModuleKinds.Name(kind)} module '{id}' at {Display(directory!)}.");
            writer.WriteLine($"Check it with: goldbox module validate {Display(directory!)}");
        }
        else
        {
            WriteDiagnostics(diagnostics);
        }

        return ok ? GoldboxCli.Ok : GoldboxCli.Invalid;
    }

    public int Validated(ModuleSet set)
    {
        if (json)
        {
            WriteJson(new
            {
                ok = set.IsValid,
                module = set.Root is null ? null : ModuleJson(set.Root),
                modules = set.LoadOrder.Select(loaded => $"{loaded.Manifest.Id}@{loaded.Manifest.Version}"),
                diagnostics = set.Diagnostics.Select(ToJson),
            });
        }
        else if (set.IsValid)
        {
            ModuleManifest root = set.Root!;
            int others = set.LoadOrder.Count - 1;
            string with = others == 0 ? "" : $" with {others} required module{(others == 1 ? "" : "s")}";
            writer.WriteLine($"ok: {root.Id} {root.Version} ({ModuleKinds.Name(root.Kind)}){with} is valid.");
        }
        else
        {
            WriteDiagnostics(set.Diagnostics);
        }

        return set.IsValid ? GoldboxCli.Ok : GoldboxCli.Invalid;
    }

    public int Dependencies(ModuleSet set)
    {
        if (json)
        {
            WriteJson(new
            {
                ok = set.IsValid,
                searchDirectories = set.SearchDirectories.Select(Display),
                loadOrder = set.LoadOrder.Select(loaded => new
                {
                    id = loaded.Manifest.Id,
                    version = loaded.Manifest.Version.ToString(),
                    kind = ModuleKinds.Name(loaded.Manifest.Kind),
                    path = Display(loaded.Manifest.Directory),
                    requires = loaded.Requires.Select(requirement => new
                    {
                        id = requirement.Id,
                        range = requirement.Range.Text,
                        version = requirement.Version.ToString(),
                    }),
                }),
                diagnostics = set.Diagnostics.Select(ToJson),
            });
            return set.IsValid ? GoldboxCli.Ok : GoldboxCli.Invalid;
        }

        if (set.LoadOrder.Count > 0)
        {
            writer.WriteLine("Load order (each module after what it requires):");
            int number = 1;
            foreach (LoadedModule loaded in set.LoadOrder)
            {
                ModuleManifest manifest = loaded.Manifest;
                writer.WriteLine($"  {number}. {manifest.Id} {manifest.Version} ({ModuleKinds.Name(manifest.Kind)})  {Display(manifest.Directory)}");
                foreach (ResolvedRequirement requirement in loaded.Requires)
                {
                    writer.WriteLine($"       requires {requirement.Id} {requirement.Range} -> {requirement.Version}");
                }

                number++;
            }

            string directories = set.SearchDirectories.Count == 0 ? "(none)" : string.Join(", ", set.SearchDirectories.Select(Display));
            writer.WriteLine($"Search directories: {directories}");
        }

        if (!set.IsValid)
        {
            WriteDiagnostics(set.Diagnostics);
        }

        return set.IsValid ? GoldboxCli.Ok : GoldboxCli.Invalid;
    }

    private void WriteDiagnostics(IReadOnlyList<ModuleDiagnostic> diagnostics)
    {
        foreach (ModuleDiagnostic diagnostic in diagnostics)
        {
            List<string> where = [];
            if (diagnostic.Module is not null)
            {
                where.Add(diagnostic.Module);
            }

            if (diagnostic.File is not null)
            {
                where.Add(Display(diagnostic.File));
            }

            if (diagnostic.JsonPath is not null)
            {
                where.Add(diagnostic.JsonPath);
            }

            writer.WriteLine($"error[{diagnostic.Rule}] {string.Join(" ", where)}");
            writer.WriteLine($"  {diagnostic.Message}");
        }

        int count = diagnostics.Count;
        writer.WriteLine($"{count} error{(count == 1 ? "" : "s")}.");
    }

    private object ModuleJson(ModuleManifest manifest)
    {
        return new
        {
            id = manifest.Id,
            version = manifest.Version.ToString(),
            kind = ModuleKinds.Name(manifest.Kind),
            title = manifest.Title,
            path = Display(manifest.Directory),
        };
    }

    private object ToJson(ModuleDiagnostic diagnostic)
    {
        return new
        {
            rule = diagnostic.Rule,
            module = diagnostic.Module,
            file = diagnostic.File is null ? null : Display(diagnostic.File),
            jsonPath = diagnostic.JsonPath,
            message = diagnostic.Message,
        };
    }

    private string Display(string path)
    {
        string relative = Path.GetRelativePath(workingDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative;
    }

    private void WriteJson(object value)
    {
        writer.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }
}
