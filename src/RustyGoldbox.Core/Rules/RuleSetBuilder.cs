using System.Text.Json;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Core.Rules;

/// <summary>
/// Builds a <see cref="RuleSet"/> from the definitions of a resolved module
/// set: resolves references, compiles tables and type-checks expressions.
/// </summary>
public sealed class RuleSetBuilder
{
    private readonly RuleSet _rules = new();
    private readonly List<ModuleDiagnostic> _diagnostics;
    private readonly Dictionary<Definition, ExprType?> _derivedTypes = [];
    private readonly HashSet<Definition> _inferring = [];
    private readonly Dictionary<string, ModuleManifest> _manifests = [];

    private RuleSetBuilder(IReadOnlyList<LoadedModule> modules, List<ModuleDiagnostic> diagnostics)
    {
        _diagnostics = diagnostics;
        foreach (LoadedModule module in modules)
        {
            _manifests[module.Manifest.Id] = module.Manifest;
            HashSet<string> visible = [module.Manifest.Id];
            visible.UnionWith(module.Requires.Select(requirement => requirement.Id));
            _rules.VisibleModules[module.Manifest.Id] = visible;
        }
    }

    public static RuleSet Build(IReadOnlyList<LoadedModule> modules, IReadOnlyList<Definition> definitions, List<ModuleDiagnostic> diagnostics)
    {
        RuleSetBuilder builder = new(modules, diagnostics);
        builder.Index(definitions);
        builder.ResolveReferences();
        builder.CompileTables();
        builder.CheckExpressions();
        builder.CompileModifiers();
        builder.CheckMonsterStats();
        builder.CheckCreationAttributes();
        builder.CheckAdvancement();
        builder.CheckLifepaths();
        builder.CheckCurrencies();
        builder.CheckEconomy();
        builder.CheckResting();
        builder.CheckGrants();
        builder.CheckActions();
        builder.CheckCampaigns();
        return builder._rules;
    }

    private void Index(IReadOnlyList<Definition> definitions)
    {
        foreach (Definition definition in definitions)
        {
            (string, string, string) key = (definition.Type.Name, definition.Module, definition.Id);
            if (_rules.ByKey.TryGetValue(key, out Definition? existing))
            {
                Error(definition, "definition.duplicate", "$.id",
                    $"{definition.Module} already has a {definition.Type.Name} '{definition.Id}' in {existing.File}. Give one of them another ID.");
                continue;
            }

            _rules.ByKey[key] = definition;
            _rules.Definitions.Add(definition);
        }

        foreach (Definition variable in _rules.OfType(DefinitionTypes.Variable))
        {
            bool area = variable.Json.TryGetProperty("scope", out JsonElement scope) && scope.GetString() == "area";
            Dictionary<string, Definition> variables = area ? _rules.AreaVariables : _rules.Variables;
            if (!variables.TryAdd(variable.Id, variable))
            {
                Error(variable, "variable.duplicate", "$.id", $"{(area ? "Area" : "Campaign")} variable '{variable.Id}' is already declared in {variables[variable.Id].QualifiedId}. Variable names are shared by the module set within each scope.");
            }
        }

        foreach (Definition currency in _rules.OfType(DefinitionTypes.Currency))
        {
            if (!_rules.Currencies.TryAdd(currency.Id, currency))
            {
                Error(currency, "currency.duplicate", "$.id", $"Currency '{currency.Id}' is already defined in {_rules.Currencies[currency.Id].QualifiedId}. Currency IDs are shared by the whole module set; use another ID.");
            }
        }

        HashSet<string> trackNames = [];
        foreach (Definition track in _rules.OfType(DefinitionTypes.Track))
        {
            if (RuleSet.BuiltInStats.ContainsKey(track.Id) || trackNames.Contains(track.Id) || RuleSet.BuiltInStats.ContainsKey($"max_{track.Id}"))
            {
                Error(track, "track.duplicate", "$.id", $"'{track.Id}' would hide a built-in or another track's max_ name. Use another ID.");
                continue;
            }

            if (!_rules.Tracks.TryAdd(track.Id, track))
            {
                Error(track, "track.duplicate", "$.id", $"Track '{track.Id}' is already defined in {_rules.Tracks[track.Id].QualifiedId}. Track IDs are shared by the whole module set.");
                continue;
            }

            trackNames.Add(track.Id);
            trackNames.Add($"max_{track.Id}");
        }

        List<Definition> levelTracks = _rules.Tracks.Values
            .Where(track => track.Json.TryGetProperty("from_levels", out JsonElement fromLevels) && fromLevels.GetBoolean())
            .ToList();
        foreach (Definition extra in levelTracks.Skip(1))
        {
            Error(extra, "track.from-levels", "$.from_levels", $"Only one track can have from_levels; {levelTracks[0].QualifiedId} already does.");
        }

        foreach (Definition track in _rules.Tracks.Values.Where(track => !track.Json.TryGetProperty("max", out _) && !levelTracks.Contains(track)))
        {
            Error(track, "track.max", "$", $"Track '{track.Id}' needs a \"max\" expression (or \"from_levels\": true): characters have nothing else to take its maximum from.");
        }

        foreach (Definition monster in _rules.OfType(DefinitionTypes.Monster))
        {
            List<string> given = monster.Json.TryGetProperty("tracks", out JsonElement monsterTracks)
                ? monsterTracks.EnumerateObject().Select(entry => entry.Name[(entry.Name.IndexOf(':', StringComparison.Ordinal) + 1)..]).ToList()
                : [];
            foreach (Definition track in _rules.Tracks.Values.Where(track => !track.Json.TryGetProperty("max", out _) && !given.Contains(track.Id)))
            {
                Error(monster, "track.max", "$.tracks",
                    $"Track '{track.Id}' doesn't compute a maximum, so the monster must give one: \"tracks\": {{ \"{track.Id}\": \"2d8\" }}.");
            }
        }

        // Class levels give hp exactly when a track is built from level gains.
        foreach (Definition characterClass in _rules.OfType(DefinitionTypes.Class))
        {
            int index = 0;
            foreach (JsonElement level in characterClass.Json.GetProperty("levels").EnumerateArray())
            {
                bool gains = level.TryGetProperty("hp", out _) || level.TryGetProperty("hp_bonus", out _);
                if (gains && levelTracks.Count == 0)
                {
                    Error(characterClass, "track.from-levels", $"$.levels[{index}]",
                        "Class levels gain \"hp\", but no track has \"from_levels\": true to receive it. Add one, for example { \"type\": \"track\", \"id\": \"hit_points\", \"name\": \"Hit points\", \"from_levels\": true }, or leave hp out.");
                    break;
                }

                if (!level.TryGetProperty("hp", out _) && levelTracks.Count > 0)
                {
                    Error(characterClass, "track.from-levels", $"$.levels[{index}]",
                        $"{levelTracks[0].QualifiedId} is built from class levels, so each level needs \"hp\": what it gains.");
                }

                index++;
            }
        }

        foreach (Definition definition in _rules.Definitions)
        {
            if (definition.Type != DefinitionTypes.Attribute && definition.Type != DefinitionTypes.Derived)
            {
                continue;
            }

            if (trackNames.Contains(definition.Id))
            {
                Error(definition, "stat.duplicate", "$.id", $"'{definition.Id}' is a track's name (tracks are read as <id> and max_<id>). Use another ID.");
                continue;
            }

            if (RuleSet.BuiltInStats.ContainsKey(definition.Id))
            {
                Error(definition, "stat.reserved", "$.id", $"'{definition.Id}' is a built-in stat every creature has. Use another ID.");
            }
            else if (_rules.Stats.TryGetValue(definition.Id, out Stat? existing))
            {
                Error(definition, "stat.duplicate", "$.id",
                    $"Stat '{definition.Id}' is already defined as a {existing.Definition.Type.Name} in {existing.Definition.QualifiedId} ({existing.Definition.File}). Stat IDs are shared by the whole module set; use another ID.");
            }
            else
            {
                // Attributes are numbers; a derived value's type is filled in when it is inferred.
                _rules.Stats[definition.Id] = new Stat(definition.Id, definition, ExprType.Number);
            }
        }
    }

    private void ResolveReferences()
    {
        foreach (Definition definition in _rules.Definitions)
        {
            foreach (ReferenceSite site in definition.References)
            {
                switch (site.Kind)
                {
                    case ReferenceKind reference:
                        Definition? target = Resolve(definition, DefinitionTypes.Find(reference.DefinitionType)!, site.Text, site.JsonPath);
                        if (target is not null)
                        {
                            _rules.References[(definition, site.JsonPath)] = target;
                            if (reference.Slot is string slot && Media.Problem(slot, target) is string unfit)
                            {
                                Error(definition, "reference.media", site.JsonPath, unfit);
                            }
                        }

                        break;
                    case StatKind stat:
                        CheckStat(definition, site, stat);
                        break;
                }
            }
        }
    }

    private void CheckStat(Definition definition, ReferenceSite site, StatKind kind)
    {
        if (!_rules.Stats.TryGetValue(site.Text, out Stat? stat))
        {
            Error(definition, "reference.stat", site.JsonPath, $"'{site.Text}' is not a stat. {StatList(kind.AttributesOnly)}");
        }
        else if (kind.AttributesOnly && !stat.IsAttribute)
        {
            Error(definition, "reference.stat", site.JsonPath, $"'{site.Text}' is a derived value, but an attribute is needed here. {StatList(true)}");
        }
    }

    private Definition? Resolve(Definition from, DefinitionType type, string text, string path)
    {
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        string module = colon < 0 ? from.Module : text[..colon];
        string id = colon < 0 ? text : text[(colon + 1)..];
        if (!_rules.VisibleModules[from.Module].Contains(module))
        {
            Error(from, "reference.module", path,
                $"'{text}' refers to module '{module}', which '{from.Module}' does not require. A module can refer only to itself and the modules in its own requires; add {{ \"id\": \"{module}\", \"version\": \"...\" }} to requires.");
            return null;
        }

        if (_rules.ByKey.TryGetValue((type.Name, module, id), out Definition? found))
        {
            return found;
        }

        List<string> known = _rules.OfType(type).Where(definition => definition.Module == module).Select(definition => definition.Id).ToList();
        string list = known.Count == 0 ? $"'{module}' has no {type.Name} definitions." : $"{type.Name} IDs in '{module}': {string.Join(", ", known)}.";
        string hint = colon < 0 ? " To refer to another module's definition, write module:id." : "";
        Error(from, "reference.not-found", path, $"There is no {type.Name} '{id}' in module '{module}'. {list}{hint}");
        return null;
    }

    private void CompileTables()
    {
        foreach (Definition definition in _rules.OfType(DefinitionTypes.Table))
        {
            CompiledTable table = new(definition);
            _rules.Tables[definition] = table;
            foreach ((int first, int second) in table.Overlaps())
            {
                Error(definition, "table.overlap", $"$.rows[{second}]",
                    $"Rows {first} and {second} match the same keys, so a lookup could find either. Make their keys or ranges distinct.");
            }
        }
    }

    private void CheckExpressions()
    {
        foreach (Definition derived in _rules.OfType(DefinitionTypes.Derived))
        {
            InferDerived(derived);
        }

        foreach (Definition definition in _rules.Definitions)
        {
            foreach (ExpressionSite site in definition.Expressions)
            {
                if (definition.Type == DefinitionTypes.Derived)
                {
                    continue;
                }

                Compile(definition, site);
            }
        }
    }

    private ExprType? InferDerived(Definition derived)
    {
        if (_derivedTypes.TryGetValue(derived, out ExprType? known))
        {
            return known;
        }

        ExpressionSite site = derived.Expressions.Single();
        if (!_inferring.Add(derived))
        {
            return null;
        }

        CompiledExpression? compiled = Compile(derived, site);
        _inferring.Remove(derived);
        _derivedTypes[derived] = compiled?.Type;
        if (compiled is not null && _rules.Stats.TryGetValue(derived.Id, out Stat? stat) && stat.Definition == derived)
        {
            _rules.Stats[derived.Id] = stat with { Type = compiled.Type };
        }

        return compiled?.Type;
    }

    private CompiledExpression? Compile(Definition definition, ExpressionSite site)
    {
        Expr root;
        try
        {
            root = Parser.Parse(site.Text);
        }
        catch (ExpressionException exception)
        {
            ExpressionError(definition, site, "expression.syntax", exception);
            return null;
        }

        ExpressionChecker checker = new(_rules, definition.Module, site.Kind.Roots, UseParameters(definition), ConditionValues(definition), InferDerived, IsInferring);
        ExprType type;
        try
        {
            type = checker.Check(root);
        }
        catch (ExpressionException exception)
        {
            ExpressionError(definition, site, "expression.type", exception);
            return null;
        }

        if (site.Kind.Expected is ExprType expected && type != expected)
        {
            Error(definition, "expression.type", site.JsonPath,
                $"In '{site.Text}': this field needs a {ExprTypes.Name(expected)}, but the expression gives a {ExprTypes.Name(type)}.");
            return null;
        }

        CompiledExpression compiled = new(site.Text, root, type, checker.Tables);
        _rules.Expressions[(definition, site.JsonPath)] = compiled;
        return compiled;
    }

    private void CompileModifiers()
    {
        foreach (Definition definition in _rules.Definitions)
        {
            if (!definition.Json.TryGetProperty("modifiers", out JsonElement modifiers))
            {
                continue;
            }

            List<Modifier> list = [];
            int index = 0;
            foreach (JsonElement modifier in modifiers.EnumerateArray())
            {
                string path = $"$.modifiers[{index}]";
                index++;
                if (!_rules.TryExpression(definition, $"{path}.value", out CompiledExpression? value))
                {
                    continue;
                }

                if (modifier.TryGetProperty("stat", out JsonElement stat))
                {
                    string id = stat.GetString()!;
                    if (_rules.Stats.TryGetValue(id, out Stat? target) && target.Type != ExprType.Number)
                    {
                        Error(definition, "expression.type", $"{path}.stat", $"Modifiers add numbers, but stat '{id}' is {ExprTypes.Name(target.Type)}.");
                        continue;
                    }

                    if (Reads(value!.Root, id))
                    {
                        Error(definition, "modifier.loop", $"{path}.value",
                            $"This modifier adds to {id} but reads self.{id}, so {id} would depend on itself. Read other stats instead.");
                        continue;
                    }

                    list.Add(new Modifier(id, null, value, path));
                }
                else if (_rules.References.TryGetValue((definition, $"{path}.track"), out Definition? track))
                {
                    list.Add(new Modifier(null, null, value!, path, Track: track));
                }
                else if (_rules.References.TryGetValue((definition, $"{path}.check"), out Definition? check))
                {
                    _rules.TryExpression(definition, $"{path}.against", out CompiledExpression? against);
                    list.Add(new Modifier(null, check, value!, path, against));
                }
            }

            _rules.Modifiers[definition] = list;
        }
    }

    private void CheckMonsterStats()
    {
        foreach (Definition monster in _rules.OfType(DefinitionTypes.Monster))
        {
            CheckMonsterSpells(monster);
            if (!monster.Json.TryGetProperty("stats", out JsonElement stats))
            {
                continue;
            }

            foreach (JsonProperty stat in stats.EnumerateObject())
            {
                string path = $"$.stats.{stat.Name}";
                if (!_rules.TryExpression(monster, path, out CompiledExpression? compiled))
                {
                    continue;
                }

                if (_rules.Stats.TryGetValue(stat.Name, out Stat? target) && target.Type != compiled!.Type)
                {
                    Error(monster, "expression.type", path,
                        $"In '{compiled.Text}': stat '{stat.Name}' is {ExprTypes.Name(target.Type)}, but this gives a {ExprTypes.Name(compiled.Type)}.");
                }
                else if (_rules.TryExpression(monster, path, out CompiledExpression? value) && Reads(value!.Root, stat.Name))
                {
                    Error(monster, "modifier.loop", path, $"This stat reads self.{stat.Name}, so it would depend on itself. Give it a value or read other stats.");
                }
            }
        }
    }

    /// <summary>A monster's spells have an effect to cast and, when cast a number of times a day, at least one.</summary>
    private void CheckMonsterSpells(Definition monster)
    {
        if (!monster.Json.TryGetProperty("spells", out JsonElement spells))
        {
            return;
        }

        for (int index = 0; index < spells.GetArrayLength(); index++)
        {
            string path = $"$.spells[{index}]";
            if (_rules.References.TryGetValue((monster, $"{path}.spell"), out Definition? spell) && !spell.Json.TryGetProperty("effect", out _))
            {
                Error(monster, "monster.spell", $"{path}.spell", $"{spell.Name} has no effect to cast in combat. Give the spell an \"effect\" or leave it out.");
            }

            if (spells[index].TryGetProperty("per_day", out JsonElement perDay) && perDay.GetInt32() < 1)
            {
                Error(monster, "monster.spell", $"{path}.per_day", "per_day must be at least 1; leave the spell out for none.");
            }
        }
    }

    /// <summary>At most one advancement definition, and class experience that matches it.</summary>
    private void CheckAdvancement()
    {
        List<Definition> all = _rules.OfType(DefinitionTypes.Advancement).ToList();
        foreach (Definition extra in all.Skip(1))
        {
            Error(extra, "advancement.duplicate", "$.id", $"A module set has at most one advancement definition, and {all[0].QualifiedId} is already one. Patch that one instead.");
        }

        Definition? advancement = all.FirstOrDefault();
        _rules.Advancement = advancement;
        string kind = advancement?.Json.TryGetProperty("kind", out JsonElement declaredKind) == true
            ? declaredKind.GetString()!
            : advancement?.Json.TryGetProperty("experience", out _) == true ? "experience" : "experience";
        bool experience = kind == "experience";
        bool byCharacter = experience && advancement?.Json.TryGetProperty("experience", out JsonElement experienceMode) == true && experienceMode.GetString() == "character";
        if (advancement is not null && !advancement.Json.TryGetProperty("experience", out _) && experience)
        {
            Error(advancement, "advancement.experience", "$.experience", "An experience advancement needs experience \"class\", \"character\" or \"split\"; give it the field or choose kind milestone or improvement.");
        }

        if (advancement is not null && advancement.Json.TryGetProperty("class_change", out _) && (!experience || advancement.Json.GetProperty("experience").GetString() != "split"))
        {
            Error(advancement, "advancement.class-change", "$.class_change", "class_change applies to experience \"split\", where a character advances in its own classes; remove it or change the experience.");
        }
        if (advancement is not null)
        {
            if (kind != "experience" && advancement.Json.TryGetProperty("experience", out _))
            {
                Error(advancement, "advancement.experience", "$.experience", $"Kind {kind} does not use experience levels; remove \"experience\" or choose kind experience.");
            }

            if (kind == "milestone")
            {
                if (!advancement.Json.TryGetProperty("milestones", out JsonElement milestones))
                {
                    Error(advancement, "advancement.milestones", "$.milestones", "A milestone advancement needs milestone choices for its skill raises, swaps or features.");
                }
                else
                {
                    CheckMilestones(advancement, milestones);
                }
            }

            if (kind == "improvement")
            {
                if (!advancement.Json.TryGetProperty("improvement", out JsonElement improvement))
                {
                    Error(advancement, "advancement.improvement", "$.improvement", "An improvement advancement needs improvement checks for marked skills.");
                }
                else
                {
                    CheckImprovement(advancement, improvement);
                }
            }

            bool hasLevels = advancement.Json.TryGetProperty("levels", out _);
            if (byCharacter && !hasLevels)
            {
                Error(advancement, "advancement.levels", "$", "With experience \"character\", give \"levels\": the experience needed for each character level, starting with 0.");
            }

            if (advancement.Json.TryGetProperty("level_boosts", out JsonElement levelBoosts))
            {
                for (int index = 0; index < levelBoosts.GetArrayLength(); index++)
                {
                    if (_rules.References.TryGetValue((advancement, $"$.level_boosts[{index}].amounts"), out Definition? amounts)
                        && (amounts.Json.GetProperty("keys").GetArrayLength() != 1 || amounts.Json.GetProperty("keys")[0].GetProperty("type").GetString() != "number"
                            || amounts.Json.GetProperty("value").GetString() != "number"))
                    {
                        Error(advancement, "advancement.level-boosts", $"$.level_boosts[{index}].amounts", $"{amounts.QualifiedId} must have one number key (the score) and number values (the raise).");
                    }

                    // Character files record which attributes a level boosted, not by how much; one table lets loading undo them.
                    if (index > 0 && amounts is not null
                        && _rules.References.TryGetValue((advancement, "$.level_boosts[0].amounts"), out Definition? first) && first != amounts)
                    {
                        Error(advancement, "advancement.level-boosts", $"$.level_boosts[{index}].amounts", $"Every level boost uses one amounts table ({first.QualifiedId}), so a character's earlier scores can be worked out from its boosts.");
                    }
                }
            }

            if (experience && !byCharacter && hasLevels)
            {
                Error(advancement, "advancement.levels", "$.levels", "With experience \"class\", each class's levels[].xp decide levels; remove \"levels\" or set experience to \"character\".");
            }
        }

        if (!experience)
        {
            return;
        }

        foreach (Definition characterClass in _rules.OfType(DefinitionTypes.Class))
        {
            int index = 0;
            foreach (JsonElement level in characterClass.Json.GetProperty("levels").EnumerateArray())
            {
                bool hasXp = level.TryGetProperty("xp", out _);
                if (byCharacter && hasXp)
                {
                    Error(characterClass, "class.xp", $"$.levels[{index}].xp", $"{advancement!.QualifiedId} sets experience by character level, so class levels don't give \"xp\". Remove it.");
                }
                else if (!byCharacter && !hasXp)
                {
                    string why = advancement is null ? "Without an advancement definition" : $"With {advancement.QualifiedId}'s experience \"{advancement.Json.GetProperty("experience").GetString()}\"";
                    Error(characterClass, "class.xp", $"$.levels[{index}]", $"{why}, each class level needs \"xp\": the experience it takes.");
                }

                index++;
            }
        }
    }

    /// <summary>Checks the data shape of term-by-term careers before Core consumes it.</summary>
    private void CheckLifepaths()
    {
        foreach (Definition lifepath in _rules.OfType(DefinitionTypes.Lifepath))
        {
            if (lifepath.Json.TryGetProperty("start_age", out JsonElement startAge) && startAge.GetInt32() < 0)
            {
                Error(lifepath, "lifepath.age", "$.start_age", "A lifepath start age cannot be negative.");
            }

            if (lifepath.Json.TryGetProperty("term_years", out JsonElement termYears) && termYears.GetInt32() <= 0)
            {
                Error(lifepath, "lifepath.term", "$.term_years", "A lifepath term must add at least one year.");
            }

            if (lifepath.Json.TryGetProperty("max_terms", out JsonElement maxTerms) && maxTerms.GetInt32() <= 0)
            {
                Error(lifepath, "lifepath.terms", "$.max_terms", "A lifepath must allow at least one term.");
            }

            JsonElement dice = lifepath.Json.GetProperty("dice");
            foreach (string dieName in new[] { "career", "skill", "aging", "benefit" })
            {
                JsonElement die = dice.GetProperty(dieName);
                if (die.GetProperty("count").GetInt32() <= 0)
                {
                    Error(lifepath, "lifepath.dice", $"$.dice.{dieName}.count", "A lifepath die count must be positive.");
                }

                if (die.GetProperty("sides").GetInt32() <= 0)
                {
                    Error(lifepath, "lifepath.dice", $"$.dice.{dieName}.sides", "A lifepath die must have positive sides.");
                }
            }

            foreach (string natural in new[] { "survival_natural_failure", "reenlistment_natural_success" })
            {
                if (lifepath.Json.TryGetProperty(natural, out JsonElement total)
                    && (total.GetInt32() < dice.GetProperty("career").GetProperty("count").GetInt32()
                        || total.GetInt32() > dice.GetProperty("career").GetProperty("count").GetInt32() * dice.GetProperty("career").GetProperty("sides").GetInt32()))
                {
                    Error(lifepath, "lifepath.dice", $"$.{natural}", $"{natural} must be a possible total of the configured career die.");
                }
            }

            int skillMaximum = dice.GetProperty("skill").GetProperty("count").GetInt32() * dice.GetProperty("skill").GetProperty("sides").GetInt32();
            int benefitMaximum = dice.GetProperty("benefit").GetProperty("count").GetInt32() * dice.GetProperty("benefit").GetProperty("sides").GetInt32();
            JsonElement aging = lifepath.Json.GetProperty("aging");

            JsonElement careers = lifepath.Json.GetProperty("careers");
            if (careers.GetArrayLength() == 0)
            {
                Error(lifepath, "lifepath.careers", "$.careers", $"{lifepath.QualifiedId} needs at least one career.");
            }

            HashSet<string> careerIds = [];
            for (int careerIndex = 0; careerIndex < careers.GetArrayLength(); careerIndex++)
            {
                JsonElement career = careers[careerIndex];
                string careerPath = $"$.careers[{careerIndex}]";
                string id = career.GetProperty("id").GetString()!;
                if (!careerIds.Add(id))
                {
                    Error(lifepath, "lifepath.career-duplicate", $"{careerPath}.id", $"Career ID '{id}' is repeated in {lifepath.QualifiedId}. Give each career a distinct ID.");
                }

                foreach (string phase in new[] { "qualification", "survival", "commission", "advancement", "reenlistment" })
                {
                    if (career.TryGetProperty(phase, out JsonElement roll))
                    {
                        CheckLifepathThrow(lifepath, roll, $"{careerPath}.{phase}");
                    }
                }

                JsonElement skillRolls = career.GetProperty("skill_rolls");
                foreach (string rollKind in new[] { "base", "no_commission", "commission", "advancement" })
                {
                    if (skillRolls.GetProperty(rollKind).GetInt32() < 0)
                    {
                        Error(lifepath, "lifepath.skill-rolls", $"{careerPath}.skill_rolls.{rollKind}", "A skill-roll count cannot be negative.");
                    }
                }

                JsonElement skills = career.GetProperty("skills");
                if (skills.GetArrayLength() == 0)
                {
                    Error(lifepath, "lifepath.skills", $"{careerPath}.skills", $"Career '{id}' needs at least one skill table.");
                }

                HashSet<string> tableIds = [];
                for (int tableIndex = 0; tableIndex < skills.GetArrayLength(); tableIndex++)
                {
                    JsonElement table = skills[tableIndex];
                    string tablePath = $"{careerPath}.skills[{tableIndex}]";
                    string tableId = table.GetProperty("id").GetString()!;
                    if (!tableIds.Add(tableId))
                    {
                        Error(lifepath, "lifepath.table-duplicate", $"{tablePath}.id", $"Skill table ID '{tableId}' is repeated for career '{id}'.");
                    }

                    HashSet<int> rolls = [];
                    JsonElement entries = table.GetProperty("entries");
                    if (entries.GetArrayLength() != skillMaximum)
                    {
                        Error(lifepath, "lifepath.table-roll", $"{tablePath}.entries", $"A skill table for the configured die needs exactly {skillMaximum} entries, one for each total from 1 through {skillMaximum}.");
                    }

                    for (int entryIndex = 0; entryIndex < entries.GetArrayLength(); entryIndex++)
                    {
                        JsonElement entry = entries[entryIndex];
                        string entryPath = $"{tablePath}.entries[{entryIndex}]";
                        int roll = entry.GetProperty("roll").GetInt32();
                        if (roll < 1 || roll > skillMaximum || !rolls.Add(roll))
                        {
                            Error(lifepath, "lifepath.table-roll", $"{entryPath}.roll", $"A skill table must have one distinct entry for each configured skill-die total from 1 through {skillMaximum}.");
                        }

                        CheckLifepathRaise(lifepath, entry, entryPath, entry.GetProperty("kind").GetString()!);
                    }
                }

                if (career.TryGetProperty("ranks", out JsonElement ranks))
                {
                    HashSet<int> rankIds = [];
                    for (int rankIndex = 0; rankIndex < ranks.GetArrayLength(); rankIndex++)
                    {
                        JsonElement rank = ranks[rankIndex];
                        string rankPath = $"{careerPath}.ranks[{rankIndex}]";
                        int number = rank.GetProperty("rank").GetInt32();
                        if (number < 1 || !rankIds.Add(number))
                        {
                            Error(lifepath, "lifepath.rank", $"{rankPath}.rank", "A career rank must be a distinct positive number.");
                        }

                        if (rank.TryGetProperty("skill", out _))
                        {
                            CheckLifepathRaise(lifepath, rank, rankPath, "skill");
                        }
                    }
                }

                JsonElement benefits = career.GetProperty("benefits");
                if (benefits.GetProperty("per_term").GetInt32() < 0)
                {
                    Error(lifepath, "lifepath.benefits", $"{careerPath}.benefits.per_term", "Benefits per successful term cannot be negative.");
                }

                JsonElement rankBenefits = benefits.GetProperty("rank_benefits");
                HashSet<int> rankBenefitRanks = [];
                for (int rankBenefitIndex = 0; rankBenefitIndex < rankBenefits.GetArrayLength(); rankBenefitIndex++)
                {
                    JsonElement rankBenefit = rankBenefits[rankBenefitIndex];
                    string rankBenefitPath = $"{careerPath}.benefits.rank_benefits[{rankBenefitIndex}]";
                    if (rankBenefit.GetProperty("min_rank").GetInt32() < 1 || !rankBenefitRanks.Add(rankBenefit.GetProperty("min_rank").GetInt32()))
                    {
                        Error(lifepath, "lifepath.benefits", $"{rankBenefitPath}.min_rank", "A rank benefit threshold must be distinct and positive.");
                    }

                    if (rankBenefit.GetProperty("count").GetInt32() < 0)
                    {
                        Error(lifepath, "lifepath.benefits", $"{rankBenefitPath}.count", "A rank benefit count cannot be negative.");
                    }
                }

                int materialModifier = 0;
                if (benefits.TryGetProperty("material_roll_modifier", out JsonElement materialRollModifier))
                {
                    if (materialRollModifier.GetProperty("min_rank").GetInt32() < 1)
                    {
                        Error(lifepath, "lifepath.benefits", $"{careerPath}.benefits.material_roll_modifier.min_rank", "A material-roll rank threshold must be positive.");
                    }

                    materialModifier = materialRollModifier.GetProperty("amount").GetInt32();
                }

                if (!benefits.TryGetProperty("cash", out _) && !benefits.TryGetProperty("material", out _))
                {
                    Error(lifepath, "lifepath.benefits", $"{careerPath}.benefits", $"Career '{id}' needs a cash or material benefit table.");
                }

                if (benefits.TryGetProperty("cash", out JsonElement cash))
                {
                    if (cash.GetArrayLength() == 0)
                    {
                        Error(lifepath, "lifepath.benefits", $"{careerPath}.benefits.cash", "A cash benefit table needs at least one row.");
                    }

                    CheckLifepathCashBenefits(lifepath, cash, $"{careerPath}.benefits.cash", benefitMaximum);
                }

                if (benefits.TryGetProperty("material", out JsonElement material))
                {
                    if (material.GetArrayLength() == 0)
                    {
                        Error(lifepath, "lifepath.benefits", $"{careerPath}.benefits.material", "A material benefit table needs at least one row.");
                    }

                    HashSet<int> rolls = [];
                    for (int benefitIndex = 0; benefitIndex < material.GetArrayLength(); benefitIndex++)
                    {
                        JsonElement benefit = material[benefitIndex];
                        string path = $"{careerPath}.benefits.material[{benefitIndex}]";
                        int roll = benefit.GetProperty("roll").GetInt32();
                        if (roll < 1 || roll > benefitMaximum + Math.Max(materialModifier, 0) || !rolls.Add(roll))
                        {
                            Error(lifepath, "lifepath.benefit-roll", $"{path}.roll", $"A material benefit table must have one distinct entry for each configured benefit-die total (and its configured modifier).");
                        }

                        string kind = benefit.GetProperty("kind").GetString()!;
                        if (kind is "skill" or "attribute")
                        {
                            CheckLifepathRaise(lifepath, benefit, path, kind);
                        }
                        else if (kind == "item" && !benefit.TryGetProperty("item", out _))
                        {
                            Error(lifepath, "lifepath.benefit", path, "An item benefit needs an \"item\" reference.");
                        }
                        else if (kind == "currency" && !benefit.TryGetProperty("currency", out _))
                        {
                            Error(lifepath, "lifepath.benefit", path, "A currency benefit needs a \"currency\" reference.");
                        }

                        if (benefit.TryGetProperty("amount", out JsonElement amount) && amount.GetInt32() < 0)
                        {
                            Error(lifepath, "lifepath.benefit", $"{path}.amount", "A material benefit amount cannot be negative.");
                        }
                    }

                    if (!rolls.Contains(1))
                    {
                        Error(lifepath, "lifepath.benefit-roll", $"{careerPath}.benefits.material", "A material benefit table needs a row for 1 so every configured benefit-die result selects a benefit.");
                    }
                }
            }

            {
                if (aging.GetProperty("start_age").GetInt32() < 0)
                {
                    Error(lifepath, "lifepath.age", "$.aging.start_age", "An ageing start age cannot be negative.");
                }

                if (aging.GetProperty("start_term").GetInt32() < 1)
                {
                    Error(lifepath, "lifepath.age", "$.aging.start_term", "An ageing start term must be positive.");
                }

                JsonElement effects = aging.GetProperty("effects");
                for (int effectIndex = 0; effectIndex < effects.GetArrayLength(); effectIndex++)
                {
                    JsonElement effect = effects[effectIndex];
                    string effectPath = $"$.aging.effects[{effectIndex}]";
                    if (effect.GetProperty("min").GetInt32() > effect.GetProperty("max").GetInt32())
                    {
                        Error(lifepath, "lifepath.aging-range", effectPath, "An ageing row's min must be no greater than its max.");
                    }

                    JsonElement changes = effect.GetProperty("changes");
                    for (int changeIndex = 0; changeIndex < changes.GetArrayLength(); changeIndex++)
                    {
                        JsonElement change = changes[changeIndex];
                        if (change.GetProperty("amount").GetInt32() < 0)
                        {
                            Error(lifepath, "lifepath.aging-change", $"{effectPath}.changes[{changeIndex}].amount", "An ageing change is subtracted, so its amount cannot be negative.");
                        }
                    }
                }
            }
        }
    }

    private void CheckLifepathThrow(Definition lifepath, JsonElement roll, string path)
    {
        bool hasStat = roll.TryGetProperty("stat", out _);
        bool hasCheck = roll.TryGetProperty("check", out _);
        if (hasStat && hasCheck)
        {
            Error(lifepath, "lifepath.throw", path, "A career throw needs exactly one of \"stat\" (a configured characteristic throw) or \"check\" (an existing check definition).");
        }

        // Re-enlistment is commonly a raw configured career throw with no characteristic.
        if (!hasStat && !hasCheck && !path.EndsWith(".reenlistment", StringComparison.Ordinal))
        {
            Error(lifepath, "lifepath.throw", path, "A career throw needs \"stat\" (a configured characteristic throw) or \"check\" (an existing check definition); only reenlistment may be a raw career throw.");
        }

        if (hasStat && !roll.TryGetProperty("target", out _))
        {
            Error(lifepath, "lifepath.throw", path, "A stat career throw needs \"target\": the number the configured career-die total must reach.");
        }

        if (!hasStat && !hasCheck && !roll.TryGetProperty("target", out _))
        {
            Error(lifepath, "lifepath.throw", path, "A raw reenlistment throw needs \"target\": the number the configured career-die total must reach.");
        }
    }

    private void CheckLifepathRaise(Definition lifepath, JsonElement entry, string path, string kind)
    {
        if (!entry.TryGetProperty("stat", out JsonElement stat) || !_rules.Stats.TryGetValue(stat.GetString()!, out Stat? target))
        {
            return;
        }

        if (kind == "attribute" && !target.IsAttribute)
        {
            Error(lifepath, "lifepath.attribute", $"{path}.stat", $"'{target.Id}' is a derived stat; an attribute is required for an attribute result.");
        }

        if (kind is "skill" or "attribute" && entry.TryGetProperty("amount", out JsonElement amount) && amount.GetInt32() < 1)
        {
            Error(lifepath, "lifepath.raise", $"{path}.amount", "A skill or attribute result must add at least one level.");
        }
    }

    private void CheckLifepathCashBenefits(Definition lifepath, JsonElement cash, string path, int maximum)
    {
        HashSet<int> rolls = [];
        for (int index = 0; index < cash.GetArrayLength(); index++)
        {
            JsonElement benefit = cash[index];
            string at = $"{path}[{index}]";
            int roll = benefit.GetProperty("roll").GetInt32();
            if (roll < 1 || roll > maximum || !rolls.Add(roll))
            {
                Error(lifepath, "lifepath.benefit-roll", $"{at}.roll", $"A cash benefit table must have one distinct entry for each configured benefit-die total from 1 through {maximum}.");
            }

            if (benefit.GetProperty("amount").GetInt32() < 0)
            {
                Error(lifepath, "lifepath.benefit", $"{at}.amount", "A cash benefit cannot subtract money.");
            }
        }

        if (!rolls.Contains(1))
        {
            Error(lifepath, "lifepath.benefit-roll", path, "A cash benefit table needs a row for 1 so every configured benefit-die result selects a benefit.");
        }
    }

    private void CheckMilestones(Definition advancement, JsonElement milestones)
    {
        if (milestones.TryGetProperty("skill_raise", out JsonElement raise))
        {
            if (raise.GetProperty("count").GetInt32() < 0)
            {
                Error(advancement, "advancement.milestones", "$.milestones.skill_raise.count", "A milestone raise count can't be negative.");
            }
            if (raise.GetProperty("amount").GetInt32() <= 0)
            {
                Error(advancement, "advancement.milestones", "$.milestones.skill_raise.amount", "A milestone raise amount must be greater than 0.");
            }
        }

        if (milestones.TryGetProperty("skill_swap", out JsonElement swap) && swap.GetProperty("count").GetInt32() < 0)
        {
            Error(advancement, "advancement.milestones", "$.milestones.skill_swap.count", "A milestone swap count can't be negative.");
        }

        if (milestones.TryGetProperty("feature", out JsonElement feature) && feature.GetProperty("count").GetInt32() < 0)
        {
            Error(advancement, "advancement.milestones", "$.milestones.feature.count", "A milestone feature count can't be negative.");
        }
    }

    private void CheckImprovement(Definition advancement, JsonElement improvement)
    {
        if (!improvement.TryGetProperty("checks", out JsonElement checks) || checks.GetArrayLength() == 0)
        {
            Error(advancement, "advancement.improvement", "$.improvement.checks", "An improvement advancement needs at least one skill check.");
            return;
        }

        HashSet<string> skills = [];
        for (int index = 0; index < checks.GetArrayLength(); index++)
        {
            string skill = checks[index].GetProperty("skill").GetString()!;
            if (!_rules.Stats.ContainsKey(skill))
            {
                Error(advancement, "advancement.improvement", $"$.improvement.checks[{index}].skill", $"'{skill}' is not a stat in this module set.");
            }
            else if (!skills.Add(skill))
            {
                Error(advancement, "advancement.improvement", $"$.improvement.checks[{index}].skill", $"Skill {skill} has more than one improvement check; give each skill one check.");
            }
        }
    }

    /// <summary>Every grant chooses at least one feature of a kind some feature has.</summary>
    private void CheckGrants()
    {
        HashSet<string> kinds = _rules.OfType(DefinitionTypes.Feature).Select(feature => feature.Json.GetProperty("kind").GetString()!).ToHashSet();
        foreach (Definition definition in _rules.Definitions)
        {
            List<(JsonElement Grants, string Path)> lists = [];
            if (definition.Type == DefinitionTypes.CharacterCreation && definition.Json.TryGetProperty("features", out JsonElement features))
            {
                lists.Add((features, "$.features"));
            }
            else if (definition.Type == DefinitionTypes.Advancement && definition.Json.TryGetProperty("grants", out JsonElement grants))
            {
                lists.Add((grants, "$.grants"));
            }
            else if (definition.Type == DefinitionTypes.Class)
            {
                int index = 0;
                foreach (JsonElement level in definition.Json.GetProperty("levels").EnumerateArray())
                {
                    if (level.TryGetProperty("grants", out JsonElement levelGrants))
                    {
                        lists.Add((levelGrants, $"$.levels[{index}].grants"));
                    }

                    index++;
                }
            }

            foreach ((JsonElement list, string path) in lists)
            {
                int index = 0;
                foreach (JsonElement grant in list.EnumerateArray())
                {
                    string at = $"{path}[{index}]";
                    index++;
                    bool hasKind = grant.TryGetProperty("kind", out JsonElement single);
                    bool hasKinds = grant.TryGetProperty("kinds", out JsonElement several);
                    if (hasKind == hasKinds)
                    {
                        Error(definition, "grant.kind", at, "A grant needs exactly one of \"kind\" (a feature kind) or \"kinds\" (several).");
                        continue;
                    }

                    List<(string Kind, string Path)> named = hasKind
                        ? [(single.GetString()!, $"{at}.kind")]
                        : several.EnumerateArray().Select((kind, number) => (kind.GetString()!, $"{at}.kinds[{number}]")).ToList();
                    foreach ((string kind, string kindPath) in named.Where(entry => !kinds.Contains(entry.Kind)))
                    {
                        string known = kinds.Count == 0 ? "No feature definitions are loaded." : $"Feature kinds: {string.Join(", ", kinds.Order(StringComparer.Ordinal))}.";
                        Error(definition, "grant.kind", kindPath, $"No feature has kind '{kind}', so nothing could fill this grant. {known}");
                    }

                    if (grant.TryGetProperty("count", out JsonElement count) && count.GetInt32() < 1)
                    {
                        Error(definition, "grant.count", $"{at}.count", "A grant chooses at least 1 feature.");
                    }
                }
            }
        }
    }

    private void CheckCreationAttributes()
    {
        List<string> attributes = _rules.Stats.Values.Where(stat => stat.IsAttribute).Select(stat => stat.Id).Order(StringComparer.Ordinal).ToList();
        foreach (Definition creation in _rules.OfType(DefinitionTypes.CharacterCreation))
        {
            List<string> listed = creation.Json.GetProperty("attributes").EnumerateArray().Select(entry => entry.GetString()!).ToList();
            if (!listed.Order(StringComparer.Ordinal).SequenceEqual(attributes))
            {
                Error(creation, "creation.attributes", "$.attributes",
                    $"attributes must list every attribute of the module set exactly once: {string.Join(", ", attributes)}.");
            }

            CheckCreationMethod(creation, listed.Count);
            CheckCreationSkillPoints(creation);
        }

        List<Definition> defaults = _rules.OfType(DefinitionTypes.CharacterCreation)
            .Where(creation => creation.Json.TryGetProperty("default", out JsonElement isDefault) && isDefault.GetBoolean())
            .ToList();
        foreach (Definition extra in defaults.Skip(1))
        {
            Error(extra, "creation.default", "$.default", $"{defaults[0].QualifiedId} is already the default character creation; only one may be.");
        }
    }

    private void CheckCreationSkillPoints(Definition creation)
    {
        if (!creation.Json.TryGetProperty("skill_points", out JsonElement config)
            || !config.TryGetProperty("skills", out JsonElement skills)
            || skills.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty entry in skills.EnumerateObject())
        {
            if (_rules.Stats.TryGetValue(entry.Name, out Stat? stat) && stat.IsAttribute)
            {
                Error(creation, "creation.skill-points", $"$.skill_points.skills.{entry.Name}",
                    $"Staged skill points must name derived skills, but '{entry.Name}' is an attribute.");
            }
        }
    }

    /// <summary>Each method's fields are there, and only that method's.</summary>
    private void CheckCreationMethod(Definition creation, int attributeCount)
    {
        string method = creation.Json.TryGetProperty("method", out JsonElement given) ? given.GetString()! : "roll";
        Dictionary<string, string[]> needs = new()
        {
            ["roll"] = ["attribute_roll"],
            ["array"] = ["array"],
            ["point-buy"] = ["base", "budget", "costs"],
            ["boosts"] = ["base", "boost"],
        };
        Dictionary<string, string> owner = new()
        {
            ["attribute_roll"] = "roll",
            ["assignment"] = "roll",
            ["attribute_rolls"] = "roll",
            ["array"] = "array",
            ["budget"] = "point-buy",
            ["costs"] = "point-buy",
            ["boost"] = "boosts",
            ["boosts"] = "boosts",
        };
        foreach (string field in needs[method].Where(field => !creation.Json.TryGetProperty(field, out _)))
        {
            Error(creation, "creation.method", "$", $"Method {method} needs \"{field}\" (see `goldbox schema character-creation`).");
        }

        foreach ((string field, string forMethod) in owner.Where(entry => entry.Value != method && creation.Json.TryGetProperty(entry.Key, out _)))
        {
            Error(creation, "creation.method", $"$.{field}", $"\"{field}\" belongs to method {forMethod}, but this creation's method is {method}. Remove it or change the method.");
        }

        if (method == "array" && creation.Json.TryGetProperty("array", out JsonElement array) && array.GetArrayLength() != attributeCount)
        {
            Error(creation, "creation.method", "$.array", $"The array needs one score per attribute ({attributeCount}), but has {array.GetArrayLength()}.");
        }

        if (method == "point-buy" && _rules.References.TryGetValue((creation, "$.costs"), out Definition? costs))
        {
            JsonElement keys = costs.Json.GetProperty("keys");
            if (keys.GetArrayLength() != 1 || keys[0].GetProperty("type").GetString() != "number" || costs.Json.GetProperty("value").GetString() != "number")
            {
                Error(creation, "creation.method", "$.costs", $"{costs.QualifiedId} must have one number key (the score) and number values (its cost).");
            }
        }
    }



    /// <summary>A field's terrain keys are single characters with a cost of 1 or more; an encounter's rows fit every field and use its keys.</summary>
    private void CheckTerrain()
    {
        List<Definition> fielded = _rules.OfType(DefinitionTypes.Combat).Where(combat => combat.Json.TryGetProperty("field", out _)).ToList();
        foreach (Definition combat in fielded)
        {
            if (!combat.Json.GetProperty("field").TryGetProperty("terrain", out JsonElement terrain))
            {
                continue;
            }

            foreach (JsonProperty entry in terrain.EnumerateObject())
            {
                if (entry.Name.Length != 1 || entry.Name[0] == CombatField.Open || char.IsWhiteSpace(entry.Name[0]))
                {
                    Error(combat, "combat.terrain", $"$.field.terrain.{entry.Name}", $"Terrain keys are one character other than '{CombatField.Open}' (open ground) and spaces, but '{entry.Name}' isn't.");
                }

                if (entry.Value.TryGetProperty("cost", out JsonElement cost) && cost.GetInt32() < 1)
                {
                    Error(combat, "combat.terrain", $"$.field.terrain.{entry.Name}.cost", "A terrain's cost must be at least 1.");
                }
            }
        }

        foreach (Definition encounter in _rules.OfType(DefinitionTypes.Encounter))
        {
            if (!encounter.Json.TryGetProperty("terrain", out JsonElement rows))
            {
                continue;
            }

            if (fielded.Count == 0)
            {
                Error(encounter, "encounter.terrain", "$.terrain", "Terrain needs a combat field to lie on, but no combat definition has a \"field\".");
                continue;
            }

            List<string> lines = rows.EnumerateArray().Select(row => row.GetString()!).ToList();
            foreach (Definition combat in fielded)
            {
                JsonElement field = combat.Json.GetProperty("field");
                int width = field.GetProperty("width").GetInt32();
                int height = field.GetProperty("height").GetInt32();
                if (lines.Count != height || lines.Any(line => line.Length != width))
                {
                    Error(encounter, "encounter.terrain", "$.terrain", $"Combat '{combat.Id}' has a {width} by {height} field, so terrain needs {height} rows of {width} characters, but it has {lines.Count} rows of {string.Join(", ", lines.Select(line => line.Length).Distinct())}.");
                    continue;
                }

                Dictionary<char, Terrain> kinds = CombatField.Kinds(field);
                for (int y = 0; y < lines.Count; y++)
                {
                    foreach (char key in lines[y].Where(key => key != CombatField.Open && !kinds.ContainsKey(key)).Distinct())
                    {
                        string known = kinds.Count == 0 ? "It declares no terrain." : $"Its terrain: {string.Join(", ", kinds.Keys.Select(k => $"'{k}'"))}.";
                        Error(encounter, "encounter.terrain", $"$.terrain[{y}]", $"'{key}' is not open ground ('{CombatField.Open}') or terrain the field of combat '{combat.Id}' declares. {known}");
                    }
                }
            }
        }
    }

    private void CheckActions()
    {
        CheckTerrain();
        foreach (Definition combat in _rules.OfType(DefinitionTypes.Combat))
        {
            if (combat.Json.TryGetProperty("round_limit", out JsonElement limit) && limit.GetInt32() < 1)
            {
                Error(combat, "combat.round-limit", "$.round_limit", "round_limit must be at least 1.");
            }

            bool elective = combat.Json.TryGetProperty("initiative_mode", out JsonElement mode) && mode.GetString() == "elective";
            if (!elective)
            {
                foreach (string field in new[] { "initiative", "initiative_by", "initiative_order", "initiative_each" })
                {
                    if (!combat.Json.TryGetProperty(field, out _))
                    {
                        Error(combat, "combat.initiative", $"$.{field}", $"Rolled initiative needs \"{field}\"; use initiative_mode \"elective\" when the last actor chooses the next creature.");
                    }
                }
            }
        }

        HashSet<string> budget = _rules.OfType(DefinitionTypes.Combat)
            .SelectMany(combat => combat.Json.GetProperty("budget").EnumerateArray().Select(entry => entry.GetProperty("id").GetString()!))
            .ToHashSet();
        foreach (Definition check in _rules.OfType(DefinitionTypes.Check))
        {
            if (!check.Json.TryGetProperty("post_roll", out JsonElement options))
            {
                continue;
            }

            for (int index = 0; index < options.GetArrayLength(); index++)
            {
                JsonElement option = options[index];
                bool bonus = option.TryGetProperty("bonus", out _);
                bool reroll = option.TryGetProperty("reroll", out JsonElement rerollValue) && rerollValue.GetBoolean();
                if (bonus == reroll)
                {
                    Error(check, "check.post-roll", $"$.post_roll[{index}]", "A post-roll option needs exactly one effect: a \"bonus\" expression or \"reroll\": true.");
                }

                if (_rules.References.TryGetValue((check, $"$.post_roll[{index}].track"), out Definition? track)
                    && track.Type != DefinitionTypes.Track)
                {
                    Error(check, "check.post-roll", $"$.post_roll[{index}].track", $"'{track.QualifiedId}' is not a resource track.");
                }
            }
        }

        foreach (Definition reaction in _rules.OfType(DefinitionTypes.Reaction))
        {
            foreach (JsonProperty entry in reaction.Json.GetProperty("cost").EnumerateObject().Where(entry => !budget.Contains(entry.Name)))
            {
                string known = budget.Count == 0 ? "No combat definition declares a budget." : $"Budget IDs: {string.Join(", ", budget.Order(StringComparer.Ordinal))}.";
                Error(reaction, "action.cost", $"$.cost.{entry.Name}", $"'{entry.Name}' is not a budget in any combat definition. {known}");
            }
        }

        foreach (Definition action in _rules.OfType(DefinitionTypes.Action))
        {
            JsonElement cost = action.Json.GetProperty("cost");
            foreach (JsonProperty entry in cost.EnumerateObject())
            {
                if (!budget.Contains(entry.Name))
                {
                    string known = budget.Count == 0 ? "No combat definition declares a budget." : $"Budget IDs: {string.Join(", ", budget.Order(StringComparer.Ordinal))}.";
                    Error(action, "action.cost", $"$.cost.{entry.Name}", $"'{entry.Name}' is not a budget in any combat definition. {known}");
                }
                else if (entry.Value.GetInt32() < 0)
                {
                    Error(action, "action.cost", $"$.cost.{entry.Name}", "Costs can't be negative.");
                }
            }

            if (!cost.EnumerateObject().Any(entry => entry.Value.GetInt32() > 0)
                && !(action.Json.TryGetProperty("available", out _)
                    && ContainsOperation(action.Json, "grant_budget"))
                && !IsReactionAction(action))
            {
                Error(action, "action.cost", "$.cost", "An action must cost at least 1 of some budget, or a creature could take it forever.");
            }

            if (action.Json.TryGetProperty("portions", out _) && action.Json.GetProperty("target").GetString() is "self" or "all_enemies" or "all_allies")
            {
                Error(action, "action.portions", "$.portions", "Portions go to one target at a time; a self or whole-side action can't divide its effect. Use a target of enemy, ally, hurt_ally or fallen_ally.");
            }

            bool hasCheck = action.Json.TryGetProperty("check", out _);
            if (action.Json.TryGetProperty("outcomes", out JsonElement outcomes))
            {
                if (!hasCheck)
                {
                    Error(action, "action.outcomes", "$.outcomes", "Outcomes need a check to choose between them. Add \"check\", or put the operations in \"always\".");
                }
                else if (_rules.References.TryGetValue((action, "$.check"), out Definition? check))
                {
                    CheckOutcomes(action, check, outcomes, "$.outcomes");
                }
            }

            if (!hasCheck && action.Json.TryGetProperty("check_bonus", out _))
            {
                Error(action, "action.check-bonus", "$.check_bonus", "check_bonus adds to the action's check, but it has none. Add \"check\", or give the bonus to a check operation's \"bonus\".");
            }

            if (!hasCheck && !action.Json.TryGetProperty("always", out _))
            {
                Error(action, "action.outcomes", "$", "An action needs \"always\" operations, or a \"check\" with \"outcomes\"; otherwise it does nothing.");
            }

            WalkOperations(action, action.Json, "$");
        }

        foreach (Definition definition in _rules.Definitions.Where(definition => definition.Type is { } type
                     && (type == DefinitionTypes.Action || type == DefinitionTypes.Condition)))
        {
            CheckBudgetOperations(definition, definition.Json, "$", budget);
        }

        foreach (Definition definition in _rules.Definitions)
        {
            foreach (ReferenceSite site in definition.References)
            {
                if (site.Kind is ReferenceKind { DefinitionType: "action" } && site.JsonPath.EndsWith(".action", StringComparison.Ordinal)
                    && _rules.References.TryGetValue((definition, site.JsonPath), out Definition? action))
                {
                    string usePath = site.JsonPath[..^".action".Length];
                    CheckUse(definition, JsonAt(definition.Json, usePath), usePath, action);
                }
            }

            if (definition.Type == DefinitionTypes.Condition)
            {
                WalkOperations(definition, definition.Json, "$");
            }
        }
    }

    private static bool ContainsOperation(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("op", out JsonElement op) && op.ValueKind == JsonValueKind.String && op.GetString() == name)
            {
                return true;
            }

            return element.EnumerateObject().Any(property => ContainsOperation(property.Value, name));
        }

        return element.ValueKind == JsonValueKind.Array && element.EnumerateArray().Any(item => ContainsOperation(item, name));
    }

    private bool IsReactionAction(Definition action)
    {
        return _rules.OfType(DefinitionTypes.Reaction).Any(reaction =>
            _rules.References.TryGetValue((reaction, "$.use.action"), out Definition? referenced) && referenced == action);
    }

    private void CheckBudgetOperations(Definition definition, JsonElement element, string path, HashSet<string> budget)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("op", out JsonElement op) && op.ValueKind == JsonValueKind.String && op.GetString() == "grant_budget"
                && element.TryGetProperty("budget", out JsonElement budgetId) && !budget.Contains(budgetId.GetString()!))
            {
                string known = budget.Count == 0 ? "No combat definition declares a budget." : $"Budget IDs: {string.Join(", ", budget.Order(StringComparer.Ordinal))}.";
                Error(definition, "operation.grant-budget", $"{path}.budget", $"'{budgetId.GetString()}' is not a budget in any combat definition. {known}");
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                CheckBudgetOperations(definition, property.Value, $"{path}.{property.Name}", budget);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                CheckBudgetOperations(definition, item, $"{path}[{index}]", budget);
                index++;
            }
        }
    }

    private void CheckCampaigns()
    {
        foreach (Definition variable in _rules.Variables.Values.Concat(_rules.AreaVariables.Values))
        {
            CheckValueType(variable, "$.initial", variable);
        }

        foreach (Definition definition in _rules.Definitions)
        {
            if (definition.Type == DefinitionTypes.Event)
            {
                CheckEvent(definition);
            }
            else if (definition.Type == DefinitionTypes.Asset)
            {
                string file = definition.Json.GetProperty("file").GetString()!;
                ModuleSource source = _manifests[definition.Module].Source;
                if (file.Split('/').Any(part => part is "" or "." or "..") || file.Contains('\\', StringComparison.Ordinal) || file.Contains(':', StringComparison.Ordinal))
                {
                    Error(definition, "asset.file", "$.file", $"'{file}' must be a relative path inside the module, with forward slashes.");
                }
                else if (!source.Contains(file))
                {
                    Error(definition, "asset.file", "$.file", $"There is no file '{file}' in module '{definition.Module}' ({source.PathOf(file)}).");
                }
                else if (Media.MediaOf(definition) == "audio")
                {
                    CheckAudio(definition, source, file);
                }
                else
                {
                    CheckImage(definition, source, file);
                }
            }
            else if (definition.Type == DefinitionTypes.Figure)
            {
                CheckFigure(definition);
            }
            else if (definition.Type == DefinitionTypes.Campaign)
            {
                CheckEntry(definition, "$.start.area", definition.Json.GetProperty("start").GetProperty("entry").GetString()!, "$.start.entry");
                JsonElement party = definition.Json.GetProperty("party");
                int min = party.GetProperty("min").GetInt32();
                int max = party.GetProperty("max").GetInt32();
                if (min < 1 || max < min)
                {
                    Error(definition, "campaign.party", "$.party", $"Party size needs 1 <= min <= max, but min is {min} and max is {max}.");
                }
            }
        }

        // After the loop above, so every asset's image size is known.
        foreach (Definition skin in _rules.OfType(DefinitionTypes.Skin))
        {
            CheckSkin(skin);
        }

        foreach (ModuleManifest module in _manifests.Values)
        {
            List<Definition> campaigns = _rules.OfType(DefinitionTypes.Campaign).Where(definition => definition.Module == module.Id).ToList();
            if (module.Kind == ModuleKind.Campaign && campaigns.Count != 1)
            {
                _diagnostics.Add(new ModuleDiagnostic("campaign.definition", campaigns.Count == 0
                    ? "A campaign module needs one campaign definition (see `goldbox schema campaign`)."
                    : $"A campaign module has exactly one campaign definition, but this one has {campaigns.Count}.", module.Id, module.ManifestPath));
            }
            else if (module.Kind != ModuleKind.Campaign)
            {
                foreach (Definition campaign in campaigns)
                {
                    Error(campaign, "campaign.definition", "$", $"Only campaign modules can define a campaign; '{module.Id}' is a {ModuleKinds.Name(module.Kind)}.");
                }
            }
        }
    }

    /// <summary>A skin is art: it lives in an assets or campaign module, its colours parse, and its slices fit their images.</summary>
    private void CheckSkin(Definition skin)
    {
        ModuleKind kind = _manifests[skin.Module].Kind;
        if (kind is not (ModuleKind.Assets or ModuleKind.Campaign))
        {
            Error(skin, "skin.module", "$", $"Skins are art, so they live in assets or campaign modules; '{skin.Module}' is a {ModuleKinds.Name(kind)}.");
        }

        if (skin.Json.TryGetProperty("colors", out JsonElement colors))
        {
            foreach (JsonProperty color in colors.EnumerateObject())
            {
                string at = $"$.colors.{color.Name}";
                if (!DefinitionTypes.SkinColors.Contains(color.Name))
                {
                    Error(skin, "skin.color", at, $"'{color.Name}' is not a skin colour; colours are {string.Join(", ", DefinitionTypes.SkinColors)}.");
                }
                else if (!IsColor(color.Value.GetString()!))
                {
                    Error(skin, "skin.color", at, $"'{color.Value.GetString()}' is not a colour; write #rgb, #rrggbb or #rrggbbaa.");
                }
            }
        }

        if (skin.Json.TryGetProperty("layout", out JsonElement layout))
        {
            Dictionary<string, double> values = layout.EnumerateObject().ToDictionary(part => part.Name, part => part.Value.GetDouble());
            foreach ((string part, string problem) in SkinLayout.Problems(values))
            {
                Error(skin, "skin.layout", $"$.layout.{part}", problem);
            }
        }

        foreach (string part in new[] { "frame", "button" })
        {
            string path = $"$.{part}.picture";
            if (!skin.Json.TryGetProperty(part, out JsonElement nine)
                || !_rules.References.TryGetValue((skin, path), out Definition? picture)
                || !_rules.ImageSizes.TryGetValue(picture, out (int Width, int Height) size))
            {
                continue;
            }

            int slice = nine.GetProperty("slice").GetInt32();
            if (slice < 1 || slice * 2 >= size.Width || slice * 2 >= size.Height)
            {
                Error(skin, "skin.slice", $"$.{part}.slice", $"A slice of {slice} doesn't cut the {size.Width} x {size.Height} image into nine; it must be at least 1 and under half of each side.");
            }
        }
    }

    private static bool IsColor(string text)
    {
        return text.Length is 4 or 7 or 9 && text[0] == '#' && text[1..].All(Uri.IsHexDigit);
    }

    /// <summary>An audio asset's file is one the Engine decodes, and it has no picture fields.</summary>
    private void CheckAudio(Definition asset, ModuleSource source, string file)
    {
        byte[] bytes;
        try
        {
            bytes = source.Read(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error(asset, "asset.file", "$.file", $"Can't read '{file}': {exception.Message}");
            return;
        }

        bool ogg = Starts(bytes, "OggS");
        bool wav = Starts(bytes, "RIFF") && bytes.Length >= 12 && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE";
        bool flac = Starts(bytes, "fLaC");
        if (!ogg && !wav && !flac)
        {
            Error(asset, "asset.audio", "$.file", $"'{file}' is not audio the Engine decodes; use {string.Join(", ", Media.AudioFormats)}.");
        }

        foreach (string field in Media.SheetFields.Append("regions").Append("sampling").Where(field => asset.Json.TryGetProperty(field, out _)))
        {
            Error(asset, "asset.audio", $"$.{field}", $"\"{field}\" is for pictures; this asset is audio.");
        }
    }

    private static bool Starts(byte[] bytes, string magic)
    {
        return bytes.Length >= magic.Length && System.Text.Encoding.ASCII.GetString(bytes, 0, magic.Length) == magic;
    }

    /// <summary>The asset's file is a PNG the renderer admits, and a wall set's frames lie inside it.</summary>
    private void CheckImage(Definition asset, ModuleSource source, string file)
    {
        byte[] bytes;
        try
        {
            bytes = source.Read(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error(asset, "asset.file", "$.file", $"Can't read '{file}': {exception.Message}");
            return;
        }

        if (PngImage.Read(bytes, out int width, out int height) is string problem)
        {
            Error(asset, "asset.image", "$.file", $"'{file}' {problem} Save it as an 8-bit RGBA PNG.");
            return;
        }

        _rules.ImageSizes[asset] = (width, height);
        bool sheet = Media.MediaOf(asset) == "sheet";
        if (asset.Json.TryGetProperty("regions", out JsonElement regions))
        {
            if (sheet)
            {
                Error(asset, "asset.regions", "$.regions", "Only an image has regions; a sheet is cut into frames by its frame_size.");
            }
            else
            {
                CheckRegions(asset, regions, width, height);
            }
        }

        if (sheet)
        {
            CheckSheet(asset, width, height);
        }
        else
        {
            foreach (string field in Media.SheetFields.Where(field => asset.Json.TryGetProperty(field, out _)))
            {
                Error(asset, "asset.sheet", $"$.{field}", $"\"{field}\" is a sheet field; this asset is an image. Make it \"media\": \"sheet\" to cut it into frames.");
            }
        }
    }

    private void CheckRegions(Definition asset, JsonElement regions, int width, int height)
    {
        foreach (JsonProperty region in regions.EnumerateObject())
        {
            int[] rect = region.Value.EnumerateArray().Select(value => value.GetInt32()).ToArray();
            if (rect[0] < 0 || rect[1] < 0 || rect[2] < 1 || rect[3] < 1 || (long)rect[0] + rect[2] > width || (long)rect[1] + rect[3] > height)
            {
                Error(asset, "asset.regions", $"$.regions.{region.Name}", $"[{string.Join(", ", rect)}] must be [x, y, width, height] with a positive size inside the {width} x {height} image.");
                continue;
            }

            CheckLinearCrop(asset, $"$.regions.{region.Name}", rect[0], rect[1], rect[2], rect[3], width, height, "region");
        }
    }

    /// <summary>
    /// The pinned Engine rejects a degenerate sprite-atlas UV rectangle. A
    /// half-pixel inset keeps linear filtering inside a crop, but a one-pixel
    /// cropped axis would collapse when both edges move inward. Reject that
    /// one concrete case at the module boundary so the DOM and Engine retain
    /// the same authored sampling policy.
    /// </summary>
    private void CheckLinearCrop(Definition asset, string path, int x, int y, int cropWidth, int cropHeight, int imageWidth, int imageHeight, string kind)
    {
        if (!asset.Json.TryGetProperty("sampling", out JsonElement sampling)
            || sampling.ValueKind != JsonValueKind.String
            || sampling.GetString() != "linear")
        {
            return;
        }

        string? axis = x > 0 || cropWidth < imageWidth
            ? cropWidth == 1 ? "width" : null
            : null;
        axis ??= y > 0 || cropHeight < imageHeight
            ? cropHeight == 1 ? "height" : null
            : null;
        if (axis is not null)
        {
            Error(asset, "asset.sampling", path, $"linear sampling cannot safely filter a one-pixel cropped {kind} on its {axis} axis: the pinned Engine rejects a degenerate sprite-atlas UV rectangle (CSHARP_SPRITE_ATLAS_FRAME: DegenerateRect). Use \"nearest\" for this crop or make its {axis} dimension at least 2 pixels.");
        }
    }

    /// <summary>A figure draws exactly one monster, class or terrain key, and nothing has two figures.</summary>
    private void CheckFigure(Definition figure)
    {
        bool monster = figure.Json.TryGetProperty("monster", out _);
        bool characterClass = figure.Json.TryGetProperty("class", out _);
        bool terrain = figure.Json.TryGetProperty("terrain", out _);
        if ((monster ? 1 : 0) + (characterClass ? 1 : 0) + (terrain ? 1 : 0) != 1)
        {
            Error(figure, "figure.subject", "$", "A figure draws exactly one thing: give \"monster\", \"class\" or \"terrain\" (with \"combat\"), not several or none.");
            return;
        }

        if (terrain)
        {
            CheckTerrainFigure(figure);
            return;
        }

        string path = monster ? "$.monster" : "$.class";
        if (!_rules.References.TryGetValue((figure, path), out Definition? subject) || !_rules.References.TryGetValue((figure, "$.sprite"), out Definition? sprite))
        {
            return;
        }

        Definition? earlier = _rules.OfType(DefinitionTypes.Figure)
            .TakeWhile(other => other != figure)
            .FirstOrDefault(other => _rules.References.TryGetValue((other, path), out Definition? drawn) && drawn == subject);
        if (earlier is not null)
        {
            Error(figure, "figure.duplicate", path, $"{subject.QualifiedId} already has a figure ({earlier.QualifiedId}); a module set has one figure for each monster or class.");
            return;
        }

        _rules.Figures[subject] = sprite;
        if (_rules.References.TryGetValue((figure, "$.icon"), out Definition? icon))
        {
            _rules.Icons[subject] = icon;
        }
    }

    /// <summary>A terrain figure names a key its combat's field declares, once in the module set.</summary>
    private void CheckTerrainFigure(Definition figure)
    {
        if (!figure.Json.TryGetProperty("combat", out _))
        {
            Error(figure, "figure.subject", "$.combat", "A terrain figure needs the \"combat\" whose field declares the terrain.");
            return;
        }

        if (!_rules.References.TryGetValue((figure, "$.combat"), out Definition? combat) || !_rules.References.TryGetValue((figure, "$.sprite"), out Definition? sprite))
        {
            return;
        }

        string key = figure.Json.GetProperty("terrain").GetString()!;
        Dictionary<char, Terrain> kinds = combat.Json.TryGetProperty("field", out JsonElement field) ? CombatField.Kinds(field) : [];
        if (key.Length != 1 || !kinds.ContainsKey(key[0]))
        {
            string known = kinds.Count == 0 ? $"Combat '{combat.Id}' declares no terrain." : $"Its terrain: {string.Join(", ", kinds.Keys.Select(k => $"'{k}'"))}.";
            Error(figure, "figure.subject", "$.terrain", $"'{key}' is not terrain the field of combat '{combat.Id}' declares. {known}");
            return;
        }

        if (!_rules.TerrainFigures.TryAdd((combat, key[0]), sprite))
        {
            Error(figure, "figure.duplicate", "$.terrain", $"Terrain '{key}' of {combat.QualifiedId} already has a figure; a module set has one figure for each.");
        }
    }

    private static bool Inside(int value, int size) => value >= 0 && value < size;

    /// <summary>A sprite sheet is a whole grid of equal frames; its animations play frames it has.</summary>
    private void CheckSheet(Definition asset, int width, int height)
    {
        JsonElement json = asset.Json;
        if (!json.TryGetProperty("frame_size", out _))
        {
            Error(asset, "asset.sheet", "$", "A sheet needs \"frame_size\", the [width, height] of one frame (see `goldbox schema asset`).");
        }

        if (json.TryGetProperty("height", out JsonElement standing) && standing.GetDouble() <= 0)
        {
            Error(asset, "asset.sheet", "$.height", "height must be more than 0 cells.");
        }

        if (!json.TryGetProperty("frame_size", out JsonElement size))
        {
            return;
        }

        int frameWidth = size[0].GetInt32();
        int frameHeight = size[1].GetInt32();
        if (frameWidth < 1 || frameHeight < 1 || width % frameWidth != 0 || height % frameHeight != 0)
        {
            Error(asset, "asset.sheet", "$.frame_size", $"[{frameWidth}, {frameHeight}] must divide the {width} x {height} image into whole frames.");
            return;
        }

        CheckLinearCrop(asset, "$.frame_size", 0, 0, frameWidth, frameHeight, width, height, "sheet frame");

        int cells = width / frameWidth * (height / frameHeight);
        int count = cells;
        if (json.TryGetProperty("frame_count", out JsonElement declared))
        {
            count = declared.GetInt32();
            if (count < 1 || count > cells)
            {
                Error(asset, "asset.sheet", "$.frame_count", $"frame_count must be from 1 to the {cells} frames the image holds.");
                return;
            }
        }

        if (json.TryGetProperty("anchor", out JsonElement anchor)
            && (!Inside(anchor[0].GetInt32(), frameWidth) || !Inside(anchor[1].GetInt32(), frameHeight)))
        {
            Error(asset, "asset.sheet", "$.anchor", $"The anchor must be a pixel inside the {frameWidth} x {frameHeight} frame.");
        }

        if (!json.TryGetProperty("animations", out JsonElement animations))
        {
            return;
        }

        foreach (JsonProperty animation in animations.EnumerateObject())
        {
            string at = $"$.animations.{animation.Name}";
            JsonElement played = animation.Value.GetProperty("frames");
            if (played.GetArrayLength() == 0)
            {
                Error(asset, "asset.sheet", $"{at}.frames", "An animation plays at least one frame.");
            }

            int index = 0;
            foreach (JsonElement frame in played.EnumerateArray())
            {
                if (frame.GetInt32() < 0 || frame.GetInt32() >= count)
                {
                    Error(asset, "asset.sheet", $"{at}.frames[{index}]", $"Frame {frame.GetInt32()} isn't in the sheet; frames are 0 to {count - 1}.");
                }

                index++;
            }

            if (animation.Value.GetProperty("fps").GetDouble() <= 0)
            {
                Error(asset, "asset.sheet", $"{at}.fps", "fps must be more than 0.");
            }
        }
    }

    private void CheckResting()
    {
        foreach (Definition resting in _rules.OfType(DefinitionTypes.Resting))
        {
            bool rounds = resting.Json.GetProperty("unit").GetString() == "rounds";
            bool combat = resting.Json.TryGetProperty("combat", out _);
            if (rounds && !combat)
            {
                Error(resting, "resting.duration", "$.combat", "A rounds policy needs a combat reference for the ruleset's round_seconds.");
            }
            else if (!rounds && combat)
            {
                Error(resting, "resting.duration", "$.combat", "combat only applies to a rounds policy; omit it for hours or days.");
            }
        }
    }

    private void CheckEconomy()
    {
        List<Definition> economies = _rules.OfType(DefinitionTypes.Economy).ToList();
        _rules.Economy = economies.FirstOrDefault();
        foreach (Definition extra in economies.Skip(1))
        {
            Error(extra, "economy.duplicate", "$.id", $"A module set has at most one economy; {economies[0].QualifiedId} already defines it. Patch that one instead.");
        }

        foreach (Definition economy in economies.Where(economy => _manifests[economy.Module].Kind != ModuleKind.Ruleset))
        {
            Error(economy, "economy.module", "$", "The economy belongs in the ruleset. Extensions and campaigns can patch its sell_fraction.");
        }
    }

    private void CheckCurrencies()
    {
        foreach (Definition currency in _rules.OfType(DefinitionTypes.Currency))
        {
            if (_manifests[currency.Module].Kind != ModuleKind.Ruleset)
            {
                Error(currency, "currency.module", "$", "A currency belongs in the ruleset. Extensions and campaigns cannot introduce money types.");
            }
        }
    }

    private void CheckEvent(Definition definition)
    {
        string kind = definition.Json.GetProperty("kind").GetString()!;
        switch (kind)
        {
            case "text":
                CheckTextViews(definition);
                break;
            case "perception":
                CheckPerception(definition);
                break;
            case "give" or "take":
                if (definition.Json.TryGetProperty("count", out JsonElement copies) && copies.GetInt32() <= 0)
                {
                    Error(definition, "event.items", "$.count", "An item count must be a positive whole number; omit count for one copy.");
                }

                break;
            case "treasure":
                bool hasCurrency = definition.Json.TryGetProperty("currency", out _);
                bool hasAmount = definition.Json.TryGetProperty("amount", out _);
                if (hasCurrency != hasAmount)
                {
                    Error(definition, "event.treasure", "$", "A treasure event needs both \"currency\" and \"amount\", or neither for item-only treasure.");
                }

                break;
            case "rest":
                bool timed = definition.Json.TryGetProperty("resting", out _);
                if (timed && (!definition.Json.TryGetProperty("periods", out JsonElement count) || count.GetInt32() <= 0))
                {
                    Error(definition, "event.rest", "$.periods", "Timed rest requires a positive whole number of periods.");
                }
                else if (!timed && (definition.Json.TryGetProperty("periods", out _) || definition.Json.TryGetProperty("wandering", out _)))
                {
                    Error(definition, "event.rest", "$", "periods and wandering require a resting policy; omit them for immediate rest.");
                }

                if (_rules.References.TryGetValue((definition, "$.wandering.event"), out Definition? roaming) && roaming.Json.GetProperty("kind").GetString() != "combat")
                {
                    Error(definition, "event.rest", "$.wandering.event", "The wandering event must have kind combat, with an encounter and its outcome chains.");
                }

                break;
            case "training" when !Characters.CharacterRules.RequiresTraining(_rules):
                Error(definition, "event.training", "$", "A training event needs advancement.training with cost and days expressions in its ruleset.");
                break;
            case "shop" when _rules.Economy is null:
                Error(definition, "event.shop", "$", "A shop needs an economy definition in its ruleset, for example { \"type\": \"economy\", \"id\": \"standard\", \"sell_fraction\": 0.5 }.");
                break;
            case "set":
                if (_rules.References.TryGetValue((definition, "$.variable"), out Definition? variable))
                {
                    CheckValueType(definition, "$.value", variable);
                }

                break;
            case "open" when string.IsNullOrWhiteSpace(definition.Json.GetProperty("door").GetString()):
                Error(definition, "event.open", "$.door", "An open event needs the ID of a door in the current area.");
                break;
            case "teleport":
                CheckEntry(definition, "$.area", definition.Json.GetProperty("entry").GetString()!, "$.entry");
                break;
            case "combat":
                CheckCombatEvent(definition);
                break;
        }
    }

    private void CheckTextViews(Definition definition)
    {
        if (!definition.Json.TryGetProperty("views", out JsonElement views))
        {
            return;
        }

        if (views.GetArrayLength() == 0)
        {
            Error(definition, "event.views", "$.views", "A text event's views must contain at least one authored mode.");
            return;
        }

        HashSet<string> modes = new(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement view in views.EnumerateArray())
        {
            string mode = view.GetProperty("mode").GetString()!;
            if (string.IsNullOrWhiteSpace(mode))
            {
                Error(definition, "event.views", $"$.views[{index}].mode", "A view mode must be nonempty text.");
            }
            else if (!modes.Add(mode))
            {
                Error(definition, "event.views", $"$.views[{index}].mode", $"View mode '{mode}' is repeated; each authored view needs a distinct mode.");
            }

            index++;
        }
    }

    private void CheckPerception(Definition definition)
    {
        foreach (string field in new[] { "scope", "success_mode", "failure_mode" })
        {
            if (string.IsNullOrWhiteSpace(definition.Json.GetProperty(field).GetString()))
            {
                Error(definition, "event.perception", $"$.{field}", $"{field} must be nonempty text.");
            }
        }
    }

    private void CheckCombatEvent(Definition definition)
    {
        Definition? combat = null;
        _rules.References.TryGetValue((definition, "$.encounter"), out Definition? encounter);
        if (definition.Json.TryGetProperty("combat", out _))
        {
            _rules.References.TryGetValue((definition, "$.combat"), out combat);
        }
        else
        {
            List<Definition> combats = _rules.OfType(DefinitionTypes.Combat).ToList();
            if (combats.Count != 1)
            {
                Error(definition, "event.combat", "$", combats.Count == 0
                    ? "There is no combat definition in the module set to fight with."
                    : "The module set has more than one combat definition; name one with \"combat\".");
            }
            else
            {
                combat = combats[0];
            }
        }

        if (definition.Json.TryGetProperty("surprise_rounds", out JsonElement rounds) && rounds.GetInt32() < 1)
        {
            Error(definition, "event.combat-surprise", "$.surprise_rounds", "surprise_rounds must be at least 1.");
        }
        if (!definition.Json.TryGetProperty("surprise", out _) && definition.Json.TryGetProperty("surprise_rounds", out _))
        {
            Error(definition, "event.combat-surprise", "$.surprise_rounds", "surprise_rounds needs surprise to name party or monsters.");
        }

        foreach (string path in new[] { "$.party_start", "$.monsters_start" })
        {
            if (!definition.Json.TryGetProperty(path[2..], out JsonElement start))
            {
                continue;
            }

            if (combat is null || !combat.Json.TryGetProperty("field", out JsonElement field))
            {
                Error(definition, "event.combat-placement", path, "A starting cell needs the selected combat definition to have a field.");
                continue;
            }

            int x = start[0].GetInt32();
            int y = start[1].GetInt32();
            int width = field.GetProperty("width").GetInt32();
            int height = field.GetProperty("height").GetInt32();
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                Error(definition, "event.combat-placement", path, $"Starting cell [{x}, {y}] is outside the combat field's {width} by {height} cells.");
            }
            else if (encounter is not null && !CombatField.Of(combat, encounter)!.Passable(new Cell(x, y)))
            {
                Error(definition, "event.combat-placement", path, $"Starting cell [{x}, {y}] is impassable in encounter '{encounter.Name}'. Choose an open or passable terrain cell.");
            }
        }
    }

    private void CheckValueType(Definition owner, string path, Definition variable)
    {
        ExprTypes.TryParse(variable.Json.GetProperty("value_type").GetString()!, out ExprType expected);
        if (_rules.TryExpression(owner, path, out CompiledExpression? value) && value!.Type != expected)
        {
            Error(owner, "expression.type", path, $"In '{value.Text}': variable '{variable.Id}' is {ExprTypes.Name(expected)}, but this gives a {ExprTypes.Name(value.Type)}.");
        }
    }

    private void CheckEntry(Definition owner, string areaPath, string entry, string entryPath)
    {
        if (_rules.References.TryGetValue((owner, areaPath), out Definition? area) && !area.Json.GetProperty("entries").TryGetProperty(entry, out _))
        {
            string known = string.Join(", ", area.Json.GetProperty("entries").EnumerateObject().Select(property => property.Name));
            Error(owner, "area.entry", entryPath, $"Area {area.QualifiedId} has no entry '{entry}'. Its entries: {known}.");
        }
    }

    /// <summary>Finds check operations under an element and checks their outcome tiers.</summary>
    private void WalkOperations(Definition definition, JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("op", out JsonElement applied) && applied.ValueKind == JsonValueKind.String && applied.GetString() == "apply_condition"
                && element.TryGetProperty("values", out JsonElement given)
                && _rules.References.TryGetValue((definition, $"{path}.condition"), out Definition? condition))
            {
                List<string> declared = ConditionValues(condition);
                foreach (JsonProperty value in given.EnumerateObject().Where(value => !declared.Contains(value.Name)))
                {
                    string known = declared.Count == 0 ? $"{condition.QualifiedId} declares no values." : $"Its values: {string.Join(", ", declared)}.";
                    Error(definition, "condition.value", $"{path}.values.{value.Name}", $"'{value.Name}' is not a value of {condition.QualifiedId}. {known}");
                }
            }

            if (element.TryGetProperty("op", out JsonElement op) && op.ValueKind == JsonValueKind.String && op.GetString() == "check"
                && element.TryGetProperty("outcomes", out JsonElement outcomes)
                && _rules.References.TryGetValue((definition, $"{path}.check"), out Definition? check))
            {
                CheckOutcomes(definition, check, outcomes, $"{path}.outcomes");
            }

            if (element.TryGetProperty("op", out op) && op.ValueKind == JsonValueKind.String && op.GetString() == "reduce_damage"
                && !element.TryGetProperty("amount", out _) && !element.TryGetProperty("fraction", out _))
            {
                Error(definition, "operation.reduce-damage", path, "reduce_damage needs an \"amount\" or \"fraction\" to change pending damage.");
            }

            if (element.TryGetProperty("op", out op) && op.ValueKind == JsonValueKind.String && op.GetString() == "grant_budget"
                && _rules.References.TryGetValue((definition, $"{path}.track"), out Definition? resource)
                && resource.Type != DefinitionTypes.Track)
            {
                Error(definition, "operation.grant-budget", $"{path}.track", $"'{resource.QualifiedId}' is not a resource track.");
            }

            if (element.TryGetProperty("op", out op) && op.ValueKind == JsonValueKind.String && op.GetString() == "reduce_damage"
                && _rules.References.TryGetValue((definition, $"{path}.shield_track"), out Definition? shield)
                && shield.Type != DefinitionTypes.Track)
            {
                Error(definition, "operation.reduce-damage", $"{path}.shield_track", $"'{shield.QualifiedId}' is not a resource track.");
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                WalkOperations(definition, property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                WalkOperations(definition, item, $"{path}[{index}]");
                index++;
            }
        }
    }

    private void CheckOutcomes(Definition owner, Definition check, JsonElement outcomes, string path)
    {
        List<string> tiers = ["success", "failure"];
        if (check.Json.TryGetProperty("tiers", out JsonElement declared))
        {
            tiers.AddRange(declared.EnumerateArray().Select(tier => tier.GetProperty("name").GetString()!));
        }

        foreach (JsonProperty outcome in outcomes.EnumerateObject())
        {
            if (!tiers.Contains(outcome.Name))
            {
                Error(owner, "action.outcomes", $"{path}.{outcome.Name}",
                    $"'{outcome.Name}' is not an outcome of check {check.QualifiedId}. Its outcomes: {string.Join(", ", tiers)}.");
            }
        }
    }

    private void CheckUse(Definition owner, JsonElement use, string path, Definition action)
    {
        List<string> parameters = UseParameters(action);
        bool fromItem = use.TryGetProperty("from_item", out _);
        foreach (JsonProperty argument in use.EnumerateObject())
        {
            if (argument.Name is "action" or "name" or "from_item")
            {
                continue;
            }

            if (!parameters.Contains(argument.Name))
            {
                string known = parameters.Count == 0 ? $"{action.QualifiedId} has no parameters." : $"Parameters of {action.QualifiedId}: {string.Join(", ", parameters)}.";
                Error(owner, "use.parameter", $"{path}.{argument.Name}", $"'{argument.Name}' is not a parameter of the action. {known}");
            }
        }

        foreach (string parameter in parameters.Where(parameter => !use.TryGetProperty(parameter, out _)))
        {
            if (!fromItem)
            {
                Error(owner, "use.parameter", path,
                    $"This use of {action.QualifiedId} doesn't give its parameter '{parameter}'. Add \"{parameter}\": an expression, or \"from_item\" to take it from an equipped item.");
            }
        }
    }

    /// <summary>The element at a path this builder recorded, such as <c>$.actions[0]</c>.</summary>
    private static JsonElement JsonAt(JsonElement root, string path)
    {
        JsonElement current = root;
        int i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                int end = i + 1;
                while (end < path.Length && path[end] != '.' && path[end] != '[')
                {
                    end++;
                }

                current = current.GetProperty(path[(i + 1)..end]);
                i = end;
            }
            else
            {
                int close = path.IndexOf(']', i);
                current = current[int.Parse(path[(i + 1)..close], System.Globalization.CultureInfo.InvariantCulture)];
                i = close + 1;
            }
        }

        return current;
    }

    /// <summary>Whether an expression reads self.<paramref name="stat"/> directly.</summary>
    private static bool Reads(Expr expr, string stat)
    {
        return expr switch
        {
            PathExpr path => path.Root == "self" && path.Name == stat,
            UnaryExpr unary => Reads(unary.Operand, stat),
            BinaryExpr binary => Reads(binary.Left, stat) || Reads(binary.Right, stat),
            ConditionalExpr conditional => Reads(conditional.Condition, stat) || Reads(conditional.Then, stat) || Reads(conditional.Else, stat),
            CallExpr call => call.Arguments.Any(argument => Reads(argument, stat)),
            _ => false,
        };
    }

    private string StatList(bool attributesOnly) => _rules.StatList(attributesOnly);

    private bool IsInferring(Definition derived) => _inferring.Contains(derived);

    /// <summary>The values a condition's own expressions may read as condition.&lt;name&gt;.</summary>
    private static List<string> ConditionValues(Definition definition)
    {
        return definition.Type == DefinitionTypes.Condition && definition.Json.TryGetProperty("values", out JsonElement values) && values.ValueKind == JsonValueKind.Object
            ? values.EnumerateObject().Select(value => value.Name).ToList()
            : [];
    }

    /// <summary>The parameters an action's expressions may read as use.&lt;name&gt;.</summary>
    private static List<string> UseParameters(Definition definition)
    {
        return definition.Json.TryGetProperty("parameters", out JsonElement parameters) && parameters.ValueKind == JsonValueKind.Array
            ? parameters.EnumerateArray().Select(parameter => parameter.GetString()!).ToList()
            : [];
    }

    private void ExpressionError(Definition definition, ExpressionSite site, string rule, ExpressionException exception)
    {
        Error(definition, rule, site.JsonPath, $"In '{site.Text}' at column {exception.Column}: {exception.Message}");
    }

    private void Error(Definition definition, string rule, string path, string message)
    {
        _diagnostics.Add(new ModuleDiagnostic(rule, message, definition.Module, definition.File, path));
    }
}
