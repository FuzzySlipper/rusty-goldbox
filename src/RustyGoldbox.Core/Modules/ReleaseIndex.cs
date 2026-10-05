using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustyGoldbox.Core.Modules;

/// <summary>One module a release index offers.</summary>
/// <param name="File">The container's file name, beside the index.</param>
/// <param name="Identity">The container's Engine content identity, checked after download.</param>
public sealed record ReleasedModule(
    string Id,
    ModuleVersion Version,
    ModuleKind Kind,
    string Title,
    string Provenance,
    IReadOnlyList<ModuleRequirement> Requires,
    string File,
    string Identity);

/// <summary>
/// <c>module-index.json</c>: what one release offers. It sits beside the
/// containers it lists, as a GitHub release asset or at an index URL.
/// </summary>
public static class ReleaseIndex
{
    public const string FileName = "module-index.json";

    /// <summary>The index entry for a module about to be published.</summary>
    public static ReleasedModule Describe(ModuleManifest manifest, string identity)
    {
        return new ReleasedModule(manifest.Id, manifest.Version, manifest.Kind, manifest.Title, manifest.Provenance, manifest.Requires,
            InstalledModules.FileName(manifest.Id, manifest.Version), identity);
    }

    public static string Write(IEnumerable<ReleasedModule> modules)
    {
        JsonObject root = new()
        {
            ["modules"] = new JsonArray(modules.Select(module => (JsonNode)new JsonObject
            {
                ["id"] = module.Id,
                ["version"] = module.Version.ToString(),
                ["kind"] = ModuleKinds.Name(module.Kind),
                ["title"] = module.Title,
                ["provenance"] = module.Provenance,
                ["requires"] = new JsonArray(module.Requires.Select(requirement => (JsonNode)Requirement(requirement)).ToArray()),
                ["file"] = module.File,
                ["identity"] = module.Identity,
            }).ToArray()),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>
    /// Reads an index fetched from <paramref name="location"/>. It is
    /// untrusted input: anything malformed is a problem naming the location,
    /// and only well-formed entries are returned.
    /// </summary>
    public static List<ReleasedModule> Read(byte[] bytes, string location, List<string> problems)
    {
        List<ReleasedModule> modules = [];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            problems.Add($"{location} isn't a JSON module index: {exception.Message}");
            return modules;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("modules", out JsonElement list)
                || list.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"{location} has no \"modules\" array, so it isn't a {FileName}.");
                return modules;
            }

            int index = 0;
            foreach (JsonElement entry in list.EnumerateArray())
            {
                if (ReadModule(entry) is ReleasedModule module)
                {
                    modules.Add(module);
                }
                else
                {
                    problems.Add($"{location} $.modules[{index}] isn't a complete module entry (id, version, kind, title, provenance, requires, file, identity).");
                }

                index++;
            }
        }

        return modules;
    }

    private static JsonObject Requirement(ModuleRequirement requirement)
    {
        JsonObject entry = new() { ["id"] = requirement.Id, ["version"] = requirement.Range.Text };
        if (requirement.Releases is not null)
        {
            entry["releases"] = requirement.Releases.Text;
        }

        return entry;
    }

    private static ReleasedModule? ReadModule(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || Text(entry, "id") is not string id || !ModuleIds.IsValid(id)
            || Text(entry, "version") is not string versionText || !ModuleVersion.TryParse(versionText, out ModuleVersion version)
            || Text(entry, "kind") is not string kindText || !ModuleKinds.TryParse(kindText, out ModuleKind kind)
            || Text(entry, "title") is not string title
            || Text(entry, "provenance") is not string provenance
            || Text(entry, "file") is not string file || file != InstalledModules.FileName(id, version)
            || Text(entry, "identity") is not string identity
            || !entry.TryGetProperty("requires", out JsonElement requiresList) || requiresList.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<ModuleRequirement> requires = [];
        foreach (JsonElement requirement in requiresList.EnumerateArray())
        {
            if (requirement.ValueKind != JsonValueKind.Object
                || Text(requirement, "id") is not string requiredId || !ModuleIds.IsValid(requiredId)
                || Text(requirement, "version") is not string rangeText || !VersionRange.TryParse(rangeText, out VersionRange? range))
            {
                return null;
            }

            ReleaseSource? releases = null;
            if (Text(requirement, "releases") is string releasesText && !ReleaseSource.TryParse(releasesText, out releases))
            {
                return null;
            }

            requires.Add(new ModuleRequirement(requiredId, range!, requires.Count, releases));
        }

        return new ReleasedModule(id, version, kind, title, provenance, requires, file, identity);
    }

    private static string? Text(JsonElement entry, string name)
    {
        return entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
