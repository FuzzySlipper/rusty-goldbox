using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustyGoldbox.Core.Modules;

/// <summary>
/// <c>sources.json</c> in a module library: where each fetched module came
/// from and which versions of it are installed, with their content
/// identities, so updates can be checked later.
/// </summary>
public static class InstalledSources
{
    public const string FileName = "sources.json";

    /// <summary>One fetched module: where it is published and its installed versions by version text.</summary>
    public sealed record Entry(string Releases, IReadOnlyDictionary<string, string> Versions);

    /// <summary>The library's record by module ID; empty when there is none or it can't be read.</summary>
    public static Dictionary<string, Entry> Read(string library)
    {
        Dictionary<string, Entry> entries = [];
        string path = Path.Combine(library, FileName);
        if (!File.Exists(path))
        {
            return entries;
        }

        try
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? [];
            foreach ((string id, JsonNode? node) in root)
            {
                if (node?["releases"]?.GetValue<string>() is string releases && node["versions"] is JsonObject versions)
                {
                    entries[id] = new Entry(releases, versions.ToDictionary(version => version.Key, version => version.Value!.GetValue<string>()));
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            // A damaged record only loses update information; the containers are still installed.
        }

        return entries;
    }

    /// <summary>Adds one installed module to the record.</summary>
    public static void Record(string library, ReleasedModule module, ReleaseSource from)
    {
        Dictionary<string, Entry> entries = Read(library);
        Dictionary<string, string> versions = entries.TryGetValue(module.Id, out Entry? known) ? new(known.Versions) : [];
        versions[module.Version.ToString()] = module.Identity;
        entries[module.Id] = new Entry(from.Text, versions);
        JsonObject root = [];
        foreach ((string id, Entry entry) in entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            root[id] = new JsonObject
            {
                ["releases"] = entry.Releases,
                ["versions"] = new JsonObject(entry.Versions.OrderBy(version => version.Key, StringComparer.Ordinal)
                    .Select(version => KeyValuePair.Create(version.Key, (JsonNode?)version.Value))),
            };
        }

        File.WriteAllText(Path.Combine(library, FileName), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
