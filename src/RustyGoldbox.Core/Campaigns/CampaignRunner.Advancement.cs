using System.Text.Json;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

public sealed partial class CampaignRunner
{
    private void MilestoneCommand(int member, string[] words, DiceRoller dice, List<PlayFact> facts)
    {
        if (member < 1 || member > _state.Party.Count)
        {
            facts.Add(new RefusedFact($"{member} is not a party member; members are 1 to {_state.Party.Count}."));
            return;
        }

        decimal amount = _rules.Advancement is Definition advancement
            && advancement.Json.TryGetProperty("milestones", out JsonElement milestones)
            && milestones.TryGetProperty("skill_raise", out JsonElement skillRaise)
            ? skillRaise.GetProperty("amount").GetDecimal()
            : 0;
        Dictionary<string, decimal> raises = [];
        List<SkillSwap> swaps = [];
        List<string> features = [];
        for (int index = 0; index < words.Length; index++)
        {
            string option = words[index];
            if (option is "--raise" or "--swap" or "--feature")
            {
                if (++index >= words.Length)
                {
                    facts.Add(new RefusedFact($"{option} needs a comma-separated value."));
                    return;
                }

                foreach (string value in words[index].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (option == "--raise")
                    {
                        raises[value] = amount;
                    }
                    else if (option == "--feature")
                    {
                        features.Add(value);
                    }
                    else
                    {
                        string[] pair = value.Split('=', 2, StringSplitOptions.TrimEntries);
                        if (pair.Length != 2 || pair.Any(string.IsNullOrWhiteSpace))
                        {
                            facts.Add(new RefusedFact($"--swap values must be from=to; got '{value}'."));
                            return;
                        }

                        swaps.Add(new SkillSwap(pair[0], pair[1]));
                    }
                }
            }
            else
            {
                facts.Add(new RefusedFact("milestone takes --raise <id>, --swap <from=to> and --feature <id> options."));
                return;
            }
        }

        List<ModuleDiagnostic> problems = [];
        if (CharacterRules.ApplyMilestone(_rules, _state.Party[member - 1], new MilestoneChoices(raises, swaps, features), problems))
        {
            facts.Add(new TextFact($"Milestone choices applied to {_state.Party[member - 1].Name}."));
        }
        else
        {
            facts.Add(new RefusedFact(string.Join(" ", problems.Select(problem => problem.Message))));
        }
    }

    private void ImproveCommand(int member, DiceRoller dice, List<PlayFact> facts)
    {
        if (member < 1 || member > _state.Party.Count)
        {
            facts.Add(new RefusedFact($"{member} is not a party member; members are 1 to {_state.Party.Count}."));
            return;
        }

        List<ModuleDiagnostic> problems = [];
        List<SkillImprovement> results = CharacterRules.ImproveMarkedSkills(_rules, _state.Party[member - 1], dice, problems);
        if (problems.Count > 0)
        {
            facts.Add(new RefusedFact(string.Join(" ", problems.Select(problem => problem.Message))));
        }
        else
        {
            string changes = string.Join(", ", results.Where(result => result.Improved).Select(result => $"{result.Skill} +{result.Amount}"));
            facts.Add(new TextFact(changes.Length == 0 ? "No marked skill improved." : $"Improved: {changes}."));
        }
    }

    private Definition? Milestone(Definition evt, DiceRoller dice, List<PlayFact> facts)
    {
        if (evt.Json.TryGetProperty("text", out JsonElement text))
        {
            facts.Add(new TextFact(text.GetString()!));
        }

        if (!Members(evt, out List<int> members, facts))
        {
            return null;
        }

        Dictionary<string, decimal> raises = [];
        if (evt.Json.TryGetProperty("raises", out JsonElement givenRaises))
        {
            foreach (JsonProperty entry in givenRaises.EnumerateObject())
            {
                raises[entry.Name] = entry.Value.GetDecimal();
            }
        }

        List<SkillSwap> swaps = [];
        if (evt.Json.TryGetProperty("swaps", out JsonElement givenSwaps))
        {
            for (int index = 0; index < givenSwaps.GetArrayLength(); index++)
            {
                swaps.Add(new SkillSwap(
                    givenSwaps[index].GetProperty("from").GetString()!,
                    givenSwaps[index].GetProperty("to").GetString()!));
            }
        }

        List<string> features = evt.Json.TryGetProperty("features", out JsonElement givenFeatures)
            ? Enumerable.Range(0, givenFeatures.GetArrayLength()).Select(index => _rules.Reference(evt, $"$.features[{index}]").QualifiedId).ToList()
            : [];
        List<string> refused = [];
        foreach (int member in members)
        {
            List<ModuleDiagnostic> problems = [];
            if (!CharacterRules.ApplyMilestone(_rules, _state.Party[member], new MilestoneChoices(raises, swaps, features), problems))
            {
                refused.Add($"{_state.Party[member].Name}: {string.Join(" ", problems.Select(problem => problem.Message))}");
            }
        }

        if (refused.Count > 0)
        {
            facts.Add(new RefusedFact(string.Join(" ", refused)));
        }
        else
        {
            facts.Add(new TextFact($"Milestone choices applied to {string.Join(", ", members.Select(member => _state.Party[member].Name))}."));
        }

        return Next(evt, "$.next");
    }

    private Definition? Improve(Definition evt, DiceRoller dice, List<PlayFact> facts)
    {
        if (evt.Json.TryGetProperty("text", out JsonElement text))
        {
            facts.Add(new TextFact(text.GetString()!));
        }

        if (!Members(evt, out List<int> members, facts))
        {
            return null;
        }

        List<string> improved = [];
        List<string> refused = [];
        foreach (int member in members)
        {
            List<ModuleDiagnostic> problems = [];
            List<SkillImprovement> results = CharacterRules.ImproveMarkedSkills(_rules, _state.Party[member], dice, problems);
            improved.AddRange(results.Where(result => result.Improved).Select(result => $"{_state.Party[member].Name}: {result.Skill} +{result.Amount}"));
            refused.AddRange(problems.Select(problem => $"{_state.Party[member].Name}: {problem.Message}"));
        }

        if (refused.Count > 0)
        {
            facts.Add(new RefusedFact(string.Join(" ", refused)));
        }
        else
        {
            facts.Add(new TextFact(improved.Count == 0 ? "No marked skill improved." : $"Improved: {string.Join(", ", improved)}."));
        }

        return Next(evt, "$.next");
    }

    private bool Members(Definition evt, out List<int> members, List<PlayFact> facts)
    {
        members = [];
        if (evt.Json.TryGetProperty("member", out JsonElement member))
        {
            int index = member.GetInt32() - 1;
            if (index < 0 || index >= _state.Party.Count)
            {
                facts.Add(new RefusedFact($"{member.GetInt32()} is not a party member; members are 1 to {_state.Party.Count}."));
                return false;
            }

            members.Add(index);
            return true;
        }

        members.AddRange(Enumerable.Range(0, _state.Party.Count));
        return true;
    }
}
