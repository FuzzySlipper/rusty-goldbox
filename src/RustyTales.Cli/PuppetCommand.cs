using System.Globalization;
using System.Text.Json;

namespace RustyTales.Cli;

/// <summary>
/// <c>tales puppet</c>: agent-friendly CLI bridge to standalone <c>puppet-studio</c>
/// workspaces — inspects status/audits, surfaces spatial <c>review-flags.json</c> pins,
/// delegates build/bake/heal, and exports baked clips as tight-union uniform Rusty Tales
/// <c>media: "sheet"</c> assets.
/// </summary>
internal static class PuppetCommand
{
    public const string Usage =
        "Usage:\n"
        + "  tales puppet status [<workspace-or-char>] [--puppets <dir>] [--json]\n"
        + "  tales puppet flags [<workspace-or-char>] [--open-only] [--puppets <dir>] [--json]\n"
        + "  tales puppet build <char-dir> [--bake] [--animation <clip>] [--json]\n"
        + "  tales puppet bake <char-dir> [--animation <clip>] [--scale <n>] [--pot] [--json]\n"
        + "  tales puppet heal <char-dir> [--no-bake] [--json]\n"
        + "  tales puppet export-sheet <char-dir> --module <module-dir> [--id <asset-id>] [--clips idle,attack,recoil:hit]\n"
        + "      [--height <px>] [--world-height <cells>] [--faces left|right] [--sampling linear|nearest]\n"
        + "      [--file <rel.png>] [--defs <rel.json>] [--rebake] [--json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static int Run(IReadOnlyList<string> args, Output output, string workingDirectory)
    {
        if (args.Count == 0 || args[0] is "--help" or "-h")
        {
            output.Line(Usage);
            return args.Count == 0 ? TalesCli.Usage : TalesCli.Ok;
        }

        IEnumerable<string> rest = args.Skip(1);
        return args[0] switch
        {
            "status" => Status(rest, output, workingDirectory),
            "flags" => Flags(rest, output, workingDirectory),
            "build" => Build(rest, output, workingDirectory),
            "bake" => Bake(rest, output, workingDirectory),
            "heal" => Heal(rest, output, workingDirectory),
            "export-sheet" => ExportSheet(rest, output, workingDirectory),
            _ => output.UsageError($"Unknown puppet command '{args[0]}'. Puppet commands are status, flags, build, bake, heal and export-sheet.\n{Usage}"),
        };
    }

    private static int Status(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--puppets"], []);
        if (error is null && parsed.Positionals.Count > 1)
        {
            error = "Usage: tales puppet status [<workspace-or-char>] [--puppets <dir>] [--json]";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        string? explicitDir = parsed.Single("--puppets") is string p ? Path.GetFullPath(p, workingDirectory) : null;
        string target = parsed.Positionals.Count == 1
            ? Path.GetFullPath(parsed.Positionals[0], workingDirectory)
            : explicitDir ?? workingDirectory;
        List<string> workspaces = PuppetBridge.DiscoverWorkspaces(target, explicitDir ?? (parsed.Positionals.Count == 1 ? target : null));
        string? exe = PuppetBridge.FindExecutable(target);

        List<PuppetBridge.PuppetCharacterSummary> characters = [];
        foreach (string ws in workspaces)
        {
            foreach (string charDir in PuppetBridge.ListCharacterDirs(ws))
            {
                characters.Add(PuppetBridge.ReadCharacterSummary(charDir));
            }
        }

        if (output.Json)
        {
            output.Line(JsonSerializer.Serialize(new
            {
                ok = true,
                available = exe is not null,
                executable = exe,
                workspaces,
                characterCount = characters.Count,
                openFlagCount = characters.Sum(c => c.OpenFlags),
                characters,
            }, JsonOptions));
            return TalesCli.Ok;
        }

        output.Line($"puppet-studio CLI: {(exe ?? "(not found — set PUPPET_STUDIO_BIN or place puppet-studio on PATH)")}");
        output.Line($"Workspaces: {(workspaces.Count == 0 ? "(none discovered)" : string.Join(", ", workspaces))}");
        if (characters.Count == 0)
        {
            output.Line("No puppet character directories found.");
            return TalesCli.Ok;
        }

        foreach (PuppetBridge.PuppetCharacterSummary c in characters)
        {
            string baked = c.BakedClips.Count == 0 ? "none" : string.Join(",", c.BakedClips);
            output.Line($"  {c.Id} ({c.Name}, {c.Archetype}) — audit:{c.AuditStatus} flags:{c.OpenFlags}/{c.TotalFlags} clm:{(c.HasClm ? "yes" : "no")} baked:[{baked}]");
        }

        return TalesCli.Ok;
    }

    private static int Flags(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--puppets"], ["--open-only"]);
        if (error is null && parsed.Positionals.Count > 1)
        {
            error = "Usage: tales puppet flags [<workspace-or-char>] [--open-only] [--puppets <dir>] [--json]";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        bool openOnly = parsed.Has("--open-only");
        string? explicitDir = parsed.Single("--puppets") is string p ? Path.GetFullPath(p, workingDirectory) : null;
        string target = parsed.Positionals.Count == 1
            ? Path.GetFullPath(parsed.Positionals[0], workingDirectory)
            : explicitDir ?? workingDirectory;
        List<string> workspaces = PuppetBridge.DiscoverWorkspaces(target, explicitDir ?? (parsed.Positionals.Count == 1 ? target : null));

        List<PuppetBridge.PuppetFlag> flags = [];
        foreach (string ws in workspaces)
        {
            flags.AddRange(PuppetBridge.ReadFlags(ws, openOnly));
        }

        if (output.Json)
        {
            output.Line(JsonSerializer.Serialize(new
            {
                ok = true,
                workspaces,
                openOnly,
                count = flags.Count,
                openCount = flags.Count(f => !f.Resolved),
                flags,
            }, JsonOptions));
            return TalesCli.Ok;
        }

        if (flags.Count == 0)
        {
            output.Line(openOnly ? "No open puppet review flags." : "No puppet review flags.");
            return TalesCli.Ok;
        }

        foreach (PuppetBridge.PuppetFlag f in flags)
        {
            string state = f.Resolved ? "resolved" : "open";
            string part = string.IsNullOrEmpty(f.Part) ? "" : $" part:{f.Part}";
            string bbox = f.BboxPx is { Length: 4 } ? $" bbox:[{string.Join(',', f.BboxPx)}]" : "";
            output.Line($"  [{state}] {f.CharacterId} {f.Id} ({f.Animation}@{f.Frame} {f.Tag}{part}{bbox})");
            if (!string.IsNullOrWhiteSpace(f.Note))
            {
                output.Line($"      note: {f.Note}");
            }

            if (!string.IsNullOrWhiteSpace(f.CropPath))
            {
                output.Line($"      crop: {f.CropPath}");
            }
        }

        return TalesCli.Ok;
    }

    private static int Build(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--animation"], ["--bake", "--frames"]);
        if (error is null && parsed.Positionals.Count != 1)
        {
            error = "Usage: tales puppet build <char-dir> [--bake] [--frames] [--animation <clip>] [--json]";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        string charDir = Path.GetFullPath(parsed.Positionals[0], workingDirectory);
        if (!TryRequireExecutable(charDir, output, out string? exe))
        {
            return TalesCli.Invalid;
        }

        List<string> toolArgs = ["build", charDir];
        if (parsed.Has("--bake"))
        {
            toolArgs.Add("--bake");
        }

        if (parsed.Has("--frames"))
        {
            toolArgs.Add("--frames");
        }

        if (parsed.Single("--animation") is string anim)
        {
            toolArgs.Add("--animation");
            toolArgs.Add(anim);
        }

        return EmitToolResult("build", charDir, PuppetBridge.RunTool(exe!, toolArgs, charDir), output);
    }

    private static int Bake(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--animation", "--scale"], ["--pot", "--frames"]);
        if (error is null && parsed.Positionals.Count != 1)
        {
            error = "Usage: tales puppet bake <char-dir> [--animation <clip>] [--scale <n>] [--pot] [--frames] [--json]";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        string charDir = Path.GetFullPath(parsed.Positionals[0], workingDirectory);
        if (!TryRequireExecutable(charDir, output, out string? exe))
        {
            return TalesCli.Invalid;
        }

        List<string> toolArgs = ["bake", charDir];
        if (parsed.Single("--animation") is string anim)
        {
            toolArgs.Add("--animation");
            toolArgs.Add(anim);
        }

        if (parsed.Single("--scale") is string scale)
        {
            toolArgs.Add("--scale");
            toolArgs.Add(scale);
        }

        if (parsed.Has("--pot"))
        {
            toolArgs.Add("--pot");
        }

        if (parsed.Has("--frames"))
        {
            toolArgs.Add("--frames");
        }

        return EmitToolResult("bake", charDir, PuppetBridge.RunTool(exe!, toolArgs, charDir), output);
    }

    private static int Heal(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, [], ["--no-bake"]);
        if (error is null && parsed.Positionals.Count != 1)
        {
            error = "Usage: tales puppet heal <char-dir> [--no-bake] [--json]";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        string charDir = Path.GetFullPath(parsed.Positionals[0], workingDirectory);
        if (!TryRequireExecutable(charDir, output, out string? exe))
        {
            return TalesCli.Invalid;
        }

        List<string> toolArgs = ["heal", charDir];
        if (parsed.Has("--no-bake"))
        {
            toolArgs.Add("--no-bake");
        }

        return EmitToolResult("heal", charDir, PuppetBridge.RunTool(exe!, toolArgs, charDir), output);
    }

    private static int ExportSheet(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(
            args,
            ["--module", "--id", "--clips", "--height", "--world-height", "--faces", "--sampling", "--file", "--defs"],
            ["--rebake"]);
        if (error is null && parsed.Positionals.Count != 1)
        {
            error = "Usage: tales puppet export-sheet <char-dir> --module <module-dir> [--id <asset-id>] [--clips idle,attack,recoil:hit] [--height <px>] [--world-height <cells>] [--faces left|right] [--sampling linear|nearest] [--file <rel.png>] [--defs <rel.json>] [--rebake] [--json]";
        }

        string? moduleArg = parsed.Single("--module");
        if (error is null && string.IsNullOrWhiteSpace(moduleArg))
        {
            error = "--module <module-dir> is required.";
        }

        int? targetHeight = 160;
        if (error is null && parsed.Single("--height") is string hText)
        {
            if (!int.TryParse(hText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hVal) || hVal < 0)
            {
                error = $"--height '{hText}' must be a nonnegative integer (0 keeps native tight-union pixels).";
            }
            else
            {
                targetHeight = hVal == 0 ? null : hVal;
            }
        }

        double worldHeight = 1.0;
        if (error is null && parsed.Single("--world-height") is string whText)
        {
            if (!double.TryParse(whText, NumberStyles.Float, CultureInfo.InvariantCulture, out double whVal) || whVal <= 0)
            {
                error = $"--world-height '{whText}' must be a positive number of world cells.";
            }
            else
            {
                worldHeight = whVal;
            }
        }

        string faces = parsed.Single("--faces") ?? "right";
        if (error is null && faces is not ("left" or "right"))
        {
            error = $"--faces '{faces}' must be 'left' or 'right'.";
        }

        string sampling = parsed.Single("--sampling") ?? "linear";
        if (error is null && sampling is not ("linear" or "nearest"))
        {
            error = $"--sampling '{sampling}' must be 'linear' or 'nearest'.";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        string charDir = Path.GetFullPath(parsed.Positionals[0], workingDirectory);
        string moduleDir = Path.GetFullPath(moduleArg!, workingDirectory);
        List<PuppetBridge.ClipSpec> clips = PuppetBridge.ParseClipSpecs(parsed.Single("--clips"));

        try
        {
            PuppetBridge.ExportSheetResult exported = PuppetBridge.ExportSheet(new PuppetBridge.ExportSheetOptions(
                CharacterDir: charDir,
                ModuleDir: moduleDir,
                AssetId: parsed.Single("--id"),
                Clips: clips,
                TargetHeight: targetHeight,
                WorldHeight: worldHeight,
                Faces: faces,
                Sampling: sampling,
                OutputPngRelative: parsed.Single("--file"),
                DefinitionsFileRelative: parsed.Single("--defs") ?? "puppets.json",
                Rebake: parsed.Has("--rebake")));

            if (output.Json)
            {
                output.Line(JsonSerializer.Serialize(new
                {
                    ok = true,
                    exported,
                }, JsonOptions));
                return TalesCli.Ok;
            }

            output.Line($"Exported puppet '{exported.AssetId}' -> {exported.PngPath}");
            output.Line($"  Grid: {exported.Columns}x{exported.Rows} ({exported.FrameCount} frames @ {exported.FrameWidth}x{exported.FrameHeight}px, {exported.Sampling})");
            output.Line($"  Anchor: [{exported.AnchorX}, {exported.AnchorY}] · faces {exported.Faces} · height {exported.WorldHeight} cells");
            output.Line($"  Clips: {string.Join(", ", exported.Clips.Select(c => $"{c.AnimationName}({c.FrameCount}f @ {c.Fps}fps{(c.Loop ? ", loop" : "")})"))}");
            output.Line($"  Definition upserted in: {exported.DefinitionsPath}");
            return TalesCli.Ok;
        }
        catch (Exception ex)
        {
            if (output.Json)
            {
                output.Line(JsonSerializer.Serialize(new { ok = false, error = ex.Message }, JsonOptions));
            }
            else
            {
                output.Line($"error: {ex.Message}");
            }

            return TalesCli.Invalid;
        }
    }

    private static bool TryRequireExecutable(string startDir, Output output, out string? exe)
    {
        exe = PuppetBridge.FindExecutable(startDir);
        if (exe is not null)
        {
            return true;
        }

        const string msg = "puppet-studio executable was not found. Install puppet-studio on PATH or set PUPPET_STUDIO_BIN.";
        if (output.Json)
        {
            output.Line(JsonSerializer.Serialize(new { ok = false, available = false, error = msg }, JsonOptions));
        }
        else
        {
            output.Line($"error: {msg}");
        }

        return false;
    }

    private static int EmitToolResult(string command, string charDir, PuppetBridge.PuppetToolResult result, Output output)
    {
        if (output.Json)
        {
            output.Line(JsonSerializer.Serialize(new
            {
                ok = result.Ok,
                command,
                characterDir = charDir,
                exitCode = result.ExitCode,
                result = result.Json,
                stderr = string.IsNullOrWhiteSpace(result.Stderr) ? null : result.Stderr.Trim(),
            }, JsonOptions));
            return result.Ok ? TalesCli.Ok : TalesCli.Invalid;
        }

        if (!string.IsNullOrWhiteSpace(result.Stdout))
        {
            output.Line(result.Stdout.TrimEnd());
        }

        if (!result.Ok && !string.IsNullOrWhiteSpace(result.Stderr))
        {
            output.Line(result.Stderr.TrimEnd());
        }

        return result.Ok ? TalesCli.Ok : TalesCli.Invalid;
    }
}
