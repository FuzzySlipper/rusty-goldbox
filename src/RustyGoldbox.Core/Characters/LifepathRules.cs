using System.Text.Json;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Characters;

/// <summary>Choices and execution for data-driven term-by-term character careers.</summary>
public static partial class CharacterRules
{
    /// <summary>One career offered by a lifepath, for CLI and Game projections.</summary>
    public sealed record LifepathCareerChoice(string Id, string Name, IReadOnlyList<string> SkillTables);

    /// <summary>Finds a lifepath and reports an actionable reference diagnostic.</summary>
    public static Definition? FindLifepath(RuleSet rules, string reference, List<ModuleDiagnostic> problems)
    {
        return Find(rules, DefinitionTypes.Lifepath, reference, "lifepath", problems);
    }

    /// <summary>Lists the careers and their table choices from a lifepath definition.</summary>
    public static IReadOnlyList<LifepathCareerChoice> LifepathCareers(Definition lifepath)
    {
        return lifepath.Json.GetProperty("careers").EnumerateArray().Select(career =>
            new LifepathCareerChoice(
                career.GetProperty("id").GetString()!,
                career.GetProperty("name").GetString()!,
                career.GetProperty("skills").EnumerateArray().Select(table => table.GetProperty("id").GetString()!).ToList())).ToList();
    }

    private static bool RunLifepath(
        RuleSet rules,
        Character character,
        string lifepathReference,
        IReadOnlyList<string>? requestedCareers,
        IReadOnlyList<string>? requestedTables,
        IReadOnlyList<string>? requestedBenefits,
        int requestedTerms,
        DiceRoller dice,
        List<ModuleDiagnostic> problems)
    {
        Definition? lifepath = FindLifepath(rules, lifepathReference, problems);
        if (lifepath is null)
        {
            return false;
        }

        List<string> careers = requestedCareers?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList() ?? [];
        if (careers.Count == 0)
        {
            problems.Add(new ModuleDiagnostic(
                "character.lifepath-career",
                $"{lifepath.QualifiedId} needs at least one career choice. Choose one of {string.Join(", ", LifepathCareers(lifepath).Select(choice => choice.Id))} with --career.",
                lifepath.Module,
                lifepath.File,
                "$.careers"));
            return false;
        }

        int terms = requestedTerms;
        if (terms < 0)
        {
            problems.Add(new ModuleDiagnostic("character.lifepath-terms", "--terms must be zero or a positive whole number.", lifepath.Module, lifepath.File, "$.max_terms"));
            return false;
        }

        if (careers.Count == 1 && terms > 1)
        {
            careers = Enumerable.Repeat(careers[0], terms).ToList();
        }
        else if (terms > 0 && terms != careers.Count)
        {
            problems.Add(new ModuleDiagnostic("character.lifepath-terms", $"--terms is {terms}, but --career gives {careers.Count} choices. Give one career to repeat it, or one career per term.", lifepath.Module, lifepath.File, "$.careers"));
            return false;
        }

        int maximum = lifepath.Json.TryGetProperty("max_terms", out JsonElement max) ? max.GetInt32() : 7;
        if (careers.Count > maximum)
        {
            problems.Add(new ModuleDiagnostic("character.lifepath-terms", $"{lifepath.QualifiedId} allows at most {maximum} terms, but {careers.Count} were requested.", lifepath.Module, lifepath.File, "$.max_terms"));
            return false;
        }

        character.Lifepath = lifepath;
        character.Age = lifepath.Json.TryGetProperty("start_age", out JsonElement startAge) ? startAge.GetInt32() : 18;
        character.CareerTerms.Clear();
        character.LifepathEnded = false;

        Evaluator evaluator = new(rules, dice);
        int tableIndex = 0;
        int previousCareerCount = 0;
        string? currentCareer = null;
        foreach (string careerId in careers)
        {
            JsonElement? career = Career(lifepath, careerId);
            if (career is null)
            {
                problems.Add(new ModuleDiagnostic("character.lifepath-career", $"There is no career '{careerId}' in {lifepath.QualifiedId}. Careers: {string.Join(", ", LifepathCareers(lifepath).Select(choice => choice.Id))}.", lifepath.Module, lifepath.File, "$.careers"));
                return false;
            }

            JsonElement careerData = career.Value;
            int number = character.CareerTerms.Count + 1;
            int ageBefore = character.Age;
            int rankBefore = character.CareerTerms.LastOrDefault(term => term.Career == careerId)?.RankAfter ?? 0;
            List<string> choices = [];
            List<string> results = [];
            LifepathRoll? qualification = null;
            LifepathRoll? survival = null;
            LifepathRoll? commission = null;
            LifepathRoll? advancement = null;
            LifepathRoll? aging = null;
            bool benefitsLost = false;
            bool ended = false;

            bool enteringCareer = currentCareer != careerId;
            if (enteringCareer)
            {
                previousCareerCount = character.CareerTerms.Select(term => term.Career).Distinct(StringComparer.Ordinal).Count();
                currentCareer = careerId;
                rankBefore = 0;
            }

            if (enteringCareer)
            {
                qualification = ResolveThrow(rules, lifepath, careerData.GetProperty("qualification"), $"$.careers[{CareerIndex(lifepath, careerId)}].qualification", evaluator, character, dice, "qualification", -2 * previousCareerCount);
            }

            if (qualification is LifepathRoll enlistment && !enlistment.Success)
            {
                results.Add("qualification failed; career ended");
                ended = true;
                benefitsLost = true;
                character.LifepathEnded = true;
            }
            else
            {
                survival = ResolveThrow(rules, lifepath, careerData.GetProperty("survival"), $"$.careers[{CareerIndex(lifepath, careerId)}].survival", evaluator, character, dice, "survival");
                if (IsNaturalTwo(dice))
                {
                    survival = survival with { Success = false };
                    results.Add("survival failed; career ended");
                    ended = true;
                    benefitsLost = true;
                    character.LifepathEnded = true;
                }
                else if (!survival.Success)
                {
                    results.Add("survival failed; career ended");
                    ended = true;
                    benefitsLost = true;
                    character.LifepathEnded = true;
                }
                else
                {
                    int rank = rankBefore;
                    if (careerData.TryGetProperty("commission", out JsonElement commissionRule) && rank == 0)
                    {
                        commission = ResolveThrow(rules, lifepath, commissionRule, $"$.careers[{CareerIndex(lifepath, careerId)}].commission", evaluator, character, dice, "commission");
                        if (commission.Success)
                        {
                            rank = 1;
                            ApplyRank(rules, lifepath, careerData, rank, character, results);
                        }
                    }

                    if (careerData.TryGetProperty("advancement", out JsonElement advancementRule) && rank >= 1)
                    {
                        advancement = ResolveThrow(rules, lifepath, advancementRule, $"$.careers[{CareerIndex(lifepath, careerId)}].advancement", evaluator, character, dice, "advancement");
                        if (advancement.Success)
                        {
                            rank++;
                            ApplyRank(rules, lifepath, careerData, rank, character, results);
                        }
                    }

                    int skillRolls = 1;
                    if (!careerData.TryGetProperty("commission", out _) && !careerData.TryGetProperty("advancement", out _))
                    {
                        skillRolls++;
                    }

                    if (commission?.Success == true)
                    {
                        skillRolls++;
                    }

                    if (advancement?.Success == true)
                    {
                        skillRolls++;
                    }

                    for (int skillRoll = 0; skillRoll < skillRolls; skillRoll++)
                    {
                        JsonElement table = SelectSkillTable(lifepath, careerData, requestedTables, ref tableIndex, careerId, problems, choices);
                        if (table.ValueKind == JsonValueKind.Undefined)
                        {
                            return false;
                        }

                        int roll = checked((int)dice.Roll(1, 6));
                        JsonElement entry = SelectEntry(table.GetProperty("entries"), roll);
                        string stat = entry.GetProperty("stat").GetString()!;
                        int amount = entry.TryGetProperty("amount", out JsonElement amountElement) ? amountElement.GetInt32() : 1;
                        AddStatBonus(rules, character, stat, amount);
                        string result = $"{entry.GetProperty("kind").GetString()} {stat} +{amount}";
                        results.Add(result);
                    }

                    int termYears = lifepath.Json.TryGetProperty("term_years", out JsonElement years) ? years.GetInt32() : 4;
                    character.Age += termYears;
                    aging = ApplyAging(rules, lifepath, character, dice, number, results);
                    if (aging is not null && character.Attributes.Any(attribute => attribute.Value <= 0))
                    {
                        results.Add("ageing crisis; career ended");
                        ended = true;
                        character.LifepathEnded = true;
                    }

                    if (!ended && number < careers.Count)
                    {
                        LifepathRoll reenlistment = ResolveThrow(rules, lifepath, careerData.GetProperty("reenlistment"), $"$.careers[{CareerIndex(lifepath, careerId)}].reenlistment", evaluator, character, dice, "reenlistment");
                        bool naturalTwelve = IsNaturalTwelve(dice);
                        if (!reenlistment.Success && naturalTwelve)
                        {
                            reenlistment = reenlistment with { Success = true };
                            results.Add("natural 12 requires another term");
                        }
                        else if (!reenlistment.Success)
                        {
                            ended = true;
                            character.LifepathEnded = true;
                            results.Add("reenlistment failed; career ended");
                        }
                        else
                        {
                            results.Add("reenlisted");
                        }
                    }

                    character.CareerTerms.Add(new LifepathTerm(careerId, number, ageBefore, character.Age, rankBefore, rank, qualification, survival, commission, advancement, aging, ended, benefitsLost, choices, results));
                    if (ended)
                    {
                        break;
                    }

                    continue;
                }
            }

            character.CareerTerms.Add(new LifepathTerm(careerId, number, ageBefore, character.Age, rankBefore, rankBefore, qualification, survival, commission, advancement, aging, ended, benefitsLost, choices, results));
            if (ended)
            {
                break;
            }
        }

        if (!ApplyBenefits(rules, lifepath, character, requestedBenefits, dice, problems))
        {
            return false;
        }

        character.LifepathEnded = true;
        return true;
    }

    private static LifepathRoll ResolveThrow(
        RuleSet rules,
        Definition lifepath,
        JsonElement rule,
        string path,
        Evaluator evaluator,
        Character character,
        DiceRoller dice,
        string kind,
        int extraModifier = 0)
    {
        int fixedModifier = rule.TryGetProperty("modifier", out JsonElement fixedModifierElement)
            ? fixedModifierElement.GetInt32()
            : 0;
        if (rule.TryGetProperty("check", out _))
        {
            Definition check = rules.Reference(lifepath, $"{path}.check");
            CheckResult result = evaluator.Check(check, character.ToCreature(), null, extraModifier + fixedModifier);
            return new LifepathRoll(kind, result.Roll, result.Bonus + result.Modifier, result.Total, result.Target, result.Success);
        }

        decimal roll = dice.Roll(2, 6);
        decimal modifier = extraModifier + fixedModifier;
        if (rule.TryGetProperty("stat", out JsonElement stat))
        {
            modifier += evaluator.Stat(character.ToCreature(), stat.GetString()!).Number;
        }

        decimal total = roll + modifier;
        decimal target = rule.TryGetProperty("target", out JsonElement required) ? required.GetInt32() : 0;
        return new LifepathRoll(kind, roll, modifier, total, target, total >= target);
    }

    private static LifepathRoll? ApplyAging(RuleSet rules, Definition lifepath, Character character, DiceRoller dice, int term, List<string> results)
    {
        if (!lifepath.Json.TryGetProperty("aging", out JsonElement aging)
            || character.Age < aging.GetProperty("start_age").GetInt32()
            || term < aging.GetProperty("start_term").GetInt32())
        {
            return null;
        }

        int modifier = -term;
        decimal raw = dice.Roll(2, 6);
        int total = checked((int)raw + modifier);
        JsonElement selected = aging.GetProperty("effects").EnumerateArray()
            .FirstOrDefault(effect => total >= effect.GetProperty("min").GetInt32() && total <= effect.GetProperty("max").GetInt32());
        if (selected.ValueKind == JsonValueKind.Undefined)
        {
            return new LifepathRoll("aging", raw, modifier, total, 0, true);
        }

        foreach (JsonElement change in selected.GetProperty("changes").EnumerateArray())
        {
            string stat = change.GetProperty("stat").GetString()!;
            int amount = change.GetProperty("amount").GetInt32();
            AddStatBonus(rules, character, stat, -amount);
            results.Add($"ageing {stat} -{amount}");
        }

        return new LifepathRoll("aging", raw, modifier, total, 0, true);
    }

    private static bool ApplyBenefits(RuleSet rules, Definition lifepath, Character character, IReadOnlyList<string>? requested, DiceRoller dice, List<ModuleDiagnostic> problems)
    {
        if (character.CareerTerms.Count == 0)
        {
            return true;
        }

        List<string> choices = requested?.Where(choice => !string.IsNullOrWhiteSpace(choice)).ToList() ?? [];
        int choiceIndex = 0;
        foreach (IGrouping<string, LifepathTerm> group in character.CareerTerms.Where(term => !term.BenefitsLost).GroupBy(term => term.Career))
        {
            JsonElement? career = Career(lifepath, group.Key);
            if (career is null)
            {
                continue;
            }

            JsonElement benefits = career.Value.GetProperty("benefits");
            int rank = group.Last().RankAfter;
            int count = group.Count() + (rank >= 6 ? 3 : rank >= 5 ? 2 : rank >= 4 ? 1 : 0);
            for (int benefitIndex = 0; benefitIndex < count; benefitIndex++)
            {
                string? selectedKind = choiceIndex < choices.Count ? choices[choiceIndex++] : null;
                if (selectedKind is null)
                {
                    bool hasCash = benefits.TryGetProperty("cash", out _);
                    bool hasMaterial = benefits.TryGetProperty("material", out _);
                    selectedKind = hasCash == hasMaterial ? null : hasCash ? "cash" : "material";
                }

                if (selectedKind is not ("cash" or "material") || !benefits.TryGetProperty(selectedKind, out JsonElement table))
                {
                    problems.Add(new ModuleDiagnostic("character.lifepath-benefit", $"Career '{group.Key}' has benefit {benefitIndex + 1}, so choose cash or material with --benefit. Available: {BenefitKinds(benefits)}.", lifepath.Module, lifepath.File, "$.careers"));
                    return false;
                }

                int roll = checked((int)dice.Roll(1, 6)) + (selectedKind == "material" && rank >= 5 ? 1 : 0);
                JsonElement entry = SelectEntry(table, roll);
                string result;
                if (selectedKind == "cash")
                {
                    Definition currency = rules.Reference(lifepath, $"$.careers[{CareerIndex(lifepath, group.Key)}].benefits.cash[{IndexOf(table, entry)}].currency");
                    int amount = entry.GetProperty("amount").GetInt32();
                    character.Balances[currency.Id] = character.Balances.GetValueOrDefault(currency.Id) + amount;
                    result = $"benefit cash {currency.Id} +{amount}";
                }
                else
                {
                    string kind = entry.GetProperty("kind").GetString()!;
                    int amount = entry.TryGetProperty("amount", out JsonElement amountElement) ? amountElement.GetInt32() : 1;
                    if (kind is "skill" or "attribute")
                    {
                        string stat = entry.GetProperty("stat").GetString()!;
                        AddStatBonus(rules, character, stat, amount);
                        result = $"benefit {kind} {stat} +{amount}";
                    }
                    else if (kind == "item")
                    {
                        Definition item = rules.Reference(lifepath, $"$.careers[{CareerIndex(lifepath, group.Key)}].benefits.material[{IndexOf(table, entry)}].item");
                        character.Equipment.Add(item);
                        result = $"benefit item {item.Id}";
                    }
                    else if (kind == "currency")
                    {
                        Definition currency = rules.Reference(lifepath, $"$.careers[{CareerIndex(lifepath, group.Key)}].benefits.material[{IndexOf(table, entry)}].currency");
                        character.Balances[currency.Id] = character.Balances.GetValueOrDefault(currency.Id) + amount;
                        result = $"benefit currency {currency.Id} +{amount}";
                    }
                    else
                    {
                        result = $"benefit {kind}";
                    }
                }

                int last = character.CareerTerms.FindLastIndex(term => term.Career == group.Key);
                if (last >= 0)
                {
                    LifepathTerm term = character.CareerTerms[last];
                    character.CareerTerms[last] = term with
                    {
                        Choices = [.. term.Choices, $"benefit:{selectedKind}"],
                        Results = [.. term.Results, result],
                    };
                }
            }
        }

        if (choiceIndex < choices.Count)
        {
            problems.Add(new ModuleDiagnostic("character.lifepath-benefit", $"Received {choices.Count} benefit choices, but only {choiceIndex} benefit rolls were needed. Remove the extras from --benefit.", lifepath.Module, lifepath.File, "$.careers"));
            return false;
        }

        return true;
    }

    private static void ApplyRank(RuleSet rules, Definition lifepath, JsonElement career, int rank, Character character, List<string> results)
    {
        if (!career.TryGetProperty("ranks", out JsonElement ranks))
        {
            return;
        }

        JsonElement entry = ranks.EnumerateArray().FirstOrDefault(candidate => candidate.GetProperty("rank").GetInt32() == rank);
        if (entry.ValueKind == JsonValueKind.Undefined || !entry.TryGetProperty("skill", out JsonElement skill))
        {
            return;
        }

        int amount = entry.TryGetProperty("amount", out JsonElement given) ? given.GetInt32() : 1;
        AddStatBonus(rules, character, skill.GetString()!, amount);
        results.Add($"rank {rank}: {skill.GetString()} +{amount}");
    }

    private static JsonElement SelectSkillTable(Definition lifepath, JsonElement career, IReadOnlyList<string>? requested, ref int index, string careerId, List<ModuleDiagnostic> problems, List<string> choices)
    {
        JsonElement tables = career.GetProperty("skills");
        string? selected = index < (requested?.Count ?? 0) ? requested![index++] : null;
        if (selected is null && tables.GetArrayLength() == 1)
        {
            selected = tables[0].GetProperty("id").GetString();
        }

        JsonElement table = tables.EnumerateArray().FirstOrDefault(candidate => candidate.GetProperty("id").GetString() == selected);
        if (selected is null || table.ValueKind == JsonValueKind.Undefined)
        {
            int careerIndex = CareerIndex(lifepath, careerId);
            problems.Add(new ModuleDiagnostic(
                "character.lifepath-skill-table",
                $"Career '{careerId}' needs a skill-table choice for this roll. Choose one of {string.Join(", ", tables.EnumerateArray().Select(entry => entry.GetProperty("id").GetString()))} with --skill-table.",
                lifepath.Module,
                lifepath.File,
                $"$.careers[{careerIndex}].skills"));
            return default;
        }

        choices.Add($"skill:{selected}");
        return table;
    }

    private static JsonElement SelectEntry(JsonElement entries, int roll)
    {
        JsonElement exact = entries.EnumerateArray().FirstOrDefault(entry => entry.GetProperty("roll").GetInt32() == roll);
        if (exact.ValueKind != JsonValueKind.Undefined)
        {
            return exact;
        }

        return entries.EnumerateArray().OrderByDescending(entry => entry.GetProperty("roll").GetInt32()).First(entry => entry.GetProperty("roll").GetInt32() <= roll);
    }

    private static JsonElement? Career(Definition lifepath, string id)
    {
        foreach (JsonElement career in lifepath.Json.GetProperty("careers").EnumerateArray())
        {
            if (career.GetProperty("id").GetString() == id)
            {
                return career;
            }
        }

        return null;
    }

    private static int CareerIndex(Definition lifepath, string id)
    {
        int index = 0;
        foreach (JsonElement career in lifepath.Json.GetProperty("careers").EnumerateArray())
        {
            if (career.GetProperty("id").GetString() == id)
            {
                return index;
            }

            index++;
        }

        return 0;
    }

    private static int IndexOf(JsonElement list, JsonElement selected)
    {
        int index = 0;
        foreach (JsonElement entry in list.EnumerateArray())
        {
            if (entry.GetProperty("roll").GetInt32() == selected.GetProperty("roll").GetInt32())
            {
                return index;
            }

            index++;
        }

        return 0;
    }

    private static bool IsNaturalTwo(DiceRoller dice)
    {
        return dice.Rolls.LastOrDefault() is DiceRoll roll && roll.Faces.Count == 2 && roll.Faces.Sum() == 2;
    }

    private static bool IsNaturalTwelve(DiceRoller dice)
    {
        return dice.Rolls.LastOrDefault() is DiceRoll roll && roll.Faces.Count == 2 && roll.Faces.Sum() == 12;
    }

    private static string BenefitKinds(JsonElement benefits)
    {
        return string.Join(", ", new[] { "cash", "material" }.Where(kind => benefits.TryGetProperty(kind, out _)));
    }
}
