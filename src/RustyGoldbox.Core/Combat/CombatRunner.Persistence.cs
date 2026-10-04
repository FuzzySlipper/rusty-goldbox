using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

/// <summary>Captures and restores a live combat without replaying its setup.</summary>
public sealed partial class CombatRunner
{
    /// <summary>
    /// Takes a primitive snapshot of the live owner at its current boundary.
    /// The snapshot contains no evaluator, delegate or random-stream handle;
    /// callers can serialize it with <see cref="CombatContinuationState.ToJson"/>.
    /// </summary>
    public CombatContinuationState Capture()
    {
        CombatContinuationState state = new()
        {
            CombatId = _combat.QualifiedId,
            MaxRounds = _maxRounds,
            Phase = _phase,
            Round = _round,
            CombatInitialized = _combatInitialized,
            RoundOpen = _roundOpen,
            TurnPrepared = _turnPrepared,
            TurnIndex = _turnIndex,
            ActiveActorId = _activeActor?.Id,
            LastActorId = _lastActor?.Id ?? _lastActorId,
            FledSide = _fledSide,
            Winner = Winner(),
            TurnOrder = _order.Select(member => member.Id).ToList(),
            TookTurns = _tookTurns.Select(member => member.Id).ToList(),
            PendingDecision = _pendingDecision,
            CommittedActionId = _pendingTargetUse is null ? null : _pendingDecision?.ActionId,
            CommittedActorId = _pendingTargetActor?.Id,
            CommittedMaximumTargets = _pendingTargetCap,
            CommittedTargetRollStart = _pendingTargetRollStart,
            CommittedTargetRolls = _pendingTargetRolls.ToList(),
            ReactionDepth = _reactions,
            RandomScope = _dice.RandomScope,
            NextRandomKey = _dice.NextRandomKey,
            Facts = _facts.Select(fact => new CombatFactState(fact.Kind, fact.Describe(), fact.Rolls, fact.SubjectIds, fact.TargetIds)).ToList(),
        };

        foreach (Combatant member in Everyone)
        {
            state.Controllers[member.Id] = member.Controller;
            state.Combatants.Add(Capture(member));
        }

        CaptureBehaviorContinuation(state);
        CaptureInterruptContinuation(state);

        return state;
    }

    /// <summary>Returns the terminal result over the same live sides and facts.</summary>
    public CombatResult Result()
    {
        if (_phase != CombatPhase.Ended)
        {
            throw new InvalidOperationException("A live combat has not ended.");
        }

        return new CombatResult(
            _facts,
            Winner(),
            _round,
            _sides,
            _track,
            _fledSide,
            CollectBehaviorTraces ? _behaviorTraces.ToArray() : null);
    }

    /// <summary>The initialized sides, including their stable IDs and positions.</summary>
    public IReadOnlyList<CombatSide> Sides => _sides;

    /// <summary>
    /// Restores a live owner from a snapshot. The supplied sides must contain
    /// the same stable combatant IDs; they are already constructed by the
    /// caller, so this method never spawns, deploys, starts tracks, surprises
    /// or rolls the fight again.
    /// </summary>
    public static CombatRunner Restore(
        RuleSet rules,
        Definition combat,
        IReadOnlyList<CombatSide> sides,
        DiceRoller dice,
        CombatContinuationState continuation,
        Definition? encounter = null,
        CombatSetup? setup = null)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        CombatRunner runner = new(rules, combat, sides, dice, encounter, setup, initialize: false);
        runner.RestoreContinuation(continuation);
        return runner;
    }

    /// <summary>Applies a snapshot after the no-initialization constructor ran.</summary>
    private void RestoreContinuation(CombatContinuationState state)
    {
        if (state.Format != 1)
        {
            throw new ArgumentException($"Combat continuation format {state.Format} is not supported.", nameof(state));
        }

        if (!string.IsNullOrEmpty(state.CombatId) && !string.Equals(state.CombatId, _combat.QualifiedId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Combat continuation belongs to '{state.CombatId}', not '{_combat.QualifiedId}'.", nameof(state));
        }

        if (state.MaxRounds < 1)
        {
            throw new ArgumentException("Combat continuation max rounds must be positive.", nameof(state));
        }

        if (state.RandomScope is string scope)
        {
            if (!_dice.IsKeyed || !string.Equals(_dice.RandomScope, scope, StringComparison.Ordinal) || _dice.NextRandomKey != state.NextRandomKey)
            {
                throw new ArgumentException("Combat continuation random scope/cursor does not match the supplied keyed dice roller.", nameof(state));
            }
        }
        else if (_dice.IsKeyed)
        {
            throw new ArgumentException("A legacy combat continuation cannot be restored with a keyed dice roller.", nameof(state));
        }

        Dictionary<string, Combatant> members = Everyone.ToDictionary(member => member.Id, StringComparer.Ordinal);
        if (members.Count != Everyone.Count())
        {
            throw new ArgumentException("Combat sides contain duplicate stable combatant IDs.", nameof(state));
        }

        foreach (CombatantState saved in state.Combatants)
        {
            if (!members.TryGetValue(saved.Id, out Combatant? member))
            {
                throw new ArgumentException($"Combat continuation names unknown combatant '{saved.Id}'.", nameof(state));
            }

            Restore(member, saved);
        }

        if (state.Combatants.Count != members.Count)
        {
            throw new ArgumentException("Combat continuation does not contain every combatant.", nameof(state));
        }

        _maxRounds = state.MaxRounds;
        _round = state.Round;
        _started = state.Phase != CombatPhase.NotStarted;
        _combatInitialized = state.CombatInitialized;
        _roundOpen = state.RoundOpen;
        _turnPrepared = state.TurnPrepared;
        _turnIndex = state.TurnIndex;
        _phase = state.Phase;
        _fledSide = state.FledSide;
        _reactions = state.ReactionDepth;
        _pendingDecision = state.PendingDecision;
        _activeActor = FindSaved(members, state.ActiveActorId);
        _turn = _activeActor;
        _lastActor = FindSaved(members, state.LastActorId);
        _lastActorId = state.LastActorId;
        _order = state.TurnOrder.Select(id => FindSaved(members, id)!).ToList();
        _tookTurns = state.TookTurns.Select(id => FindSaved(members, id)!).ToHashSet();
        _facts.Clear();
        foreach (CombatFactState fact in state.Facts)
        {
            RestoredCombatFact restored = new(fact.Kind, fact.Description)
            {
                Rolls = fact.Rolls,
                SubjectIds = fact.SubjectIds,
                TargetIds = fact.TargetIds,
            };
            _facts.Add(restored);
        }
        RestoreCommittedCheckFact(state);

        ClearPendingTargetSelection();
        if (state.CommittedActorId is string actorId
            && state.CommittedActionId is string actionId
            && members.TryGetValue(actorId, out Combatant? actor)
            && FindUse(actor, actionId) is UseOption use)
        {
            _pendingTargetActor = actor;
            _pendingTargetUse = use;
            _pendingTargetCandidates = Targets(actor, use, preview: true);
            _pendingTargetCap = state.CommittedMaximumTargets;
            _pendingTargetRollStart = state.CommittedTargetRollStart is long rollStart ? (int)rollStart : null;
            _pendingTargetRolls = state.CommittedTargetRolls ?? [];
            _pendingTargetRollsRestored = true;
        }
        RestoreInterruptContinuation(state);
        RestoreBehaviorContinuation(state);
        _previewOwners.Clear();
        SetCombatMoment(_round);
    }

    private void RestoreCommittedCheckFact(CombatContinuationState state)
    {
        CombatCheckState? saved = state.PendingCheck;
        if (saved?.FactIndex is not int index || index < 0 || index >= _facts.Count || _facts[index].Kind != "check")
        {
            return;
        }

        string? actorId = state.PendingDecision?.ActorId ?? state.PendingOperation?.ActorId;
        Combatant? actor = actorId is null ? null : Find(actorId);
        Definition? check = _rules.Find(DefinitionTypes.Check, saved.CheckId, out _);
        if (actor is null || check is null || _facts[index] is not RestoredCombatFact restored)
        {
            return;
        }

        CheckResult result = new(saved.Roll, saved.Bonus, saved.Modifier, saved.Total, saved.Target, saved.Margin, saved.Success, saved.Tier);
        _facts[index] = new CheckFact(actor.Name, check.Name, result)
        {
            Rolls = state.PendingCheckRolls.Count > 0
                ? state.PendingCheckRolls
                : saved.Rolls ?? restored.Rolls,
            SubjectIds = restored.SubjectIds,
            TargetIds = restored.TargetIds,
        };
    }

    private static Combatant? FindSaved(IReadOnlyDictionary<string, Combatant> members, string? id)
    {
        return id is null ? null : members.TryGetValue(id, out Combatant? member)
            ? member
            : throw new ArgumentException($"Combat continuation names unknown combatant '{id}'.", nameof(id));
    }

    private static CombatantState Capture(Combatant member)
    {
        CombatantState state = new()
        {
            Id = member.Id,
            Side = member.Side,
            Defeated = member.Defeated,
            Escaped = member.Escaped,
            SurprisedRounds = member.SurprisedRounds,
            Position = member.Creature.Position,
            Controller = member.Controller,
            Budget = member.Budget.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            Values = member.Creature.Values.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            Rolled = member.Creature.Rolled.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            Preparing = member.Preparing.Select(spell => spell.QualifiedId).ToList(),
            Prepared = member.Prepared.Select(spell => spell.QualifiedId).ToList(),
            CastsLeft = member.CastsLeft.ToDictionary(entry => entry.Key.QualifiedId, entry => entry.Value, StringComparer.Ordinal),
            AppliedThisTurn = member.AppliedThisTurn.Select(condition => condition.QualifiedId).ToList(),
        };

        foreach ((string id, TrackValue value) in member.Creature.Tracks)
        {
            state.Tracks[id] = value.Current is decimal current ? current : null;
            if (value.Max is decimal maximum)
            {
                state.TrackMaximums[id] = maximum;
            }
        }

        foreach (Definition condition in member.Creature.Conditions)
        {
            member.ConditionRounds.TryGetValue(condition, out decimal rounds);
            Dictionary<string, decimal> values = member.Creature.ConditionValues.TryGetValue(condition, out Dictionary<string, decimal>? conditionValues)
                ? new(conditionValues, StringComparer.Ordinal)
                : [];
            state.Conditions.Add(new CombatConditionState(
                condition.QualifiedId,
                member.ConditionRounds.ContainsKey(condition) ? rounds : null,
                values));
        }

        return state;
    }

    private void Restore(Combatant member, CombatantState state)
    {
        if (member.Id != state.Id)
        {
            throw new ArgumentException($"Combat continuation ID '{state.Id}' does not match '{member.Id}'.", nameof(state));
        }

        member.Side = state.Side;
        member.Defeated = state.Defeated;
        member.Escaped = state.Escaped;
        member.SurprisedRounds = state.SurprisedRounds;
        member.Controller = state.Controller;

        member.Budget.Clear();
        foreach ((string id, int value) in state.Budget)
        {
            member.Budget[id] = value;
        }

        member.Creature.Values.Clear();
        foreach ((string id, decimal value) in state.Values)
        {
            member.Creature.Values[id] = value;
        }

        member.Creature.Rolled.Clear();
        foreach ((string id, int value) in state.Rolled)
        {
            member.Creature.Rolled[id] = value;
        }

        member.Creature.Tracks.Clear();
        foreach ((string id, decimal? current) in state.Tracks)
        {
            TrackValue track = member.Creature.Track(id);
            track.Current = current;
            track.Max = state.TrackMaximums.TryGetValue(id, out decimal maximum) ? maximum : track.Max;
        }

        member.Creature.Position = state.Position;
        member.Creature.Conditions.Clear();
        member.Creature.ConditionValues.Clear();
        member.ConditionRounds.Clear();
        foreach (CombatConditionState condition in state.Conditions)
        {
            Definition definition = Resolve(DefinitionTypes.Condition, condition.ConditionId);
            member.Creature.Conditions.Add(definition);
            if (condition.Rounds is decimal rounds)
            {
                member.ConditionRounds[definition] = rounds;
            }

            member.Creature.ConditionValues[definition] = new Dictionary<string, decimal>(condition.Values, StringComparer.Ordinal);
        }

        member.AppliedThisTurn.Clear();
        foreach (string id in state.AppliedThisTurn)
        {
            member.AppliedThisTurn.Add(Resolve(DefinitionTypes.Condition, id));
        }

        member.Preparing.Clear();
        foreach (string id in state.Preparing)
        {
            member.Preparing.Add(ResolveSpell(id));
        }

        member.Prepared.Clear();
        foreach (string id in state.Prepared)
        {
            member.Prepared.Add(ResolveSpell(id));
        }

        member.CastsLeft.Clear();
        foreach ((string id, int count) in state.CastsLeft)
        {
            member.CastsLeft[ResolveSpell(id)] = count;
        }
    }

    private Definition Resolve(DefinitionType type, string id)
    {
        return _rules.Find(type, id, out string? problem)
            ?? throw new ArgumentException(problem ?? $"Unknown {type.Name} '{id}'.", nameof(id));
    }

    private Definition ResolveSpell(string id)
    {
        return Resolve(DefinitionTypes.Spell, id);
    }
}
