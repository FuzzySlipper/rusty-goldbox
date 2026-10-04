using System.Text.Json;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
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
        decimal? maximum = null;
        Definition? buyingCurrency = null;
        if (shop.Json.TryGetProperty("buying", out _))
        {
            (decimal _, Definition currency, Definition? balance, maximum) = BuyingPolicy(shop);
            buyingCurrency = currency;
            if (balance is Definition merchantBalance)
            {
                text += $" Buying cash: {Fact(BuyingCash(merchantBalance))} {currency.Name.ToLowerInvariant()}.";
            }

            if (maximum is decimal maxValue)
            {
                text += $" Buys items up to {Fact(maxValue)} {currency.Name.ToLowerInvariant()}.";
            }
        }

        return new ShopFact(text, balances, Stock(shop), Carried().Select(entry => entry.Offer).ToList(), maximum, buyingCurrency);
    }

    private List<ShopOffer> Stock(Definition shop)
    {
        return StockEntries(shop).Select(entry => entry.Offer).ToList();
    }

    private List<(ShopOffer Offer, Definition? StockVariable)> StockEntries(Definition shop)
    {
        List<(ShopOffer Offer, Definition? StockVariable)> offered = [];
        JsonElement items = shop.Json.GetProperty("items");
        for (int index = 0; index < items.GetArrayLength(); index++)
        {
            if (items[index].TryGetProperty("when", out _) && !Evaluate(shop, $"$.items[{index}].when", null).Boolean)
            {
                continue;
            }

            Definition item = _rules.Reference(shop, $"$.items[{index}].item");
            Definition currency = _rules.Reference(item, "$.currency");
            string stockPath = $"$.items[{index}].stock";
            Definition? stockVariable = _rules.References.GetValueOrDefault((shop, stockPath));
            decimal? remaining = StockRemaining(shop, stockPath, stockVariable);
            offered.Add((new ShopOffer(offered.Count + 1, item, ItemCost(item), currency, Remaining: remaining), stockVariable));
        }

        return offered;
    }

    private decimal? StockRemaining(Definition shop, string path, Definition? variable)
    {
        if (variable is null)
        {
            return null;
        }

        Dictionary<string, Value> values = VariableValues(variable);
        if (!values.TryGetValue(variable.Id, out Value value)
            || value.Type != ExprType.Number
            || value.Number < 0
            || decimal.Truncate(value.Number) != value.Number
            )
        {
            throw new RuleFailure(new ModuleDiagnostic(
                "event.shop.stock",
                $"Stock variable '{variable.QualifiedId}' must be a nonnegative whole number.",
                shop.Module,
                shop.File,
                path));
        }

        return value.Number;
    }

    private void DecrementStock(Definition variable, decimal remaining)
    {
        VariableValues(variable)[variable.Id] = Value.Of(remaining - 1);
    }

    private decimal ItemCost(Definition item)
    {
        if (item.Json.GetProperty("cost").TryGetDecimal(out decimal cost))
        {
            return cost;
        }

        throw new RuleFailure(new ModuleDiagnostic(
            "event.shop.item-cost",
            $"Item '{item.QualifiedId}' has a cost outside the decimal currency range.",
            item.Module,
            item.File,
            "$.cost"));
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
            (fraction, _, _, _) = BuyingPolicy(shop);
            JsonElement buying = shop.Json.GetProperty("buying");
            if (buying.TryGetProperty("fraction", out _))
            {
                priceOwner = shop;
                pricePath = "$.buying.fraction";
            }
        }

        List<(ShopOffer, List<Definition>, int)> carried = [];
        void Add(List<Definition> items, string? holder)
        {
            for (int index = 0; index < items.Count; index++)
            {
                Definition item = items[index];
                decimal price = Located(priceOwner, pricePath, () => checked(ItemCost(item) * fraction));
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

        List<(ShopOffer Offer, Definition? StockVariable)> stock = StockEntries(shop);
        if (number < 1 || number > stock.Count)
        {
            facts.Add(new RefusedFact($"{number} is not stock offered by this shop; use status to see buy numbers."));
            return;
        }

        (ShopOffer offer, Definition? stockVariable) = stock[number - 1];
        if (offer.Remaining is 0)
        {
            facts.Add(new RefusedFact($"{offer.Item.Name} is sold out."));
            return;
        }

        if (!CanPay(shop, offer.Item.Name, offer.Currency, offer.Price, facts))
        {
            return;
        }

        CurrencyLedger.Pay(_state.Party, offer.Currency.Id, offer.Price);
        _state.Inventory.Add(offer.Item);
        if (stockVariable is Definition variable && offer.Remaining is decimal remaining)
        {
            DecrementStock(variable, remaining);
        }

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
            (decimal _, Definition buyingCurrency, Definition? balance, decimal? maximum) = BuyingPolicy(shop);
            if (offer.Currency.Id != buyingCurrency.Id)
            {
                facts.Add(new RefusedFact($"this shop buys only {buyingCurrency.Name.ToLowerInvariant()}; {offer.Item.Name} is priced in {offer.Currency.Name.ToLowerInvariant()}."));
                return;
            }

            if (maximum is decimal maxValue && ItemCost(offer.Item) > maxValue)
            {
                facts.Add(new RefusedFact($"this shop buys items costing at most {Fact(maxValue)} {buyingCurrency.Name.ToLowerInvariant()}; {offer.Item.Name} costs {Fact(ItemCost(offer.Item))}."));
                return;
            }

            if (balance is Definition merchantBalance)
            {
                decimal cash = BuyingCash(merchantBalance);
                if (cash < offer.Price)
                {
                    facts.Add(new RefusedFact($"this shop has {Fact(cash)} {buyingCurrency.Name.ToLowerInvariant()} left; it cannot buy {offer.Item.Name} for {Fact(offer.Price)}."));
                    return;
                }

                // Credit the named currency before changing the merchant balance or removing the item, so an overflow can't lose it.
                string buyingPath = shop.Json.GetProperty("buying").TryGetProperty("fraction", out _)
                    ? "$.buying.fraction"
                    : "$.buying";
                Located(shop, buyingPath, () =>
                {
                    CurrencyLedger.CreditSplit(_state.Party, offer.Currency.Id, offer.Price);
                    SetBuyingCash(merchantBalance, checked(cash - offer.Price));
                    return true;
                });
            }
            else
            {
                // Credit the named currency before removing the item, so an overflow can't lose it.
                Located(shop, "$.buying", () =>
                {
                    CurrencyLedger.CreditSplit(_state.Party, offer.Currency.Id, offer.Price);
                    return true;
                });
            }
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

    private (decimal Fraction, Definition Currency, Definition? Balance, decimal? MaximumValue) BuyingPolicy(Definition shop)
    {
        JsonElement buying = shop.Json.GetProperty("buying");
        decimal fraction = _rules.Economy!.Json.GetProperty("sell_fraction").GetDecimal();
        if (buying.TryGetProperty("fraction", out JsonElement fractionValue))
        {
            if (!fractionValue.TryGetDecimal(out fraction))
            {
                throw new RuleFailure(new ModuleDiagnostic(
                    "event.shop.buying-fraction",
                    "A shop buying fraction must fit the decimal currency range.",
                    shop.Module,
                    shop.File,
                    "$.buying.fraction"));
            }
        }

        decimal? maximum = null;
        if (buying.TryGetProperty("max_value", out JsonElement maximumValue))
        {
            if (!maximumValue.TryGetDecimal(out decimal max))
            {
                throw new RuleFailure(new ModuleDiagnostic(
                    "event.shop.buying-max-value",
                    "A shop maximum item value must fit the decimal currency range.",
                    shop.Module,
                    shop.File,
                    "$.buying.max_value"));
            }

            maximum = max;
        }

        Definition? balance = _rules.References.GetValueOrDefault((shop, "$.buying.balance"));
        return (
            fraction,
            _rules.Reference(shop, "$.buying.currency"),
            balance,
            maximum);
    }

    private decimal BuyingCash(Definition balance)
    {
        return VariableValues(balance)[balance.Id].Number;
    }

    private void SetBuyingCash(Definition balance, decimal value)
    {
        VariableValues(balance)[balance.Id] = Value.Of(value);
    }

    private Dictionary<string, Value> VariableValues(Definition variable)
    {
        return variable.Json.TryGetProperty("scope", out JsonElement scope)
            && scope.GetString() == "area"
            ? _state.ValuesFor(_state.Area)
            : _state.Variables;
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
