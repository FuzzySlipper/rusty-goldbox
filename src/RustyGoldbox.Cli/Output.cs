using System.Text.Encodings.Web;
using System.Text.Json;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Cli;

/// <summary>Prints command results as text or, with --json, as one JSON object.</summary>
internal sealed class Output(TextWriter writer, string workingDirectory, bool json)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public bool Json => json;

    public void Line(string text = "")
    {
        writer.WriteLine(text);
    }

    public int UsageError(string message)
    {
        if (json)
        {
            WriteJson(new { ok = false, diagnostics = new[] { new { rule = "usage", message } } });
        }
        else
        {
            writer.WriteLine($"error[usage] {message}");
        }

        return GoldboxCli.Usage;
    }

    public int Created(string? directory, string id, ModuleKind kind, List<ModuleDiagnostic> diagnostics)
    {
        bool ok = directory is not null;
        if (json)
        {
            WriteJson(new
            {
                ok,
                module = ok ? new { id, kind = ModuleKinds.Name(kind), path = Display(directory!) } : null,
                diagnostics = diagnostics.Select(ToJson),
            });
        }
        else if (ok)
        {
            writer.WriteLine($"Created {ModuleKinds.Name(kind)} module '{id}' at {Display(directory!)}.");
            writer.WriteLine($"Check it with: goldbox module validate {Display(directory!)}");
        }
        else
        {
            WriteDiagnostics(diagnostics);
        }

        return ok ? GoldboxCli.Ok : GoldboxCli.Invalid;
    }

    public int Validated(ModuleSet set)
    {
        if (json)
        {
            WriteJson(new
            {
                ok = set.IsValid,
                module = set.Root is null ? null : ModuleJson(set.Root),
                modules = set.LoadOrder.Select(loaded => $"{loaded.Manifest.Id}@{loaded.Manifest.Version}"),
                diagnostics = set.Diagnostics.Select(ToJson),
            });
        }
        else if (set.IsValid)
        {
            ModuleManifest root = set.Root!;
            int others = set.LoadOrder.Count - 1;
            string with = others == 0 ? "" : $" with {others} required module{(others == 1 ? "" : "s")}";
            writer.WriteLine($"ok: {root.Id} {root.Version} ({ModuleKinds.Name(root.Kind)}){with} is valid.");
        }
        else
        {
            WriteDiagnostics(set.Diagnostics);
        }

        return set.IsValid ? GoldboxCli.Ok : GoldboxCli.Invalid;
    }

    public int Dependencies(ModuleSet set)
    {
        if (json)
        {
            WriteJson(new
            {
                ok = set.IsValid,
                searchDirectories = set.SearchDirectories.Select(Display),
                loadOrder = set.LoadOrder.Select(loaded => new
                {
                    id = loaded.Manifest.Id,
                    version = loaded.Manifest.Version.ToString(),
                    kind = ModuleKinds.Name(loaded.Manifest.Kind),
                    path = Display(loaded.Manifest.Directory),
                    requires = loaded.Requires.Select(requirement => new
                    {
                        id = requirement.Id,
                        range = requirement.Range.Text,
                        version = requirement.Version.ToString(),
                    }),
                }),
                diagnostics = set.Diagnostics.Select(ToJson),
            });
            return set.IsValid ? GoldboxCli.Ok : GoldboxCli.Invalid;
        }

        if (set.LoadOrder.Count > 0)
        {
            writer.WriteLine("Load order (each module after what it requires):");
            int number = 1;
            foreach (LoadedModule loaded in set.LoadOrder)
            {
                ModuleManifest manifest = loaded.Manifest;
                writer.WriteLine($"  {number}. {manifest.Id} {manifest.Version} ({ModuleKinds.Name(manifest.Kind)})  {Display(manifest.Directory)}");
                foreach (ResolvedRequirement requirement in loaded.Requires)
                {
                    writer.WriteLine($"       requires {requirement.Id} {requirement.Range} -> {requirement.Version}");
                }

                number++;
            }

            string directories = set.SearchDirectories.Count == 0 ? "(none)" : string.Join(", ", set.SearchDirectories.Select(Display));
            writer.WriteLine($"Search directories: {directories}");
        }

        if (!set.IsValid)
        {
            WriteDiagnostics(set.Diagnostics);
        }

        return set.IsValid ? GoldboxCli.Ok : GoldboxCli.Invalid;
    }

    public void WriteDiagnostics(IReadOnlyList<ModuleDiagnostic> diagnostics)
    {
        foreach (ModuleDiagnostic diagnostic in diagnostics)
        {
            List<string> where = [];
            if (diagnostic.Module is not null)
            {
                where.Add(diagnostic.Module);
            }

            if (diagnostic.File is not null)
            {
                where.Add(Display(diagnostic.File));
            }

            if (diagnostic.JsonPath is not null)
            {
                where.Add(diagnostic.JsonPath);
            }

            writer.WriteLine($"error[{diagnostic.Rule}] {string.Join(" ", where)}");
            writer.WriteLine($"  {diagnostic.Message}");
        }

        int count = diagnostics.Count;
        writer.WriteLine($"{count} error{(count == 1 ? "" : "s")}.");
    }

    private object ModuleJson(ModuleManifest manifest)
    {
        return new
        {
            id = manifest.Id,
            version = manifest.Version.ToString(),
            kind = ModuleKinds.Name(manifest.Kind),
            title = manifest.Title,
            path = Display(manifest.Directory),
        };
    }

    private object ToJson(ModuleDiagnostic diagnostic)
    {
        return new
        {
            rule = diagnostic.Rule,
            module = diagnostic.Module,
            file = diagnostic.File is null ? null : Display(diagnostic.File),
            jsonPath = diagnostic.JsonPath,
            message = diagnostic.Message,
        };
    }

    private string Display(string path)
    {
        string relative = Path.GetRelativePath(workingDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative;
    }

    /// <summary>Reports a module set that failed to load.</summary>
    public int ModuleErrors(ModuleSet set)
    {
        if (json)
        {
            WriteJson(new { ok = false, diagnostics = set.Diagnostics.Select(ToJson) });
        }
        else
        {
            WriteDiagnostics(set.Diagnostics);
        }

        return GoldboxCli.Invalid;
    }

    /// <summary>An expression given on the command line that doesn't parse or type-check.</summary>
    public int ExpressionError(string text, ExpressionException exception)
    {
        if (json)
        {
            WriteJson(new { ok = false, diagnostics = new[] { new { rule = "expression", message = exception.Message, expression = text, column = exception.Column } } });
        }
        else
        {
            writer.WriteLine($"error[expression] column {exception.Column}");
            writer.WriteLine($"  {text}");
            writer.WriteLine($"  {new string(' ', Math.Max(0, exception.Column - 1))}^");
            writer.WriteLine($"  {exception.Message}");
        }

        return GoldboxCli.Usage;
    }

    public int EvaluationError(string what, ExpressionException exception, ulong seed, IReadOnlyList<DiceRoll> rolls)
    {
        if (json)
        {
            WriteJson(new { ok = false, seed, rolls = rolls.Select(RollJson), diagnostics = new[] { new { rule = "evaluate", message = exception.Message, expression = what } } });
        }
        else
        {
            writer.WriteLine($"error[evaluate] {what}");
            writer.WriteLine($"  {exception.Message}");
        }

        return GoldboxCli.Invalid;
    }

    public void Evaluated(string text, Value value, ulong seed, IReadOnlyList<DiceRoll> rolls)
    {
        if (json)
        {
            WriteJson(new { ok = true, expression = text, type = ExprTypes.Name(value.Type), value = ValueJson(value), seed, rolls = rolls.Select(RollJson) });
            return;
        }

        writer.WriteLine($"{text} = {value} ({ExprTypes.Name(value.Type)})");
        WriteRolls(seed, rolls);
    }

    public void Checked(Definition check, CheckResult result, ulong seed, IReadOnlyList<DiceRoll> rolls)
    {
        string comparison = check.Json.GetProperty("succeeds").GetString()!;
        if (json)
        {
            WriteJson(new
            {
                ok = true,
                check = check.QualifiedId,
                roll = result.Roll,
                bonus = result.Bonus,
                modifier = result.Modifier,
                total = result.Total,
                target = result.Target,
                margin = result.Margin,
                succeeds = comparison,
                tier = result.Tier,
                seed,
                rolls = rolls.Select(RollJson),
            });
            return;
        }

        string bonus = result.Bonus == 0 ? "" : $" {(result.Bonus > 0 ? "+" : "-")} {Math.Abs(result.Bonus)}";
        string modifier = result.Modifier == 0 ? "" : $" {(result.Modifier > 0 ? "+" : "-")} modifier {Math.Abs(result.Modifier)}";
        string total = bonus.Length + modifier.Length == 0 ? "" : $" = {result.Total}";
        string needs = comparison == "at-least" ? $"{result.Target} or more" : $"{result.Target} or less";
        writer.WriteLine($"{check.QualifiedId} ({check.Name}): roll {result.Roll}{bonus}{modifier}{total}, needs {needs}: {result.Tier} (margin {result.Margin})");
        WriteRolls(seed, rolls);
    }

    public void DefinitionList(RuleSet rules, IReadOnlyList<Definition> definitions, bool includeStats)
    {
        if (json)
        {
            WriteJson(new
            {
                ok = true,
                definitions = definitions.Select(definition => new { id = definition.QualifiedId, type = definition.Type.Name, name = definition.Name, file = Display(definition.File) }),
                stats = includeStats
                    ? rules.Stats.Values.OrderBy(stat => stat.Id, StringComparer.Ordinal).Select(stat => new { id = stat.Id, kind = stat.Definition.Type.Name, type = ExprTypes.Name(stat.Type) })
                    : null,
            });
            return;
        }

        foreach (IGrouping<string, Definition> group in definitions.GroupBy(definition => definition.Type.Name))
        {
            writer.WriteLine($"{group.Key} ({group.Count()}):");
            foreach (Definition definition in group)
            {
                writer.WriteLine($"  {definition.QualifiedId,-32} {Display(definition.File)}");
            }
        }

        if (includeStats && rules.Stats.Count > 0)
        {
            writer.WriteLine("stats (read as self.<id> or target.<id>; built in: level, class, race):");
            foreach (Stat stat in rules.Stats.Values.OrderBy(stat => stat.Id, StringComparer.Ordinal))
            {
                writer.WriteLine($"  {stat.Id,-16} {stat.Definition.Type.Name,-10} {ExprTypes.Name(stat.Type)}");
            }
        }
    }

    public void Definitions(RuleSet rules, IReadOnlyList<Definition> definitions)
    {
        if (json)
        {
            WriteJson(new
            {
                ok = true,
                definitions = definitions.Select(definition => new
                {
                    id = definition.QualifiedId,
                    type = definition.Type.Name,
                    file = Display(definition.File),
                    definition = definition.Json,
                    expressions = InspectCommand.Expressions(rules, definition).Select(entry => new { path = entry.Path, expression = entry.Text, type = entry.Type }),
                }),
            });
            return;
        }

        foreach (Definition definition in definitions)
        {
            writer.WriteLine($"{definition.QualifiedId} ({definition.Type.Name})  {Display(definition.File)}");
            writer.WriteLine(JsonSerializer.Serialize(definition.Json, JsonOptions));
            List<(string Path, string Text, string Type)> expressions = InspectCommand.Expressions(rules, definition).ToList();
            if (expressions.Count > 0)
            {
                writer.WriteLine("Expression types:");
                foreach ((string path, string text, string type) in expressions)
                {
                    writer.WriteLine($"  {path}: {text}  -> {type}");
                }
            }

            writer.WriteLine();
        }
    }

    /// <summary>Rule problems that stop a command, such as a class requirement the character misses.</summary>
    public int Problems(IReadOnlyList<ModuleDiagnostic> problems)
    {
        if (json)
        {
            WriteJson(new { ok = false, diagnostics = problems.Select(ToJson) });
        }
        else
        {
            WriteDiagnostics(problems);
        }

        return GoldboxCli.Invalid;
    }

    /// <param name="path">The file the character was just saved to, if any.</param>
    public void CharacterSheet(RuleSet rules, Character character, string? path, ulong? seed, IReadOnlyList<DiceRoll> rolls, IReadOnlyList<LevelGain> gains)
    {
        List<SheetStat> stats = Core.Characters.CharacterSheet.Stats(rules, character);
        List<SheetTrack> tracks = Core.Characters.CharacterSheet.Tracks(rules, character);
        string levelTrack = rules.LevelTrack?.Name.ToLowerInvariant() ?? "level track";
        if (json)
        {
            WriteJson(new
            {
                ok = true,
                saved_to = path is null ? null : Display(path),
                character = JsonDocument.Parse(CharacterFile.ToJson(character)).RootElement,
                next_level_experience = character.NextLevelExperience(),
                spell_slots = character.SpellSlots(),
                stats = stats.Select(stat => new { id = stat.Id, name = stat.Name, kind = stat.Kind, value = stat.Value is Value value ? ValueJson(value) : null, problem = stat.Problem }),
                tracks = tracks.Select(track => new { id = track.Track.Id, name = track.Track.Name, current = track.Current, max = track.Max, problem = track.Problem }),
                levels_gained = gains.Select(gain => new { level = gain.Level, gained = gain.Amount }),
                seed,
                rolls = rolls.Select(RollJson),
            });
            return;
        }

        foreach (LevelGain gain in gains)
        {
            writer.WriteLine($"Reached level {gain.Level}: +{gain.Amount} {levelTrack}.");
        }

        string next = character.NextLevelExperience() is decimal needed ? $"next level at {needed}" : "highest level";
        writer.WriteLine($"{character.Name}: {character.Race.Name} {character.Class.Name} {character.Level} ({character.Experience} xp, {next})");
        foreach (SheetTrack track in tracks)
        {
            string max = track.Max is decimal known ? N(known) : $"(can't compute: {track.Problem})";
            writer.WriteLine($"  {track.Track.Name.ToLowerInvariant()} {N(track.Current ?? 0)}/{max}");
        }

        writer.WriteLine($"  gold {character.Gold}");
        IReadOnlyList<int> slots = character.SpellSlots();
        if (slots.Count > 0)
        {
            writer.WriteLine($"  spells per day by spell level: {string.Join(" / ", slots)}");
        }

        foreach (SheetStat stat in stats)
        {
            string value = stat.Value is Value shown ? shown.ToString() : $"(can't compute: {stat.Problem})";
            writer.WriteLine($"  {stat.Id,-14} {value,-6} {stat.Name}");
        }

        if (path is not null)
        {
            writer.WriteLine($"Saved to {Display(path)}.");
        }

        if (seed is ulong used)
        {
            WriteRolls(used, rolls);
        }
    }

    public void CombatTranscript(CombatResult result, ulong seed)
    {
        if (json)
        {
            WriteJson(new
            {
                ok = true,
                seed,
                winner = result.Winner is int side ? result.Sides[side].Name : null,
                rounds = result.Rounds,
                facts = result.Facts.Select(fact => new { kind = fact.Kind, text = fact.Describe(), rolls = fact.Rolls.Select(RollJson) }),
                combatants = Combatants(result),
            });
            return;
        }

        writer.WriteLine($"seed {seed}");
        foreach (CombatFact fact in result.Facts)
        {
            string indent = fact is RoundFact or EndFact or SurprisedFact ? "" : "  ";
            string rolls = fact.Rolls.Count == 0 ? "" : $"  [{string.Join("; ", fact.Rolls)}]";
            writer.WriteLine($"{indent}{fact.Describe()}{rolls}");
        }

        foreach (CombatSide side in result.Sides)
        {
            string members = string.Join(", ", side.Members.Select(member =>
                $"{member.Name} {TrackText(member, result.Track)}{(member.Defeated ? " (out)" : "")}"));
            writer.WriteLine($"{side.Name}: {members}");
        }
    }

    public void CombatSummary(IReadOnlyList<CombatResult> results, ulong seed)
    {
        List<string> sideNames = results[0].Sides.Select(side => side.Name).ToList();
        Dictionary<string, int> wins = sideNames.Select((name, index) => (name, index)).ToDictionary(entry => entry.name, entry => results.Count(result => result.Winner == entry.index));
        int undecided = results.Count(result => result.Winner is null);
        List<int> rounds = results.Select(result => result.Rounds).ToList();
        List<string> partyNames = results[0].Sides[0].Members.Select(member => member.Name).ToList();
        Dictionary<string, int> survived = partyNames.Select((name, index) => (name, index)).ToDictionary(entry => entry.name, entry => results.Count(result => !result.Sides[0].Members[entry.index].Defeated));
        if (json)
        {
            WriteJson(new
            {
                ok = true,
                seed,
                runs = results.Count,
                wins,
                undecided,
                rounds = new { min = rounds.Min(), mean = rounds.Average(), max = rounds.Max() },
                party_survival = survived,
            });
            return;
        }

        int count = results.Count;
        writer.WriteLine($"{count} runs from seed {seed}");
        foreach ((string name, int won) in wins)
        {
            writer.WriteLine($"  {name} wins {won} ({Percent(won, count)})");
        }

        if (undecided > 0)
        {
            writer.WriteLine($"  undecided {undecided} ({Percent(undecided, count)})");
        }

        writer.WriteLine($"  rounds: min {rounds.Min()}, mean {rounds.Average().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}, max {rounds.Max()}");
        foreach ((string name, int alive) in survived)
        {
            writer.WriteLine($"  {name} still fighting at the end: {alive} ({Percent(alive, count)})");
        }
    }

    private static string Percent(int part, int whole) => (100.0 * part / whole).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static string N(decimal value) => value.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture);

    private static string TrackText(Combatant member, Definition track)
    {
        TrackValue value = member.Creature.Track(track.Id);
        string max = value.Max is decimal known ? $"/{N(known)}" : "";
        return $"{N(value.Current ?? 0)}{max}";
    }

    private static IEnumerable<object> Combatants(CombatResult result)
    {
        return result.Sides.SelectMany(side => side.Members.Select(member => (object)new
        {
            name = member.Name,
            side = side.Name,
            tracks = member.Creature.Tracks.ToDictionary(entry => entry.Key, entry => new { current = entry.Value.Current, max = entry.Value.Max }),
            defeated = member.Defeated,
        }));
    }

    private void WriteRolls(ulong seed, IReadOnlyList<DiceRoll> rolls)
    {
        string shown = rolls.Count == 0 ? "no dice rolled" : string.Join("; ", rolls);
        writer.WriteLine($"  seed {seed}: {shown}");
    }

    private static object RollJson(DiceRoll roll) => new { dice = $"{roll.Count}d{roll.Sides}", faces = roll.Faces, kept = roll.Kept, total = roll.Total };

    private static object ValueJson(Value value)
    {
        return value.Type switch
        {
            ExprType.Number => value.Number,
            ExprType.Boolean => value.Boolean,
            _ => value.Text,
        };
    }

    public void WriteJson(object value)
    {
        writer.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }
}
