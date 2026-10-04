using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Core.Campaigns;

public sealed partial class CampaignRunner
{
    private Definition? ChangeParty(Definition evt, List<PlayFact> facts)
    {
        Definition npc = _rules.Reference(evt, "$.npc");
        Character? present = _state.Party.FirstOrDefault(character => character.Npc == npc);
        bool joining = evt.Json.GetProperty("kind").GetString() == "join";
        var size = _state.Campaign.Json.GetProperty("party");
        string? refused = joining
            ? present is not null ? $"{present.Name} is already in the party."
                : _state.Party.Count >= size.GetProperty("max").GetInt32() ? "The party is at its campaign maximum; dismiss a member before recruiting." : null
            : present is null ? $"{_rules.Npcs[npc].Name} is not in the party."
                : _state.Party.Count <= size.GetProperty("min").GetInt32() ? "Dismissal would take the party below its campaign minimum." : null;
        if (refused is not null)
        {
            facts.Add(new RefusedFact(refused));
            return Next(evt, "$.on_refused");
        }

        Character character;
        if (joining)
        {
            Character? absent = _state.AbsentNpcs.FirstOrDefault(character => character.Npc == npc);
            character = absent ?? _rules.Npcs[npc].Copy();
            if (absent is null)
            {
                character.Perception = null;
            }

            _state.AbsentNpcs.Remove(character);
            _state.Party.Add(character);
        }
        else
        {
            character = present!;
            _state.Party.Remove(character);
            _state.AbsentNpcs.Add(character);
            if (ReferenceEquals(_state.ViewedCharacter, character))
            {
                _state.ViewedCharacter = null;
                _state.Picture = null;
            }
        }

        facts.Add(new PartyFact(joining, npc, character.Name, _state.Party.Count));
        return Next(evt, "$.next");
    }
}
