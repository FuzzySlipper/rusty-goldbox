using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Campaigns;

/// <summary>Everything about a campaign in progress: the one mutable owner of play state.</summary>
public sealed class CampaignState
{
    public required Definition Campaign { get; init; }

    /// <summary>The seed every command's dice derive from.</summary>
    public required ulong Seed { get; init; }

    public required Definition Area { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public Facing Facing { get; set; }

    public List<Character> Party { get; } = [];

    /// <summary>NPCs currently outside the party; the same character moves between these two lists.</summary>
    public List<Character> AbsentNpcs { get; } = [];

    public List<Definition> Inventory { get; } = [];

    /// <summary>All items currently owned by the party, including equipped copies.</summary>
    public IEnumerable<Definition> CarriedItems => Inventory.Concat(Party.SelectMany(character => character.Equipment));

    public Dictionary<string, Value> Variables { get; } = [];

    /// <summary>Values of area-scoped variables, keyed by each area's qualified ID.</summary>
    public Dictionary<string, Dictionary<string, Value>> AreaVariables { get; } = [];

    /// <summary>Secret doors discovered by search, keyed by area qualified ID and canonical map edge.</summary>
    public HashSet<string> FoundSecrets { get; } = [];

    /// <summary>Values for an area, created by campaign start or save loading.</summary>
    public Dictionary<string, Value> ValuesFor(Definition area)
    {
        if (!AreaVariables.TryGetValue(area.QualifiedId, out Dictionary<string, Value>? values))
        {
            values = [];
            AreaVariables[area.QualifiedId] = values;
        }

        return values;
    }

    public string EdgeKey(Definition area, AreaEdge edge) => $"{area.QualifiedId}|{edge.Canonical.Key}";

    /// <summary>Once-only triggers that have run, as "module:area@x,y".</summary>
    public HashSet<string> Fired { get; } = [];

    /// <summary>A menu waiting for a choice.</summary>
    public Definition? PendingMenu { get; set; }

    /// <summary>A shop waiting for buy, sell or leave.</summary>
    public Definition? PendingShop { get; set; }

    public Definition? PendingTemple { get; set; }

    public Definition? PendingTraining { get; set; }

    /// <summary>Fictional campaign time in days, advanced by authored training and rest.</summary>
    public decimal ElapsedDays { get; set; }

    /// <summary>The picture the latest event showed, until the party moves or another replaces it.</summary>
    public Definition? Picture { get; set; }

    /// <summary>The music playing: the latest event's that named some.</summary>
    public Definition? Music { get; set; }

    public bool Ended { get; set; }

    /// <summary>Commands taken so far; command n rolls on random scope goldbox.play.n.</summary>
    public int Commands { get; set; }
}
