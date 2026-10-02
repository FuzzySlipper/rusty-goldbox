using System.Text.Json;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Rules;

/// <summary>A table definition ready for lookups.</summary>
public sealed class CompiledTable
{
    private readonly List<(object[] Keys, Value Value)> _rows = [];

    public CompiledTable(Definition definition)
    {
        Definition = definition;
        JsonElement json = definition.Json;
        KeyTypes = json.GetProperty("keys").EnumerateArray()
            .Select(key => key.GetProperty("type").GetString() == "text" ? ExprType.Text : ExprType.Number)
            .ToList();
        KeyNames = json.GetProperty("keys").EnumerateArray().Select(key => key.GetProperty("name").GetString()!).ToList();
        ExprTypes.TryParse(json.GetProperty("value").GetString()!, out ExprType valueType);
        ValueType = valueType;

        foreach (JsonElement row in json.GetProperty("rows").EnumerateArray())
        {
            object[] keys = new object[KeyTypes.Count];
            for (int i = 0; i < KeyTypes.Count; i++)
            {
                keys[i] = KeyTypes[i] == ExprType.Text ? row[i].GetString()! : NumberKeyRange.FromCell(row[i]);
            }

            JsonElement cell = row[KeyTypes.Count];
            Value value = ValueType switch
            {
                ExprType.Number => Value.Of(cell.GetDecimal()),
                ExprType.Text => Value.Of(cell.GetString()!),
                _ => Value.Of(cell.GetBoolean()),
            };
            _rows.Add((keys, value));
        }
    }

    public Definition Definition { get; }

    public IReadOnlyList<ExprType> KeyTypes { get; }

    public IReadOnlyList<string> KeyNames { get; }

    public ExprType ValueType { get; }

    /// <summary>Pairs of row indexes whose keys overlap, so a lookup could match both.</summary>
    public IEnumerable<(int First, int Second)> Overlaps()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            for (int j = i + 1; j < _rows.Count; j++)
            {
                if (KeysOverlap(_rows[i].Keys, _rows[j].Keys))
                {
                    yield return (i, j);
                }
            }
        }
    }

    public Value? Lookup(IReadOnlyList<Value> keys)
    {
        foreach ((object[] rowKeys, Value value) in _rows)
        {
            if (Matches(rowKeys, keys))
            {
                return value;
            }
        }

        return null;
    }

    private static bool Matches(object[] rowKeys, IReadOnlyList<Value> keys)
    {
        for (int i = 0; i < rowKeys.Length; i++)
        {
            bool match = rowKeys[i] switch
            {
                string text => keys[i].Text == text,
                NumberKeyRange range => range.Contains(keys[i].Number),
                _ => false,
            };
            if (!match)
            {
                return false;
            }
        }

        return true;
    }

    private static bool KeysOverlap(object[] first, object[] second)
    {
        for (int i = 0; i < first.Length; i++)
        {
            bool overlap = first[i] switch
            {
                string text => text == (string)second[i],
                NumberKeyRange range => range.Overlaps((NumberKeyRange)second[i]),
                _ => false,
            };
            if (!overlap)
            {
                return false;
            }
        }

        return true;
    }
}
