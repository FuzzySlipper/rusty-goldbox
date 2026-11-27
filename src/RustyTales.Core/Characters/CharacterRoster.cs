using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RustyTales.Core.Modules;

namespace RustyTales.Core.Characters;

/// <summary>
/// One ruleset-bound character stored in the player's persistent character
/// stable, ready to be imported into any campaign built on the same ruleset.
/// </summary>
public sealed record StableEntry(
    string Id,
    string RulesetId,
    ModuleVersion RulesetVersion,
    string RulesetTitle,
    string Name,
    string? Race,
    string ClassText,
    int Level,
    string? PortraitId,
    string? OriginCampaign,
    DateTimeOffset UpdatedUtc,
    JsonObject Data)
{
    public ModuleStamp RulesetStamp => new(RulesetId, RulesetVersion);
}

/// <summary>
/// Portable JSON codec and compatibility helpers for the player's ruleset-bound
/// character stable. Both the Game and the CLI share this format over Engine persistence.
/// </summary>
public static class CharacterRoster
{
    public const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Builds a stable entry for <paramref name="character"/> bound to <paramref name="set"/>'s baseline ruleset.</summary>
    public static StableEntry CreateEntry(ModuleSet set, Character character, string? existingId = null, string? originCampaign = null, IReadOnlyList<StableEntry>? existing = null)
    {
        LoadedModule ruleset = set.LoadOrder.FirstOrDefault(loaded => loaded.Manifest.Kind == ModuleKind.Ruleset)
            ?? throw new InvalidOperationException("Cannot store a character in the stable without a loaded ruleset module.");

        string id = string.IsNullOrWhiteSpace(existingId) ? NewId(character.Name, existing ?? []) : existingId;
        JsonObject data = JsonNode.Parse(CharacterFile.ToJson(character))!.AsObject();
        string classText = character.ClassLevels().Count > 1
            ? character.ClassText
            : character.Class is not null ? $"{character.Class.Name} {character.Level}" : $"Level {character.Level}";
        return new StableEntry(
            id,
            ruleset.Manifest.Id,
            ruleset.Manifest.Version,
            ruleset.Manifest.Title,
            character.Name,
            character.Race?.Name,
            classText,
            character.Level,
            character.Portrait?.QualifiedId,
            originCampaign,
            DateTimeOffset.UtcNow,
            data);
    }

    /// <summary>Whether <paramref name="entry"/> shares a compatible baseline ruleset with <paramref name="targetRuleset"/>.</summary>
    public static bool IsCompatibleWith(StableEntry entry, ModuleStamp targetRuleset)
    {
        if (!string.Equals(entry.RulesetId, targetRuleset.Id, StringComparison.Ordinal))
        {
            return false;
        }

        if (VersionRange.TryParse($"^{entry.RulesetVersion}", out VersionRange? forward) && forward!.Contains(targetRuleset.Version))
        {
            return true;
        }

        return VersionRange.TryParse($"^{targetRuleset.Version}", out VersionRange? backward) && backward!.Contains(entry.RulesetVersion);
    }

    public static byte[] Write(IReadOnlyList<StableEntry> entries)
    {
        JsonArray list = new(entries.Select(entry => (JsonNode)new JsonObject
        {
            ["id"] = entry.Id,
            ["rulesetId"] = entry.RulesetId,
            ["rulesetVersion"] = entry.RulesetVersion.ToString(),
            ["rulesetTitle"] = entry.RulesetTitle,
            ["name"] = entry.Name,
            ["race"] = entry.Race,
            ["classText"] = entry.ClassText,
            ["level"] = entry.Level,
            ["portraitId"] = entry.PortraitId,
            ["originCampaign"] = entry.OriginCampaign,
            ["updatedUtc"] = entry.UpdatedUtc.ToString("O"),
            ["data"] = entry.Data.DeepClone(),
        }).ToArray());
        JsonObject root = new()
        {
            ["format"] = CurrentFormat,
            ["characters"] = list,
        };
        return Encoding.UTF8.GetBytes(root.ToJsonString(WriteOptions));
    }

    /// <summary>Reads a stable payload fail-open: malformed individual entries become warnings rather than wiping the roster.</summary>
    public static List<StableEntry> Read(ReadOnlySpan<byte> utf8, List<string> warnings)
    {
        List<StableEntry> entries = [];
        if (utf8.IsEmpty)
        {
            return entries;
        }

        try
        {
            JsonNode? parsed = JsonNode.Parse(utf8);
            if (parsed is not JsonObject root || root["characters"] is not JsonArray characters)
            {
                warnings.Add("Character stable payload is missing its characters list.");
                return entries;
            }

            foreach (JsonNode? item in characters)
            {
                if (item is not JsonObject obj
                    || obj["id"]?.GetValue<string>() is not string id
                    || obj["rulesetId"]?.GetValue<string>() is not string rulesetId
                    || obj["rulesetVersion"]?.GetValue<string>() is not string versionText
                    || !ModuleVersion.TryParse(versionText, out ModuleVersion version)
                    || obj["name"]?.GetValue<string>() is not string name
                    || obj["data"] is not JsonObject data)
                {
                    warnings.Add("Skipped an unreadable entry in the character stable.");
                    continue;
                }

                string rulesetTitle = obj["rulesetTitle"]?.GetValue<string>() ?? rulesetId;
                string? race = obj["race"]?.GetValue<string>();
                string classText = obj["classText"]?.GetValue<string>() ?? "Level 1";
                int level = obj["level"]?.GetValue<int>() ?? 1;
                string? portraitId = obj["portraitId"]?.GetValue<string>();
                string? originCampaign = obj["originCampaign"]?.GetValue<string>();
                DateTimeOffset updated = obj["updatedUtc"]?.GetValue<string>() is string stamp && DateTimeOffset.TryParse(stamp, out DateTimeOffset dto)
                    ? dto
                    : DateTimeOffset.UnixEpoch;
                entries.Add(new StableEntry(id, rulesetId, version, rulesetTitle, name, race, classText, level, portraitId, originCampaign, updated, data.DeepClone().AsObject()));
            }
        }
        catch (JsonException exception)
        {
            warnings.Add($"Could not read the character stable: {exception.Message}");
        }

        return entries;
    }

    private static string NewId(string name, IReadOnlyList<StableEntry> existing)
    {
        string slug = new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        if (string.IsNullOrEmpty(slug))
        {
            slug = "hero";
        }

        HashSet<string> used = existing.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int suffix = 1;
        while (used.Contains($"{slug}-{suffix}"))
        {
            suffix++;
        }

        return $"{slug}-{suffix}";
    }
}
