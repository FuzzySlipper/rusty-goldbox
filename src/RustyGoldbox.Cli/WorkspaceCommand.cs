using RustyGoldbox.Core.Authoring;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Cli;

/// <summary><c>goldbox workspace</c>: create and discover an authoring workspace.</summary>
internal static class WorkspaceCommand
{
    public static int Run(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, [], []);
        if (error is null && parsed.Positionals.Count == 0)
        {
            error = "Usage: goldbox workspace new <dir> | inspect [<path>]";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        return parsed.Positionals[0] switch
        {
            "new" => New(parsed.Positionals.Skip(1), output, workingDirectory),
            "inspect" => Inspect(parsed.Positionals.Skip(1), output, workingDirectory),
            _ => output.UsageError($"Unknown workspace command '{parsed.Positionals[0]}'. Use new or inspect."),
        };
    }

    private static int New(IEnumerable<string> positionals, Output output, string workingDirectory)
    {
        string[] values = positionals.ToArray();
        if (values.Length != 1)
        {
            return output.UsageError("Usage: goldbox workspace new <dir>");
        }

        List<ModuleDiagnostic> diagnostics = [];
        Workspace? workspace = WorkspaceScaffold.Create(Path.GetFullPath(values[0], workingDirectory), diagnostics);
        return output.WorkspaceCreated(workspace, diagnostics);
    }

    private static int Inspect(IEnumerable<string> positionals, Output output, string workingDirectory)
    {
        string[] values = positionals.ToArray();
        if (values.Length > 1)
        {
            return output.UsageError("Usage: goldbox workspace inspect [<path>]");
        }

        List<ModuleDiagnostic> diagnostics = [];
        string path = values.Length == 0 ? workingDirectory : Path.GetFullPath(values[0], workingDirectory);
        WorkspaceInspection? inspection = WorkspaceInspector.Read(path, diagnostics);
        return output.WorkspaceInspected(inspection, diagnostics);
    }
}
