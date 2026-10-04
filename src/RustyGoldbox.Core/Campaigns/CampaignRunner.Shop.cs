using System.Text.Json;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
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

        IReadOnlyDictionary<Definition, decimal> balances = Located(shop, "$", () => CurrencyLedger.Snapshot(_state.Party, _rules));
        string text = shop.Json.GetProperty("text").GetString()!;
        if (shop.Json.TryGetProperty("buying", out _))
        {
            (decimal _, Definition currency, Definition balance) = BuyingPolicy(shop);
            text += $" Buying cash: {Fact(BuyingCash(balance))} {currency.Name.ToLowerInvariant()}.";
        }

        return new ShopFact(text, balances, Stock(shop), Carried().Select(entry => entry.Offer).ToList());
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
            Definition currency = _rules.Reference(item, "$.currency");
            offered.Add(new ShopOffer(offered.Count + 1, item, item.Json.GetProperty("cost").GetDecimal(), currency));
        }

        return offered;
    }

    private List<(ShopOffer Offer, List<Definition> Items, int Index)> Carried()
    {
        Definition shop = _state.PendingShop!;
        Definition economy = _rules.Economy!;
        Definition priceOwner = economy;
        string pricePath = "$.sell_fraction";
        decimal fraction = economy.Json.GetProperty("sell_fraction").GetDecimal();
        if (shop.Json.TryGetProperty("buying", out _))
        {
            (fraction, _, _) = BuyingPolicy(shop);
            priceOwner = shop;
            pricePath = "$.buying.fraction";
        }

        List<(ShopOffer, List<Definition>, int)> carried = [];
        void Add(List<Definition> items, string? holder)
        {
            for (int index = 0; index < items.Count; index++)
            {
                Definition item = items[index];
                decimal price = Located(priceOwner, pricePath, () => checked(item.Json.GetProperty("cost").GetDecimal() * fraction));
                Definition currency = _rules.Reference(item, "$.currency");
                carried.Add((new ShopOffer(carried.Count + 1, item, price, currency, holder), items, index));
            }
        }

        foreach ((List<Definition> items, string? holder) in ItemStores())
        {
            Add(items, holder);
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
        if (!CanPay(shop, offer.Item.Name, offer.Currency, offer.Price, facts))
        {
            return;
        }

        CurrencyLedger.Pay(_state.Party, offer.Currency.Id, offer.Price);

        _state.Inventory.Add(offer.Item);
        facts.Add(new TradeFact(true, offer.Item.Name, offer.Currency, offer.Price));
        facts.Add(Shop()!);
    }

    private void Sell(int number, List<PlayFact> facts)
    {
        if (_state.PendingShop is not Definition shop)
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
        if (shop.Json.TryGetProperty("buying", out _))
        {
            (decimal _, Definition buyingCurrency, Definition balance) = BuyingPolicy(shop);
            if (offer.Currency.Id != buyingCurrency.Id)
            {
                facts.Add(new RefusedFact($"this shop buys only {buyingCurrency.Name.ToLowerInvariant()}; {offer.Item.Name} is priced in {offer.Currency.Name.ToLowerInvariant()}."));
                return;
            }

            decimal cash = BuyingCash(balance);
            if (cash < offer.Price)
            {
                facts.Add(new RefusedFact($"this shop has {Fact(cash)} {buyingCurrency.Name.ToLowerInvariant()} left; it cannot buy {offer.Item.Name} for {Fact(offer.Price)}."));
                return;
            }

            // Credit the named currency before changing the merchant balance or removing the item, so an overflow can't lose it.
            Located(shop, "$.buying.fraction", () =>
            {
                CurrencyLedger.CreditSplit(_state.Party, offer.Currency.Id, offer.Price);
                SetBuyingCash(balance, checked(cash - offer.Price));
                return true;
            });
        }
        else
        {
            // Credit the named currency before removing the item, so an overflow can't lose it.
            Located(_rules.Economy!, "$.sell_fraction", () =>
            {
                CurrencyLedger.CreditSplit(_state.Party, offer.Currency.Id, offer.Price);
                return true;
            });
        }

        items.RemoveAt(index);
        facts.Add(new TradeFact(false, offer.Item.Name, offer.Currency, offer.Price));
        facts.Add(Shop()!);
    }

    private (decimal Fraction, Definition Currency, Definition Balance) BuyingPolicy(Definition shop)
    {
        JsonElement buying = shop.Json.GetProperty("buying");
        return (
            buying.GetProperty("fraction").GetDecimal(),
            _rules.Reference(shop, "$.buying.currency"),
            _rules.Reference(shop, "$.buying.balance"));
    }

    private decimal BuyingCash(Definition balance)
    {
        Dictionary<string, Value> values = balance.Json.TryGetProperty("scope", out JsonElement scope)
            && scope.GetString() == "area"
            ? _state.ValuesFor(_state.Area)
            : _state.Variables;
        return values[balance.Id].Number;
    }

    private void SetBuyingCash(Definition balance, decimal value)
    {
        Dictionary<string, Value> values = balance.Json.TryGetProperty("scope", out JsonElement scope)
            && scope.GetString() == "area"
            ? _state.ValuesFor(_state.Area)
            : _state.Variables;
        values[balance.Id] = Value.Of(value);
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
