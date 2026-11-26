using System.Text;
using RustyTales.Core.Modules;

namespace RustyTales.Core.Authoring;

/// <summary>Metadata for one modular genre starter kit shipped inside Core.</summary>
public sealed record GenreKit(
    string Id,
    string Title,
    string Summary,
    string CombatPresentation,
    string PartyStyle,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> Patterns);

/// <summary>
/// Modular, opinionated RPG genre starter kits that scaffold a ready-to-validate,
/// ready-to-play workspace (scoped ruleset + starter art + minimal campaign +
/// smoke script + kit guide) so agents can build without wiring every ruleset
/// primitive from scratch.
/// </summary>
public static partial class GenreKits
{
    static GenreKits()
    {
        All =
        [
            TacticalDungeon,
            FirstPersonCrawler,
            ClassicJrpg,
        ];
    }

    public static IReadOnlyList<GenreKit> All { get; }

    public static GenreKit? Find(string id) =>
        All.FirstOrDefault(kit => string.Equals(kit.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Derives a valid default module ID from a workspace directory name, falling back to the kit ID.</summary>
    public static string DefaultCampaignId(string workspaceRoot, GenreKit kit)
    {
        string raw = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot)));
        StringBuilder builder = new();
        bool lastDash = false;
        foreach (char ch in raw.ToLowerInvariant())
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(ch);
                lastDash = false;
            }
            else if (builder.Length > 0 && !lastDash)
            {
                builder.Append('-');
                lastDash = true;
            }
        }

        string candidate = builder.ToString().TrimEnd('-');
        return ModuleIds.IsValid(candidate) ? candidate : kit.Id;
    }

    public static string DefaultTitle(string campaignId, GenreKit kit)
    {
        if (string.Equals(campaignId, kit.Id, StringComparison.Ordinal))
        {
            return kit.Title;
        }

        return string.Join(' ', campaignId.Split('-').Select(word =>
            word.Length == 0 ? "" : char.ToUpperInvariant(word[0]) + word[1..]));
    }

    internal static void Scaffold(
        GenreKit kit,
        string workspaceRoot,
        string campaignId,
        string title,
        List<ModuleDiagnostic> diagnostics)
    {
        string rulesId = $"{campaignId}-rules";
        string artId = $"{campaignId}-art";
        if (!ModuleIds.IsValid(campaignId) || !ModuleIds.IsValid(rulesId) || !ModuleIds.IsValid(artId))
        {
            diagnostics.Add(new ModuleDiagnostic(
                "workspace.kit.id",
                $"'{campaignId}' is not a valid module ID. Use {ModuleIds.FormatDescription}."));
            return;
        }

        string rulesDir = Path.Combine(workspaceRoot, "modules", rulesId);
        string artDir = Path.Combine(workspaceRoot, "modules", artId);
        string campaignDir = Path.Combine(workspaceRoot, "modules", campaignId);

        if (kit == TacticalDungeon)
        {
            ScaffoldTacticalDungeon(workspaceRoot, rulesDir, artDir, campaignDir, rulesId, artId, campaignId, title);
        }
        else if (kit == FirstPersonCrawler)
        {
            ScaffoldFirstPersonCrawler(workspaceRoot, rulesDir, artDir, campaignDir, rulesId, artId, campaignId, title);
        }
        else if (kit == ClassicJrpg)
        {
            ScaffoldClassicJrpg(workspaceRoot, rulesDir, artDir, campaignDir, rulesId, artId, campaignId, title);
        }
        else
        {
            throw new InvalidOperationException($"Unhandled genre kit '{kit.Id}'.");
        }
    }

    private static void WriteText(string root, string relativePath, string content)
    {
        string full = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content.TrimStart('\r', '\n'), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteBytes(string root, string relativePath, byte[] bytes)
    {
        string full = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    private static void WriteStarterArt(
        string artDir,
        string artId,
        string skinId,
        string skinName,
        string skinColorsJson,
        StarterPng.Rgba wall,
        StarterPng.Rgba mortar,
        StarterPng.Rgba door,
        StarterPng.Rgba trim,
        StarterPng.Rgba floor,
        StarterPng.Rgba ceiling,
        StarterPng.Rgba backdropTop,
        StarterPng.Rgba backdropBottom,
        StarterPng.Rgba heroPrimary,
        StarterPng.Rgba heroSecondary,
        StarterPng.Rgba magePrimary,
        StarterPng.Rgba mageSecondary,
        StarterPng.Rgba foePrimary,
        StarterPng.Rgba foeSecondary,
        StarterPng.Rgba accent)
    {
        WriteText(artDir, "module.json", $$"""
            {
              "format": 1,
              "id": "{{artId}}",
              "kind": "assets",
              "version": "0.1.0",
              "title": "{{skinName}} Starter Art",
              "requires": [],
              "provenance": "Original procedural starter art generated by Rusty Tales."
            }
            """);

        WriteText(artDir, "PROVENANCE.md", $"""
            # Provenance: {artId}

            Original placeholder art generated by `tales workspace new --kit`.
            Replace any PNG under `walls/`, `backdrops/`, `sprites/` or `icons/`
            with your own 8-bit RGBA PNGs while keeping the logical asset IDs in
            `assets.json` stable.
            """);

        WriteBytes(artDir, "walls/stone.png", StarterPng.WallSet(wall, mortar, door, trim, floor, ceiling));
        WriteBytes(artDir, "backdrops/dungeon.png", StarterPng.Backdrop(backdropTop, backdropBottom, mortar));
        WriteBytes(artDir, "backdrops/hub.png", StarterPng.Backdrop(ceiling, floor, trim));
        WriteBytes(artDir, "sprites/hero_a.png", StarterPng.Sprite(heroPrimary, heroSecondary, accent, humanoid: true));
        WriteBytes(artDir, "sprites/hero_b.png", StarterPng.Sprite(magePrimary, mageSecondary, accent, humanoid: true));
        WriteBytes(artDir, "sprites/foe.png", StarterPng.Sprite(foePrimary, foeSecondary, accent, humanoid: false));
        WriteBytes(artDir, "sprites/pillar.png", StarterPng.Sprite(wall, mortar, trim, humanoid: false));
        WriteBytes(artDir, "icons/foe.png", StarterPng.Icon(foePrimary, accent));
        WriteBytes(artDir, "icons/hero.png", StarterPng.Icon(heroPrimary, accent));

        WriteText(artDir, "assets.json", $$"""
            [
              {
                "type": "asset",
                "id": "wall_set",
                "media": "image",
                "file": "walls/stone.png",
                "regions": {
                  "wall": [0, 0, 32, 32],
                  "door": [32, 0, 32, 32],
                  "floor": [0, 32, 32, 32],
                  "ceiling": [32, 32, 32, 32]
                }
              },
              { "type": "asset", "id": "dungeon_backdrop", "media": "image", "file": "backdrops/dungeon.png" },
              { "type": "asset", "id": "hub_backdrop", "media": "image", "file": "backdrops/hub.png" },
              {
                "type": "asset",
                "id": "hero_a",
                "media": "sheet",
                "file": "sprites/hero_a.png",
                "frame_size": [32, 48],
                "faces": "right",
                "anchor": [16, 46],
                "height": 0.85
              },
              {
                "type": "asset",
                "id": "hero_b",
                "media": "sheet",
                "file": "sprites/hero_b.png",
                "frame_size": [32, 48],
                "faces": "right",
                "anchor": [16, 46],
                "height": 0.85
              },
              {
                "type": "asset",
                "id": "foe",
                "media": "sheet",
                "file": "sprites/foe.png",
                "frame_size": [32, 48],
                "faces": "left",
                "anchor": [16, 46],
                "height": 0.8
              },
              {
                "type": "asset",
                "id": "pillar",
                "media": "sheet",
                "file": "sprites/pillar.png",
                "frame_size": [32, 48],
                "faces": "right",
                "anchor": [16, 46],
                "height": 0.9
              },
              { "type": "asset", "id": "hero_icon", "media": "image", "file": "icons/hero.png", "tags": ["portrait"] },
              { "type": "asset", "id": "foe_icon", "media": "image", "file": "icons/foe.png" },
              {
                "type": "skin",
                "id": "{{skinId}}",
                "name": "{{skinName}}",
                "colors": {{skinColorsJson}}
              }
            ]
            """);
    }
}
