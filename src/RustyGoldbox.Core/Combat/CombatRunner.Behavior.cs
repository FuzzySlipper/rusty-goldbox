using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

/// <summary>
/// The live runner's bridge to data-authored combat behavior. The controller
/// only proposes a command; the runner still sends that command through the
/// same action resolver used by manual and CLI callers.
/// </summary>
public sealed partial class CombatRunner
{
    private CombatBehaviorController? _behaviorController;
    private CombatBehaviorTrace? _lastBehaviorTrace;
    private readonly List<CombatBehaviorTrace> _behaviorTraces = [];
    private bool _resolvingBehavior;
    private HashSet<string>? _behaviorRejectedActionIds;
    private string? _pendingBehaviorActorId;
    private string? _pendingBehaviorId;
    private int? _pendingBehaviorRuleIndex;
    private int? _pendingBehaviorStepIndex;
    private bool _pendingBehaviorMovementOnly;

    /// <summary>The controller attached to this live combat.</summary>
    public CombatBehaviorController BehaviorController => EnsureBehaviorController();

    /// <summary>The most recent authored proposal, when one was made.</summary>
    public CombatBehaviorTrace? LastBehaviorTrace => _lastBehaviorTrace;

    /// <summary>Whether actual authored proposals are retained for inspection.</summary>
    public bool CollectBehaviorTraces { get; set; }

    /// <summary>Actual authored proposals retained since this runner was created.</summary>
    public IReadOnlyList<CombatBehaviorTrace> BehaviorTraces => _behaviorTraces;

    private CombatBehaviorController EnsureBehaviorController()
    {
        if (_behaviorController is null)
        {
            _behaviorController = new CombatBehaviorController(_rules, _combat, _field);
            _behaviorController.Register(Everyone);
        }

        return _behaviorController;
    }

    private bool HasBehavior(Combatant actor) => EnsureBehaviorController().ProfileFor(actor) is not null;

    private void InitializeBehaviorController()
    {
        _ = EnsureBehaviorController();
    }

    private void ResolveBehaviorAutomaticTurn(Combatant actor)
    {
        CombatBehaviorController controller = EnsureBehaviorController();
        _resolvingBehavior = true;
        _behaviorRejectedActionIds = [];
        try
        {
            if (_pendingDecision is null || _pendingDecision.ActorId != actor.Id || _pendingDecision.Kind != CombatDecisionKind.Action)
            {
                _pendingDecision = BuildDecision(actor);
                _phase = CombatPhase.AwaitingAction;
            }

            bool retriedFallback = false;
            while (!actor.Defeated && StandingSides() > 1 && _activeActor == actor)
            {
                if (_phase != CombatPhase.AwaitingAction || _pendingDecision is null || _pendingDecision.ActorId != actor.Id)
                {
                    break;
                }

                CombatBehaviorProposal? proposal = controller.Propose(actor, Observe());
                if (proposal is null)
                {
                    break;
                }

                RememberBehaviorTrace(proposal.Trace);
                if (proposal.Command is CombatCommand.UseAction command)
                {
                    CombatDecision? decisionBefore = _pendingDecision;
                    CombatCommandResult result;
                    try
                    {
                        RememberPendingBehaviorCommit(proposal);
                        result = SubmitAction(command);
                    }
                    catch (CombatSuspendedException)
                    {
                        // The action has passed the shared resolver's checks and
                        // its committed plan must advance after the optional
                        // reaction or post-roll choice resumes it.
                        RecordBehaviorTrace();
                        throw;
                    }

                    ClearPendingBehaviorCommit();
                    bool sameActionDecision = decisionBefore is CombatDecision before
                        && before.Kind == CombatDecisionKind.Action
                        && result.Observation.PendingDecision is CombatDecision after
                        && after.Kind == CombatDecisionKind.Action
                        && after.Id == before.Id
                        && after.ActorId == actor.Id;
                    bool retainedPriceRefusal = !result.Accepted
                        && sameActionDecision
                        && IsCommittedSpellPriceUnavailable(actor, command.ActionId);
                    if (sameActionDecision && (result.Accepted || retainedPriceRefusal))
                    {
                        if (retainedPriceRefusal)
                        {
                            _behaviorRejectedActionIds?.Add(command.ActionId);
                            RemovePendingActionChoice(command.ActionId);
                        }

                        // A legal command committed a price but did not
                        // complete the authored step. Keep the behavior state
                        // uncommitted so Propose can reassess the remaining
                        // choices and its authored fallback without spending
                        // or rerolling the retained price.
                        RecordBehaviorTrace();
                        retriedFallback = false;
                        continue;
                    }

                    if (!result.Accepted)
                    {
                        RememberBehaviorRefusal(proposal, result.Reason);
                        RecordBehaviorTrace();
                        controller.Reset(actor.Id);
                        EndBehaviorTurn(actor);
                        break;
                    }

                    controller.Commit(proposal);
                    RecordBehaviorTrace();
                    retriedFallback = false;
                    continue;
                }

                bool committed = controller.States.ContainsKey(actor.Id);
                if (proposal.Fallback == CombatBehaviorFallback.Next && committed && !retriedFallback)
                {
                    RecordBehaviorTrace();
                    controller.Reset(actor.Id);
                    retriedFallback = true;
                    _pendingDecision = BuildDecision(actor);
                    _phase = CombatPhase.AwaitingAction;
                    continue;
                }

                controller.Reset(actor.Id);
                RecordBehaviorTrace();
                if (proposal.Flees)
                {
                    Escape(actor);
                }

                EndBehaviorTurn(actor);
                break;
            }
        }
        finally
        {
            _behaviorRejectedActionIds = null;
            _resolvingBehavior = false;
        }
    }

    private bool IsCommittedSpellPriceUnavailable(Combatant actor, string actionId)
    {
        CombatActionChoice? choice = _pendingDecision?.Actions.FirstOrDefault(action => action.Id == actionId);
        UseOption? use = choice is null ? null : FindUse(actor, choice.Id);
        if (choice?.SpellCosts is not IReadOnlyDictionary<string, decimal> costs
            || use?.Spell is not Definition spell
            || actor.CastsLeft.ContainsKey(spell))
        {
            return false;
        }

        try
        {
            return !SpellAffordable(actor, spell, costs);
        }
        catch (RuleFailure)
        {
            return false;
        }
    }

    private void EndBehaviorTurn(Combatant actor)
    {
        if (_activeActor == actor)
        {
            FinishCurrentTurn();
            _phase = CombatPhase.Advancing;
        }
    }

    private void RememberBehaviorTrace(CombatBehaviorTrace? trace)
    {
        if (trace is not null)
        {
            _lastBehaviorTrace = trace with { Round = _round };
        }
    }

    private void RecordBehaviorTrace()
    {
        if (CollectBehaviorTraces && _lastBehaviorTrace is CombatBehaviorTrace trace)
        {
            _behaviorTraces.Add(trace);
        }
    }

    private void RememberBehaviorRefusal(CombatBehaviorProposal proposal, string? reason)
    {
        if (_lastBehaviorTrace is CombatBehaviorTrace trace)
        {
            _lastBehaviorTrace = trace with
            {
                Abandoned = true,
                Reason = reason ?? "The proposed command was refused by the live resolver.",
            };
        }
        else if (proposal.Trace is CombatBehaviorTrace proposedTrace)
        {
            _lastBehaviorTrace = proposedTrace with
            {
                Round = _round,
                Abandoned = true,
                Reason = reason ?? "The proposed command was refused by the live resolver.",
            };
        }
    }

    private void RememberPendingBehaviorCommit(CombatBehaviorProposal proposal)
    {
        if (proposal.Behavior is null || proposal.RuleIndex is not int ruleIndex || proposal.StepIndex is not int stepIndex)
        {
            ClearPendingBehaviorCommit();
            return;
        }

        _pendingBehaviorActorId = proposal.ActorId;
        _pendingBehaviorId = proposal.Behavior.Definition.QualifiedId;
        _pendingBehaviorRuleIndex = ruleIndex;
        _pendingBehaviorStepIndex = stepIndex;
        _pendingBehaviorMovementOnly = proposal.MovementOnly;
    }

    private void ClearPendingBehaviorCommit()
    {
        _pendingBehaviorActorId = null;
        _pendingBehaviorId = null;
        _pendingBehaviorRuleIndex = null;
        _pendingBehaviorStepIndex = null;
        _pendingBehaviorMovementOnly = false;
    }

    private void CommitPendingBehavior()
    {
        if (_pendingBehaviorActorId is not string actorId
            || _pendingBehaviorId is not string behaviorId
            || _pendingBehaviorRuleIndex is not int ruleIndex
            || _pendingBehaviorStepIndex is not int stepIndex
            || Find(actorId) is not Combatant actor)
        {
            ClearPendingBehaviorCommit();
            return;
        }

        Definition? definition = _rules.Find(DefinitionTypes.CombatBehavior, behaviorId, out _);
        CombatBehaviorProfile? profile = definition is null ? null : _rules.CombatBehaviorOf(definition);
        if (profile is not null)
        {
            EnsureBehaviorController().Commit(new CombatBehaviorProposal(
                actorId,
                profile,
                ruleIndex,
                stepIndex,
                null,
                null,
                null,
                _pendingBehaviorMovementOnly));
        }

        ClearPendingBehaviorCommit();
    }

    private void CaptureBehaviorContinuation(CombatContinuationState state)
    {
        CombatBehaviorController controller = EnsureBehaviorController();
        state.BehaviorStates = controller.States.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Clone(),
            StringComparer.Ordinal);
        state.BehaviorAssignments = controller.ExplicitAssignments.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
        state.BehaviorPendingActorId = _pendingBehaviorActorId;
        state.BehaviorPendingId = _pendingBehaviorId;
        state.BehaviorPendingRuleIndex = _pendingBehaviorRuleIndex;
        state.BehaviorPendingStepIndex = _pendingBehaviorStepIndex;
        state.BehaviorPendingMovementOnly = _pendingBehaviorMovementOnly;
    }

    private void RestoreBehaviorContinuation(CombatContinuationState state)
    {
        CombatBehaviorController controller = EnsureBehaviorController();
        foreach ((string actorId, string? behaviorId) in state.BehaviorAssignments)
        {
            if (Find(actorId) is null)
            {
                throw new ArgumentException($"Combat continuation names unknown behavior assignment actor '{actorId}'.", nameof(state));
            }

            CombatBehaviorProfile? profile = behaviorId is null
                ? null
                : _rules.CombatBehaviorOf(_rules.Find(DefinitionTypes.CombatBehavior, behaviorId, out string? problem)
                    ?? throw new ArgumentException(problem ?? $"Unknown combat behavior '{behaviorId}'.", nameof(state)));
            controller.Assign(actorId, profile);
        }

        foreach ((string actorId, CombatBehaviorState behaviorState) in state.BehaviorStates)
        {
            if (Find(actorId) is null)
            {
                throw new ArgumentException($"Combat continuation names unknown behavior state actor '{actorId}'.", nameof(state));
            }

            controller.RestoreState(actorId, behaviorState);
        }

        _pendingBehaviorActorId = state.BehaviorPendingActorId;
        _pendingBehaviorId = state.BehaviorPendingId;
        _pendingBehaviorRuleIndex = state.BehaviorPendingRuleIndex;
        _pendingBehaviorStepIndex = state.BehaviorPendingStepIndex;
        _pendingBehaviorMovementOnly = state.BehaviorPendingMovementOnly;
    }
}
