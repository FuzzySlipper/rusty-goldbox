namespace RustyGoldbox.Core.Modules;

/// <summary>
/// Picks one version of each required module and orders the set so each
/// module comes after everything it requires.
/// </summary>
/// <remarks>
/// Modules are visited breadth-first from the root. Each ID gets the highest
/// available version that satisfies every range known when it is first
/// required. A range seen later that excludes that version is a conflict; the
/// resolver does not backtrack.
/// </remarks>
internal sealed class ModuleResolver(ModuleCatalog catalog, List<ModuleDiagnostic> diagnostics)
{
    private readonly Dictionary<string, ModuleManifest> _selected = [];
    private readonly Dictionary<string, List<(ModuleManifest By, ModuleRequirement Requirement)>> _constraints = [];
    private ModuleManifest _root = null!;

    public List<LoadedModule> Resolve(ModuleManifest root)
    {
        _root = root;
        _selected[root.Id] = root;
        Queue<ModuleManifest> pending = new([root]);
        while (pending.Count > 0)
        {
            ModuleManifest module = pending.Dequeue();
            foreach (ModuleRequirement requirement in module.Requires)
            {
                ModuleManifest? picked = Require(module, requirement);
                if (picked is not null)
                {
                    pending.Enqueue(picked);
                }
            }
        }

        List<LoadedModule> order = Order();
        CheckKinds(order);
        return order;
    }

    /// <summary>Records a requirement; returns a newly selected module to visit.</summary>
    private ModuleManifest? Require(ModuleManifest module, ModuleRequirement requirement)
    {
        if (!_constraints.TryGetValue(requirement.Id, out List<(ModuleManifest By, ModuleRequirement Requirement)>? constraints))
        {
            constraints = [];
            _constraints[requirement.Id] = constraints;
        }

        constraints.Add((module, requirement));
        string at = $"$.requires[{requirement.Index}]";

        if (_selected.TryGetValue(requirement.Id, out ModuleManifest? selected))
        {
            if (!requirement.Range.Contains(selected.Version))
            {
                string why = selected == _root
                    ? "it is the module being loaded"
                    : Describe(constraints.Where(constraint => constraint.Requirement != requirement));
                Error(module, "resolve.conflict", $"{at}.version",
                    $"'{module.Id}' requires {requirement.Id} {requirement.Range}, but {requirement.Id} {selected.Version} is already selected ({why}). "
                    + $"Change the ranges so they overlap. {Available(requirement.Id)}");
            }

            return null;
        }

        IReadOnlyList<ModuleManifest> candidates = catalog.Find(requirement.Id);
        if (candidates.Count == 0)
        {
            Error(module, "resolve.not-found", $"{at}.id", NotFoundMessage(module, requirement));
            return null;
        }

        List<ModuleManifest> matching = candidates
            .Where(candidate => constraints.All(constraint => constraint.Requirement.Range.Contains(candidate.Version)))
            .OrderByDescending(candidate => candidate.Version)
            .ToList();
        if (matching.Count == 0)
        {
            Error(module, "resolve.conflict", $"{at}.version",
                $"No available version of '{requirement.Id}' satisfies every requirement: {Describe(constraints)}. {Available(requirement.Id)}");
            return null;
        }

        ModuleManifest picked = matching[0];
        List<ModuleManifest> copies = matching.Where(candidate => candidate.Version == picked.Version).ToList();
        if (copies.Count > 1)
        {
            Error(module, "resolve.ambiguous", $"{at}.id",
                $"{picked.Id} {picked.Version} is in more than one directory: {string.Join(", ", copies.Select(copy => copy.Directory))}. Remove one copy or give it a different version.");
            return null;
        }

        _selected[picked.Id] = picked;
        return picked;
    }

    private List<LoadedModule> Order()
    {
        List<LoadedModule> order = [];
        Dictionary<string, bool> finished = [];
        List<string> path = [];
        Visit(_root, order, finished, path);
        return order;
    }

    private void Visit(ModuleManifest module, List<LoadedModule> order, Dictionary<string, bool> finished, List<string> path)
    {
        finished[module.Id] = false;
        path.Add(module.Id);
        List<ResolvedRequirement> resolved = [];
        foreach (ModuleRequirement requirement in module.Requires)
        {
            if (!_selected.TryGetValue(requirement.Id, out ModuleManifest? dependency))
            {
                continue;
            }

            resolved.Add(new ResolvedRequirement(requirement.Id, requirement.Range, dependency.Version));
            if (!finished.TryGetValue(dependency.Id, out bool done))
            {
                Visit(dependency, order, finished, path);
            }
            else if (!done)
            {
                int start = path.IndexOf(dependency.Id);
                string cycle = string.Join(" -> ", path.Skip(start).Append(dependency.Id));
                Error(module, "resolve.cycle", $"$.requires[{requirement.Index}].id",
                    $"Requirement cycle: {cycle}. Modules load after what they require, so requirements can't loop. Remove one of these requirements.");
            }
        }

        path.RemoveAt(path.Count - 1);
        finished[module.Id] = true;
        order.Add(new LoadedModule(module, resolved));
    }

    private void CheckKinds(List<LoadedModule> order)
    {
        foreach (LoadedModule loaded in order)
        {
            CheckRequirementKinds(loaded.Manifest);
        }

        List<ModuleManifest> rulesets = order
            .Select(loaded => loaded.Manifest)
            .Where(manifest => manifest.Kind == ModuleKind.Ruleset)
            .ToList();
        if (rulesets.Count > 1)
        {
            string list = string.Join(", ", rulesets.Select(ruleset => $"{ruleset.Id} ({RequiredBy(ruleset.Id)})"));
            Error(_root, "resolve.rulesets", "$.requires",
                $"The module set contains more than one ruleset: {list}. A module set is built on exactly one ruleset; make every module require the same one.");
        }
    }

    private void CheckRequirementKinds(ModuleManifest module)
    {
        int rulesets = 0;
        int assets = 0;
        bool allResolved = true;
        foreach (ModuleRequirement requirement in module.Requires)
        {
            if (!_selected.TryGetValue(requirement.Id, out ModuleManifest? dependency))
            {
                allResolved = false;
                continue;
            }

            rulesets += dependency.Kind == ModuleKind.Ruleset ? 1 : 0;
            assets += dependency.Kind == ModuleKind.Assets ? 1 : 0;
            string? problem = KindProblem(module.Kind, dependency);
            if (problem is not null)
            {
                Error(module, "requires.kind", $"$.requires[{requirement.Index}].id", problem);
            }
        }

        if (!allResolved)
        {
            return;
        }

        string kind = module.Kind == ModuleKind.Extension ? "An extension" : "A campaign";
        if (module.Kind is ModuleKind.Extension or ModuleKind.Campaign && rulesets != 1)
        {
            Error(module, "requires.kind", "$.requires",
                $"{kind} must require exactly one ruleset module, but '{module.Id}' requires {rulesets}. "
                + "Add or keep one entry like { \"id\": \"osric\", \"version\": \"^0.1.0\" } that names a ruleset.");
        }

        if (module.Kind == ModuleKind.Campaign && assets == 0)
        {
            Error(module, "requires.kind", "$.requires",
                $"A campaign must require at least one assets module, but '{module.Id}' requires none. Add an entry that names an assets module.");
        }
    }

    private static string? KindProblem(ModuleKind kind, ModuleManifest dependency)
    {
        string dependencyKind = ModuleKinds.Name(dependency.Kind);
        if (dependency.Kind == ModuleKind.Campaign)
        {
            return $"'{dependency.Id}' is a campaign. No module can require a campaign.";
        }

        if (kind == ModuleKind.Ruleset && dependency.Kind is ModuleKind.Ruleset or ModuleKind.Extension)
        {
            return $"A ruleset can't require a {dependencyKind} ('{dependency.Id}'). To change a ruleset, write an extension that requires it.";
        }

        if (kind == ModuleKind.Assets && dependency.Kind != ModuleKind.Assets)
        {
            return $"An assets module can only require other assets modules, but '{dependency.Id}' is a {dependencyKind}.";
        }

        return null;
    }

    private string NotFoundMessage(ModuleManifest module, ModuleRequirement requirement)
    {
        string directories = catalog.SearchDirectories.Count == 0
            ? "(there are none)"
            : string.Join(", ", catalog.SearchDirectories);
        string message = $"'{module.Id}' requires '{requirement.Id}', but no module with that ID is in the search directories: {directories}. "
            + "Add the directory that holds it with --modules <dir> or to the \"modules\" list in goldbox.json.";
        if (catalog.Unreadable.Count > 0)
        {
            message += $" These module directories were skipped because their module.json has errors: {string.Join(", ", catalog.Unreadable)}.";
        }

        return message;
    }

    private string Available(string id)
    {
        IEnumerable<string> versions = catalog.Find(id)
            .OrderBy(candidate => candidate.Version)
            .Select(candidate => candidate.Version.ToString());
        return $"Available versions: {string.Join(", ", versions)}.";
    }

    private string RequiredBy(string id)
    {
        if (id == _root.Id)
        {
            return "the module being loaded";
        }

        return "required by " + string.Join(", ", _constraints[id].Select(constraint => constraint.By.Id).Distinct());
    }

    private static string Describe(IEnumerable<(ModuleManifest By, ModuleRequirement Requirement)> constraints)
    {
        return string.Join("; ", constraints.Select(constraint => $"'{constraint.By.Id}' requires {constraint.Requirement.Range}"));
    }

    private void Error(ModuleManifest module, string rule, string jsonPath, string message)
    {
        diagnostics.Add(new ModuleDiagnostic(rule, message, module.Id, module.ManifestPath, jsonPath));
    }
}
