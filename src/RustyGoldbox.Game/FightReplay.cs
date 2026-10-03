using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Combat;

namespace RustyGoldbox.Game;

/// <summary>
/// Plays a fight Core has already resolved back to the player, one fact at a
/// time: each fact shows after a short beat, and what it does to the fight's
/// track, defeats and the acting combatant is applied as it shows. Nothing is
/// decided here; the facts are the fight.
/// </summary>
internal sealed class FightReplay
{
    private readonly Dictionary<string, decimal> _values = [];
    private readonly HashSet<string> _defeated = [];
    private readonly Dictionary<string, Cell> _positions = [];
    private readonly List<string> _lines = [];
    private double _waited;

    public FightReplay(FightFact fight)
    {
        Fight = fight;
        foreach (FightMember member in fight.Members)
        {
            _values[member.Name] = member.Start;
            if (member.Position is Cell cell)
            {
                _positions[member.Name] = cell;
            }
        }
    }

    public FightFact Fight { get; }

    /// <summary>How many of the fight's facts have shown.</summary>
    public int Shown { get; private set; }

    public bool Done => Shown >= Fight.Facts.Count;

    /// <summary>Each member's value on the fight's track, as of the facts shown.</summary>
    public IReadOnlyDictionary<string, decimal> Values => _values;

    public IReadOnlySet<string> Defeated => _defeated;

    /// <summary>Where each member stands on the fight's field, as of the facts shown; empty for a fight without one.</summary>
    public IReadOnlyDictionary<string, Cell> Positions => _positions;

    /// <summary>The combatant of the latest action shown, and how many actions have shown (so a repeat acts again).</summary>
    public (string? Who, int Count) Acting { get; private set; }

    /// <summary>The shown facts as text, oldest first.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Lets <paramref name="seconds"/> pass; returns whether a fact showed.</summary>
    public bool Advance(double seconds)
    {
        bool showed = false;
        _waited += seconds;
        while (!Done && _waited >= Beat(Fight.Facts[Shown]))
        {
            _waited -= Beat(Fight.Facts[Shown]);
            Show(Fight.Facts[Shown]);
            showed = true;
        }

        return showed;
    }

    /// <summary>Shows every remaining fact at once.</summary>
    public void Finish()
    {
        while (!Done)
        {
            Show(Fight.Facts[Shown]);
        }
    }

    private void Show(CombatFact fact)
    {
        Shown++;
        _lines.Add(fact.Describe());
        switch (fact)
        {
            case ActionFact action:
                Acting = (action.Who, Acting.Count + 1);
                break;
            case DamageFact damage when damage.Track == Fight.Track:
                _values[damage.Who] = damage.Left;
                break;
            case HealFact heal when heal.Track == Fight.Track:
                _values[heal.Who] = heal.Now;
                break;
            case MoveFact move:
                _positions[move.Who] = move.To;
                break;
            case DefeatedFact defeated:
                _defeated.Add(defeated.Who);
                break;
            case EscapedFact escaped:
                _defeated.Add(escaped.Who);
                break;
            case ReturnedFact returned:
                _defeated.Remove(returned.Who);
                break;
        }
    }

    /// <summary>How long a fact waits before it shows: long enough to follow, short enough not to drag.</summary>
    private static double Beat(CombatFact fact) => fact switch
    {
        RoundFact => 0.5,
        ActionFact => 0.6,
        DamageFact or HealFact => 0.5,
        MoveFact => 0.4,
        DefeatedFact or EscapedFact or EndFact => 0.7,
        _ => 0.25,
    };
}
