using System.Text.Json;
using RustyTales.Core.Definitions;
using RustyTales.Core.Expressions;

namespace RustyTales.Core.Rules;

public sealed partial class RuleSetBuilder
{
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
            : "experience";
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
            Error(creation, "creation.method", "$", $"Method {method} needs \"{field}\" (see `tales schema character-creation`).");
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
}
