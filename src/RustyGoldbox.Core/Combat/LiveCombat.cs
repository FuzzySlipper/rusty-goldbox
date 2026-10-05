using System.Text.Json;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

/// <summary>The point at which a live combat is waiting or has stopped.</summary>
/// <remarks>Saved continuations store these numbers.</remarks>
public enum CombatPhase
{
    NotStarted = 0,
    Advancing = 1,
    AwaitingAction = 2,
    AwaitingInterrupt = 4,
    Ended = 5,
}

/// <summary>Who supplies a combatant's decisions at its turns.</summary>
public enum CombatControlMode
{
    Automatic,
    Manual,
}

/// <summary>The kind of choice a live combat can expose.</summary>
/// <remarks>Saved continuations store these numbers.</remarks>
public enum CombatDecisionKind
{
    Action = 0,
    Targets = 1,
    Interrupt = 4,
    PostRoll = 5,
    Initiative = 6,
}

/// <summary>A target the current actor may inspect or select.</summary>
public sealed record CombatTargetChoice(
    string Id,
    string Name,
    int Side,
    bool Defeated,
    bool Escaped,
    Cell? Position,
    decimal? Track,
    decimal? MaximumTrack);

/// <summary>
/// A destination and path that can be reached by an explicit move choice.
/// Target IDs bind the path to the target-relative legality that produced it;
/// generated live choices always carry the target that was used to calculate
/// the path.
/// </summary>
public sealed record CombatMoveChoice(
    Cell Destination,
    IReadOnlyList<Cell> Path,
    int Cost,
    IReadOnlyList<string>? TargetIds = null);

/// <summary>
/// One legal use of an action. IDs are stable for the life of a combat and
/// include the actor's stable ID, so duplicate display names remain distinct.
/// </summary>
public sealed record CombatActionChoice(
    string Id,
    string ActionId,
    string Name,
    string? SpellId,
    IReadOnlyDictionary<string, int> Cost,
    IReadOnlyList<CombatTargetChoice> Targets,
    IReadOnlyList<CombatMoveChoice> Moves,
    string? TargetKind = null,
    string TargetMode = "one",
    int? PortionCount = null,
    IReadOnlyDictionary<string, decimal>? SpellCosts = null);

/// <summary>The decision currently required from a controller.</summary>
public sealed record CombatDecision(
    string Id,
    CombatDecisionKind Kind,
    string ActorId,
    int Round,
    IReadOnlyList<CombatActionChoice> Actions,
    IReadOnlyList<CombatMoveChoice> Moves,
    bool CanEndTurn,
    string? OperationOwner = null,
    string? OperationPath = null,
    IReadOnlyList<CombatDecisionOption>? Options = null,
    CombatInterruptState? Interrupt = null,
    CombatCheckState? Check = null,
    string? ActionId = null,
    int? MaximumTargets = null);

/// <summary>
/// One legal choice for an interrupt or other non-action decision. The
/// primitive fields keep the live snapshot and continuation JSON-friendly;
/// Core resolves the referenced definition and never treats this as a rule.
/// </summary>
public sealed record CombatDecisionOption(
    string Id,
    string Name,
    string Kind,
    string? QualifiedId = null,
    string? TargetId = null,
    string? TrackId = null,
    decimal Cost = 0,
    decimal? Bonus = null,
    bool Reroll = false,
    decimal? Score = null,
    int? Index = null);

/// <summary>
/// The committed parts of a check that is waiting for a post-roll choice.
/// Keeping this in the pending decision prevents observation or save/load from
/// making the original roll again.
/// </summary>
public sealed record CombatCheckState(
    string CheckId,
    decimal Roll,
    decimal Bonus,
    decimal Modifier,
    decimal Total,
    decimal Target,
    decimal Margin,
    bool Success,
    string Tier,
    IReadOnlyList<DiceRoll>? Rolls = null,
    int? FactIndex = null,
    string? ById = null,
    string? AgainstId = null);

/// <summary>Committed check values carried by an operation scope across a save.</summary>
public sealed record CombatScopeCheckState(
    decimal Roll,
    decimal Bonus,
    decimal Modifier,
    decimal Total,
    decimal Target,
    decimal Margin,
    bool Success,
    string Tier);

/// <summary>
/// The operation boundary at which a reaction is waiting. Pending damage is
/// carried as data so accepting or declining the choice resumes the original
/// operation rather than replaying it.
/// </summary>
public sealed record CombatInterruptState(
    string Trigger,
    string ReactorId,
    string SourceId,
    string? ReactionId = null,
    string? ActionId = null,
    string? TargetId = null,
    string? TrackId = null,
    decimal? PendingDamage = null,
    string? OperationOwner = null,
    string? OperationPath = null,
    string? UseId = null,
    IReadOnlyList<DiceRoll>? PendingRolls = null);

/// <summary>
/// Primitive continuation data for an operation that is suspended while an
/// optional decision is answered. The live resolver remains the owner of the
/// operation stack; this record identifies the frame to resume and carries
/// only values that were already committed before the interruption.
/// </summary>
public sealed record CombatOperationState(
    string OwnerId,
    string Path,
    string ActorId,
    string? TargetId = null,
    string? SourceId = null,
    string? ActionId = null,
    int Index = 0,
    decimal? PendingDamage = null,
    string? UseId = null,
    IReadOnlyList<string>? TargetIds = null,
    int TargetIndex = 0,
    bool AlreadyPaid = false,
    string? ListPath = null,
    CombatScopeCheckState? Check = null,
    CombatScopeCheckState? Outer = null,
    IReadOnlyDictionary<string, decimal>? ConditionValues = null,
    bool IsCursor = false);

/// <summary>
/// A suspended interrupt frame beneath the currently offered decision. The
/// frame is deliberately primitive so nested reactions can survive a JSON
/// save without retaining a delegate, expression closure or replay command.
/// </summary>
public sealed class CombatInterruptFrameState
{
    public CombatInterruptState Interrupt { get; set; } = new("", "", "");

    public List<CombatDecisionOption> Options { get; set; } = [];

    public CombatOperationState Operation { get; set; } = new("", "", "");

    public decimal PendingDamage { get; set; }

    public int RollsBefore { get; set; }

    public int FactsBefore { get; set; }

    public bool Physical { get; set; }

    public bool Attack { get; set; }

    public CombatOperationState? Action { get; set; }

    public CombatMovementState? Movement { get; set; }

    public List<DiceRoll> PendingRolls { get; set; } = [];
}

/// <summary>Primitive movement values captured while a leaves-reach reaction is answered.</summary>
public sealed record CombatMovementState(
    string OwnerId,
    string Path,
    string ActorId,
    string TargetId,
    Cell Start,
    Cell Here,
    Cell? PendingStep,
    decimal Allowed,
    decimal Within,
    decimal? Beyond,
    bool Away,
    bool Provokes,
    bool Escape,
    decimal Spent,
    int Steps,
    int EnemyIndex,
    IReadOnlyList<Cell>? Selected = null,
    int SelectedIndex = 0);

/// <summary>A read-only live combat view. Constructed without advancing or rolling.</summary>
public sealed record CombatObservation(
    CombatPhase Phase,
    int Round,
    string? ActiveActorId,
    CombatDecision? PendingDecision,
    int? Winner,
    int? FledSide,
    IReadOnlyList<CombatFact> Facts,
    IReadOnlyList<CombatantObservation> Combatants,
    CombatBehaviorTrace? BehaviorTrace = null)
{
    /// <summary>Actual authored proposals collected when trace capture is enabled.</summary>
    public IReadOnlyList<CombatBehaviorTrace> BehaviorTraces { get; init; } = [];
}

/// <summary>A primitive transcript entry retained across a combat continuation.</summary>
public sealed record CombatFactState(
    string Kind,
    string Description,
    IReadOnlyList<DiceRoll> Rolls,
    IReadOnlyList<string> SubjectIds,
    IReadOnlyList<string> TargetIds);

/// <summary>Opaque fact restored from a continuation when its concrete definition is not needed.</summary>
internal sealed record RestoredCombatFact(string FactKind, string Text) : CombatFact
{
    public override string Kind => FactKind;

    public override string Describe() => Text;
}

/// <summary>Mutable combat state projected for observation and save coordination.</summary>
public sealed record CombatantObservation(
    string Id,
    string Name,
    int Side,
    CombatControlMode Controller,
    bool Defeated,
    bool Escaped,
    decimal SurprisedRounds,
    Cell? Position,
    IReadOnlyDictionary<string, int> Budget,
    IReadOnlyDictionary<string, decimal?> Tracks);

/// <summary>A command submitted to the one live combat owner.</summary>
public abstract record CombatCommand
{
    /// <summary>Take one action with explicit targets and, when applicable, a path.</summary>
    public sealed record UseAction(
        string ActorId,
        string ActionId,
        IReadOnlyList<string> TargetIds,
        IReadOnlyList<Cell>? Path = null) : CombatCommand;

    /// <summary>Finish the active actor's turn, even when some budget remains.</summary>
    public sealed record EndTurn(string ActorId) : CombatCommand;

    /// <summary>
    /// Accept one currently offered option, or decline when
    /// <paramref name="OptionId"/> is null. The decision ID makes a stale
    /// response a refusal without changing combat or consuming random draws.
    /// </summary>
    public sealed record Decide(string DecisionId, string? OptionId = null) : CombatCommand;
}

/// <summary>The result of accepting or refusing a command.</summary>
public sealed record CombatCommandResult(
    bool Accepted,
    string? Reason,
    CombatObservation Observation)
{
    public static CombatCommandResult Refused(string reason, CombatObservation observation) => new(false, reason, observation);

    public static CombatCommandResult AcceptedResult(CombatObservation observation) => new(true, null, observation);
}

/// <summary>A condition and its explicit continuation values.</summary>
public sealed record CombatConditionState(
    string ConditionId,
    decimal? Rounds,
    IReadOnlyDictionary<string, decimal> Values);

/// <summary>
/// Serializable state for resuming a combat at a decision or an operation
/// boundary. It contains IDs and primitive values only; no delegates, closures,
/// evaluator objects or replay instructions are persisted.
/// </summary>
public sealed class CombatContinuationState
{
    public int Format { get; set; } = 1;

    public string CombatId { get; set; } = "";

    public int MaxRounds { get; set; }

    public CombatPhase Phase { get; set; }

    public int Round { get; set; }

    /// <summary>Whether the one-time combat setup (tracks, surprise and budgets) has run.</summary>
    public bool CombatInitialized { get; set; }

    /// <summary>Whether a round has begun and still has turns to run.</summary>
    public bool RoundOpen { get; set; }

    /// <summary>Whether the active actor has completed start-of-turn preparation.</summary>
    public bool TurnPrepared { get; set; }

    /// <summary>Index into the fixed initiative order when initiative is not elective.</summary>
    public int TurnIndex { get; set; }

    public string? ActiveActorId { get; set; }

    public string? LastActorId { get; set; }

    public int? FledSide { get; set; }

    public int? Winner { get; set; }

    public List<string> TurnOrder { get; set; } = [];

    public List<string> TookTurns { get; set; } = [];

    public Dictionary<string, CombatControlMode> Controllers { get; set; } = [];

    /// <summary>
    /// The explicit continuation phase of each authored behavior. The live
    /// owner fills this from <see cref="CombatBehaviorController.States"/> and
    /// restores it before the next proposal; it contains no evaluator or
    /// executable state.
    /// </summary>
    public Dictionary<string, CombatBehaviorState> BehaviorStates { get; set; } = [];

    /// <summary>Explicit behavior overrides, including null opt-outs, by actor ID.</summary>
    public Dictionary<string, string?> BehaviorAssignments { get; set; } = [];

    /// <summary>Behavior proposal committed by an action waiting on an interrupt.</summary>
    public string? BehaviorPendingActorId { get; set; }

    public string? BehaviorPendingId { get; set; }

    public int? BehaviorPendingRuleIndex { get; set; }

    public int? BehaviorPendingStepIndex { get; set; }

    public bool BehaviorPendingMovementOnly { get; set; }

    public CombatDecision? PendingDecision { get; set; }

    /// <summary>
    /// The keyed random scope and next logical draw used by a live combat.
    /// Engine streams do not expose a serializable cursor, so persistence
    /// carries the deterministic continuation key instead of an opaque Rng.
    /// </summary>
    public string? RandomScope { get; set; }

    public long NextRandomKey { get; set; }

    public CombatInterruptState? PendingInterrupt { get; set; }

    /// <summary>Committed dice journal for a pending damage interrupt.</summary>
    public List<DiceRoll> PendingInterruptRolls { get; set; } = [];

    public CombatCheckState? PendingCheck { get; set; }

    /// <summary>Committed dice journal for the pending check, kept separate from the observable choice record.</summary>
    public List<DiceRoll> PendingCheckRolls { get; set; } = [];

    /// <summary>All interrupt frames below the current decision, outermost first.</summary>
    public List<CombatInterruptFrameState> ParentInterrupts { get; set; } = [];

    public List<CombatFactState> Facts { get; set; } = [];

    public string? CommittedActionId { get; set; }

    public string? CommittedActorId { get; set; }

    public int? CommittedMaximumTargets { get; set; }

    public long? CommittedTargetRollStart { get; set; }

    /// <summary>Dice consumed while evaluating a committed target cap.</summary>
    public List<DiceRoll> CommittedTargetRolls { get; set; } = [];

    /// <summary>The operation frame to resume after a pending interrupt, when one exists.</summary>
    public CombatOperationState? PendingOperation { get; set; }

    public CombatMovementState? PendingMovement { get; set; }

    /// <summary>All active operation frames from the action down to the suspended operation.</summary>
    public List<CombatOperationState> OperationStack { get; set; } = [];

    /// <summary>Reaction nesting at the suspension boundary: 0 for an action, 1 for a reaction, 2 for its counter.</summary>
    public int ReactionDepth { get; set; }

    public List<CombatantState> Combatants { get; set; } = [];

    public static string ToJson(CombatContinuationState state)
    {
        return JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
    }

    public static CombatContinuationState FromJson(string json)
    {
        return JsonSerializer.Deserialize<CombatContinuationState>(json)
            ?? throw new JsonException("A combat continuation must be a JSON object.");
    }
}

/// <summary>
/// Explicit mutable state for one combatant. Definition references are
/// qualified IDs, so a save loader can resolve them against the already checked
/// module set without replaying prior actions.
/// </summary>
public sealed class CombatantState
{
    public string Id { get; set; } = "";

    public int Side { get; set; }

    public bool Defeated { get; set; }

    public bool Escaped { get; set; }

    public decimal SurprisedRounds { get; set; }

    public Cell? Position { get; set; }

    public Dictionary<string, int> Budget { get; set; } = [];

    public Dictionary<string, decimal?> Tracks { get; set; } = [];

    /// <summary>Per-combat maxima kept separately from current track values.</summary>
    public Dictionary<string, decimal> TrackMaximums { get; set; } = [];

    public Dictionary<string, decimal> Values { get; set; } = [];

    public Dictionary<string, int> Rolled { get; set; } = [];

    public List<CombatConditionState> Conditions { get; set; } = [];

    public List<string> AppliedThisTurn { get; set; } = [];

    public List<string> Preparing { get; set; } = [];

    public List<string> Prepared { get; set; } = [];

    public Dictionary<string, int> CastsLeft { get; set; } = [];

    public CombatControlMode Controller { get; set; }
}
