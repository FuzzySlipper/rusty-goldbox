using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Cli;

/// <summary>
/// <c>goldbox module pack</c>: validates a module with its requirements, then
/// packs its directory into an Engine content container with the pinned
/// pair's <c>rusty pack-content</c>. The container installs on its own.
/// </summary>
internal static class PackCommand
{
    private const string Usage = "Usage: goldbox module pack <module-dir> [--output <file>.rpak | --install] [--modules <dir>]...";

    public static int Run(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--output", "--modules"], ["--install"]);
        if (error is null && (parsed.Positionals.Count != 1 || (parsed.Has("--install") && parsed.Single("--output") is not null)))
        {
            error = Usage;
        }

        string path = error is null ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(parsed.Positionals[0], workingDirectory)) : "";
        if (error is null && !Directory.Exists(path))
        {
            error = $"{path} is not a module directory. Pack a module's source directory (the one with module.json).";
        }

        string? requested = parsed.Single("--output");
        if (error is null && requested is not null && !InstalledModules.IsContainer(requested))
        {
            error = $"--output must end in {InstalledModules.Extension}, which is how module searches recognise installed modules.";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        ModuleSet set = ModuleSets.Load(path, parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList());
        if (set.Root is null || !set.IsValid)
        {
            return output.ModuleErrors(set);
        }

        ModuleManifest root = set.Root;
        string target = requested is not null
            ? Path.GetFullPath(requested, workingDirectory)
            : Path.Combine(parsed.Has("--install") ? InstalledModules.DefaultDirectory() : workingDirectory, InstalledModules.FileName(root.Id, root.Version));
        if (target.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return output.UsageError($"The container would be written inside the module it packs ({target}). Run from outside the module directory, or pass --output or --install.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return output.Problems([new ModuleDiagnostic("pack.output", $"Can't create the output directory: {exception.Message}", root.Id, target)]);
        }

        if (ContentPacker.Pack(path, target) is string failure)
        {
            return output.Problems([new ModuleDiagnostic("pack.failed", failure, root.Id, target)]);
        }

        return output.Packed(root, target);
    }
}
