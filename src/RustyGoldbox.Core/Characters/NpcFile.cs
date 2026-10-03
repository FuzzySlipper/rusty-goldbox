using System.Text.Json;
using System.Text.Json.Nodes;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Core.Characters;

public static class NpcFile
{
    public static string ToJson(Character character, string id)
    {
        JsonObject data = JsonNode.Parse(CharacterFile.ToJson(character))!.AsObject();
        data.Remove("format");
        data.Remove("modules");
        data.Remove("npc");
        JsonObject npc = new() { ["type"] = "npc", ["id"] = id, ["character"] = data };
        return npc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    internal static void Check(ModuleSet set, List<ModuleDiagnostic> problems)
    {
        foreach (Definition npc in set.Rules!.OfType(DefinitionTypes.Npc))
        {
            JsonObject data = JsonNode.Parse(npc.Json.GetProperty("character").GetRawText())!.AsObject();
            data["format"] = CharacterFile.CurrentFormat;
            data["modules"] = new JsonArray(set.LoadOrder.Where(module => set.Rules.VisibleModules[npc.Module].Contains(module.Manifest.Id))
                .Select(module => (JsonNode)new JsonObject { ["id"] = module.Manifest.Id, ["version"] = module.Manifest.Version.ToString() }).ToArray());
            using JsonDocument document = JsonDocument.Parse(data.ToJsonString());
            List<ModuleDiagnostic> characterProblems = [];
            Character? character = CharacterFile.Read(document.RootElement, npc.File, "$.character", set, characterProblems);
            problems.AddRange(characterProblems.Select(problem => problem with { Module = npc.Module }));
            if (character is not null)
            {
                character.Npc = npc;
                set.Rules.Npcs.Add(npc, character);
            }
        }
    }
}
