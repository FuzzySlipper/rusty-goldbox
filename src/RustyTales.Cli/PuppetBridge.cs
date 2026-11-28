using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustyTales.Cli;

/// <summary>
/// Standalone integration bridge between Rusty Tales and <c>puppet-studio</c>:
/// discovers puppet workspaces and CLI binaries, reads <c>audit.json</c> and
/// <c>review-flags.json</c>, delegates build/bake/heal commands, and packs baked
/// clips into tight-union uniform-cell Rusty Tales <c>media: "sheet"</c> assets.
/// </summary>
internal static class PuppetBridge
{
    internal sealed record PuppetFlag(
        string CharacterId,
        string CharacterDir,
        string Id,
        string Animation,
        int Frame,
        string Tag,
        string? Part,
        int[]? BboxPx,
        string? CropPath,
        string Note,
        bool Resolved);

    internal sealed record PuppetCharacterSummary(
        string Id,
        string Name,
        string Directory,
        string Archetype,
        string AuditStatus,
        int OpenFlags,
        int TotalFlags,
        IReadOnlyList<string> Clips,
        IReadOnlyList<string> BakedClips,
        bool HasClm,
        string? ClmPath,
        IReadOnlyList<string> FailedAssertions,
        int OrphanIslandCount);

    internal sealed record PuppetToolResult(bool Ok, int ExitCode, string Stdout, string Stderr, JsonNode? Json);

    internal sealed record ClipSpec(string SourceClip, string AnimationName, bool Loop);

    internal sealed record ExportSheetOptions(
        string CharacterDir,
        string ModuleDir,
        string? AssetId = null,
        IReadOnlyList<ClipSpec>? Clips = null,
        int? TargetHeight = 160,
        double WorldHeight = 1.0,
        string Faces = "right",
        string Sampling = "linear",
        string? OutputPngRelative = null,
        string DefinitionsFileRelative = "puppets.json",
        bool Rebake = false,
        int MarginPx = 2);

    internal sealed record ExportClipSummary(string SourceClip, string AnimationName, int StartFrame, int FrameCount, double Fps, bool Loop);

    internal sealed record ExportSheetResult(
        string AssetId,
        string ModuleDir,
        string PngPath,
        string RelativePngPath,
        string DefinitionsPath,
        int FrameWidth,
        int FrameHeight,
        int Columns,
        int Rows,
        int FrameCount,
        int AnchorX,
        int AnchorY,
        string Faces,
        double WorldHeight,
        string Sampling,
        IReadOnlyList<ExportClipSummary> Clips);

    public static readonly IReadOnlyList<ClipSpec> DefaultClips =
    [
        new("idle", "idle", Loop: true),
        new("attack", "attack", Loop: false),
        new("recoil", "hit", Loop: false),
    ];

    /// <summary>Finds the <c>puppet-studio</c> executable via env var, PATH, or sibling checkout.</summary>
    public static string? FindExecutable(string? startDirectory = null)
    {
        string? fromEnv = Environment.GetEnvironmentVariable("PUPPET_STUDIO_BIN");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return Path.GetFullPath(fromEnv);
        }

        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate = Path.Combine(dir, "puppet-studio");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Check sibling checkouts relative to working/module directory (never a build-time dependency).
        DirectoryInfo? cursor = new(Path.GetFullPath(startDirectory ?? Directory.GetCurrentDirectory()));
        for (int depth = 0; depth < 6 && cursor is not null; depth++)
        {
            string sibling = Path.Combine(cursor.FullName, "puppet-studio", "bin", "puppet-studio");
            if (File.Exists(sibling))
            {
                return sibling;
            }

            if (cursor.Parent is not null)
            {
                string parentSibling = Path.Combine(cursor.Parent.FullName, "puppet-studio", "bin", "puppet-studio");
                if (File.Exists(parentSibling))
                {
                    return parentSibling;
                }
            }

            cursor = cursor.Parent;
        }

        return null;
    }

    /// <summary>Discovers candidate puppet workspace directories for a module or working directory.</summary>
    public static List<string> DiscoverWorkspaces(string startPath, string? explicitDir = null)
    {
        List<string> found = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        void TryAdd(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string full = Path.GetFullPath(path);
            if (Directory.Exists(full) && seen.Add(full))
            {
                found.Add(full);
            }
        }

        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            TryAdd(explicitDir);
            return found;
        }

        string? envDir = Environment.GetEnvironmentVariable("TALES_PUPPETS_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            TryAdd(envDir);
        }

        string baseDir = File.Exists(startPath) ? Path.GetDirectoryName(Path.GetFullPath(startPath))! : Path.GetFullPath(startPath);
        if (IsCharacterDir(baseDir))
        {
            TryAdd(Path.GetDirectoryName(baseDir));
            return found;
        }

        if (ListCharacterDirs(baseDir).Count > 0)
        {
            TryAdd(baseDir);
        }

        DirectoryInfo? cursor = new(baseDir);
        for (int depth = 0; depth < 5 && cursor is not null; depth++)
        {
            string talesJson = Path.Combine(cursor.FullName, "tales.json");
            if (File.Exists(talesJson))
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(talesJson));
                    if (doc.RootElement.TryGetProperty("puppets", out JsonElement puppetsEl) && puppetsEl.ValueKind == JsonValueKind.String)
                    {
                        TryAdd(Path.Combine(cursor.FullName, puppetsEl.GetString()!));
                    }
                    else if (doc.RootElement.TryGetProperty("authoring", out JsonElement authEl)
                        && authEl.TryGetProperty("puppets", out JsonElement authPuppets)
                        && authPuppets.ValueKind == JsonValueKind.String)
                    {
                        TryAdd(Path.Combine(cursor.FullName, authPuppets.GetString()!));
                    }
                }
                catch
                {
                    // Ignore malformed tales.json during passive discovery.
                }
            }

            foreach (string rel in new[] { "puppets", Path.Combine("art-src", "puppets"), Path.Combine("art", "puppets") })
            {
                string candidate = Path.Combine(cursor.FullName, rel);
                if (Directory.Exists(candidate) && ListCharacterDirs(candidate).Count > 0)
                {
                    TryAdd(candidate);
                }
            }

            cursor = cursor.Parent;
        }

        return found;
    }

    public static bool IsCharacterDir(string path) =>
        Directory.Exists(path) && File.Exists(Path.Combine(path, "character.json"));

    public static List<string> ListCharacterDirs(string workspaceOrCharDir)
    {
        string full = Path.GetFullPath(workspaceOrCharDir);
        if (IsCharacterDir(full))
        {
            return [full];
        }

        if (!Directory.Exists(full))
        {
            return [];
        }

        return Directory.EnumerateDirectories(full)
            .Where(IsCharacterDir)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    public static PuppetCharacterSummary ReadCharacterSummary(string charDir)
    {
        string full = Path.GetFullPath(charDir);
        string id = Path.GetFileName(full);
        string name = id;
        string archetype = "melee";

        string charJsonPath = Path.Combine(full, "character.json");
        if (File.Exists(charJsonPath))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(charJsonPath));
                if (doc.RootElement.TryGetProperty("name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String)
                {
                    name = nameEl.GetString()!;
                }

                if (doc.RootElement.TryGetProperty("archetype", out JsonElement archEl) && archEl.ValueKind == JsonValueKind.String)
                {
                    archetype = archEl.GetString()!;
                }
            }
            catch
            {
                // Keep defaults when character.json cannot be parsed.
            }
        }

        string auditStatus = "unbuilt";
        List<string> clips = [];
        List<string> failedAssertions = [];
        int orphanCount = 0;

        string auditPath = Path.Combine(full, "audit.json");
        if (File.Exists(auditPath))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(auditPath));
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("status", out JsonElement statusEl) && statusEl.ValueKind == JsonValueKind.String)
                {
                    auditStatus = statusEl.GetString()!;
                }

                if (root.TryGetProperty("clips", out JsonElement clipsEl) && clipsEl.ValueKind == JsonValueKind.Array)
                {
                    clips.AddRange(clipsEl.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));
                }

                if (root.TryGetProperty("assertions", out JsonElement assertionsEl)
                    && assertionsEl.TryGetProperty("failed", out JsonElement failedEl)
                    && failedEl.ValueKind == JsonValueKind.Array)
                {
                    failedAssertions.AddRange(failedEl.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));
                }

                if (root.TryGetProperty("orphan_islands", out JsonElement orphansEl) && orphansEl.ValueKind == JsonValueKind.Array)
                {
                    orphanCount = orphansEl.GetArrayLength();
                }
            }
            catch
            {
                auditStatus = "invalid-audit";
            }
        }

        List<PuppetFlag> flags = ReadFlags(full, openOnly: false);
        int openFlags = flags.Count(f => !f.Resolved);

        List<string> bakedClips = [];
        string bakedRoot = Path.Combine(full, "baked");
        if (Directory.Exists(bakedRoot))
        {
            foreach (string clipDir in Directory.EnumerateDirectories(bakedRoot).OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                if (File.Exists(Path.Combine(clipDir, "sheet-manifest.json")))
                {
                    bakedClips.Add(Path.GetFileName(clipDir));
                }
            }
        }

        string clmPath = Path.Combine(full, "build", "puppet.clm");
        bool hasClm = File.Exists(clmPath);

        return new PuppetCharacterSummary(
            id,
            name,
            full,
            archetype,
            auditStatus,
            openFlags,
            flags.Count,
            clips,
            bakedClips,
            hasClm,
            hasClm ? clmPath : null,
            failedAssertions,
            orphanCount);
    }

    public static List<PuppetFlag> ReadFlags(string workspaceOrCharDir, bool openOnly = false)
    {
        List<PuppetFlag> result = [];
        foreach (string charDir in ListCharacterDirs(workspaceOrCharDir))
        {
            string flagsPath = Path.Combine(charDir, "review-flags.json");
            if (!File.Exists(flagsPath))
            {
                continue;
            }

            string charId = Path.GetFileName(charDir);
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(flagsPath));
                if (!doc.RootElement.TryGetProperty("flags", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement item in arr.EnumerateArray())
                {
                    bool resolved = item.TryGetProperty("resolved", out JsonElement resEl) && resEl.ValueKind == JsonValueKind.True;
                    if (openOnly && resolved)
                    {
                        continue;
                    }

                    string flagId = item.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString()! : "";
                    string animation = item.TryGetProperty("animation", out JsonElement animEl) && animEl.ValueKind == JsonValueKind.String ? animEl.GetString()! : "";
                    int frame = item.TryGetProperty("frame", out JsonElement frameEl) && frameEl.TryGetInt32(out int frameVal) ? frameVal : 0;
                    string tag = item.TryGetProperty("tag", out JsonElement tagEl) && tagEl.ValueKind == JsonValueKind.String ? tagEl.GetString()! : "";
                    string? part = item.TryGetProperty("part", out JsonElement partEl) && partEl.ValueKind == JsonValueKind.String ? partEl.GetString() : null;
                    string note = item.TryGetProperty("note", out JsonElement noteEl) && noteEl.ValueKind == JsonValueKind.String ? noteEl.GetString()! : "";

                    int[]? bbox = null;
                    if (item.TryGetProperty("bbox_px", out JsonElement bboxEl) && bboxEl.ValueKind == JsonValueKind.Array && bboxEl.GetArrayLength() == 4)
                    {
                        bbox = bboxEl.EnumerateArray().Select(v => v.GetInt32()).ToArray();
                    }

                    string? cropPath = null;
                    if (item.TryGetProperty("crop", out JsonElement cropEl) && cropEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cropEl.GetString()))
                    {
                        string rel = cropEl.GetString()!;
                        cropPath = Path.IsPathRooted(rel) ? rel : Path.GetFullPath(Path.Combine(charDir, rel));
                    }

                    result.Add(new PuppetFlag(charId, charDir, flagId, animation, frame, tag, part, bbox, cropPath, note, resolved));
                }
            }
            catch
            {
                // Skip malformed review-flags.json rather than crashing discovery.
            }
        }

        return result;
    }

    public static PuppetToolResult RunTool(string executable, IReadOnlyList<string> args, string? workingDirectory = null, int timeoutMs = 120_000)
    {
        ProcessStartInfo psi = new()
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using Process proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start '{executable}'.");
        Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(timeoutMs))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill races.
            }

            throw new TimeoutException($"puppet-studio timed out after {timeoutMs / 1000}s.");
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        JsonNode? parsed = null;
        string trimmed = stdout.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                parsed = JsonNode.Parse(trimmed);
            }
            catch
            {
                // Leave parsed null if stdout contains non-JSON logs.
            }
        }

        return new PuppetToolResult(proc.ExitCode == 0, proc.ExitCode, stdout, stderr, parsed);
    }

    /// <summary>Parses a comma-separated clip list like <c>idle,attack,recoil:hit</c>.</summary>
    public static List<ClipSpec> ParseClipSpecs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DefaultClips.ToList();
        }

        List<ClipSpec> result = [];
        foreach (string raw in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
            string source = parts[0];
            string target = parts.Length == 2 && parts[1].Length > 0 ? parts[1] : source;
            bool loop = target is "idle" or "walk";
            result.Add(new ClipSpec(source, target, loop));
        }

        return result.Count > 0 ? result : DefaultClips.ToList();
    }

    /// <summary>
    /// Bakes any missing clips (via <c>puppet-studio bake</c>), computes the tight union bounding
    /// box across every frame of all exported clips, rescales with premultiplied alpha to a clean
    /// uniform cell grid PNG, projects the world <c>(0,0)</c> ground anchor into pixel coordinates,
    /// and upserts the <c>media: "sheet"</c> asset definition in the target module.
    /// </summary>
    public static ExportSheetResult ExportSheet(ExportSheetOptions options, string? executable = null)
    {
        string charDir = Path.GetFullPath(options.CharacterDir);
        string moduleDir = Path.GetFullPath(options.ModuleDir);
        if (!IsCharacterDir(charDir))
        {
            throw new InvalidOperationException($"'{charDir}' is not a puppet-studio character directory (missing character.json).");
        }

        if (!File.Exists(Path.Combine(moduleDir, "module.json")))
        {
            throw new InvalidOperationException($"'{moduleDir}' is not a Rusty Tales module directory (missing module.json).");
        }

        IReadOnlyList<ClipSpec> clips = options.Clips is { Count: > 0 } ? options.Clips : DefaultClips;
        string? tool = executable ?? FindExecutable(charDir);

        // 1. Ensure each clip has a baked sheet-manifest.json (invoke puppet-studio bake if missing or --rebake).
        List<(ClipSpec Spec, string ClipDir, JsonDocument Manifest)> loadedClips = [];
        try
        {
            foreach (ClipSpec clip in clips)
            {
                string clipDir = Path.Combine(charDir, "baked", clip.SourceClip);
                string manifestPath = Path.Combine(clipDir, "sheet-manifest.json");
                if (options.Rebake || !File.Exists(manifestPath))
                {
                    if (tool is null)
                    {
                        throw new InvalidOperationException($"Clip '{clip.SourceClip}' is not baked at '{manifestPath}' and puppet-studio executable was not found. Set PUPPET_STUDIO_BIN or install puppet-studio.");
                    }

                    PuppetToolResult bake = RunTool(tool, ["bake", charDir, "--animation", clip.SourceClip], charDir);
                    if (!bake.Ok || !File.Exists(manifestPath))
                    {
                        throw new InvalidOperationException($"Failed to bake clip '{clip.SourceClip}' (exit {bake.ExitCode}): {bake.Stderr.Trim()} {bake.Stdout.Trim()}".Trim());
                    }
                }

                loadedClips.Add((clip, clipDir, JsonDocument.Parse(File.ReadAllText(manifestPath))));
            }

            // 2. Compute tight union bounding box across all frames of all clips (in sourceSize pixel space).
            int unionMinX = int.MaxValue;
            int unionMinY = int.MaxValue;
            int unionMaxX = int.MinValue;
            int unionMaxY = int.MinValue;
            int sourceW = 0;
            int sourceH = 0;
            double anchorNormX = 0.5;
            double anchorNormY = 1.0;
            bool hasAnchor = false;
            int totalFrames = 0;

            List<BakedFrameRef> allFrames = [];
            List<ExportClipSummary> clipSummaries = [];

            foreach ((ClipSpec spec, string clipDir, JsonDocument doc) in loadedClips)
            {
                JsonElement root = doc.RootElement;
                double fps = root.TryGetProperty("fps", out JsonElement fpsEl) && fpsEl.TryGetDouble(out double fpsVal) && fpsVal > 0
                    ? Math.Round(fpsVal, 2)
                    : 12.0;

                JsonElement framesEl = root.GetProperty("frames");
                int startFrame = totalFrames;
                int count = 0;

                foreach (JsonElement f in framesEl.EnumerateArray())
                {
                    string imageFile = f.GetProperty("image").GetString()!;
                    JsonElement frameRect = f.GetProperty("frame");
                    JsonElement spriteSource = f.GetProperty("spriteSourceSize");
                    JsonElement srcSize = f.GetProperty("sourceSize");
                    int sw = srcSize.GetProperty("w").GetInt32();
                    int sh = srcSize.GetProperty("h").GetInt32();
                    if (sourceW == 0)
                    {
                        sourceW = sw;
                        sourceH = sh;
                    }

                    if (!hasAnchor && f.TryGetProperty("anchor", out JsonElement anchorEl))
                    {
                        anchorNormX = anchorEl.GetProperty("x").GetDouble();
                        anchorNormY = anchorEl.GetProperty("y").GetDouble();
                        hasAnchor = true;
                    }

                    int sx = spriteSource.GetProperty("x").GetInt32();
                    int sy = spriteSource.GetProperty("y").GetInt32();
                    int spW = spriteSource.GetProperty("w").GetInt32();
                    int spH = spriteSource.GetProperty("h").GetInt32();
                    bool empty = f.TryGetProperty("empty", out JsonElement emptyEl) && emptyEl.ValueKind == JsonValueKind.True;

                    if (!empty && spW > 0 && spH > 0)
                    {
                        unionMinX = Math.Min(unionMinX, sx);
                        unionMinY = Math.Min(unionMinY, sy);
                        unionMaxX = Math.Max(unionMaxX, sx + spW);
                        unionMaxY = Math.Max(unionMaxY, sy + spH);
                    }

                    allFrames.Add(new BakedFrameRef(
                        Path.Combine(clipDir, imageFile),
                        frameRect.GetProperty("x").GetInt32(),
                        frameRect.GetProperty("y").GetInt32(),
                        frameRect.GetProperty("w").GetInt32(),
                        frameRect.GetProperty("h").GetInt32(),
                        sx,
                        sy,
                        spW,
                        spH,
                        empty));
                    totalFrames++;
                    count++;
                }

                clipSummaries.Add(new ExportClipSummary(spec.SourceClip, spec.AnimationName, startFrame, count, fps, spec.Loop));
            }

            if (totalFrames == 0 || sourceW <= 0 || sourceH <= 0)
            {
                throw new InvalidOperationException($"No baked frames were found for '{charDir}'.");
            }

            if (unionMinX == int.MaxValue)
            {
                unionMinX = 0;
                unionMinY = 0;
                unionMaxX = sourceW;
                unionMaxY = sourceH;
            }

            int pad = Math.Max(0, options.MarginPx);
            int cropX = unionMinX - pad;
            int cropY = unionMinY - pad;
            int cropW = Math.Max(2, (unionMaxX - unionMinX) + (pad * 2));
            int cropH = Math.Max(2, (unionMaxY - unionMinY) + (pad * 2));

            // 3. Determine output cell size and projected pixel anchor.
            int cellW;
            int cellH;
            double scale = 1.0;
            if (options.TargetHeight is int targetH && targetH > 0 && targetH != cropH)
            {
                scale = (double)targetH / cropH;
                cellH = targetH;
                cellW = Math.Max(2, (int)Math.Round(cropW * scale));
            }
            else
            {
                cellW = cropW;
                cellH = cropH;
            }

            double origAnchorPxX = anchorNormX * sourceW;
            double origAnchorPxY = anchorNormY * sourceH;
            int anchorX = Math.Clamp((int)Math.Round((origAnchorPxX - cropX) * scale), 0, cellW - 1);
            int anchorY = Math.Clamp((int)Math.Round((origAnchorPxY - cropY) * scale), 0, cellH - 1);

            // 4. Composite every frame into one uniform grid PNG.
            int columns = (int)Math.Ceiling(Math.Sqrt(totalFrames));
            int rows = (int)Math.Ceiling((double)totalFrames / columns);
            PngRgba atlas = new(columns * cellW, rows * cellH);
            Dictionary<string, PngRgba> pageCache = new(StringComparer.Ordinal);
            bool useNearest = string.Equals(options.Sampling, "nearest", StringComparison.OrdinalIgnoreCase);

            for (int i = 0; i < allFrames.Count; i++)
            {
                BakedFrameRef fr = allFrames[i];
                if (fr.Empty || fr.FrameW <= 0 || fr.FrameH <= 0)
                {
                    continue;
                }

                if (!pageCache.TryGetValue(fr.ImagePath, out PngRgba? page))
                {
                    page = PngRgba.Load(fr.ImagePath);
                    pageCache[fr.ImagePath] = page;
                }

                PngRgba cropped = new(cropW, cropH);
                cropped.BlitCopy(page, fr.FrameX, fr.FrameY, fr.FrameW, fr.FrameH, fr.SpriteX - cropX, fr.SpriteY - cropY);
                PngRgba cellImg = (cellW == cropW && cellH == cropH)
                    ? cropped
                    : cropped.Resize(cellW, cellH, nearest: useNearest);

                int col = i % columns;
                int row = i / columns;
                atlas.BlitCopy(cellImg, 0, 0, cellW, cellH, col * cellW, row * cellH);
            }

            string assetId = SanitizeId(options.AssetId ?? Path.GetFileName(charDir));
            string relPng = (options.OutputPngRelative ?? $"sprites/{assetId}.png").Replace('\\', '/');
            string pngPath = Path.GetFullPath(Path.Combine(moduleDir, relPng));
            atlas.Save(pngPath);

            // 5. Upsert the standard Rusty Tales `media: "sheet"` definition into the module.
            string defsPath = Path.GetFullPath(Path.Combine(moduleDir, options.DefinitionsFileRelative));
            UpsertSheetDefinition(
                defsPath,
                assetId,
                relPng,
                cellW,
                cellH,
                totalFrames,
                anchorX,
                anchorY,
                options.Faces,
                options.WorldHeight,
                options.Sampling,
                clipSummaries);

            return new ExportSheetResult(
                assetId,
                moduleDir,
                pngPath,
                relPng,
                defsPath,
                cellW,
                cellH,
                columns,
                rows,
                totalFrames,
                anchorX,
                anchorY,
                options.Faces,
                options.WorldHeight,
                options.Sampling,
                clipSummaries);
        }
        finally
        {
            foreach ((_, _, JsonDocument doc) in loadedClips)
            {
                doc.Dispose();
            }
        }
    }

    private static void UpsertSheetDefinition(
        string defsPath,
        string assetId,
        string relPng,
        int cellW,
        int cellH,
        int frameCount,
        int anchorX,
        int anchorY,
        string faces,
        double worldHeight,
        string sampling,
        IReadOnlyList<ExportClipSummary> clips)
    {
        JsonArray entries;
        if (File.Exists(defsPath))
        {
            JsonNode? parsed = JsonNode.Parse(File.ReadAllText(defsPath));
            entries = parsed switch
            {
                JsonArray arr => arr,
                JsonObject obj => new JsonArray(obj),
                _ => [],
            };
        }
        else
        {
            entries = [];
        }

        JsonObject animsObj = new();
        foreach (ExportClipSummary clip in clips)
        {
            JsonArray frameIndices = new();
            for (int f = 0; f < clip.FrameCount; f++)
            {
                frameIndices.Add(clip.StartFrame + f);
            }

            animsObj[clip.AnimationName] = new JsonObject
            {
                ["frames"] = frameIndices,
                ["fps"] = clip.Fps,
                ["loop"] = clip.Loop,
            };
        }

        JsonObject assetDef = new()
        {
            ["type"] = "asset",
            ["id"] = assetId,
            ["media"] = "sheet",
            ["sampling"] = sampling,
            ["file"] = relPng,
            ["frame_size"] = new JsonArray(cellW, cellH),
            ["frame_count"] = frameCount,
            ["faces"] = faces,
            ["anchor"] = new JsonArray(anchorX, anchorY),
            ["height"] = Math.Round(worldHeight, 3),
            ["animations"] = animsObj,
        };

        bool replaced = false;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] is JsonObject existing
                && string.Equals(existing["type"]?.GetValue<string>(), "asset", StringComparison.Ordinal)
                && string.Equals(existing["id"]?.GetValue<string>(), assetId, StringComparison.Ordinal))
            {
                entries[i] = assetDef;
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            entries.Add(assetDef);
        }

        string? dir = Path.GetDirectoryName(defsPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        JsonSerializerOptions writeOpts = new() { WriteIndented = true };
        File.WriteAllText(defsPath, entries.ToJsonString(writeOpts) + "\n");
    }

    private static string SanitizeId(string raw)
    {
        char[] chars = raw.Trim().ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_')
            .ToArray();
        string cleaned = new(chars);
        return cleaned.Length > 0 ? cleaned : "puppet";
    }

    private sealed record BakedFrameRef(
        string ImagePath,
        int FrameX,
        int FrameY,
        int FrameW,
        int FrameH,
        int SpriteX,
        int SpriteY,
        int SpriteW,
        int SpriteH,
        bool Empty);
}
