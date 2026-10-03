using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Core.Campaigns;

public sealed partial class CampaignRunner
{
    private Definition? ChangeItems(Definition evt, List<PlayFact> facts)
    {
        Definition item = _rules.Reference(evt, "$.item");
        int count = evt.Json.TryGetProperty("count", out var copies) ? copies.GetInt32() : 1;
        bool given = evt.Json.GetProperty("kind").GetString() == "give";
        if (given)
        {
            for (int index = 0; index < count; index++)
            {
                _state.Inventory.Add(item);
            }
        }
        else
        {
            if (_state.CarriedItems.Count(carried => carried == item) < count)
            {
                facts.Add(new RefusedFact($"the party needs {count} × {item.Name}."));
                return Next(evt, "$.on_refused");
            }

            int remaining = count;
            foreach ((List<Definition> store, _) in ItemStores())
            {
                while (remaining > 0)
                {
                    int index = store.IndexOf(item);
                    if (index < 0)
                    {
                        break;
                    }

                    store.RemoveAt(index);
                    remaining--;
                }
            }
        }

        facts.Add(new ItemsFact(given, item, count));
        return Next(evt, "$.next");
    }

    /// <summary>The existing inventory owners, in the order items are offered or removed.</summary>
    private IEnumerable<(List<Definition> Items, string? Holder)> ItemStores()
    {
        yield return (_state.Inventory, null);
        foreach (var character in _state.Party)
        {
            yield return (character.Equipment, character.Name);
        }
    }
}
