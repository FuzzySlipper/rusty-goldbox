using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

/// <summary>
/// The one campaign owner for pooled currency payments and credits. Currency
/// IDs never convert: a payment consumes only the named balance.
/// </summary>
internal static class CurrencyLedger
{
    public static IReadOnlyDictionary<Definition, decimal> Snapshot(IEnumerable<Character> party, RuleSet rules)
    {
        return rules.Currencies.Values.ToDictionary(currency => currency, currency => Total(party, currency.Id));
    }

    public static decimal Total(IEnumerable<Character> party, string currency)
    {
        return party.Sum(character => character.Balances.GetValueOrDefault(currency));
    }

    public static bool CanPay(IEnumerable<Character> party, string currency, decimal amount)
    {
        return amount >= 0 && Total(party, currency) >= amount;
    }

    public static void Pay(IReadOnlyList<Character> party, string currency, decimal amount)
    {
        if (!CanPay(party, currency, amount))
        {
            throw new InvalidOperationException($"The party cannot pay {amount} {currency}.");
        }

        decimal left = amount;
        foreach (Character character in party)
        {
            decimal balance = character.Balances.GetValueOrDefault(currency);
            decimal paid = Math.Min(balance, left);
            character.Balances[currency] = checked(balance - paid);
            left -= paid;
        }
    }

    public static void CreditSplit(IReadOnlyList<Character> party, string currency, decimal amount)
    {
        if (amount == 0 || party.Count == 0)
        {
            return;
        }

        if (amount < 0)
        {
            throw new InvalidOperationException($"A currency credit cannot be negative: {amount} {currency}.");
        }

        decimal share = decimal.Floor(amount / party.Count);
        decimal[] balances = party.Select(character => character.Balances.GetValueOrDefault(currency)).ToArray();
        decimal[] updated = new decimal[party.Count];
        for (int index = 0; index < party.Count; index++)
        {
            decimal credit = share + (index == 0 ? amount - share * party.Count : 0);
            updated[index] = checked(balances[index] + credit);
        }

        for (int index = 0; index < party.Count; index++)
        {
            party[index].Balances[currency] = updated[index];
        }
    }
}
