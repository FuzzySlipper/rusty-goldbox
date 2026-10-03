using System.Text.Json;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

public sealed partial class CampaignRunner
{
    public TempleFact? Temple()
    {
        if (_state.PendingTemple is not Definition temple)
        {
            return null;
        }

        List<TempleOffer> services = [];
        int index = 0;
        foreach (JsonElement service in temple.Json.GetProperty("services").EnumerateArray())
        {
            string path = $"$.services[{index}].cost";
            services.Add(new TempleOffer(index + 1, service.GetProperty("label").GetString()!, _state.Party
                .Select(character => ServicePrice(temple, path, character)).ToList()));
            index++;
        }

        return new TempleFact(temple.Json.GetProperty("text").GetString()!, services);
    }

    private decimal ServicePrice(Definition owner, string path, Character character)
    {
        return Located(owner, path, () =>
        {
            decimal price = EvaluateFor(owner, path, character, null).Number;
            if (price < 0)
            {
                throw new Expressions.ExpressionException("A service cost cannot be negative; change its cost expression.", 0);
            }

            return price;
        });
    }

    private Expressions.Value EvaluateFor(Definition owner, string path, Character character, DiceRoller? dice)
    {
        Evaluator evaluator = new(_rules, dice);
        return evaluator.Evaluate(_rules.Expression(owner, path), new Scope(character.ToCreature(), null, Variables: _state.Variables));
    }

    private bool CanPay(Definition owner, string label, decimal price, List<PlayFact> facts)
    {
        decimal gold = Located(owner, "$", () => _state.Party.Sum(character => Math.Max(0, character.Gold)));
        if (_state.Party.Count == 0 || gold < price)
        {
            facts.Add(new RefusedFact($"{label} costs {Fact(price)} gold; the party has {Fact(gold)}."));
            return false;
        }

        return true;
    }

    private void Pay(decimal price)
    {
        foreach (Character character in _state.Party)
        {
            decimal paid = Math.Min(Math.Max(0, character.Gold), price);
            character.Gold -= paid;
            price -= paid;
        }
    }

    private void Serve(int number, int member, DiceRoller dice, List<PlayFact> facts)
    {
        if (_state.PendingTemple is not Definition temple)
        {
            facts.Add(new RefusedFact("there is no temple open."));
            return;
        }

        JsonElement services = temple.Json.GetProperty("services");
        if (number < 1 || number > services.GetArrayLength() || member < 1 || member > _state.Party.Count)
        {
            facts.Add(new RefusedFact("choose a listed service and party member: serve <service> <member>."));
            return;
        }

        Character character = _state.Party[member - 1];
        string path = $"$.services[{number - 1}]";
        string label = services[number - 1].GetProperty("label").GetString()!;
        decimal price = ServicePrice(temple, $"{path}.cost", character);
        if (!CanPay(temple, label, price, facts))
        {
            return;
        }

        // Evaluate on the existing creature view before applying a paid service.
        Creature creature = character.ToCreature();
        Evaluator evaluator = new(_rules, dice);
        Scope scope = new(creature, null, Variables: _state.Variables);
        int before = dice.Rolls.Count;
        JsonElement operations = services[number - 1].GetProperty("operations");
        for (int index = 0; index < operations.GetArrayLength(); index++)
        {
            string at = $"{path}.operations[{index}]";
            Located(temple, at, () =>
            {
                if (operations[index].GetProperty("op").GetString() == "heal")
                {
                    Definition track = _rules.Reference(temple, $"{at}.track");
                    decimal amount = evaluator.Evaluate(_rules.Expression(temple, $"{at}.amount"), scope).Number;
                    TrackOperations.Heal(evaluator, creature, track, amount);
                }
                else
                {
                    creature.Conditions.Remove(_rules.Reference(temple, $"{at}.condition"));
                }

                return true;
            });
        }

        Pay(price);
        foreach ((string id, TrackValue value) in creature.Tracks)
        {
            character.Tracks[id] = value;
        }

        character.Conditions.Clear();
        character.Conditions.AddRange(creature.Conditions);
        facts.Add(new TextFact($"{character.Name} receives {label} for {Fact(price)} gold.") { Rolls = dice.Rolls.Skip(before).ToList() });
        facts.Add(Temple()!);
    }

    private void LeaveTemple(DiceRoller dice, List<PlayFact> facts)
    {
        Definition temple = _state.PendingTemple!;
        _state.PendingTemple = null;
        facts.Add(new TextFact("The party leaves the temple."));
        if (Next(temple, "$.next") is Definition next)
        {
            RunChain(next, dice, facts);
        }
    }
}
