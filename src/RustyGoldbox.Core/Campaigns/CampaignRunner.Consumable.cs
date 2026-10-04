using System.Text.Json;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

public sealed partial class CampaignRunner
{
    private void Use(int member, string itemId, DiceRoller dice, List<PlayFact> facts)
    {
        if (!TryPartyMember(member, "use", out Character character, facts))
        {
            return;
        }

        if (_rules.Find(DefinitionTypes.Item, itemId, out string? problem) is not Definition item)
        {
            facts.Add(new RefusedFact($"{problem ?? $"There is no item '{itemId}'."} Use use <member> <item-id> with a usable carried item. Carried: {ItemChoices(_state.Inventory)}"));
            return;
        }

        int inventoryIndex = _state.Inventory.IndexOf(item);
        if (inventoryIndex < 0)
        {
            facts.Add(new RefusedFact($"{item.Name} is not in party inventory. Use use <member> <item-id> with a usable carried item. Carried: {ItemChoices(_state.Inventory)}"));
            return;
        }

        if (!item.Json.TryGetProperty("use", out JsonElement use))
        {
            facts.Add(new RefusedFact($"{item.Name} has no use. Use equip <member> <item-id> for carried gear, or choose a usable item. Carried: {ItemChoices(_state.Inventory)}"));
            return;
        }

        JsonElement operations = use.GetProperty("operations");
        decimal? durationDays = null;
        if (use.TryGetProperty("duration_days", out _))
        {
            Creature creature = character.ToCreature();
            Evaluator evaluator = new(_rules, dice);
            durationDays = Located(item, "$.use.duration_days", () =>
            {
                decimal days = evaluator.Evaluate(
                    _rules.Expression(item, "$.use.duration_days"),
                    SceneScope(creature)).Number;
                if (days < 0)
                {
                    throw new ExpressionException("A consumable condition duration must be nonnegative fictional campaign days.", 0);
                }

                return days;
            });
        }

        _state.Inventory.RemoveAt(inventoryIndex);
        facts.Add(new ItemUseFact(member, character.Name, item));
        ApplySceneOperations(item, "$.use.operations", operations, character, member, dice, facts, durationDays);
    }

    private void ExpireConditions(List<PlayFact> facts)
    {
        for (int index = 0; index < _state.Party.Count; index++)
        {
            ExpireCharacterConditions(_state.Party[index], index + 1, facts);
        }

        foreach (Character character in _state.AbsentNpcs)
        {
            ExpireCharacterConditions(character, null, facts);
        }
    }

    private void ExpireCharacterConditions(Character character, int? member, List<PlayFact> facts)
    {
        foreach ((Definition condition, decimal expiry) in character.ConditionExpiryDays
            .Where(entry => entry.Value <= _state.ElapsedDays)
            .ToList())
        {
            character.ConditionExpiryDays.Remove(condition);
            if (character.Conditions.Remove(condition) && member is int activeMember)
            {
                facts.Add(new SceneConditionFact(activeMember, character.Name, condition.Name, false));
            }
        }
    }
}

