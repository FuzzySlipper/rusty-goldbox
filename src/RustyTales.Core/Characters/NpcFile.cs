using System.Text.Json;
using System.Text.Json.Nodes;
using RustyTales.Core.Definitions;
using RustyTales.Core.Modules;

namespace RustyTales.Core.Characters;

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
            string characterModule = npc.ModuleOf("character");
            data["modules"] = new JsonArray(set.LoadOrder.Where(module => set.Rules.VisibleModules[characterModule].Contains(module.Manifest.Id))
                .Select(module => (JsonNode)new JsonObject { ["id"] = module.Manifest.Id, ["version"] = module.Manifest.Version.ToString() }).ToArray());
            using JsonDocument document = JsonDocument.Parse(data.ToJsonString());
            List<ModuleDiagnostic> characterProblems = [];
            (string diagModule, string diagFile, string diagPath) = npc.DiagnosticLocation("$.character");
            Character? character = CharacterFile.Read(document.RootElement, diagFile, diagPath, set, characterProblems);
            problems.AddRange(characterProblems.Select(problem => problem with { Module = diagModule }));
            if (character is not null)
            {
                character.Npc = npc;
                set.Rules.Npcs.Add(npc, character);
            }
        }
    }
}
