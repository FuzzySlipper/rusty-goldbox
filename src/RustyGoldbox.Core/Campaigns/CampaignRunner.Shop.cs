using System.Text.Json;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

public sealed partial class CampaignRunner
{
    /// <summary>The open shop's current offers, read from its guards and the live inventory.</summary>
    public ShopFact? Shop()
    {
        if (_state.PendingShop is not Definition shop)
        {
            return null;
        }

        decimal gold = Located(shop, "$", () => _state.Party.Sum(character => character.Gold));
        return new ShopFact(shop.Json.GetProperty("text").GetString()!, gold, Stock(shop), Carried().Select(entry => entry.Offer).ToList());
    }

    private List<ShopOffer> Stock(Definition shop)
    {
        List<ShopOffer> offered = [];
        JsonElement items = shop.Json.GetProperty("items");
        for (int index = 0; index < items.GetArrayLength(); index++)
        {
            if (items[index].TryGetProperty("when", out _) && !Evaluate(shop, $"$.items[{index}].when", null).Boolean)
            {
                continue;
            }

            Definition item = _rules.Reference(shop, $"$.items[{index}].item");
            offered.Add(new ShopOffer(offered.Count + 1, item, item.Json.GetProperty("cost").GetDecimal()));
        }

        return offered;
    }

    private List<(ShopOffer Offer, List<Definition> Items, int Index)> Carried()
    {
        Definition economy = _rules.Economy!;
        decimal fraction = economy.Json.GetProperty("sell_fraction").GetDecimal();
        List<(ShopOffer, List<Definition>, int)> carried = [];
        void Add(List<Definition> items, string? holder)
        {
            for (int index = 0; index < items.Count; index++)
            {
                Definition item = items[index];
                decimal price = Located(economy, "$.sell_fraction", () => checked(item.Json.GetProperty("cost").GetDecimal() * fraction));
                carried.Add((new ShopOffer(carried.Count + 1, item, price, holder), items, index));
            }
        }

        Add(_state.Inventory, null);
        foreach (var character in _state.Party)
        {
            Add(character.Equipment, character.Name);
        }

        return carried;
    }

    private void Buy(int number, List<PlayFact> facts)
    {
        if (_state.PendingShop is not Definition shop)
        {
            facts.Add(new RefusedFact("there is no shop open."));
            return;
        }

        List<ShopOffer> stock = Stock(shop);
        if (number < 1 || number > stock.Count)
        {
            facts.Add(new RefusedFact($"{number} is not stock offered by this shop; use status to see buy numbers."));
            return;
        }

        ShopOffer offer = stock[number - 1];
        decimal gold = Located(shop, "$", () => _state.Party.Sum(character => character.Gold));
        if (_state.Party.Count == 0 || gold < offer.Price)
        {
            facts.Add(new RefusedFact($"{offer.Item.Name} costs {Fact(offer.Price)} gold; the party has {Fact(gold)}."));
            return;
        }

        // Spend the pooled purse in party order; characters remain the only gold owners.
        decimal left = offer.Price;
        foreach (var character in _state.Party)
        {
            decimal paid = Math.Min(Math.Max(0, character.Gold), left);
            character.Gold -= paid;
            left -= paid;
        }

        _state.Inventory.Add(offer.Item);
        facts.Add(new TradeFact(true, offer.Item.Name, offer.Price));
        facts.Add(Shop()!);
    }

    private void Sell(int number, List<PlayFact> facts)
    {
        if (_state.PendingShop is null)
        {
            facts.Add(new RefusedFact("there is no shop open."));
            return;
        }

        List<(ShopOffer Offer, List<Definition> Items, int Index)> carried = Carried();
        if (number < 1 || number > carried.Count || _state.Party.Count == 0)
        {
            facts.Add(new RefusedFact($"{number} is not a carried item; use status to see sell numbers."));
            return;
        }

        (ShopOffer offer, List<Definition> items, int index) = carried[number - 1];
        // Calculate the new balances before removing the item, so a numeric overflow can't lose it.
        decimal share = decimal.Floor(offer.Price / _state.Party.Count);
        decimal[] balances = Located(_rules.Economy!, "$.sell_fraction", () => _state.Party
            .Select((character, member) => checked(character.Gold + share + (member == 0 ? offer.Price - share * _state.Party.Count : 0)))
            .ToArray());
        Located(_rules.Economy!, "$.sell_fraction", () => balances.Sum());
        for (int member = 0; member < balances.Length; member++)
        {
            _state.Party[member].Gold = balances[member];
        }

        items.RemoveAt(index);
        facts.Add(new TradeFact(false, offer.Item.Name, offer.Price));
        facts.Add(Shop()!);
    }

    private void LeaveShop(DiceRoller dice, List<PlayFact> facts)
    {
        if (_state.PendingShop is not Definition shop)
        {
            facts.Add(new RefusedFact("there is no shop open."));
            return;
        }

        _state.PendingShop = null;
        facts.Add(new TextFact("The party leaves the shop."));
        if (Next(shop, "$.next") is Definition next)
        {
            RunChain(next, dice, facts);
        }
    }
}
