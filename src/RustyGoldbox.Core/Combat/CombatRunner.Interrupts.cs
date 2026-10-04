using System.Globalization;
using System.Text.Json;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

/// <summary>
/// Optional live-combat decisions are owned by the same runner as ordinary
/// actions. This partial contains only the committed values at an interrupt
/// boundary and the small continuation methods that consume them.
/// </summary>
public sealed partial class CombatRunner
{
    private bool _suspending;
    private CombatOperationState? _pendingOperation;
    private List<CombatOperationState> _operationStack = [];
    private ActionContinuation? _actionContinuation;
    private DamageContinuation? _damageContinuation;
    private PendingInterruptFrame? _pendingInterruptFrame;
    private PendingPostRollFrame? _pendingPostRollFrame;
    private PendingInitiativeFrame? _pendingInitiativeFrame;
    private Combatant? _resumedInitiative;
    private readonly List<ParentInterruptFrame> _parentInterruptFrames = [];
    private MovementContinuation? _movementContinuation;
    private readonly List<OperationFrame> _operationFrames = [];

    private sealed class CombatSuspendedException : Exception
    {
        public CombatSuspendedException() : base("Combat is waiting for an explicit decision.")
        {
        }
    }

    private sealed record ActionContinuation(
        Combatant Actor,
        UseOption Use,
        Combatant Target,
        Scope Scope,
        IReadOnlyList<Combatant>? Targets = null,
        int TargetIndex = 0,
        bool AlreadyPaid = false,
        int OperationFrameStart = 0);

    private sealed record CheckOperationContext(
        Definition Owner,
        string Path,
        Scope Scope,
        Combatant Actor,
        Combatant? Target,
        Combatant? Source,
        bool IsOperation);

    private sealed class OperationFrame
    {
        public required Definition Owner { get; init; }
        public required JsonElement Operations { get; init; }
        public required string Path { get; init; }
        public required Scope Scope { get; set; }
        public required Combatant Actor { get; init; }
        public Combatant? Target { get; init; }
        public Combatant? Source { get; init; }
        public string? UseId { get; init; }
        public string? ActionId { get; init; }
        public int Index { get; set; }
    }

    private sealed record DamageContinuation(
        Definition Owner,
        string Path,
        Combatant Actor,
        Combatant Target,
        Combatant Source,
        Definition Track,
        decimal Amount,
        int RollsBefore,
        int FactsBefore,
        bool Physical,
        bool Attack,
        CombatOperationState Operation);

    private sealed record ReactionCandidate(
        Definition Reaction,
        UseOption Use,
        string OptionId,
        decimal Cost,
        int ReactionIndex = -1);

    private sealed record ParentInterruptFrame(PendingInterruptFrame Frame, decimal Amount, PendingDamage? Damage);

    private sealed class MovementContinuation
    {
        public required Definition Owner { get; init; }
        public required string Path { get; init; }
        public required Scope Scope { get; init; }
        public required Combatant Actor { get; init; }
        public required Combatant Target { get; init; }
        public required Cell Start { get; init; }
        public required Cell Here { get; set; }
        public required decimal Allowed { get; init; }
        public required decimal Within { get; init; }
        public decimal? Beyond { get; init; }
        public required bool Away { get; init; }
        public required bool Provokes { get; init; }
        public required bool Escape { get; init; }
        public required HashSet<Cell> Blocked { get; init; }
        public Dictionary<Cell, int>? ToGoal { get; init; }
        public decimal Spent { get; set; }
        public int Steps { get; set; }
        public Cell? PendingStep { get; set; }
        public int EnemyIndex { get; set; }
        public required IReadOnlyList<Combatant> Enemies { get; init; }
        public IReadOnlyList<Cell>? Selected { get; init; }
        public int SelectedIndex { get; set; }
        public bool Explicit { get; init; }
    }

    private sealed class PendingInterruptFrame
    {
        public required string Trigger { get; init; }
        public required Combatant Reactor { get; init; }
        public required Combatant Source { get; init; }
        public required IReadOnlyList<ReactionCandidate> Candidates { get; init; }
        public required CombatOperationState Operation { get; init; }
        public Combatant? Target { get; init; }
        public Definition? Track { get; init; }
        public decimal PendingDamage { get; init; }
        public int RollsBefore { get; init; }
        public int FactsBefore { get; init; }
        public bool Physical { get; init; }
        public bool Attack { get; init; }
        public ActionContinuation? Action { get; init; }
        public MovementContinuation? Movement { get; set; }
        /// <summary>
        /// Dice that were already committed by the interrupted operation. A
        /// non-null value means the frame was restored from a save and the
        /// fresh roller does not contain these journal entries.
        /// </summary>
        public IReadOnlyList<DiceRoll>? PendingRolls { get; init; }
    }

    private sealed record PostRollCandidate(
        int Index,
        Definition Track,
        decimal Cost,
        decimal Score,
        decimal? Bonus,
        bool Reroll,
        string Name,
        string OptionId);

    private sealed class PendingPostRollFrame
    {
        public required Definition Check { get; init; }
        public required Combatant By { get; init; }
        public required Combatant Against { get; init; }
        public required CheckResult Initial { get; init; }
        public required IReadOnlyList<PostRollCandidate> Candidates { get; init; }
        public ActionContinuation? Action { get; init; }
        public required CombatOperationState Operation { get; init; }
        public required CheckOperationContext Context { get; init; }
        public int OperationFrameStart { get; init; }
    }

    private sealed class PendingInitiativeFrame
    {
        public Combatant? Last { get; init; }
        public required Combatant Chooser { get; init; }
        public required IReadOnlyList<Combatant> Candidates { get; init; }
        public required IReadOnlyDictionary<string, decimal> Scores { get; init; }
        public required CombatOperationState Operation { get; init; }
    }

    private void BeginActionContinuation(Combatant actor, UseOption use, Combatant target)
    {
        Scope scope = new(actor.Creature, target.Creature, use.Parameters);
        ActionContinuation? previous = _actionContinuation;
        _actionContinuation = new ActionContinuation(
            actor,
            use,
            target,
            scope,
            previous?.Targets,
            previous?.TargetIndex ?? 0,
            previous?.AlreadyPaid ?? false,
            _operationFrames.Count);
        CombatOperationState operation = new(
            use.Action.QualifiedId,
            "$.action",
            actor.Id,
            target.Id,
            actor.Id,
            use.Action.QualifiedId,
            UseId: UseId(actor, use),
            TargetIds: _actionContinuation?.Targets?.Select(member => member.Id).ToList(),
            TargetIndex: _actionContinuation?.TargetIndex ?? 0,
            AlreadyPaid: _actionContinuation?.AlreadyPaid ?? false);
        _pendingOperation = operation;
        _operationStack.RemoveAll(frame => frame.Path == "$.action" && frame.ActorId == actor.Id);
        _operationStack.Add(operation);
    }

    private void SetActionTargetContinuation(Combatant actor, UseOption use, IReadOnlyList<Combatant> targets, int index, bool alreadyPaid)
    {
        Combatant target = targets[index];
        _actionContinuation = new ActionContinuation(
            actor,
            use,
            target,
            new Scope(actor.Creature, target.Creature, use.Parameters),
            targets,
            index,
            alreadyPaid);
    }

    private void EndActionContinuation()
    {
        if (_suspending || _actionContinuation is null)
        {
            return;
        }

        FinishActionContinuation(_actionContinuation);
    }

    private void BeginDamageContinuation(
        Definition owner,
        string path,
        Combatant actor,
        Combatant target,
        Combatant source,
        Definition track,
        decimal amount,
        int rollsBefore,
        bool physical,
        bool attack)
    {
        CombatOperationState operation = new(
            owner.QualifiedId,
            path,
            actor.Id,
            target.Id,
            source.Id,
            _actionContinuation?.Use.Action.QualifiedId,
            PendingDamage: amount,
            UseId: _actionContinuation is null ? null : UseId(_actionContinuation.Actor, _actionContinuation.Use),
            ListPath: _operationFrames.LastOrDefault()?.Path,
            Check: ToScopeCheck(_operationFrames.LastOrDefault()?.Scope.Check),
            Outer: ToScopeCheck(_operationFrames.LastOrDefault()?.Scope.Outer),
            ConditionValues: _operationFrames.LastOrDefault()?.Scope.ConditionValues);
        _damageContinuation = new DamageContinuation(
            owner,
            path,
            actor,
            target,
            source,
            track,
            amount,
            rollsBefore,
            _facts.Count,
            physical,
            attack,
            operation);
        _pendingOperation = operation;
    }

    private void EndDamageContinuation()
    {
        if (_suspending)
        {
            return;
        }

        _damageContinuation = null;
        _pendingOperation = _actionContinuation is null ? null : _operationStack.LastOrDefault(frame => frame.Path == "$.action");
    }

    private bool ReactionFits(
        string trigger,
        Combatant reactor,
        Combatant source,
        Definition reaction,
        Func<Definition, bool>? fits,
        bool physical,
        bool attack)
    {
        bool counter = reaction.Json.TryGetProperty("counter", out JsonElement counters) && counters.GetBoolean();
        return reaction.Json.GetProperty("trigger").GetString() == trigger
            && (_reactions != 1 || counter)
            && Affordable(reactor, reaction)
            && (fits is null || fits(reaction))
            && (trigger != "hit" || !reaction.Json.TryGetProperty("physical", out JsonElement physicalOnly) || !physicalOnly.GetBoolean() || physical)
            && (trigger != "hit" || !reaction.Json.TryGetProperty("attack", out JsonElement attackOnly) || !attackOnly.GetBoolean() || attack)
            && (!reaction.Json.TryGetProperty("when", out _) || Evaluate(reaction, "$.when", new Scope(reactor.Creature, source.Creature)).Boolean);
    }

    private void SuspendReaction(
        string trigger,
        Combatant reactor,
        Combatant source,
        IReadOnlyList<(Definition Reaction, UseOption Use)> legal,
        bool physical,
        bool attack)
    {
        List<ReactionCandidate> candidates = legal
            .Select(entry => new ReactionCandidate(
                entry.Reaction,
                entry.Use,
                $"reaction:{reactor.Id}:{entry.Reaction.QualifiedId}",
                ReactionCost(entry.Reaction, reactor),
                reactor.Reactions.FindIndex(candidate => ReferenceEquals(candidate.Reaction, entry.Reaction)
                    && ReferenceEquals(candidate.Use, entry.Use))))
            .ToList();
        CombatOperationState operation = _pendingOperation
            ?? new CombatOperationState(
                _combat.QualifiedId,
                "$",
                _activeActor?.Id ?? source.Id,
                source.Id,
                source.Id,
                _actionContinuation?.Use.Action.QualifiedId,
                PendingDamage: _pendingDamage?.Amount,
                UseId: _actionContinuation is null ? null : UseId(_actionContinuation.Actor, _actionContinuation.Use),
                TargetIds: _actionContinuation?.Targets?.Select(member => member.Id).ToList(),
                TargetIndex: _actionContinuation?.TargetIndex ?? 0,
                AlreadyPaid: _actionContinuation?.AlreadyPaid ?? false);
        CombatInterruptState interrupt = new(
            trigger,
            reactor.Id,
            source.Id,
            candidates.Count == 1 ? candidates[0].Reaction.QualifiedId : null,
            _actionContinuation?.Use.Action.QualifiedId,
            _damageContinuation?.Target.Id ?? _actionContinuation?.Target.Id,
            _damageContinuation?.Track.Id,
            _pendingDamage?.Amount,
            operation.OwnerId,
            operation.Path,
            operation.UseId,
            null);
        _pendingInterruptFrame = new PendingInterruptFrame
        {
            Trigger = trigger,
            Reactor = reactor,
            Source = source,
            Candidates = candidates,
            Operation = operation,
            Target = _damageContinuation?.Target ?? _actionContinuation?.Target,
            Track = _damageContinuation?.Track,
            PendingDamage = _pendingDamage?.Amount ?? 0,
            RollsBefore = _damageContinuation?.RollsBefore ?? _dice.Rolls.Count,
            FactsBefore = _damageContinuation?.FactsBefore ?? _facts.Count,
            Physical = physical,
            Attack = attack,
            Action = _actionContinuation,
            Movement = _movementContinuation,
        };
        _pendingOperation = operation;
        _operationStack = _operationStack.Count == 0 ? [operation] : _operationStack;
        _pendingDecision = new CombatDecision(
            $"interrupt:{reactor.Id}:{_round}:{_facts.Count}",
            CombatDecisionKind.Interrupt,
            reactor.Id,
            _round,
            [],
            [],
            false,
            operation.OwnerId,
            operation.Path,
            candidates.Select(candidate => new CombatDecisionOption(
                candidate.OptionId,
                candidate.Reaction.Name,
                "reaction",
                candidate.Reaction.QualifiedId,
                null,
                null,
                candidate.Cost,
                Index: candidate.ReactionIndex)).ToList(),
            interrupt);
        _phase = CombatPhase.AwaitingInterrupt;
        _suspending = true;
        throw new CombatSuspendedException();
    }

    private static decimal ReactionCost(Definition reaction, Combatant reactor)
    {
        if (!reaction.Json.TryGetProperty("cost", out JsonElement cost) || !cost.TryGetProperty("reaction", out JsonElement amount))
        {
            return 0;
        }

        return amount.ValueKind == JsonValueKind.Number ? amount.GetDecimal() : 0;
    }

    private void CaptureInterruptContinuation(CombatContinuationState state)
    {
        SyncOperationFrames();
        state.PendingInterrupt = _pendingDecision?.Interrupt;
        state.PendingInterruptRolls = _damageContinuation is null
            ? []
            : _dice.Rolls.Skip(_damageContinuation.RollsBefore).ToList();
        state.PendingCheck = _pendingDecision?.Check;
        state.PendingCheckRolls = _pendingDecision?.Check?.FactIndex is int factIndex
            && factIndex >= 0
            && factIndex < _facts.Count
            ? _facts[factIndex].Rolls.ToList()
            : [];
        state.PendingOperation = _pendingOperation;
        state.PendingMovement = _movementContinuation is null ? null : CaptureMovement(_movementContinuation);
        state.OperationStack = _operationStack.ToList();
        state.ReactionDepth = _reactions;
        state.ParentInterrupts = _parentInterruptFrames.Select(CaptureParentInterrupt).ToList();
    }

    private void RestoreInterruptContinuation(CombatContinuationState state)
    {
        _pendingOperation = state.PendingOperation;
        _operationStack = state.OperationStack.ToList();
        _operationFrames.Clear();
        RestoreOperationFrames(state);
        _suspending = false;
        _pendingInterruptFrame = null;
        _pendingPostRollFrame = null;
        _pendingInitiativeFrame = null;
        _movementContinuation = null;
        _parentInterruptFrames.Clear();
        if ((state.PendingDecision?.Kind == CombatDecisionKind.Interrupt
                || state.PendingDecision?.Kind == CombatDecisionKind.Initiative)
            && state.PendingInterrupt is CombatInterruptState interrupt)
        {
            if (interrupt.Trigger == "initiative")
            {
                RestoreInitiative(interrupt, state);
            }
            else
            {
                RestoreInterrupt(interrupt, state);
            }
        }
        else if (state.PendingDecision?.Kind == CombatDecisionKind.PostRoll && state.PendingCheck is CombatCheckState check)
        {
            RestorePostRoll(check, state);
        }

        if (state.PendingMovement is CombatMovementState movement)
        {
            RestoreMovement(movement, state);
        }

        foreach (CombatInterruptFrameState parent in state.ParentInterrupts)
        {
            RestoreParentInterrupt(parent, state);
        }

    }

    private CombatInterruptFrameState CaptureParentInterrupt(ParentInterruptFrame parent)
    {
        PendingInterruptFrame frame = parent.Frame;
        decimal amount = parent.Damage?.Amount ?? parent.Amount;
        IReadOnlyList<DiceRoll> pendingRolls = frame.PendingRolls
            ?? _dice.Rolls.Skip(frame.RollsBefore).ToList();
        CombatInterruptState interrupt = new(
            frame.Trigger,
            frame.Reactor.Id,
            frame.Source.Id,
            frame.Candidates.Count == 1 ? frame.Candidates[0].Reaction.QualifiedId : null,
            frame.Action?.Use.Action.QualifiedId,
            frame.Target?.Id ?? frame.Action?.Target.Id,
            frame.Track?.Id,
            amount,
            frame.Operation.OwnerId,
            frame.Operation.Path,
            frame.Action is null ? null : UseId(frame.Action.Actor, frame.Action.Use),
            null);
        return new CombatInterruptFrameState
        {
            Interrupt = interrupt,
            Options = frame.Candidates.Select(candidate => new CombatDecisionOption(
                candidate.OptionId,
                candidate.Reaction.Name,
                "reaction",
                candidate.Reaction.QualifiedId,
                null,
                null,
                candidate.Cost,
                Index: candidate.ReactionIndex)).ToList(),
            Operation = frame.Operation,
            PendingDamage = amount,
            RollsBefore = frame.RollsBefore,
            FactsBefore = frame.FactsBefore,
            Physical = frame.Physical,
            Attack = frame.Attack,
            Action = frame.Action is null ? null : ActionOperation(frame.Action),
            Movement = frame.Movement is null ? null : CaptureMovement(frame.Movement),
            PendingRolls = pendingRolls.ToList(),
        };
    }

    private void RestoreParentInterrupt(CombatInterruptFrameState saved, CombatContinuationState state)
    {
        CombatInterruptState interrupt = saved.Interrupt;
        Combatant reactor = Find(interrupt.ReactorId)
            ?? throw new ArgumentException($"Unknown parent interrupt reactor '{interrupt.ReactorId}'.", nameof(state));
        Combatant source = Find(interrupt.SourceId)
            ?? throw new ArgumentException($"Unknown parent interrupt source '{interrupt.SourceId}'.", nameof(state));
        List<ReactionCandidate> candidates = RestoreReactionCandidates(reactor, saved.Options);
        ActionContinuation? action = RestoreActionContinuation(saved.Operation, interrupt, state, saved.Action);
        Combatant? target = interrupt.TargetId is string targetId ? Find(targetId) : null;
        Definition? track = interrupt.TrackId is string trackId ? _rules.Find(DefinitionTypes.Track, trackId, out _) : null;
        PendingInterruptFrame frame = new()
        {
            Trigger = interrupt.Trigger,
            Reactor = reactor,
            Source = source,
            Candidates = candidates,
            Operation = saved.Operation,
            Target = target,
            Track = track,
            PendingDamage = saved.PendingDamage,
            RollsBefore = saved.RollsBefore,
            FactsBefore = saved.FactsBefore,
            Physical = saved.Physical,
            Attack = saved.Attack,
            Action = action,
            Movement = saved.Movement is CombatMovementState movement
                ? BuildMovementContinuation(movement, state)
                : null,
            PendingRolls = saved.PendingRolls is { Count: > 0 } ? saved.PendingRolls : interrupt.PendingRolls,
        };
        _parentInterruptFrames.Add(new ParentInterruptFrame(frame, saved.PendingDamage, null));
    }

    private CombatOperationState ActionOperation(ActionContinuation action)
    {
        return new CombatOperationState(
            action.Use.Action.QualifiedId,
            "$.action",
            action.Actor.Id,
            action.Target.Id,
            action.Actor.Id,
            action.Use.Action.QualifiedId,
            UseId: UseId(action.Actor, action.Use),
            TargetIds: action.Targets?.Select(member => member.Id).ToList(),
            TargetIndex: action.TargetIndex,
            AlreadyPaid: action.AlreadyPaid);
    }

    private static CombatScopeCheckState? ToScopeCheck(CheckResult? result)
    {
        return result is null
            ? null
            : new CombatScopeCheckState(
                result.Roll,
                result.Bonus,
                result.Modifier,
                result.Total,
                result.Target,
                result.Margin,
                result.Success,
                result.Tier);
    }

    private static CheckResult? FromScopeCheck(CombatScopeCheckState? result)
    {
        return result is null
            ? null
            : new CheckResult(
                result.Roll,
                result.Bonus,
                result.Modifier,
                result.Total,
                result.Target,
                result.Margin,
                result.Success,
                result.Tier);
    }

    private void FinishActionContinuation(ActionContinuation action)
    {
        while (_operationFrames.Count > action.OperationFrameStart)
        {
            RemoveOperationFrame(_operationFrames[^1]);
        }

        string? useId = UseId(action.Actor, action.Use);
        int root = _operationStack.FindLastIndex(frame =>
            frame.Path == "$.action"
            && frame.ActorId == action.Actor.Id
            && frame.TargetId == action.Target.Id
            && frame.UseId == useId);
        if (root >= 0)
        {
            _operationStack.RemoveAt(root);
        }

        if (ReferenceEquals(_actionContinuation, action))
        {
            _actionContinuation = null;
        }

        _pendingOperation = _operationStack.LastOrDefault();
        if (_operationFrames.Count == 0 && !_operationStack.Any(frame => frame.Path == "$.action"))
        {
            _pendingOperation = null;
            _operationStack = [];
        }
    }

    private void RemoveOperationFrame(OperationFrame frame)
    {
        int stateIndex = _operationStack.FindLastIndex(state =>
            state.IsCursor
            && state.OwnerId == frame.Owner.QualifiedId
            && state.ListPath == frame.Path
            && state.ActorId == frame.Actor.Id
            && state.TargetId == frame.Target?.Id);
        if (stateIndex >= 0)
        {
            _operationStack.RemoveAt(stateIndex);
        }

        if (_operationFrames.Count > 0 && ReferenceEquals(_operationFrames[^1], frame))
        {
            _operationFrames.RemoveAt(_operationFrames.Count - 1);
        }
    }

    private void SyncOperationFrames()
    {
        _operationStack.RemoveAll(frame => frame.IsCursor);
        _operationStack.AddRange(_operationFrames.Select(frame => new CombatOperationState(
            frame.Owner.QualifiedId,
            frame.Path,
            frame.Actor.Id,
            frame.Target?.Id,
            frame.Source?.Id,
            frame.ActionId,
            Index: frame.Index,
            UseId: frame.UseId,
            ListPath: frame.Path,
            Check: ToScopeCheck(frame.Scope.Check),
            Outer: ToScopeCheck(frame.Scope.Outer),
            ConditionValues: frame.Scope.ConditionValues,
            IsCursor: true)));
    }

    private void RestoreOperationFrames(CombatContinuationState state)
    {
        foreach (CombatOperationState saved in state.OperationStack.Where(frame => frame.IsCursor))
        {
            Definition? owner = _rules.Definitions.FirstOrDefault(definition => definition.QualifiedId == saved.OwnerId);
            Combatant? actor = Find(saved.ActorId);
            if (owner is null || actor is null || saved.ListPath is not string listPath)
            {
                continue;
            }

            JsonElement operations = JsonAtPath(owner.Json, listPath);
            if (operations.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            Combatant? target = saved.TargetId is string targetId ? Find(targetId) : null;
            Combatant? source = saved.SourceId is string sourceId ? Find(sourceId) : null;
            IReadOnlyDictionary<string, CompiledExpression>? use = saved.UseId is string useId
                ? FindUseById(useId)?.Use.Parameters
                : null;
            _operationFrames.Add(new OperationFrame
            {
                Owner = owner,
                Operations = operations,
                Path = listPath,
                Scope = new Scope(
                    actor.Creature,
                    target?.Creature,
                    use,
                    FromScopeCheck(saved.Check),
                    Outer: FromScopeCheck(saved.Outer),
                    ConditionValues: saved.ConditionValues),
                Actor = actor,
                Target = target,
                Source = source,
                UseId = saved.UseId,
                ActionId = saved.ActionId,
                Index = Math.Clamp(saved.Index, 0, operations.GetArrayLength()),
            });
        }
    }

    private (Combatant Owner, UseOption Use)? FindUseById(string useId)
    {
        foreach (Combatant member in Everyone)
        {
            if (FindUse(member, useId) is UseOption use)
            {
                return (member, use);
            }
        }

        return null;
    }

    private int OperationFrameStart(Combatant actor, UseOption use, Combatant target)
    {
        string? useId = UseId(actor, use);
        for (int index = 0; index < _operationFrames.Count; index++)
        {
            OperationFrame frame = _operationFrames[index];
            if (frame.Actor == actor
                && frame.Target == target
                && frame.UseId == useId)
            {
                return index;
            }
        }

        return _operationFrames.Count;
    }

    private static JsonElement JsonAtPath(JsonElement root, string path)
    {
        if (path == "$")
        {
            return root;
        }

        JsonElement current = root;
        int position = path.StartsWith("$.", StringComparison.Ordinal) ? 2 : 1;
        while (position < path.Length)
        {
            if (path[position] == '.')
            {
                position++;
            }

            int propertyStart = position;
            while (position < path.Length && path[position] is not ('.' or '['))
            {
                position++;
            }

            if (position > propertyStart)
            {
                current = current.GetProperty(path[propertyStart..position]);
            }

            while (position < path.Length && path[position] == '[')
            {
                int close = path.IndexOf(']', position + 1);
                if (close < 0 || !int.TryParse(path[(position + 1)..close], out int index))
                {
                    throw new ArgumentException($"Invalid operation JSON path '{path}'.", nameof(path));
                }

                current = current.EnumerateArray().ElementAt(index);
                position = close + 1;
            }
        }

        return current;
    }

    private void RestoreInterrupt(CombatInterruptState interrupt, CombatContinuationState state)
    {
        Combatant reactor = Find(interrupt.ReactorId) ?? throw new ArgumentException($"Unknown interrupt reactor '{interrupt.ReactorId}'.", nameof(state));
        Combatant source = Find(interrupt.SourceId) ?? throw new ArgumentException($"Unknown interrupt source '{interrupt.SourceId}'.", nameof(state));
        List<ReactionCandidate> candidates = RestoreReactionCandidates(reactor, state.PendingDecision?.Options);
        CombatOperationState operation = state.PendingOperation
            ?? new CombatOperationState(
                interrupt.OperationOwner ?? _combat.QualifiedId,
                interrupt.OperationPath ?? "$",
                source.Id,
                interrupt.TargetId,
                interrupt.SourceId,
                interrupt.ActionId,
                PendingDamage: interrupt.PendingDamage,
                UseId: interrupt.UseId);
        ActionContinuation? action = RestoreActionContinuation(operation, interrupt, state);

        Definition? track = interrupt.TrackId is string trackId ? _rules.Find(DefinitionTypes.Track, trackId, out _) : null;
        Combatant? targetForDamage = interrupt.TargetId is string targetIdForDamage ? Find(targetIdForDamage) : null;
        _pendingInterruptFrame = new PendingInterruptFrame
        {
            Trigger = interrupt.Trigger,
            Reactor = reactor,
            Source = source,
            Candidates = candidates,
            Operation = operation,
            Target = targetForDamage,
            Track = track,
            PendingDamage = interrupt.PendingDamage ?? 0,
            RollsBefore = _dice.Rolls.Count,
            FactsBefore = _facts.Count,
            Action = action,
            PendingRolls = state.PendingInterruptRolls.Count > 0 ? state.PendingInterruptRolls : null,
        };
        _pendingOperation = operation;
        _operationStack = state.OperationStack.Count == 0 ? [operation] : state.OperationStack.ToList();
    }

    private List<ReactionCandidate> RestoreReactionCandidates(
        Combatant reactor,
        IReadOnlyList<CombatDecisionOption>? options)
    {
        List<ReactionCandidate> candidates = [];
        foreach (CombatDecisionOption option in options?.Where(option => option.Kind == "reaction") ?? [])
        {
            int index = option.Index is int offeredIndex
                && offeredIndex >= 0
                && offeredIndex < reactor.Reactions.Count
                ? offeredIndex
                : reactor.Reactions.FindIndex(entry =>
                    entry.Reaction.QualifiedId == option.QualifiedId
                    && (string.IsNullOrEmpty(option.Name) || entry.Reaction.Name == option.Name));
            if (index < 0)
            {
                continue;
            }

            (Definition reaction, UseOption use) = reactor.Reactions[index];
            if (option.QualifiedId is string qualifiedId
                && !string.Equals(qualifiedId, reaction.QualifiedId, StringComparison.Ordinal))
            {
                continue;
            }

            // Cost and eligibility were committed when this option was
            // offered. Do not call ReactionCost or evaluate its policy again
            // while restoring a save.
            candidates.Add(new ReactionCandidate(reaction, use, option.Id, option.Cost, index));
        }

        return candidates;
    }

    private ActionContinuation? RestoreActionContinuation(
        CombatOperationState? operation,
        CombatInterruptState? interrupt,
        CombatContinuationState state,
        CombatOperationState? explicitAction = null)
    {
        CombatOperationState? actionOperation = explicitAction
            ?? state.OperationStack.LastOrDefault(frame => frame.Path == "$.action")
            ?? (operation?.Path == "$.action" ? operation : null);
        string? actorId = actionOperation?.ActorId ?? operation?.ActorId;
        Combatant? actor = actorId is null ? null : Find(actorId);
        if (actor is null)
        {
            return null;
        }

        string? useId = actionOperation?.UseId
            ?? operation?.UseId
            ?? interrupt?.UseId
            ?? interrupt?.ActionId;
        if (useId is null || FindUse(actor, useId) is not UseOption use)
        {
            return null;
        }

        string? targetId = actionOperation?.TargetId ?? operation?.TargetId ?? interrupt?.TargetId;
        Combatant? target = targetId is null ? null : Find(targetId);
        if (target is null)
        {
            return null;
        }

        IReadOnlyList<Combatant>? targets = actionOperation?.TargetIds is { Count: > 0 } targetIds
            ? targetIds.Select(Find).Where(member => member is not null).Cast<Combatant>().ToList()
            : null;
        int targetIndex = actionOperation?.TargetIndex ?? 0;
        bool alreadyPaid = actionOperation?.AlreadyPaid ?? false;
        Scope scope = _operationFrames.FirstOrDefault(frame =>
                frame.Actor == actor
                && frame.Target == target
                && frame.UseId == UseId(actor, use))?.Scope
            ?? new Scope(actor.Creature, target.Creature, use.Parameters);
        return new ActionContinuation(
            actor,
            use,
            target,
            scope,
            targets,
            targetIndex,
            alreadyPaid,
            OperationFrameStart(actor, use, target));
    }

    private void RestorePostRoll(CombatCheckState saved, CombatContinuationState state)
    {
        Definition check = _rules.Find(DefinitionTypes.Check, saved.CheckId, out string? problem)
            ?? throw new ArgumentException(problem ?? $"Unknown check '{saved.CheckId}'.", nameof(state));
        CombatOperationState operation = state.PendingOperation
            ?? new CombatOperationState(
                check.QualifiedId,
                "$.check",
                state.PendingDecision?.ActorId ?? "",
                null,
                state.PendingDecision?.ActorId,
                state.PendingDecision?.ActionId);
        string? byId = saved.ById ?? state.PendingDecision?.ActorId ?? operation.ActorId;
        Combatant by = Find(byId ?? "")
            ?? Find(state.PendingDecision?.ActorId ?? "")
            ?? Find(operation.ActorId)
            ?? throw new ArgumentException($"Unknown post-roll actor '{byId}'.", nameof(state));
        string? againstId = saved.AgainstId ?? operation.TargetId;
        Combatant against = Find(againstId ?? "") ?? Find(operation.TargetId ?? "") ?? by;
        CheckResult initial = new(saved.Roll, saved.Bonus, saved.Modifier, saved.Total, saved.Target, saved.Margin, saved.Success, saved.Tier);
        // Candidate values were evaluated once when the choice was offered.
        // Rebuilding them from the definition would re-run dice expressions
        // in cost, score or bonus and could change the offered menu.
        List<PostRollCandidate> candidates = RestorePostRollCandidates(check, state.PendingDecision?.Options);
        ActionContinuation? action = RestoreActionContinuation(operation, null, state);
        Definition owner = _rules.Definitions.FirstOrDefault(definition => definition.QualifiedId == operation.OwnerId) ?? check;
        Combatant operationActor = Find(operation.ActorId) ?? action?.Actor ?? by;
        Combatant? operationTarget = operation.TargetId is string targetId
            ? Find(targetId) ?? action?.Target
            : action?.Target;
        Combatant? operationSource = operation.SourceId is string sourceId ? Find(sourceId) : null;
        IReadOnlyDictionary<string, CompiledExpression>? use = operation.UseId is string useId
            ? FindUseById(useId)?.Use.Parameters
            : action?.Use.Parameters;
        OperationFrame? operationFrame = operation.ListPath is string listPath
            ? _operationFrames.LastOrDefault(frame => frame.Owner.QualifiedId == owner.QualifiedId && frame.Path == listPath)
            : null;
        Scope scope = operationFrame?.Scope
            ?? new Scope(
                operationActor.Creature,
                operationTarget?.Creature,
                use,
                FromScopeCheck(operation.Check),
                Outer: FromScopeCheck(operation.Outer),
                ConditionValues: operation.ConditionValues);
        CheckOperationContext context = new(
            owner,
            operation.Path,
            scope,
            operationActor,
            operationTarget,
            operationSource,
            operation.ListPath is not null);

        _pendingPostRollFrame = new PendingPostRollFrame
        {
            Check = check,
            By = by,
            Against = against,
            Initial = initial,
            Candidates = candidates,
            Action = action,
            Operation = operation,
            Context = context,
            OperationFrameStart = action?.OperationFrameStart ?? 0,
        };
        _pendingOperation = operation;
        _operationStack = state.OperationStack.Count == 0 ? [operation] : state.OperationStack.ToList();
    }

    private List<PostRollCandidate> RestorePostRollCandidates(
        Definition check,
        IReadOnlyList<CombatDecisionOption>? options)
    {
        List<PostRollCandidate> candidates = [];
        foreach (CombatDecisionOption option in options?.Where(option => option.Kind == "post_roll") ?? [])
        {
            if (option.TrackId is not string trackId)
            {
                continue;
            }

            Definition track = _rules.Find(DefinitionTypes.Track, trackId, out string? problem)
                ?? throw new ArgumentException(problem ?? $"Unknown post-roll track '{trackId}'.", nameof(options));
            int index = option.Index ?? ParsePostRollIndex(option.Id);
            candidates.Add(new PostRollCandidate(
                index,
                track,
                option.Cost,
                option.Score ?? 0,
                option.Bonus,
                option.Reroll,
                option.Name,
                option.Id));
        }

        return candidates;
    }

    private static int ParsePostRollIndex(string optionId)
    {
        int index = optionId.LastIndexOf(':');
        return index >= 0 && int.TryParse(optionId[(index + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 0;
    }

    private void RestoreInitiative(CombatInterruptState interrupt, CombatContinuationState state)
    {
        Combatant chooser = Find(interrupt.ReactorId) ?? throw new ArgumentException($"Unknown initiative chooser '{interrupt.ReactorId}'.", nameof(state));
        Combatant? last = interrupt.SourceId.Length == 0 ? null : Find(interrupt.SourceId);
        List<Combatant> candidates = state.PendingDecision!.Options?
            .Select(option => Find(option.TargetId ?? option.QualifiedId ?? ""))
            .Where(member => member is not null)
            .Cast<Combatant>()
            .ToList() ?? [];
        Dictionary<string, decimal> scores = state.PendingDecision.Options?
            .Where(option => option.TargetId is not null && option.Score is decimal)
            .ToDictionary(option => option.TargetId!, option => option.Score!.Value, StringComparer.Ordinal)
            ?? [];
        CombatOperationState operation = state.PendingOperation
            ?? new CombatOperationState(interrupt.OperationOwner ?? _combat.QualifiedId, interrupt.OperationPath ?? "$.initiative_score", chooser.Id, null, last?.Id ?? chooser.Id);
        _pendingInitiativeFrame = new PendingInitiativeFrame
        {
            Last = last,
            Chooser = chooser,
            Candidates = candidates,
            Scores = scores,
            Operation = operation,
        };
        _pendingOperation = operation;
        _operationStack = state.OperationStack.Count == 0 ? [operation] : state.OperationStack.ToList();
    }

    private static CombatMovementState CaptureMovement(MovementContinuation movement)
    {
        return new CombatMovementState(
            movement.Owner.QualifiedId,
            movement.Path,
            movement.Actor.Id,
            movement.Target.Id,
            movement.Start,
            movement.Here,
            movement.PendingStep,
            movement.Allowed,
            movement.Within,
            movement.Beyond,
            movement.Away,
            movement.Provokes,
            movement.Escape,
            movement.Spent,
            movement.Steps,
            movement.EnemyIndex,
            movement.Selected,
            movement.SelectedIndex);
    }

    private void RestoreMovement(CombatMovementState state, CombatContinuationState continuation)
    {
        _movementContinuation = BuildMovementContinuation(state, continuation);
        if (_movementContinuation is null)
        {
            return;
        }

        if (_pendingInterruptFrame is not null)
        {
            _pendingInterruptFrame.Movement = _movementContinuation;
        }
    }

    private MovementContinuation? BuildMovementContinuation(CombatMovementState state, CombatContinuationState continuation)
    {
        Combatant actor = Find(state.ActorId) ?? throw new ArgumentException($"Unknown movement actor '{state.ActorId}'.", nameof(continuation));
        Combatant target = Find(state.TargetId) ?? throw new ArgumentException($"Unknown movement target '{state.TargetId}'.", nameof(continuation));
        if (_field is null || target.Creature.Position is not Cell goal)
        {
            return null;
        }

        HashSet<Cell> blocked = BlockedCellsForMove(actor);
        Definition owner = _rules.Find(DefinitionTypes.Action, state.OwnerId, out string? problem)
            ?? throw new ArgumentException(problem ?? $"Unknown movement action '{state.OwnerId}'.", nameof(continuation));
        Dictionary<Cell, int>? toGoal = state.Away ? null : CostsToReach(goal, state.Within, blocked);
        Scope scope = new(actor.Creature, target.Creature);
        return new MovementContinuation
        {
            Owner = owner,
            Path = state.Path,
            Scope = scope,
            Actor = actor,
            Target = target,
            Start = state.Start,
            Here = state.Here,
            Allowed = state.Allowed,
            Within = state.Within,
            Beyond = state.Beyond,
            Away = state.Away,
            Provokes = state.Provokes,
            Escape = state.Escape,
            Blocked = blocked,
            ToGoal = toGoal,
            Spent = state.Spent,
            Steps = state.Steps,
            PendingStep = state.PendingStep,
            EnemyIndex = state.EnemyIndex,
            Enemies = Everyone.Where(member => state.Provokes && member.Side != actor.Side && !member.Defeated && member.Creature.Position is not null).ToList(),
            Selected = state.Selected,
            SelectedIndex = state.SelectedIndex,
            Explicit = state.Selected is not null,
        };
    }

    private List<PostRollCandidate> BuildPostRollCandidates(Definition check, Combatant by, Combatant against, CheckResult result)
    {
        if (!check.Json.TryGetProperty("post_roll", out JsonElement options))
        {
            return [];
        }

        List<PostRollCandidate> candidates = [];
        for (int index = 0; index < options.GetArrayLength(); index++)
        {
            JsonElement option = options[index];
            Definition track = _rules.Reference(check, $"$.post_roll[{index}].track");
            Scope optionScope = new(by.Creature, against.Creature, Check: result);
            decimal cost = Number(check, $"$.post_roll[{index}].cost", optionScope);
            decimal score = Number(check, $"$.post_roll[{index}].score", optionScope);
            if (cost <= 0 || _evaluator.TrackCurrent(by.Creature, track) < cost)
            {
                continue;
            }

            decimal? bonus = option.TryGetProperty("bonus", out _)
                ? Number(check, $"$.post_roll[{index}].bonus", optionScope)
                : null;
            bool reroll = option.TryGetProperty("reroll", out JsonElement rerollElement) && rerollElement.GetBoolean();
            string name = option.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString()!
                : reroll ? "Reroll" : $"+{bonus?.ToString("0.############", CultureInfo.InvariantCulture) ?? "0"}";
            candidates.Add(new PostRollCandidate(
                index,
                track,
                cost,
                score,
                bonus,
                reroll,
                name,
                $"post-roll:{by.Id}:{check.QualifiedId}:{index}"));
        }

        return candidates;
    }

    private void SuspendPostRoll(
        Definition check,
        Combatant by,
        Combatant against,
        CheckResult result,
        IReadOnlyList<PostRollCandidate> candidates,
        CheckOperationContext? checkContext)
    {
        CheckOperationContext context = checkContext
            ?? new CheckOperationContext(check, "$.check", new Scope(by.Creature, against.Creature), by, against, null, IsOperation: false);
        OperationFrame? operationFrame = context.IsOperation ? _operationFrames.LastOrDefault() : null;
        CombatOperationState operation = new(
            context.Owner.QualifiedId,
            context.Path,
            context.Actor.Id,
            context.Target?.Id,
            context.Source?.Id,
            _actionContinuation?.Use.Action.QualifiedId,
            PendingDamage: null,
            UseId: _actionContinuation is null ? null : UseId(_actionContinuation.Actor, _actionContinuation.Use),
            ListPath: operationFrame?.Path,
            Check: ToScopeCheck(context.Scope.Check),
            Outer: ToScopeCheck(context.Scope.Outer),
            ConditionValues: context.Scope.ConditionValues);
        CombatCheckState committed = new(
            check.QualifiedId,
            result.Roll,
            result.Bonus,
            result.Modifier,
            result.Total,
            result.Target,
            result.Margin,
            result.Success,
            result.Tier,
            null,
            _facts.FindLastIndex(fact => fact is CheckFact { Who: var who, Check: var name }
                && who == by.Name
                && name == check.Name),
            by.Id,
            against.Id);
        _pendingPostRollFrame = new PendingPostRollFrame
        {
            Check = check,
            By = by,
            Against = against,
            Initial = result,
            Candidates = candidates,
            Action = _actionContinuation,
            Operation = operation,
            Context = context,
            OperationFrameStart = _actionContinuation?.OperationFrameStart ?? 0,
        };
        _pendingOperation = operation;
        _operationStack = _operationStack.Count == 0 ? [operation] : _operationStack;
        _pendingDecision = new CombatDecision(
            $"post-roll:{by.Id}:{check.QualifiedId}:{_facts.Count}",
            CombatDecisionKind.PostRoll,
            by.Id,
            _round,
            [],
            [],
            false,
            operation.OwnerId,
            operation.Path,
            candidates.Select(candidate => new CombatDecisionOption(
                candidate.OptionId,
                candidate.Name,
                "post_roll",
                check.QualifiedId,
                null,
                candidate.Track.QualifiedId,
                candidate.Cost,
                candidate.Bonus,
                candidate.Reroll,
                candidate.Score,
                candidate.Index)).ToList(),
            null,
            committed,
            _actionContinuation?.Use.Action.QualifiedId);
        _phase = CombatPhase.AwaitingInterrupt;
        _suspending = true;
        throw new CombatSuspendedException();
    }

    private bool ShouldSuspendInitiative(Combatant? last, IReadOnlyList<Combatant> candidates)
    {
        if (_pendingInitiativeFrame is not null || !_combat.Json.TryGetProperty("initiative_score", out _))
        {
            return false;
        }

        Combatant? chooser = last ?? candidates.FirstOrDefault(member => member.Controller == CombatControlMode.Manual);
        if (chooser is null || chooser.Controller != CombatControlMode.Manual)
        {
            return false;
        }

        Dictionary<string, decimal> scores = candidates.ToDictionary(
            candidate => candidate.Id,
            candidate => Number(_combat, "$.initiative_score", new Scope(chooser.Creature, candidate.Creature)));
        CombatOperationState operation = new(
            _combat.QualifiedId,
            "$.initiative_score",
            chooser.Id,
            null,
            last?.Id ?? chooser.Id);
        _pendingInitiativeFrame = new PendingInitiativeFrame
        {
            Last = last,
            Chooser = chooser,
            Candidates = candidates,
            Scores = scores,
            Operation = operation,
        };
        CombatInterruptState interrupt = new(
            "initiative",
            chooser.Id,
            last?.Id ?? chooser.Id,
            OperationOwner: operation.OwnerId,
            OperationPath: operation.Path);
        _pendingOperation = operation;
        _operationStack = [operation];
        _pendingDecision = new CombatDecision(
            $"initiative:{chooser.Id}:{_round}:{_facts.Count}",
            CombatDecisionKind.Initiative,
            chooser.Id,
            _round,
            [],
            [],
            false,
            operation.OwnerId,
            operation.Path,
            candidates.Select(candidate => new CombatDecisionOption(
                $"initiative:{candidate.Id}",
                candidate.Name,
                "initiative",
                candidate.Id,
                candidate.Id,
                null,
                0,
                null,
                false,
                scores[candidate.Id])).ToList(),
            interrupt);
        _phase = CombatPhase.AwaitingInterrupt;
        _suspending = true;
        throw new CombatSuspendedException();
    }

    private CombatCommandResult SubmitDecision(CombatCommand.Decide command)
    {
        if (_pendingDecision is null || command.DecisionId != _pendingDecision.Id)
        {
            return CombatCommandResult.Refused("That decision is stale or belongs to another combat.", Observe());
        }

        return _pendingDecision.Kind switch
        {
            CombatDecisionKind.Interrupt => SubmitInterruptDecision(command),
            CombatDecisionKind.PostRoll => SubmitPostRollDecision(command),
            CombatDecisionKind.Initiative => SubmitInitiativeDecision(command),
            _ => CombatCommandResult.Refused("That decision cannot be answered here.", Observe()),
        };
    }

    private CombatCommandResult SubmitInterruptDecision(CombatCommand.Decide command)
    {
        PendingInterruptFrame frame = _pendingInterruptFrame
            ?? throw new InvalidOperationException("An interrupt decision has no continuation frame.");
        ReactionCandidate? selected = command.OptionId is null
            ? null
            : frame.Candidates.FirstOrDefault(candidate => candidate.OptionId == command.OptionId);
        if (command.OptionId is not null && selected is null)
        {
            return CombatCommandResult.Refused("That reaction is not an offered option.", Observe());
        }

        ClearPendingDecision();
        decimal damage = frame.PendingDamage;
        if (selected is not null && CanResumeReaction(frame.Reactor, frame.Source, selected.Reaction))
        {
            PendingDamage? previous = _pendingDamage;
            if (frame.Target is not null && frame.Trigger == "hit")
            {
                _pendingDamage = new PendingDamage(frame.Target, frame.Source, damage);
            }

            int previousDepth = _reactions;
            _reactions = previousDepth + 1;
            ParentInterruptFrame parent = new(frame, damage, _pendingDamage);
            _parentInterruptFrames.Add(parent);
            bool nested = false;
            try
            {
                Spend(frame.Reactor, selected.Reaction);
                Record(new ReactionFact(frame.Reactor.Name, selected.Reaction.Name, frame.Source.Name), subject: frame.Reactor, targets: [frame.Source]);
                Act(frame.Reactor, selected.Use, [selected.Use.Action.Json.GetProperty("target").GetString() == "self" ? frame.Reactor : frame.Source]);
                damage = _pendingDamage?.Amount ?? damage;
            }
            catch (CombatSuspendedException)
            {
                nested = true;
                throw;
            }
            finally
            {
                if (!nested)
                {
                    _parentInterruptFrames.RemoveAt(_parentInterruptFrames.Count - 1);
                }
                _reactions = previousDepth;
                _pendingDamage = previous;
            }
        }

        _pendingInterruptFrame = null;
        _suspending = false;
        if (frame.Trigger == "hit" && frame.Target is not null && frame.Track is not null)
        {
            ApplyResumedDamage(frame, damage);
            if (_phase != CombatPhase.AwaitingInterrupt && frame.Action is not null)
            {
                ResumeOperationFramesForAction(frame.Action);
            }

            if (_phase != CombatPhase.AwaitingInterrupt && frame.Action is not null)
            {
                ContinueRemainingTargets(frame.Action);
            }
        }
        else if (frame.Trigger == "targeted" && frame.Action is not null && !frame.Action.Actor.Defeated)
        {
            ContinueAction(frame.Action);
        }
        else if (frame.Trigger == "leaves_reach" && frame.Movement is not null && !frame.Movement.Actor.Defeated)
        {
            ResumeMovement(frame.Movement);
            if (_phase != CombatPhase.AwaitingInterrupt && frame.Action is not null)
            {
                ResumeOperationFramesForAction(frame.Action);
                if (_phase != CombatPhase.AwaitingInterrupt)
                {
                    ContinueRemainingTargets(frame.Action);
                }
            }
        }
        else if (frame.Trigger is "damaged" or "ally_defeated"
            && frame.Action is not null
            && !frame.Action.Actor.Defeated)
        {
            // These triggers fire after the current operation has already
            // applied. Resume at the next target; rerunning the action would
            // duplicate the operation that caused the reaction.
            ResumeOperationFramesForAction(frame.Action);
            if (_phase != CombatPhase.AwaitingInterrupt)
            {
                ContinueRemainingTargets(frame.Action);
            }
        }

        if (_phase != CombatPhase.AwaitingInterrupt)
        {
            _damageContinuation = null;
            _actionContinuation = null;
            _movementContinuation = null;
            _pendingOperation = null;
            _operationStack = [];
        }

        ResumeParentInterruptIfReady();

        if (_phase == CombatPhase.AwaitingInterrupt)
        {
            return CombatCommandResult.AcceptedResult(Observe());
        }

        return CompleteSuspendedAction();
    }

    private CombatCommandResult SubmitPostRollDecision(CombatCommand.Decide command)
    {
        PendingPostRollFrame frame = _pendingPostRollFrame
            ?? throw new InvalidOperationException("A post-roll decision has no continuation frame.");
        PostRollCandidate? selected = command.OptionId is null
            ? null
            : frame.Candidates.FirstOrDefault(candidate => candidate.OptionId == command.OptionId);
        if (command.OptionId is not null && selected is null)
        {
            return CombatCommandResult.Refused("That post-roll option is not offered.", Observe());
        }

        ClearPendingDecision();
        _pendingPostRollFrame = null;
        _suspending = false;
        CheckResult result = selected is null ? frame.Initial : ApplyPostRollOption(frame.Check, frame.By, frame.Against, frame.Initial, selected);
        if (frame.Context.IsOperation)
        {
            MarkSuccessfulSkill(frame.Check, frame.By, result);
            ResumeCheckOperation(frame.Context, result);
            if (!_suspending)
            {
                ResumeOperationFrames(frame.OperationFrameStart);
                if (!_suspending && frame.Action is not null)
                {
                    ContinueRemainingTargets(frame.Action);
                }
            }
        }
        else if (frame.Action is not null)
        {
            ContinueAction(frame.Action, result);
        }

        if (!_suspending && frame.Context.IsOperation)
        {
            _actionContinuation = null;
            _pendingOperation = null;
            _operationStack = [];
        }

        return CompleteSuspendedAction();
    }

    private void MarkSuccessfulSkill(Definition check, Combatant by, CheckResult result)
    {
        if (result.Success && by.Character is Character character && check.Json.TryGetProperty("skill", out _))
        {
            CharacterRules.MarkSkillUse(_rules, character, check.Json.GetProperty("skill").GetString()!, []);
        }
    }

    private void ResumeCheckOperation(CheckOperationContext context, CheckResult result)
    {
        JsonElement operation = JsonAtPath(context.Owner.Json, context.Path);
        if (!operation.TryGetProperty("outcomes", out JsonElement outcomes)
            || !outcomes.TryGetProperty(result.Tier, out JsonElement operations))
        {
            return;
        }

        RunOperations(
            context.Owner,
            operations,
            $"{context.Path}.outcomes.{result.Tier}",
            context.Scope with { Check = result, Outer = context.Scope.Check },
            context.Actor,
            context.Target,
            context.Source);
    }

    private CombatCommandResult SubmitInitiativeDecision(CombatCommand.Decide command)
    {
        PendingInitiativeFrame frame = _pendingInitiativeFrame
            ?? throw new InvalidOperationException("An initiative decision has no continuation frame.");
        Combatant? selected = command.OptionId is null
            ? frame.Candidates.FirstOrDefault()
            : frame.Candidates.FirstOrDefault(candidate => $"initiative:{candidate.Id}" == command.OptionId);
        if (selected is null || (command.OptionId is not null && !frame.Candidates.Contains(selected)))
        {
            return CombatCommandResult.Refused("That initiative option is not offered.", Observe());
        }

        ClearPendingDecision();
        _pendingInitiativeFrame = null;
        _suspending = false;
        decimal score = frame.Scores[selected.Id];
        string who = frame.Last?.Name ?? frame.Chooser.Name;
        Record(new InitiativeChoiceFact(who, selected.Name, score), subject: frame.Last ?? frame.Chooser, targets: [selected]);
        _lastActor = selected;
        _lastActorId = selected.Id;
        _resumedInitiative = selected;
        return CombatCommandResult.AcceptedResult(Advance());
    }

    private void ClearPendingDecision()
    {
        _pendingDecision = null;
        _pendingOperation = null;
        _operationStack = [];
        _phase = CombatPhase.Advancing;
    }

    private bool CanResumeReaction(Combatant reactor, Combatant source, Definition reaction)
    {
        // The reaction's `when` was evaluated while the option was offered.
        // Re-evaluating it here would consume dice again and could refuse a
        // choice the caller just accepted. Stable state checks still protect
        // a defeated reactor or an already-spent budget.
        return !reactor.Defeated && reactor != source && Affordable(reactor, reaction);
    }

    private void ApplyResumedDamage(PendingInterruptFrame frame, decimal amount)
    {
        Combatant target = frame.Target!;
        Definition track = frame.Track!;
        Combatant source = frame.Source;
        TrackValue value = target.Creature.Track(track.Id);
        decimal current = value.Current ?? 0;
        decimal lowered = current - amount;
        if (_evaluator.TrackMin(target.Creature, track) is decimal floor && lowered < floor)
        {
            lowered = Math.Min(current, floor);
        }

        value.Current = lowered;
        if (frame.PendingRolls is IReadOnlyList<DiceRoll> committed)
        {
            Record(
                new DamageFact(target.Name, track, current - lowered, lowered),
                frame.RollsBefore,
                source,
                [target],
                committed);
        }
        else
        {
            Record(new DamageFact(target.Name, track, current - lowered, lowered), frame.RollsBefore, source, [target]);
        }
        bool fell = CheckDefeated(target);
        if (amount > 0 && target != source && target.Side != source.Side)
        {
            React("damaged", target, source);
        }

        if (fell && target.Side != source.Side)
        {
            foreach (Combatant ally in Everyone.Where(member => member.Side == target.Side && member != target).ToList())
            {
                React("ally_defeated", ally, source);
            }
        }
    }

    private void ResumeMovement(MovementContinuation movement)
    {
        if (movement.PendingStep is Cell step)
        {
            int nextEnemy = movement.EnemyIndex + 1;
            movement.Here = step;
            movement.Spent += _field!.Cost(step);
            movement.Steps++;
            movement.PendingStep = null;
            movement.EnemyIndex = movement.Explicit ? 0 : nextEnemy;
            if (movement.Explicit)
            {
                movement.SelectedIndex++;
            }
        }

        if (movement.Explicit)
        {
            ContinueExplicitMovement(movement);
        }
        else
        {
            ContinueMovement(movement);
        }
    }

    private void ContinueMovement(MovementContinuation movement)
    {
        _movementContinuation = movement;
        while (!movement.Actor.Defeated
            && (movement.Away
                ? movement.Beyond is not decimal far || _field!.Distance(movement.Here, movement.Target.Creature.Position!.Value) < far
                : !(_field!.Distance(movement.Here, movement.Target.Creature.Position!.Value) <= movement.Within
                    && _field.CanSee(movement.Here, movement.Target.Creature.Position!.Value))))
        {
            Cell goal = movement.Target.Creature.Position!.Value;
            Cell? next = movement.Away
                ? StepAway(movement.Here, goal, movement.Blocked)
                : StepToward(movement.Here, movement.ToGoal!);
            if (next is not Cell step || movement.Spent + _field!.Cost(step) > movement.Allowed)
            {
                break;
            }

            movement.PendingStep = step;
            for (; movement.EnemyIndex < movement.Enemies.Count; movement.EnemyIndex++)
            {
                Combatant enemy = movement.Enemies[movement.EnemyIndex];
                if (enemy.Defeated || enemy.Creature.Position is not Cell watcher)
                {
                    continue;
                }

                Cell from = movement.Here;
                React("leaves_reach", enemy, movement.Actor, reaction =>
                {
                    decimal reach = reaction.Json.TryGetProperty("reach", out _)
                        ? Number(reaction, "$.reach", new Scope(enemy.Creature, null))
                        : 1;
                    return _field!.Distance(watcher, from) <= reach && _field.Distance(watcher, step) > reach;
                });
            }

            if (movement.Actor.Defeated)
            {
                break;
            }

            movement.Here = step;
            movement.Spent += _field!.Cost(step);
            movement.Steps++;
            movement.PendingStep = null;
            movement.EnemyIndex = 0;
        }

        if (movement.Steps > 0)
        {
            movement.Actor.Creature.Position = movement.Here;
            Record(new MoveFact(movement.Actor.Name, movement.Start, movement.Here, movement.Steps), subject: movement.Actor);
        }

        if (movement.Escape && !movement.Actor.Defeated && movement.Spent < movement.Allowed
            && IsEdge(movement.Here) && StepAway(movement.Here, movement.Target.Creature.Position!.Value, movement.Blocked) is null)
        {
            Escape(movement.Actor);
        }

        _movementContinuation = null;
    }

    private void ContinueExplicitMovement(MovementContinuation movement)
    {
        _movementContinuation = movement;
        IReadOnlyList<Cell> selected = movement.Selected!;
        while (!movement.Actor.Defeated && movement.SelectedIndex < selected.Count)
        {
            Cell step = selected[movement.SelectedIndex];
            if (!_field!.Neighbours(movement.Here).Contains(step)
                || !_field.Passable(step)
                || movement.Blocked.Contains(step)
                || movement.Spent + _field.Cost(step) > movement.Allowed)
            {
                _movementContinuation = null;
                return;
            }

            movement.PendingStep = step;
            for (; movement.EnemyIndex < movement.Enemies.Count; movement.EnemyIndex++)
            {
                Combatant enemy = movement.Enemies[movement.EnemyIndex];
                if (enemy.Defeated || enemy.Creature.Position is not Cell watcher)
                {
                    continue;
                }

                Cell from = movement.Here;
                React("leaves_reach", enemy, movement.Actor, reaction =>
                {
                    decimal reach = reaction.Json.TryGetProperty("reach", out _)
                        ? Number(reaction, "$.reach", new Scope(enemy.Creature, null))
                        : 1;
                    return _field!.Distance(watcher, from) <= reach && _field.Distance(watcher, step) > reach;
                });
            }

            if (movement.Actor.Defeated)
            {
                break;
            }

            movement.Here = step;
            movement.Spent += _field.Cost(step);
            movement.Steps++;
            movement.PendingStep = null;
            movement.SelectedIndex++;
            movement.EnemyIndex = 0;
        }

        if (movement.Steps > 0)
        {
            movement.Actor.Creature.Position = movement.Here;
            Record(new MoveFact(movement.Actor.Name, movement.Start, movement.Here, movement.Steps), subject: movement.Actor);
        }

        if (!movement.Actor.Defeated
            && !MovementDestinationAllowedForCommit(movement.Here, movement.Target.Creature.Position!.Value, movement.Within, movement.Beyond, movement.Away))
        {
            _movementContinuation = null;
            return;
        }

        if (movement.Escape && !movement.Actor.Defeated && movement.Spent < movement.Allowed
            && IsEdge(movement.Here) && StepAway(movement.Here, movement.Target.Creature.Position!.Value, movement.Blocked) is null)
        {
            Escape(movement.Actor);
        }

        _movementContinuation = null;
    }

    private void ContinueAction(ActionContinuation action, CheckResult? checkedResult = null)
    {
        _actionContinuation = action;
        CombatOperationState operation = new(
            action.Use.Action.QualifiedId,
            "$.action",
            action.Actor.Id,
            action.Target.Id,
            action.Actor.Id,
            action.Use.Action.QualifiedId,
            UseId: UseId(action.Actor, action.Use),
            TargetIds: action.Targets?.Select(member => member.Id).ToList(),
            TargetIndex: action.TargetIndex,
            AlreadyPaid: action.AlreadyPaid);
        _pendingOperation = operation;
        _operationStack = [operation];
        try
        {
            Scope scope = action.Scope;
            CheckResult? result = checkedResult;
            if (result is null && action.Use.Action.Json.TryGetProperty("check", out _))
            {
                decimal extra = action.Use.Action.Json.TryGetProperty("check_bonus", out _)
                    ? Number(action.Use.Action, "$.check_bonus", scope)
                    : 0;
                result = MakeCheck(
                    _rules.Reference(action.Use.Action, "$.check"),
                    action.Actor,
                    action.Target,
                    extra,
                    new CheckOperationContext(
                        action.Use.Action,
                        "$.check",
                        scope,
                        action.Actor,
                        action.Target,
                        null,
                        IsOperation: false));
            }

            if (result is not null
                && action.Use.Action.Json.TryGetProperty("outcomes", out JsonElement outcomes)
                && outcomes.TryGetProperty(result.Tier, out JsonElement operations))
            {
                RunOperations(action.Use.Action, operations, $"$.outcomes.{result.Tier}", scope with { Check = result }, action.Actor, action.Target);
            }

            if (action.Use.Action.Json.TryGetProperty("always", out JsonElement always))
            {
                RunOperations(action.Use.Action, always, "$.always", scope, action.Actor, action.Target);
            }

            if (result is CheckResult successful && successful.Success && action.Actor.Character is Character character && action.Use.Action.Json.TryGetProperty("check", out _))
            {
                Definition check = _rules.Reference(action.Use.Action, "$.check");
                if (check.Json.TryGetProperty("skill", out _))
                {
                    CharacterRules.MarkSkillUse(_rules, character, check.Json.GetProperty("skill").GetString()!, []);
                }
            }

            ContinueRemainingTargets(action);
        }
        finally
        {
            if (!_suspending)
            {
                _pendingOperation = null;
                _operationStack = [];
                _actionContinuation = null;
            }
        }
    }

    private void ContinueRemainingTargets(ActionContinuation action)
    {
        if (action.Targets is not IReadOnlyList<Combatant> targets)
        {
            return;
        }

        for (int index = action.TargetIndex + 1; index < targets.Count; index++)
        {
            Combatant target = targets[index];
            if (target.Defeated && target != action.Actor && action.Use.Action.Json.GetProperty("target").GetString() != "fallen_ally")
            {
                continue;
            }

            SetActionTargetContinuation(action.Actor, action.Use, targets, index, action.AlreadyPaid);
            if (!Resolve(action.Actor, action.Use, target))
            {
                return;
            }
        }
    }

    private void ResumeParentInterruptIfReady()
    {
        if (_phase == CombatPhase.AwaitingInterrupt || _parentInterruptFrames.Count == 0)
        {
            return;
        }

        ParentInterruptFrame parent = _parentInterruptFrames[^1];
        _parentInterruptFrames.RemoveAt(_parentInterruptFrames.Count - 1);
        PendingInterruptFrame frame = parent.Frame;
        decimal amount = parent.Damage?.Amount ?? parent.Amount;
        if (frame.Trigger == "hit" && frame.Target is not null && frame.Track is not null)
        {
            ApplyResumedDamage(frame, amount);
            if (_phase != CombatPhase.AwaitingInterrupt && frame.Action is not null)
            {
                ResumeOperationFramesForAction(frame.Action);
            }

            if (_phase != CombatPhase.AwaitingInterrupt && frame.Action is not null)
            {
                ContinueRemainingTargets(frame.Action);
            }
        }
        else if (frame.Trigger == "targeted" && frame.Action is not null && !frame.Action.Actor.Defeated)
        {
            ContinueAction(frame.Action);
        }
        else if (frame.Trigger == "leaves_reach" && frame.Movement is not null && !frame.Movement.Actor.Defeated)
        {
            ResumeMovement(frame.Movement);
            if (_phase != CombatPhase.AwaitingInterrupt && frame.Action is not null)
            {
                ResumeOperationFramesForAction(frame.Action);
                if (_phase != CombatPhase.AwaitingInterrupt)
                {
                    ContinueRemainingTargets(frame.Action);
                }
            }
        }
        else if (frame.Trigger is "damaged" or "ally_defeated"
            && frame.Action is not null
            && !frame.Action.Actor.Defeated)
        {
            ResumeOperationFramesForAction(frame.Action);
            if (_phase != CombatPhase.AwaitingInterrupt)
            {
                ContinueRemainingTargets(frame.Action);
            }
        }
    }

    private CombatCommandResult CompleteSuspendedAction()
    {
        if (_activeActor is null)
        {
            _phase = CombatPhase.Advancing;
            return CombatCommandResult.AcceptedResult(Advance());
        }

        if (_activeActor.Controller == CombatControlMode.Automatic)
        {
            CommitPendingBehavior();
            if (!_activeActor.Defeated)
            {
                ResolveAutomaticTurn(_activeActor);
            }

            FinishCurrentTurn();
            _phase = CombatPhase.Advancing;
            return CombatCommandResult.AcceptedResult(Advance());
        }

        return CombatCommandResult.AcceptedResult(AdvanceAfterAction());
    }

    private CheckResult ApplyPostRollOption(Definition check, Combatant by, Combatant against, CheckResult result, PostRollCandidate selected)
    {
        TrackValue resource = by.Creature.Track(selected.Track.Id);
        resource.Current = _evaluator.TrackCurrent(by.Creature, selected.Track) - selected.Cost;
        Record(new SpentFact(by.Name, selected.Track, selected.Cost, resource.Current.Value), subject: by);
        decimal before = result.Total;
        int rollsBefore = _dice.Rolls.Count;
        CheckResult changed;
        string effect;
        if (selected.Reroll)
        {
            changed = Located(check, $"$.post_roll[{selected.Index}]", () =>
            {
                decimal roll = _evaluator.Roll(check, by.Creature, against.Creature);
                return _evaluator.ResolveCheck(check, by.Creature, against.Creature, roll, result.Bonus, result.Modifier, result.Target);
            });
            effect = "reroll";
        }
        else
        {
            decimal bonus = selected.Bonus ?? 0;
            changed = Located(check, $"$.post_roll[{selected.Index}].bonus", () => _evaluator.ResolveCheck(
                check,
                by.Creature,
                against.Creature,
                result.Roll,
                result.Bonus,
                result.Modifier + bonus,
                result.Target));
            effect = $"+{bonus.ToString("0.############", CultureInfo.InvariantCulture)}";
        }

        Record(new PostRollFact(by.Name, check.Name, selected.Name, selected.Track, selected.Cost, effect, before, changed.Total), rollsBefore, by, [against]);
        int fact = _facts.FindLastIndex(entry => entry is CheckFact { Who: var who, Check: var name } && who == by.Name && name == check.Name);
        if (fact >= 0 && _facts[fact] is CheckFact original)
        {
            IReadOnlyList<DiceRoll> rolls = [.. original.Rolls, .. _dice.Rolls.Skip(rollsBefore)];
            _facts[fact] = original with { Result = changed, Rolls = rolls };
        }

        return changed;
    }

    private static CombatCheckState ToCheckState(Definition check, CheckResult result) => new(
        check.QualifiedId,
        result.Roll,
        result.Bonus,
        result.Modifier,
        result.Total,
        result.Target,
        result.Margin,
        result.Success,
        result.Tier);

    private bool TakeResumedInitiative(out Combatant? actor)
    {
        actor = _resumedInitiative;
        _resumedInitiative = null;
        return actor is not null;
    }

}
