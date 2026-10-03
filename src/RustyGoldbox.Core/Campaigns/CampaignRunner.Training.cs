using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

public sealed partial class CampaignRunner
{
    public TextFact? Training()
    {
        if (_state.PendingTraining is not Definition trainer)
        {
            return null;
        }

        Definition advancement = _rules.Advancement!;
        Definition currency = _rules.Reference(advancement, "$.training.currency");
        string prices = string.Join("; ", _state.Party.Select((character, index) =>
            $"[{index + 1}] {character.Name}: {Fact(ServicePrice(advancement, "$.training.cost", character))} {currency.Name.ToLowerInvariant()}{(CharacterRules.ReadyToLevel(_rules, character) ? " (ready)" : " (needs experience)")}"));
        return new TextFact($"{trainer.Json.GetProperty("text").GetString()} {prices}. Commands: train <member> [--class <id>] [--feature <id>,...] [--boosts <id>,...], leave.");
    }

    private void Train(int member, string[] options, DiceRoller dice, List<PlayFact> facts)
    {
        if (_state.PendingTraining is not Definition trainer || member < 1 || member > _state.Party.Count)
        {
            facts.Add(new RefusedFact("train needs an open training event and a party member number."));
            return;
        }

        Character character = _state.Party[member - 1];
        Definition advancement = _rules.Advancement!;
        Definition currency = _rules.Reference(advancement, "$.training.currency");
        decimal price = ServicePrice(advancement, "$.training.cost", character);
        if (!CanPay(trainer, "Training", currency, price, facts))
        {
            return;
        }

        int before = dice.Rolls.Count;
        decimal days = Located(advancement, "$.training.days", () =>
        {
            decimal duration = EvaluateFor(advancement, "$.training.days", character, dice).Number;
            if (duration < 0)
            {
                throw new ExpressionException("Training days must be nonnegative; change training.days.", 0);
            }

            return duration;
        });
        decimal elapsed = Located(advancement, "$.training.days", () => checked(_state.ElapsedDays + days));
        if (!Level(member, options, dice, facts, trained: true))
        {
            return;
        }

        CurrencyLedger.Pay(_state.Party, currency.Id, price);
        _state.ElapsedDays = elapsed;
        facts.Add(new TextFact($"{character.Name} trains for {Fact(days)} days and {Fact(price)} {currency.Name.ToLowerInvariant()}. Campaign time: {Fact(elapsed)} days.") { Rolls = dice.Rolls.Skip(before).ToList() });
        facts.Add(Training()!);
    }
}
