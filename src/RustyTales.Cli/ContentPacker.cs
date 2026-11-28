using System.Diagnostics;
using System.Reflection;

namespace RustyTales.Cli;

/// <summary>Runs the pinned Engine content packer used by module and workspace commands.</summary>
internal static class ContentPacker
{
    /// <summary>Runs <c>rusty pack-content</c>; returns why it failed, or null.</summary>
    public static string? Pack(string directory, string target)
    {
        string rusty = FindRusty();
        ProcessStartInfo start = new(rusty, ["pack-content", directory, "--output", target, "--compress"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        try
        {
            using Process process = Process.Start(start)!;
            Task<string> errors = process.StandardError.ReadToEndAsync();
            string printed = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                return null;
            }

            string said = (errors.Result + printed).Trim();
            string hint = said.Contains("unknown command", StringComparison.Ordinal)
                ? " This `rusty` is an older bootstrap without pack-content: run `rusty install` here for the pinned pair, or refresh the bootstrap."
                : "";
            return $"{rusty} pack-content failed ({process.ExitCode}): {said}{hint}";
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return $"Can't run {rusty}: {exception.Message}. Install the Engine's `rusty` (see README) and run `rusty install`.";
        }
    }

    /// <summary>Returns the pinned pair's <c>rusty</c> executable when installed, else <c>"rusty"</c> from PATH.</summary>
    internal static string FindRusty() => PairRusty() ?? "rusty";

    /// <summary>Returns the pinned pair's <c>rusty-live-debug</c> executable when installed, else <c>"rusty-live-debug"</c> from PATH.</summary>
    internal static string FindLiveDebug()
    {
        string? pair = PairRusty();
        if (pair is not null && Path.GetDirectoryName(pair) is string dir)
        {
            string candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "rusty-live-debug.exe" : "rusty-live-debug");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "rusty-live-debug";
    }

    /// <summary>The pinned pair's own <c>rusty</c>, recorded at build time, when it is installed here.</summary>
    private static string? PairRusty()
    {
        string? path = typeof(ContentPacker).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "RustyEnginePairRusty")?.Value;
        return path is not null && File.Exists(path) ? path : null;
    }
}
