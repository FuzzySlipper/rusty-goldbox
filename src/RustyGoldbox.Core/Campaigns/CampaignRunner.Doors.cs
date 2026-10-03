using System.Text.Json;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

public sealed partial class CampaignRunner
{
    private sealed record DoorInfo(Facing Direction, int Index, AreaEdge Edge, JsonElement Descriptor)
    {
        public bool HasDescriptor => Descriptor.ValueKind != JsonValueKind.Undefined;
    }

    private void Open(string method, string? direction, DiceRoller dice, List<PlayFact> facts)
    {
        Facing facing;
        if (direction is null)
        {
            facing = _state.Facing;
        }
        else if (!Facings.TryParse(direction, out facing))
        {
            facts.Add(new RefusedFact($"'{direction}' is not a direction: {string.Join(", ", Facings.Names)}."));
            return;
        }

        Open(facing, method, dice, facts);
    }

    private bool Open(Facing direction, string method, DiceRoller dice, List<PlayFact> facts)
    {
        AreaMap map = Map(_state.Area);
        Edge raw = map.EdgeOf(_state.X, _state.Y, direction);
        if (raw != Edge.Door)
        {
            if (raw == Edge.Secret && _state.FoundSecrets.Contains(_state.EdgeKey(_state.Area, new AreaEdge(_state.X, _state.Y, direction))))
            {
                return true;
            }

            facts.Add(new RefusedFact(raw == Edge.Secret
                ? $"a secret door is hidden {Facings.Name(direction)}; search there first."
                : $"there is no door {Facings.Name(direction)}."));
            return false;
        }

        DoorInfo door = DoorAt(direction)!;
        string key = _state.EdgeKey(_state.Area, door.Edge);
        if (_state.OpenedDoors.Contains(key))
        {
            facts.Add(new TextFact($"The door {Facings.Name(direction)} is already open."));
            return true;
        }

        if (!door.HasDescriptor)
        {
            return true;
        }

        if (!Locked(door))
        {
            MarkOpen(door, "unlocked", facts);
            return true;
        }

        if (method is "pick" or "force")
        {
            return CheckDoor(door, method, dice, facts);
        }

        if (HasKey(door))
        {
            MarkOpen(door, "key", facts);
            return true;
        }

        if (door.Descriptor.TryGetProperty("event", out _))
        {
            RunChain(_rules.Reference(_state.Area, $"$.doors[{door.Index}].event"), dice, facts);
            if (_state.OpenedDoors.Contains(key))
            {
                return true;
            }

            facts.Add(new RefusedFact($"the door {Facings.Name(direction)} is still locked after its event."));
            return false;
        }

        List<string> alternatives = [];
        if (door.Descriptor.TryGetProperty("pick", out _))
        {
            alternatives.Add($"pick {Facings.Name(direction)}");
        }

        if (door.Descriptor.TryGetProperty("force", out _))
        {
            alternatives.Add($"force {Facings.Name(direction)}");
        }

        string ways = alternatives.Count == 0 ? "no key, check or event opens it" : $"try {string.Join(" or ", alternatives)}";
        facts.Add(new RefusedFact($"the door {Facings.Name(direction)} is locked; {ways}."));
        return false;
    }

    private bool CheckDoor(DoorInfo door, string method, DiceRoller dice, List<PlayFact> facts)
    {
        if (!door.Descriptor.TryGetProperty(method, out _))
        {
            facts.Add(new RefusedFact($"the door has no {method} check."));
            return false;
        }

        Definition check = _rules.Reference(_state.Area, $"$.doors[{door.Index}].{method}");
        Evaluator evaluator = new(_rules, dice);
        int before = dice.Rolls.Count;
        bool succeeded = false;
        foreach (Character character in _state.Party)
        {
            if (Located(check, "$", () => evaluator.Check(check, character.ToCreature(), null)).Success)
            {
                succeeded = true;
                break;
            }
        }

        DoorFact fact = new(door.Direction, method, succeeded) { Rolls = dice.Rolls.Skip(before).ToList() };
        if (succeeded)
        {
            _state.OpenedDoors.Add(_state.EdgeKey(_state.Area, door.Edge));
        }

        facts.Add(fact);
        return succeeded;
    }

    private bool HasKey(DoorInfo door)
    {
        if (!door.Descriptor.TryGetProperty("key", out _))
        {
            return false;
        }

        Definition key = _rules.Reference(_state.Area, $"$.doors[{door.Index}].key");
        return _state.Inventory.Any(item => item == key)
            || _state.Party.Any(character => character.Equipment.Any(item => item == key));
    }

    private bool Locked(DoorInfo door)
    {
        return !door.Descriptor.TryGetProperty("locked", out JsonElement locked) || locked.GetBoolean();
    }

    private void MarkOpen(DoorInfo door, string method, List<PlayFact> facts)
    {
        _state.OpenedDoors.Add(_state.EdgeKey(_state.Area, door.Edge));
        facts.Add(new DoorFact(door.Direction, method, true));
    }

    private DoorInfo? DoorAt(Facing direction)
    {
        AreaMap map = Map(_state.Area);
        if (map.EdgeOf(_state.X, _state.Y, direction) != Edge.Door)
        {
            return null;
        }

        AreaEdge edge = new AreaEdge(_state.X, _state.Y, direction).Canonical;
        if (_state.Area.Json.TryGetProperty("doors", out JsonElement doors))
        {
            int index = 0;
            foreach (JsonElement descriptor in doors.EnumerateArray())
            {
                JsonElement at = descriptor.GetProperty("at");
                Facings.TryParse(descriptor.GetProperty("facing").GetString()!, out Facing facing);
                if (new AreaEdge(at[0].GetInt32(), at[1].GetInt32(), facing).Canonical == edge)
                {
                    return new DoorInfo(direction, index, edge, descriptor);
                }

                index++;
            }
        }

        return new DoorInfo(direction, -1, edge, default);
    }

    private DoorInfo? DoorById(string id)
    {
        if (!_state.Area.Json.TryGetProperty("doors", out JsonElement doors))
        {
            return null;
        }

        int index = 0;
        foreach (JsonElement descriptor in doors.EnumerateArray())
        {
            if (descriptor.GetProperty("id").GetString() == id)
            {
                JsonElement at = descriptor.GetProperty("at");
                Facings.TryParse(descriptor.GetProperty("facing").GetString()!, out Facing facing);
                AreaEdge edge = new AreaEdge(at[0].GetInt32(), at[1].GetInt32(), facing).Canonical;
                return new DoorInfo(facing, index, edge, descriptor);
            }

            index++;
        }

        return null;
    }

    private void OpenByEvent(Definition owner, string id, List<PlayFact> facts)
    {
        DoorInfo? door = DoorById(id);
        if (door is null)
        {
            throw new RuleFailure(new ModuleDiagnostic("event.open", $"Area {_state.Area.QualifiedId} has no door '{id}'.", owner.Module, owner.File, "$.door"));
        }

        AreaMap map = Map(_state.Area);
        if (!map.ContainsEdge(door.Edge.X, door.Edge.Y, door.Edge.Facing) || map.EdgeOf(door.Edge.X, door.Edge.Y, door.Edge.Facing) != Edge.Door)
        {
            throw new RuleFailure(new ModuleDiagnostic("event.open", $"Door '{id}' in {_state.Area.QualifiedId} is not a DD map edge.", owner.Module, owner.File, "$.door"));
        }

        string key = _state.EdgeKey(_state.Area, door.Edge);
        if (_state.OpenedDoors.Add(key))
        {
            facts.Add(new DoorFact(door.Direction, "event", true));
        }
    }
}
