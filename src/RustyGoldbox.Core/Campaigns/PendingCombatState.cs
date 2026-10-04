using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Core.Campaigns;

/// <summary>
/// The event suspended while a live combat owns campaign commands. Definitions
/// are resolved references in memory; SaveFile writes their qualified IDs and
/// the primitive continuation separately.
/// </summary>
public sealed class PendingCombatState
{
    public required Definition Event { get; init; }

    public required Definition Encounter { get; init; }

    public required Definition Combat { get; init; }

    public required CombatContinuationState Continuation { get; set; }

    /// <summary>How each continuation ID is reconstructed without encounter rerolls.</summary>
    public List<PendingCombatantSource> Participants { get; } = [];

    /// <summary>The presentation metadata captured before the first combat turn.</summary>
    public List<PendingFightMember> Members { get; } = [];

    /// <summary>Whether the terminal result has already been applied to campaign state.</summary>
    public bool Finalized { get; set; }
}

/// <summary>The source identity for one saved combatant.</summary>
public sealed record PendingCombatantSource(
    string Id,
    int Side,
    string Name,
    string? MonsterId,
    int? PartyIndex);

/// <summary>Primitive form of a FightMember retained across a suspended event.</summary>
public sealed record PendingFightMember(
    string Name,
    int Side,
    string? MonsterId,
    string? ClassId,
    decimal Start,
    decimal? Max,
    Cell? Position);

/// <summary>The typed result returned by a campaign-owned combat command.</summary>
public sealed record CampaignCombatCommandResult(
    bool Accepted,
    string? Reason,
    CombatObservation Observation,
    IReadOnlyList<PlayFact> Facts);
