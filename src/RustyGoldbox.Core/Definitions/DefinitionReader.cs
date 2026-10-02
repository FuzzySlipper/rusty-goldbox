using System.Globalization;
using System.Text.Json;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Core.Definitions;

/// <summary>
/// Checks one definition file's JSON against its definition type. Expressions
/// and references are collected for checking against the whole module set.
/// </summary>
public static class DefinitionReader
{
    public static Definition? Read(JsonElement root, string module, string file, List<ModuleDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out JsonElement typeElement)
            || typeElement.ValueKind != JsonValueKind.String)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "definition.type-missing",
                $"A definition file must be a JSON object with a string \"type\" field. Types: {TypeList()}.",
                module,
                file,
                "$.type"));
            return null;
        }

        string typeName = typeElement.GetString()!;
        DefinitionType? type = DefinitionTypes.Find(typeName);
        if (type is null)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "definition.type-unknown",
                $"'{typeName}' is not a definition type. Types: {TypeList()}. Run `goldbox schema <type>` for a type's fields.",
                module,
                file,
                "$.type"));
            return null;
        }

        Reader reader = new(type, module, file, diagnostics);
        return reader.Read(root);
    }

    private static string TypeList() => string.Join(", ", DefinitionTypes.All.Select(type => type.Name));

    private sealed class Reader(DefinitionType type, string module, string file, List<ModuleDiagnostic> diagnostics)
    {
        private readonly int _errorsBefore = diagnostics.Count;
        private readonly List<ExpressionSite> _expressions = [];
        private readonly List<ReferenceSite> _references = [];

        public Definition? Read(JsonElement root)
        {
            List<Field> fields = [.. DefinitionType.CommonFields, .. type.Fields];
            string what = $"a {type.Name}";
            if (type == DefinitionTypes.Event
                && root.TryGetProperty("kind", out JsonElement kindElement)
                && kindElement.ValueKind == JsonValueKind.String
                && EventTypes.Find(kindElement.GetString()!) is DefinitionType kind)
            {
                fields.AddRange(kind.Fields);
                what = $"a {kind.Name} event";
            }

            ReadObject(root, fields, "$", what);
            string? id = root.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;
            if (id is not null && !DefinitionIds.IsValid(id))
            {
                Error("definition.id", "$.id", $"'{id}' is not a valid definition ID. Use {DefinitionIds.FormatDescription}.");
            }

            if (type == DefinitionTypes.Table && diagnostics.Count == _errorsBefore)
            {
                CheckTableRows(root);
            }

            if (type == DefinitionTypes.Class && diagnostics.Count == _errorsBefore)
            {
                CheckClassLevels(root);
            }

            if (type == DefinitionTypes.Area && diagnostics.Count == _errorsBefore)
            {
                CheckArea(root);
            }

            if (type == DefinitionTypes.Event && diagnostics.Count == _errorsBefore && root.GetProperty("kind").GetString() == "menu"
                && root.GetProperty("options").EnumerateArray().All(option => option.TryGetProperty("when", out _)))
            {
                Error("event.menu", "$.options", "Every option has a \"when\", so the menu could offer nothing and leave the party stuck. Give at least one option no \"when\".");
            }

            if (diagnostics.Count > _errorsBefore)
            {
                return null;
            }

            return new Definition(type, id!, module, file, root.Clone(), _expressions, _references);
        }

        private void ReadObject(JsonElement element, IReadOnlyList<Field> fields, string path, string what)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                Error("definition.field-type", path, $"Expected {what} object, but this is {JsonFiles.Describe(element.ValueKind)}.");
                return;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!fields.Any(field => field.Name == property.Name))
                {
                    Error("definition.unknown-field", $"{path}.{property.Name}",
                        $"'{property.Name}' is not a field of {what}. Fields: {string.Join(", ", fields.Select(field => field.Name))}.");
                }
            }

            foreach (Field field in fields)
            {
                if (element.TryGetProperty(field.Name, out JsonElement value))
                {
                    ReadValue(value, field.Kind, $"{path}.{field.Name}");
                }
                else if (field.Required)
                {
                    Error("definition.field-required", path, $"Missing required field \"{field.Name}\": {field.Kind.Describe()}. {field.Description}");
                }
            }
        }

        private void ReadValue(JsonElement value, FieldKind kind, string path)
        {
            switch (kind)
            {
                case TextKind:
                    ExpectKind(value, JsonValueKind.String, path, kind);
                    break;
                case IntegerKind:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out _))
                    {
                        Error("definition.field-type", path, $"Expected a whole number, but this is {Show(value)}.");
                    }

                    break;
                case NumberKind:
                    ExpectKind(value, JsonValueKind.Number, path, kind);
                    break;
                case EnumKind enumKind:
                    if (value.ValueKind != JsonValueKind.String || !enumKind.Values.Contains(value.GetString()!))
                    {
                        Error("definition.field-value", path, $"Expected {enumKind.Describe()}, but this is {Show(value)}.");
                    }

                    break;
                case ExpressionKind expression:
                    ReadExpression(value, expression, path);
                    break;
                case ReferenceKind or StatKind:
                    if (ExpectKind(value, JsonValueKind.String, path, kind))
                    {
                        _references.Add(new ReferenceSite(path, value.GetString()!, kind));
                    }

                    break;
                case ListKind list:
                    ReadList(value, list, path);
                    break;
                case MapKind map:
                    ReadMap(value, map, path);
                    break;
                case ObjectKind obj:
                    ReadObject(value, obj.Fields, path, "an entry");
                    break;
                case ModifierKind:
                    ReadModifier(value, path);
                    break;
                case TableRowsKind:
                    ExpectKind(value, JsonValueKind.Array, path, kind);
                    break;
                case BooleanKind:
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        Error("definition.field-type", path, $"Expected true or false, but this is {Show(value)}.");
                    }

                    break;
                case OperationKind operation:
                    ReadOperation(value, operation.Roots, path);
                    break;
                case UseKind:
                    ReadUse(value, path);
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled field kind {kind}.");
            }
        }

        private void ReadOperation(JsonElement value, Roots roots, string path)
        {
            if (!ExpectKind(value, JsonValueKind.Object, path, new OperationKind(roots)))
            {
                return;
            }

            string ops = string.Join(", ", OperationTypes.All.Select(operation => operation.Name));
            if (!value.TryGetProperty("op", out JsonElement name) || name.ValueKind != JsonValueKind.String)
            {
                Error("definition.field-required", path, $"An operation needs \"op\" naming what it does: {ops}.");
                return;
            }

            DefinitionType? operation = OperationTypes.Find(name.GetString()!);
            if (operation is null)
            {
                Error("definition.operation", $"{path}.op", $"'{name.GetString()}' is not an operation. Operations: {ops}. Run `goldbox schema operations` for their fields.");
                return;
            }

            // Operation fields read what their surroundings allow: without a target
            // (a condition's each_turn) "to" can only be self; outside a check, no check.
            List<Field> fields = [new("op", new TextKind(), true, "The operation.")];
            foreach (Field field in operation.Fields)
            {
                fields.Add(field.Kind switch
                {
                    ExpressionKind expression => field with { Kind = expression with { Roots = roots } },
                    MapKind { Value: ListKind { Item: OperationKind } } map => field with { Kind = map with { Value = new ListKind(new OperationKind(roots | Roots.Check)) } },
                    _ => field,
                });
            }

            ReadObject(value, fields, path, $"a {operation.Name} operation");
            foreach (string direction in new[] { "to", "by" })
            {
                if (!roots.HasFlag(Roots.Target) && value.TryGetProperty(direction, out JsonElement who) && who.ValueKind == JsonValueKind.String && who.GetString() == "target")
                {
                    Error("definition.field-value", $"{path}.{direction}", "There is no target here, so this can only be \"self\".");
                }
            }

            if (!roots.HasFlag(Roots.Target) && operation == OperationTypes.Check && !value.TryGetProperty("by", out _))
            {
                Error("definition.field-value", path, "There is no target here, so a check operation needs \"by\": \"self\".");
            }
        }

        private void ReadUse(JsonElement value, string path)
        {
            if (!ExpectKind(value, JsonValueKind.Object, path, new UseKind()))
            {
                return;
            }

            if (!value.TryGetProperty("action", out JsonElement action) || action.ValueKind != JsonValueKind.String)
            {
                Error("definition.field-required", path, "A use needs \"action\": the action it takes, for example { \"action\": \"melee_attack\", \"damage\": \"1d6\" }.");
                return;
            }

            _references.Add(new ReferenceSite($"{path}.action", action.GetString()!, new ReferenceKind("action")));
            foreach (JsonProperty property in value.EnumerateObject())
            {
                string at = $"{path}.{property.Name}";
                switch (property.Name)
                {
                    case "action":
                        break;
                    case "name":
                        ReadValue(property.Value, new TextKind(), at);
                        break;
                    case "from_item":
                        ReadValue(property.Value, new TextKind(), at);
                        break;
                    default:
                        ReadExpression(property.Value, new ExpressionKind(ExprType.Number, Roots.Self | Roots.Target), at);
                        break;
                }
            }
        }

        private void ReadExpression(JsonElement value, ExpressionKind kind, string path)
        {
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False && kind.Expected is null or ExprType.Boolean)
            {
                _expressions.Add(new ExpressionSite(path, value.GetBoolean() ? "true" : "false", kind));
                return;
            }

            // A plain number is a constant expression; it saves quoting "3".
            if (value.ValueKind == JsonValueKind.Number && kind.Expected is null or ExprType.Number)
            {
                _expressions.Add(new ExpressionSite(path, value.GetRawText(), kind));
                return;
            }

            if (ExpectKind(value, JsonValueKind.String, path, kind))
            {
                _expressions.Add(new ExpressionSite(path, value.GetString()!, kind));
            }
        }

        private void ReadList(JsonElement value, ListKind list, string path)
        {
            if (!ExpectKind(value, JsonValueKind.Array, path, list))
            {
                return;
            }

            int count = value.GetArrayLength();
            if (list.Count is int expected && count != expected)
            {
                Error("definition.field-value", path, $"Expected {list.Describe()}, but it has {count} entries.");
                return;
            }

            int index = 0;
            foreach (JsonElement item in value.EnumerateArray())
            {
                ReadValue(item, list.Item, $"{path}[{index}]");
                index++;
            }
        }

        private void ReadMap(JsonElement value, MapKind map, string path)
        {
            if (!ExpectKind(value, JsonValueKind.Object, path, map))
            {
                return;
            }

            foreach (JsonProperty property in value.EnumerateObject())
            {
                string entry = $"{path}.{property.Name}";
                _references.Add(new ReferenceSite(entry, property.Name, map.Key));
                ReadValue(property.Value, map.Value, entry);
            }
        }

        private void ReadModifier(JsonElement value, string path)
        {
            if (!ExpectKind(value, JsonValueKind.Object, path, new ModifierKind()))
            {
                return;
            }

            bool hasStat = value.TryGetProperty("stat", out JsonElement stat);
            bool hasCheck = value.TryGetProperty("check", out JsonElement check);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (property.Name is not ("stat" or "check" or "value"))
                {
                    Error("definition.unknown-field", $"{path}.{property.Name}", $"'{property.Name}' is not a modifier field. Fields: stat or check, and value.");
                }
            }

            if (hasStat == hasCheck)
            {
                Error("definition.field-required", path, "A modifier needs exactly one of \"stat\" (a stat ID) or \"check\" (a check reference), plus \"value\".");
            }
            else if (hasStat)
            {
                ReadValue(stat, new StatKind(false), $"{path}.stat");
            }
            else
            {
                ReadValue(check, new ReferenceKind("check"), $"{path}.check");
            }

            if (value.TryGetProperty("value", out JsonElement amount))
            {
                ReadExpression(amount, new ExpressionKind(ExprType.Number, Roots.Self), $"{path}.value");
            }
            else
            {
                Error("definition.field-required", path, "A modifier needs \"value\": a number expression added to the stat or check roll.");
            }
        }

        private void CheckTableRows(JsonElement root)
        {
            JsonElement keys = root.GetProperty("keys");
            string valueType = root.GetProperty("value").GetString()!;
            int keyCount = keys.GetArrayLength();
            if (keyCount == 0)
            {
                Error("definition.field-value", "$.keys", "A table needs at least one key.");
                return;
            }

            int index = 0;
            foreach (JsonElement row in root.GetProperty("rows").EnumerateArray())
            {
                string at = $"$.rows[{index}]";
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != keyCount + 1)
                {
                    Error("definition.table-row", at, $"Each row must be an array of {keyCount} key{(keyCount == 1 ? "" : "s")} followed by the value, like [{string.Join(", ", Enumerable.Repeat("key", keyCount))}, value].");
                    index++;
                    continue;
                }

                for (int column = 0; column < keyCount; column++)
                {
                    string keyType = keys[column].GetProperty("type").GetString()!;
                    CheckKeyCell(row[column], keyType, $"{at}[{column}]");
                }

                CheckValueCell(row[keyCount], valueType, $"{at}[{keyCount}]");
                index++;
            }
        }

        private void CheckKeyCell(JsonElement cell, string keyType, string path)
        {
            if (keyType == "text")
            {
                if (cell.ValueKind != JsonValueKind.String)
                {
                    Error("definition.table-row", path, $"This key is text, but the cell is {Show(cell)}.");
                }

                return;
            }

            if (cell.ValueKind == JsonValueKind.Number && cell.TryGetInt32(out _))
            {
                return;
            }

            if (cell.ValueKind != JsonValueKind.String || !NumberKeyRange.TryParse(cell.GetString()!, out _))
            {
                Error("definition.table-row", path, $"This key is a number: use a whole number, \"a-b\" (inclusive) or \"a+\", but the cell is {Show(cell)}.");
            }
        }

        private void CheckValueCell(JsonElement cell, string valueType, string path)
        {
            bool ok = valueType switch
            {
                "number" => cell.ValueKind == JsonValueKind.Number && cell.TryGetDecimal(out _),
                "text" => cell.ValueKind == JsonValueKind.String,
                _ => cell.ValueKind is JsonValueKind.True or JsonValueKind.False,
            };
            if (!ok)
            {
                Error("definition.table-row", path, $"Table values are {valueType} (numbers no larger than {decimal.MaxValue}), but the cell is {Show(cell)}.");
            }
        }

        private void CheckArea(JsonElement root)
        {
            List<string> rows = root.GetProperty("map").EnumerateArray().Select(row => row.GetString()!).ToList();
            List<(int Row, int Column, string Message)> problems = [];
            Campaigns.AreaMap? map = Campaigns.AreaMap.Parse(rows, problems);
            foreach ((int row, int column, string message) in problems)
            {
                Error("area.map", $"$.map[{row}]", $"Column {column}: {message} The map format: {Campaigns.AreaMap.FormatDescription}");
            }

            if (map is null)
            {
                return;
            }

            if (root.TryGetProperty("cells", out JsonElement cells))
            {
                int index = 0;
                HashSet<(int, int)> seen = [];
                foreach (JsonElement cell in cells.EnumerateArray())
                {
                    (int x, int y) = (cell.GetProperty("at")[0].GetInt32(), cell.GetProperty("at")[1].GetInt32());
                    CheckInside(map, x, y, $"$.cells[{index}].at");
                    if (!seen.Add((x, y)))
                    {
                        Error("area.cell", $"$.cells[{index}].at", $"Cell [{x}, {y}] is listed more than once; put all its features in one entry.");
                    }

                    index++;
                }
            }

            foreach (JsonProperty entry in root.GetProperty("entries").EnumerateObject())
            {
                if (entry.Name.Trim().Length == 0)
                {
                    Error("area.entry", "$.entries", "Entry names can't be empty; name each entry, for example \"gate\".");
                    continue;
                }

                JsonElement at = entry.Value.GetProperty("at");
                CheckInside(map, at[0].GetInt32(), at[1].GetInt32(), $"$.entries.{entry.Name}.at");
            }
        }

        private void CheckInside(Campaigns.AreaMap map, int x, int y, string path)
        {
            if (!map.Contains(x, y))
            {
                Error("area.cell", path, $"[{x}, {y}] is outside the map, which is {map.Width} wide and {map.Height} high (x 0 to {map.Width - 1}, y 0 to {map.Height - 1}).");
            }
        }

        private void CheckClassLevels(JsonElement root)
        {
            JsonElement levels = root.GetProperty("levels");
            if (levels.GetArrayLength() == 0)
            {
                Error("definition.field-value", "$.levels", "A class needs at least one level.");
                return;
            }

            int previous = -1;
            int index = 0;
            foreach (JsonElement level in levels.EnumerateArray())
            {
                int xp = level.GetProperty("xp").GetInt32();
                if (index == 0 && xp != 0)
                {
                    Error("definition.field-value", "$.levels[0].xp", "The first level must need 0 experience.");
                }
                else if (xp <= previous)
                {
                    Error("definition.field-value", $"$.levels[{index}].xp", $"Experience must rise with each level, but {xp} is not more than {previous}.");
                }

                previous = xp;
                index++;
            }

            if (root.TryGetProperty("spell_slots", out JsonElement slots) && slots.GetArrayLength() != levels.GetArrayLength())
            {
                Error("definition.field-value", "$.spell_slots",
                    $"spell_slots needs one entry per level ({levels.GetArrayLength()}), but it has {slots.GetArrayLength()}. Use [] for levels without spells.");
            }
        }

        private bool ExpectKind(JsonElement value, JsonValueKind expected, string path, FieldKind kind)
        {
            if (value.ValueKind == expected)
            {
                return true;
            }

            Error("definition.field-type", path, $"Expected {kind.Describe()}, but this is {Show(value)}.");
            return false;
        }

        private static string Show(JsonElement value)
        {
            string raw = value.GetRawText();
            return raw.Length <= 40
                ? $"{JsonFiles.Describe(value.ValueKind)} ({raw})"
                : JsonFiles.Describe(value.ValueKind);
        }

        private void Error(string rule, string path, string message)
        {
            diagnostics.Add(new ModuleDiagnostic(rule, message, module, file, path));
        }
    }
}

/// <summary>A number table key: a single value, an inclusive range or an open-ended "a+".</summary>
public readonly record struct NumberKeyRange(int Low, int High)
{
    public bool Contains(decimal value) => value >= Low && value <= High && value == decimal.Truncate(value);

    public bool Overlaps(NumberKeyRange other) => Low <= other.High && other.Low <= High;

    public override string ToString()
    {
        if (High == int.MaxValue)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Low}+");
        }

        return Low == High
            ? Low.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{Low}-{High}");
    }

    public static bool TryParse(string text, out NumberKeyRange range)
    {
        range = default;
        if (text.EndsWith('+') && TryInt(text[..^1], out int low))
        {
            range = new NumberKeyRange(low, int.MaxValue);
            return true;
        }

        // The separator is the first '-' after the first character, so "-3" and "-5--3" work.
        int dash = text.IndexOf('-', 1);
        if (dash > 0 && TryInt(text[..dash], out int from) && TryInt(text[(dash + 1)..], out int to) && from <= to)
        {
            range = new NumberKeyRange(from, to);
            return true;
        }

        if (dash < 0 && TryInt(text, out int single))
        {
            range = new NumberKeyRange(single, single);
            return true;
        }

        return false;
    }

    public static NumberKeyRange FromCell(JsonElement cell)
    {
        if (cell.ValueKind == JsonValueKind.Number)
        {
            int value = cell.GetInt32();
            return new NumberKeyRange(value, value);
        }

        TryParse(cell.GetString()!, out NumberKeyRange range);
        return range;
    }

    private static bool TryInt(string text, out int value)
    {
        return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }
}
