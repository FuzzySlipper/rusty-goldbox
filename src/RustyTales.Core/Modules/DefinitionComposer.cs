using System.Text.Json;
using System.Text.Json.Nodes;
using RustyTales.Core.Definitions;

namespace RustyTales.Core.Modules;

/// <summary>One patch entry that was applied while loading a module set.</summary>
public sealed record AppliedPatch(
    string PatchId,
    string Module,
    string File,
    string RootPath,
    string TargetType,
    string TargetId,
    IReadOnlyList<string> Fields);

/// <summary>
/// Applies <c>patch</c> definitions in module load order and resolves
/// <c>extends</c> inheritance before each resulting definition is checked by
/// <see cref="DefinitionReader"/>.
/// </summary>
internal static class DefinitionComposer
{
    public static (List<Definition> Definitions, List<AppliedPatch> Patches) Compose(
        IReadOnlyList<LoadedModule> modules,
        IReadOnlyList<RawDefinition> rawEntries,
        List<ModuleDiagnostic> diagnostics)
    {
        Dictionary<string, ModuleManifest> manifests = [];
        Dictionary<string, HashSet<string>> visibleModules = [];
        foreach (LoadedModule module in modules)
        {
            manifests[module.Manifest.Id] = module.Manifest;
            HashSet<string> visible = [module.Manifest.Id];
            visible.UnionWith(module.Requires.Select(requirement => requirement.Id));
            visibleModules[module.Manifest.Id] = visible;
        }

        List<RawDefinition> rawPatches = [];
        List<Draft> drafts = [];
        Dictionary<(string Type, string Module, string Id), Draft> byKey = [];
        foreach (RawDefinition raw in rawEntries)
        {
            if (raw.Type == DefinitionTypes.Patch)
            {
                rawPatches.Add(raw);
                continue;
            }

            string? id = raw.Json.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;
            Draft draft = new(raw, id);
            drafts.Add(draft);
            if (!string.IsNullOrEmpty(id))
            {
                byKey.TryAdd((raw.Type.Name, raw.Module, id), draft);
            }
        }

        List<AppliedPatch> appliedPatches = [];
        Dictionary<(string Module, string Id), RawDefinition> seenPatches = [];
        foreach (RawDefinition rawPatch in rawPatches)
        {
            ApplyPatch(rawPatch, manifests, visibleModules, byKey, seenPatches, appliedPatches, diagnostics);
        }

        foreach (Draft draft in drafts)
        {
            ResolveExtends(draft, [], visibleModules, byKey, diagnostics);
        }

        List<Definition> definitions = [];
        foreach (Draft draft in drafts)
        {
            if (draft.State == ResolveState.Failed)
            {
                continue;
            }

            Definition? definition;
            if (draft.Json is null)
            {
                definition = DefinitionReader.Read(draft.Raw.Json, draft.Raw.Module, draft.Raw.File, diagnostics, draft.Raw.RootPath);
            }
            else
            {
                using JsonDocument document = JsonDocument.Parse(draft.Json.ToJsonString());
                definition = DefinitionReader.Read(
                    document.RootElement,
                    draft.Raw.Module,
                    draft.Raw.File,
                    diagnostics,
                    draft.Raw.RootPath,
                    draft.Origins,
                    draft.Removed);
            }

            if (definition is not null)
            {
                definitions.Add(definition);
            }
        }

        return (definitions, appliedPatches);
    }

    private static void ApplyPatch(
        RawDefinition rawPatch,
        Dictionary<string, ModuleManifest> manifests,
        Dictionary<string, HashSet<string>> visibleModules,
        Dictionary<(string Type, string Module, string Id), Draft> byKey,
        Dictionary<(string Module, string Id), RawDefinition> seenPatches,
        List<AppliedPatch> appliedPatches,
        List<ModuleDiagnostic> diagnostics)
    {
        Definition? patch = DefinitionReader.Read(rawPatch.Json, rawPatch.Module, rawPatch.File, diagnostics, rawPatch.RootPath);
        if (patch is null)
        {
            return;
        }

        if (!seenPatches.TryAdd((patch.Module, patch.Id), rawPatch))
        {
            RawDefinition existing = seenPatches[(patch.Module, patch.Id)];
            diagnostics.Add(new ModuleDiagnostic(
                "definition.duplicate",
                $"{patch.Module} already has a patch '{patch.Id}' in {existing.File}. Give one of them another ID.",
                patch.Module,
                patch.File,
                patch.DiagnosticPath("$.id")));
            return;
        }

        ModuleKind moduleKind = manifests[patch.Module].Kind;
        if (moduleKind is not (ModuleKind.Extension or ModuleKind.Campaign))
        {
            diagnostics.Add(new ModuleDiagnostic(
                "patch.module",
                $"Only extension and campaign modules can contain patches; '{patch.Module}' is a {ModuleKinds.Name(moduleKind)}.",
                patch.Module,
                patch.File,
                patch.DiagnosticPath("$")));
            return;
        }

        string targetTypeName = patch.Json.GetProperty("target_type").GetString()!;
        string targetText = patch.Json.GetProperty("target").GetString()!;
        int colon = targetText.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == targetText.Length - 1)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "patch.target",
                $"'{targetText}' must name a required module's definition as \"module:id\", for example \"classic:standard\".",
                patch.Module,
                patch.File,
                patch.DiagnosticPath("$.target")));
            return;
        }

        string targetModule = targetText[..colon];
        string targetId = targetText[(colon + 1)..];
        if (targetModule == patch.Module)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "patch.target",
                $"'{targetText}' belongs to this module ('{patch.Module}'). Edit that definition directly instead of patching it.",
                patch.Module,
                patch.File,
                patch.DiagnosticPath("$.target")));
            return;
        }

        if (!visibleModules[patch.Module].Contains(targetModule))
        {
            diagnostics.Add(new ModuleDiagnostic(
                "reference.module",
                $"'{targetText}' refers to module '{targetModule}', which '{patch.Module}' does not require. A module can refer only to itself and the modules in its own requires; add {{ \"id\": \"{targetModule}\", \"version\": \"...\" }} to requires.",
                patch.Module,
                patch.File,
                patch.DiagnosticPath("$.target")));
            return;
        }

        if (!byKey.TryGetValue((targetTypeName, targetModule, targetId), out Draft? target))
        {
            List<string> known = byKey.Keys
                .Where(key => key.Type == targetTypeName && key.Module == targetModule)
                .Select(key => key.Id)
                .Order(StringComparer.Ordinal)
                .ToList();
            string list = known.Count == 0
                ? $"'{targetModule}' has no {targetTypeName} definitions."
                : $"{targetTypeName} IDs in '{targetModule}': {string.Join(", ", known)}.";
            diagnostics.Add(new ModuleDiagnostic(
                "reference.not-found",
                $"There is no {targetTypeName} '{targetId}' in module '{targetModule}'. {list}",
                patch.Module,
                patch.File,
                patch.DiagnosticPath("$.target")));
            return;
        }

        target.EnsureMutable();
        FieldOrigin patchOrigin = new(patch.Module, patch.File, patch.RootPath, "$.set", AttributeErrorsToOrigin: true);
        List<string> fields = [];
        foreach (JsonProperty property in patch.Json.GetProperty("set").EnumerateObject())
        {
            fields.Add(property.Name);
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                target.Json!.Remove(property.Name);
                target.Origins!.Remove(property.Name);
                target.Removed![property.Name] = patchOrigin;
            }
            else
            {
                target.Json![property.Name] = JsonNode.Parse(property.Value.GetRawText());
                target.Origins![property.Name] = patchOrigin;
                target.Removed!.Remove(property.Name);
            }
        }

        appliedPatches.Add(new AppliedPatch(
            patch.QualifiedId,
            patch.Module,
            patch.File,
            patch.RootPath,
            targetTypeName,
            $"{targetModule}:{targetId}",
            fields));
    }

    private static bool ResolveExtends(
        Draft draft,
        List<Draft> chain,
        Dictionary<string, HashSet<string>> visibleModules,
        Dictionary<(string Type, string Module, string Id), Draft> byKey,
        List<ModuleDiagnostic> diagnostics)
    {
        if (draft.State == ResolveState.Done)
        {
            return true;
        }

        if (draft.State == ResolveState.Failed)
        {
            return false;
        }

        if (!draft.Raw.Json.TryGetProperty("extends", out JsonElement extendsElement))
        {
            draft.State = ResolveState.Done;
            return true;
        }

        if (extendsElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(extendsElement.GetString()))
        {
            diagnostics.Add(new ModuleDiagnostic(
                "definition.field-type",
                "Expected \"extends\" to name another definition of the same type (\"id\" or \"module:id\").",
                draft.Raw.Module,
                draft.Raw.File,
                Definition.CombinePath(draft.Raw.RootPath, "$.extends")));
            draft.State = ResolveState.Failed;
            return false;
        }

        string extendsText = extendsElement.GetString()!;
        int colon = extendsText.IndexOf(':', StringComparison.Ordinal);
        string parentModule = colon < 0 ? draft.Raw.Module : extendsText[..colon];
        string parentId = colon < 0 ? extendsText : extendsText[(colon + 1)..];
        if (!visibleModules[draft.Raw.Module].Contains(parentModule))
        {
            diagnostics.Add(new ModuleDiagnostic(
                "reference.module",
                $"'{extendsText}' refers to module '{parentModule}', which '{draft.Raw.Module}' does not require. A module can refer only to itself and the modules in its own requires; add {{ \"id\": \"{parentModule}\", \"version\": \"...\" }} to requires.",
                draft.Raw.Module,
                draft.Raw.File,
                Definition.CombinePath(draft.Raw.RootPath, "$.extends")));
            draft.State = ResolveState.Failed;
            return false;
        }

        if (!byKey.TryGetValue((draft.Raw.Type.Name, parentModule, parentId), out Draft? parent))
        {
            List<string> known = byKey.Keys
                .Where(key => key.Type == draft.Raw.Type.Name && key.Module == parentModule)
                .Select(key => key.Id)
                .Order(StringComparer.Ordinal)
                .ToList();
            string list = known.Count == 0
                ? $"'{parentModule}' has no {draft.Raw.Type.Name} definitions."
                : $"{draft.Raw.Type.Name} IDs in '{parentModule}': {string.Join(", ", known)}.";
            string hint = colon < 0 ? " To inherit from another module's definition, write module:id." : "";
            diagnostics.Add(new ModuleDiagnostic(
                "reference.not-found",
                $"There is no {draft.Raw.Type.Name} '{parentId}' in module '{parentModule}'. {list}{hint}",
                draft.Raw.Module,
                draft.Raw.File,
                Definition.CombinePath(draft.Raw.RootPath, "$.extends")));
            draft.State = ResolveState.Failed;
            return false;
        }

        if (parent == draft || parent.State == ResolveState.Visiting)
        {
            int start = chain.IndexOf(parent);
            IEnumerable<string> cycle = start >= 0
                ? chain.Skip(start).Append(draft).Append(parent).Select(Label)
                : [Label(draft), Label(parent)];
            diagnostics.Add(new ModuleDiagnostic(
                "definition.extends-cycle",
                $"\"extends\" forms a cycle ({string.Join(" -> ", cycle)}). Break the inheritance loop.",
                draft.Raw.Module,
                draft.Raw.File,
                Definition.CombinePath(draft.Raw.RootPath, "$.extends")));
            draft.State = ResolveState.Failed;
            return false;
        }

        draft.State = ResolveState.Visiting;
        chain.Add(draft);
        bool parentOk = ResolveExtends(parent, chain, visibleModules, byKey, diagnostics);
        chain.RemoveAt(chain.Count - 1);
        if (!parentOk)
        {
            draft.State = ResolveState.Failed;
            return false;
        }

        Merge(draft, parent);
        draft.State = ResolveState.Done;
        return true;
    }

    private static void Merge(Draft child, Draft parent)
    {
        JsonObject merged = new();
        Dictionary<string, FieldOrigin> origins = [];
        Dictionary<string, FieldOrigin> removed = [];
        FieldOrigin childOrigin = new(child.Raw.Module, child.Raw.File, child.Raw.RootPath);
        FieldOrigin parentDefaultOrigin = new(parent.Raw.Module, parent.Raw.File, parent.Raw.RootPath);

        if (child.Raw.Json.TryGetProperty("type", out JsonElement typeElement))
        {
            merged["type"] = JsonNode.Parse(typeElement.GetRawText());
            origins["type"] = childOrigin;
        }

        if (child.Raw.Json.TryGetProperty("id", out JsonElement idElement))
        {
            merged["id"] = JsonNode.Parse(idElement.GetRawText());
            origins["id"] = childOrigin;
        }

        JsonObject parentObject = parent.Json ?? JsonNode.Parse(parent.Raw.Json.GetRawText())!.AsObject();
        foreach (KeyValuePair<string, JsonNode?> property in parentObject)
        {
            if (property.Key is "type" or "id" or "extends")
            {
                continue;
            }

            merged[property.Key] = property.Value?.DeepClone();
            FieldOrigin inheritedOrigin = parent.Origins is not null && parent.Origins.TryGetValue(property.Key, out FieldOrigin? parentOrigin)
                ? parentOrigin with { AttributeErrorsToOrigin = false }
                : parentDefaultOrigin;
            origins[property.Key] = inheritedOrigin;
        }

        JsonObject childObject = child.Json ?? JsonNode.Parse(child.Raw.Json.GetRawText())!.AsObject();
        foreach (KeyValuePair<string, JsonNode?> property in childObject)
        {
            if (property.Key is "type" or "id" or "extends")
            {
                continue;
            }

            FieldOrigin ownOrigin = child.Origins is not null && child.Origins.TryGetValue(property.Key, out FieldOrigin? existingOrigin)
                ? existingOrigin
                : childOrigin;
            if (property.Value is null || property.Value.GetValueKind() == JsonValueKind.Null)
            {
                merged.Remove(property.Key);
                origins.Remove(property.Key);
                removed[property.Key] = ownOrigin;
            }
            else
            {
                merged[property.Key] = property.Value.DeepClone();
                origins[property.Key] = ownOrigin;
            }
        }

        if (child.Removed is not null)
        {
            foreach ((string key, FieldOrigin remover) in child.Removed)
            {
                merged.Remove(key);
                origins.Remove(key);
                removed[key] = remover;
            }
        }

        child.Json = merged;
        child.Origins = origins;
        child.Removed = removed;
    }

    private static string Label(Draft draft) =>
        string.IsNullOrEmpty(draft.Id) ? $"{draft.Raw.Module}:({draft.Raw.RootPath})" : $"{draft.Raw.Module}:{draft.Id}";

    private enum ResolveState
    {
        Unvisited,
        Visiting,
        Done,
        Failed,
    }

    private sealed class Draft(RawDefinition raw, string? id)
    {
        public RawDefinition Raw { get; } = raw;

        public string? Id { get; } = id;

        public ResolveState State { get; set; }

        public JsonObject? Json { get; set; }

        public Dictionary<string, FieldOrigin>? Origins { get; set; }

        public Dictionary<string, FieldOrigin>? Removed { get; set; }

        public void EnsureMutable()
        {
            if (Json is not null)
            {
                return;
            }

            Json = JsonNode.Parse(Raw.Json.GetRawText())!.AsObject();
            Origins = [];
            Removed = [];
            FieldOrigin ownOrigin = new(Raw.Module, Raw.File, Raw.RootPath);
            foreach (KeyValuePair<string, JsonNode?> property in Json)
            {
                Origins[property.Key] = ownOrigin;
            }
        }
    }
}
