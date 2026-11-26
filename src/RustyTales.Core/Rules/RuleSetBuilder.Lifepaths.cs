using System.Text.Json;
using RustyTales.Core.Definitions;

namespace RustyTales.Core.Rules;

public sealed partial class RuleSetBuilder
{
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
                            Error(lifepath, "lifepath.benefit-roll", $"{path}.roll", "A material benefit table must have one distinct entry for each configured benefit-die total (and its configured modifier).");
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
}
