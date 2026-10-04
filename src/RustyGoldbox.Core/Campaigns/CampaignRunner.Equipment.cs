using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Core.Campaigns;

public sealed partial class CampaignRunner
{
    /// <summary>Moves one existing carried copy to a party member's equipment.</summary>
    private void Equip(int member, string itemId, List<PlayFact> facts)
    {
        if (!TryPartyMember(member, "equip", out Character character, facts))
        {
            return;
        }

        if (_rules.Find(DefinitionTypes.Item, itemId, out string? problem) is not Definition item)
        {
            facts.Add(new RefusedFact($"{problem ?? $"There is no item '{itemId}'."} {CarriedGuidance()}"));
            return;
        }

        int inventoryIndex = _state.Inventory.IndexOf(item);
        if (inventoryIndex < 0)
        {
            facts.Add(new RefusedFact($"{item.Name} is not in party inventory. {CarriedGuidance()}"));
            return;
        }

        if (CharacterRules.EquipmentProblem(_rules, character, item) is string restriction)
        {
            facts.Add(new RefusedFact($"{restriction} {CarriedGuidance()}"));
            return;
        }

        _state.Inventory.RemoveAt(inventoryIndex);
        character.Equipment.Add(item);
        facts.Add(new EquipmentFact(true, member, character.Name, item));
    }

    /// <summary>Moves one existing equipped copy back to party inventory.</summary>
    private void Unequip(int member, string itemId, List<PlayFact> facts)
    {
        if (!TryPartyMember(member, "unequip", out Character character, facts))
        {
            return;
        }

        if (_rules.Find(DefinitionTypes.Item, itemId, out string? problem) is not Definition item)
        {
            facts.Add(new RefusedFact($"{problem ?? $"There is no item '{itemId}'."} {EquippedGuidance(character)}"));
            return;
        }

        int equipmentIndex = character.Equipment.IndexOf(item);
        if (equipmentIndex < 0)
        {
            facts.Add(new RefusedFact($"{character.Name} does not have {item.Name} equipped. {EquippedGuidance(character)}"));
            return;
        }

        character.Equipment.RemoveAt(equipmentIndex);
        _state.Inventory.Add(item);
        facts.Add(new EquipmentFact(false, member, character.Name, item));
    }

    private bool TryPartyMember(int member, string command, out Character character, List<PlayFact> facts)
    {
        if (member < 1 || member > _state.Party.Count)
        {
            character = null!;
            facts.Add(new RefusedFact($"{member} is not a party member; use {command} <member> <item-id>. Members: {MemberChoices()}."));
            return false;
        }

        character = _state.Party[member - 1];
        return true;
    }

    private string CarriedGuidance() => $"Use equip <member> <item-id> with a carried item. Carried: {ItemChoices(_state.Inventory)}";

    private string EquippedGuidance(Character character) => $"Use unequip <member> <item-id> with one of {character.Name}'s equipped items: {ItemChoices(character.Equipment)}";

    private string MemberChoices()
    {
        return _state.Party.Count == 0
            ? "none"
            : string.Join(", ", _state.Party.Select((character, index) => $"{index + 1}={character.Name}"));
    }

    private static string ItemChoices(IEnumerable<Definition> items)
    {
        List<string> choices = items
            .GroupBy(item => item.QualifiedId, StringComparer.Ordinal)
            .Select(group => $"{group.Key}{(group.Count() == 1 ? "" : $" ×{group.Count()}")}")
            .ToList();
        return choices.Count == 0 ? "none" : string.Join(", ", choices);
    }
}

/// <summary>A single existing item copy moved between party inventory and a member.</summary>
public sealed record EquipmentFact(bool Equipped, int Member, string Who, Definition Item) : PlayFact
{
    public override string Kind => Equipped ? "equipped" : "unequipped";

    public override string Describe() => $"{Who} {(Equipped ? "equips" : "unequips")} {Item.Name}.";
}
