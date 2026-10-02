using System.Text;
using RustyGoldbox.Cli;

namespace RustyGoldbox.Tests;

/// <summary>Runs goldbox commands and records each with its output, for golden transcripts.</summary>
internal static class CliTranscript
{
    /// <summary>Runs the commands in a fresh scratch directory.</summary>
    public static string Run(params string[][] commands)
    {
        using TempModules scratch = new();
        return Run(scratch.Root, commands);
    }

    /// <summary>Runs the commands with <paramref name="workingDirectory"/> as the working directory; paths under it and the repository print relative.</summary>
    public static string Run(string workingDirectory, params string[][] commands)
    {
        StringBuilder transcript = new();
        foreach (string[] command in commands)
        {
            using StringWriter output = new();
            int code = GoldboxCli.Run(command, output, workingDirectory);
            transcript.AppendLine($"$ goldbox {Relative(string.Join(' ', command), workingDirectory)}");
            transcript.Append(Relative(output.ToString(), workingDirectory));
            transcript.AppendLine($"[exit {code}]");
            transcript.AppendLine();
        }

        return transcript.ToString();
    }

    private static string Relative(string text, string workingDirectory)
    {
        return text
            .Replace(workingDirectory + Path.DirectorySeparatorChar, "", StringComparison.Ordinal)
            .Replace(Rules.RepositoryRoot + Path.DirectorySeparatorChar, "", StringComparison.Ordinal);
    }
}
