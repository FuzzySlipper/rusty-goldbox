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

    public List<Definition> Inventory { get; } = [];

    public Dictionary<string, Value> Variables { get; } = [];

    /// <summary>Once-only triggers that have run, as "module:area@x,y".</summary>
    public HashSet<string> Fired { get; } = [];

    /// <summary>A menu waiting for a choice.</summary>
    public Definition? PendingMenu { get; set; }

    /// <summary>The picture the latest event showed, until the party moves or another replaces it.</summary>
    public Definition? Picture { get; set; }

    /// <summary>The music playing: the latest event's that named some.</summary>
    public Definition? Music { get; set; }

    public bool Ended { get; set; }

    /// <summary>Commands taken so far; command n rolls on random scope goldbox.play.n.</summary>
    public int Commands { get; set; }
}
