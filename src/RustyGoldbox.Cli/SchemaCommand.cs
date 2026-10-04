using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Cli;

/// <summary><c>goldbox schema [topic]</c>: the module format reference.</summary>
internal static class SchemaCommand
{
    private const string ManifestExample = """
        {
          "format": 1,
          "id": "my-campaign",
          "kind": "campaign",
          "version": "0.1.0",
          "title": "My campaign",
          "requires": [ { "id": "classic", "version": "^0.1.0" }, { "id": "crypt-art", "version": "*" } ],
          "provenance": "Original content."
        }
        """;

    private const string WorkspaceExample = """
        {
          "modules": ["modules", "../shared-rules"],
          "authoring": {
            "modules": ["modules/blackapple-campaign", "modules/blackapple-art"],
            "staging": ".goldbox/staged",
            "exports": "exports"
          }
        }
        """;

    public static int Run(IEnumerable<string> args, Output output)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, [], []);
        if (error is null && parsed.Positionals.Count > 1)
        {
            error = "Usage: goldbox schema [type | workspace | module | expressions | operations | events | media]";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        string? topic = parsed.Positionals.Count == 0 ? null : parsed.Positionals[0];
        if (topic is null)
        {
            return Overview(output);
        }

        if (topic == "module")
        {
            return Manifest(output);
        }

        if (topic == "workspace")
        {
            return Workspace(output);
        }

        if (topic == "expressions")
        {
            return Expressions(output);
        }

        if (topic == "operations")
        {
            return Operations(output);
        }

        if (topic == "events")
        {
            return Events(output);
        }

        if (topic == "media")
        {
            return MediaTopic(output);
        }

        DefinitionType? type = DefinitionTypes.Find(topic);
        if (type is null)
        {
            string types = string.Join(", ", DefinitionTypes.All.Select(definition => definition.Name));
            return output.UsageError($"'{topic}' is not a schema topic. Topics: {types}, workspace, module, expressions, operations, events, media.");
        }

        return Type(output, type);
    }

    private static int Overview(Output output)
    {
        if (output.Json)
        {
            output.WriteJson(new
            {
                types = DefinitionTypes.All.Select(type => new { name = type.Name, description = type.Description }),
                topics = new[] { "workspace", "module", "expressions", "operations", "events", "media" },
            });
            return GoldboxCli.Ok;
        }

        output.Line("A module is a directory with module.json and one definition per .json file.");
        output.Line("Each definition file has \"type\" (below) and \"id\". Run `goldbox schema <topic>` for details.");
        output.Line();
        output.Line("Definition types:");
        foreach (DefinitionType type in DefinitionTypes.All)
        {
            output.Line($"  {type.Name,-19} {type.Description}");
        }

        output.Line();
        output.Line("Other topics:");
        output.Line("  module              The module.json manifest.");
        output.Line("  workspace           The goldbox.json authoring workspace.");
        output.Line("  expressions         The expression language and its functions.");
        output.Line("  operations          What actions and conditions can do: damage, heal, conditions, checks.");
        output.Line("  events              Campaign event kinds and their fields.");
        return GoldboxCli.Ok;
    }

    private static int Type(Output output, DefinitionType type)
    {
        List<Field> fields = [.. DefinitionType.CommonFields, .. type.Fields];
        if (output.Json)
        {
            output.WriteJson(new
            {
                name = type.Name,
                description = type.Description,
                fields = fields.Select(FieldJson),
                example = System.Text.Json.JsonDocument.Parse(type.Example).RootElement,
            });
            return GoldboxCli.Ok;
        }

        output.Line($"{type.Name}: {type.Description}");
        output.Line();
        output.Line("Fields:");
        WriteFields(output, fields, "  ");
        output.Line();
        output.Line("Example:");
        output.Line(type.Example.Trim());
        return GoldboxCli.Ok;
    }

    private static void WriteFields(Output output, IReadOnlyList<Field> fields, string indent)
    {
        foreach (Field field in fields)
        {
            string required = field.Required ? "required" : "optional";
            output.Line($"{indent}{field.Name} ({required}): {field.Kind.Describe()}");
            output.Line($"{indent}    {field.Description}");
            IReadOnlyList<Field>? nested = field.Kind switch
            {
                ObjectKind obj => obj.Fields,
                ListKind { Item: ObjectKind obj } => obj.Fields,
                _ => null,
            };
            if (nested is not null)
            {
                WriteFields(output, nested, indent + "    ");
            }
        }
    }

    private static object FieldJson(Field field)
    {
        IReadOnlyList<Field>? nested = field.Kind switch
        {
            ObjectKind obj => obj.Fields,
            ListKind { Item: ObjectKind obj } => obj.Fields,
            _ => null,
        };
        return new
        {
            name = field.Name,
            required = field.Required,
            kind = field.Kind.Describe(),
            description = field.Description,
            fields = nested?.Select(FieldJson),
        };
    }

    private static int Manifest(Output output)
    {
        if (output.Json)
        {
            output.WriteJson(new
            {
                file = ManifestReader.FileName,
                fields = ManifestReader.Fields.Select(field => new { name = field.Name, description = field.Description }),
                ranges = VersionRange.FormatDescription,
                example = System.Text.Json.JsonDocument.Parse(ManifestExample).RootElement,
            });
            return GoldboxCli.Ok;
        }

        output.Line($"{ManifestReader.FileName}: the module manifest. Every field is required.");
        output.Line();
        foreach ((string name, string description) in ManifestReader.Fields)
        {
            output.Line($"  {name,-11} {description}");
        }

        output.Line();
        output.Line($"Version ranges: {VersionRange.FormatDescription}.");
        output.Line("Kinds: a ruleset may require only assets modules; an extension exactly one ruleset;");
        output.Line("an assets module only assets modules; a campaign exactly one ruleset and at least one assets module.");
        output.Line();
        output.Line("Example:");
        output.Line(ManifestExample.Trim());
        return GoldboxCli.Ok;
    }

    private static int Workspace(Output output)
    {
        if (output.Json)
        {
            output.WriteJson(new
            {
                name = "workspace",
                file = "goldbox.json",
                fields = new object[]
                {
                    new
                    {
                        name = "modules",
                        required = true,
                        kind = "array of directory paths",
                        description = "Dependency search directories, relative to goldbox.json or absolute. Existing module commands use this list.",
                    },
                    new
                    {
                        name = "authoring",
                        required = false,
                        kind = "object",
                        description = "Optional authoring source and generated-output locations. Its modules are explicit runtime module directories, not extra dependency search paths.",
                        fields = new object[]
                        {
                            new { name = "modules", required = true, kind = "array of directory paths", description = "Authored module source directories; keep canon, prompts and reference art outside these directories." },
                            new { name = "staging", required = true, kind = "directory path", description = "Generated clean module staging root." },
                            new { name = "exports", required = true, kind = "directory path", description = "Generated exported content root." },
                        },
                    },
                },
                editableDirectories = RustyGoldbox.Core.Authoring.Workspace.EditableDirectoryNames,
                generatedDirectories = new[] { ".goldbox/staged", "exports" },
                commands = new object[]
                {
                    new { name = "workspace build", description = "Validate each explicit authored module and replace staging with its runtime files." },
                    new { name = "workspace export", description = "Build first, then pack each staged module independently into exports as an Engine container." },
                },
                example = System.Text.Json.JsonDocument.Parse(WorkspaceExample).RootElement,
            });
            return GoldboxCli.Ok;
        }

        output.Line("goldbox.json workspace: dependency paths plus an optional authoring layout.");
        output.Line();
        output.Line("Fields:");
        output.Line("  modules (required): array of dependency search directories, relative to goldbox.json or absolute.");
        output.Line("  authoring (optional): object describing authored runtime module sources and generated outputs.");
        output.Line("    modules (required): explicit module source directories; canon, prompts and art stay outside them.");
        output.Line("    staging (required): generated clean staging directory, commonly .goldbox/staged.");
        output.Line("    exports (required): generated exported container directory, commonly exports.");
        output.Line("Run `goldbox workspace build` to validate and stage only authoring.modules; run `goldbox workspace export` to pack each staged module independently.");
        output.Line("Build reports included runtime files and unresolved dependencies as JSON. A generated root must stay outside module sources and editable roots before it can be cleaned.");
        output.Line();
        output.Line("Editable roots created by `goldbox workspace new`: canon, art/references, art/accepted, art/rejected, prompts, scripts.");
        output.Line("These roots are discoverable conventions; only runtime module directories and their required licence/provenance files are distributed.");
        output.Line();
        output.Line("Example:");
        output.Line(WorkspaceExample.Trim());
        return GoldboxCli.Ok;
    }

    private static int Operations(Output output)
    {
        if (output.Json)
        {
            output.WriteJson(new
            {
                operations = OperationTypes.All.Select(operation => new
                {
                    name = operation.Name,
                    description = operation.Description,
                    fields = operation.Fields.Select(FieldJson),
                    example = System.Text.Json.JsonDocument.Parse(operation.Example).RootElement,
                }),
            });
            return GoldboxCli.Ok;
        }

        output.Line("Operations are the only way rules data changes combat state. Actions list them in");
        output.Line("\"outcomes\" (per check tier) and \"always\"; conditions in \"each_turn\". Each is");
        output.Line("{ \"op\": <name>, ...fields }, and its expressions may read self, target and use");
        output.Line("(and check, inside outcomes).");
        foreach (DefinitionType operation in OperationTypes.All)
        {
            output.Line();
            output.Line($"{operation.Name}: {operation.Description}");
            WriteFields(output, operation.Fields, "  ");
            output.Line($"  Example: {operation.Example}");
        }

        return GoldboxCli.Ok;
    }

    private static int Events(Output output)
    {
        if (output.Json)
        {
            output.WriteJson(new
            {
                kinds = EventTypes.All.Select(kind => new
                {
                    name = kind.Name,
                    description = kind.Description,
                    fields = kind.Fields.Select(FieldJson),
                    example = System.Text.Json.JsonDocument.Parse(kind.Example).RootElement,
                }),
            });
            return GoldboxCli.Ok;
        }

        output.Line("An event definition is { \"type\": \"event\", \"id\", \"kind\", ...the kind's fields }. Events chain by");
        output.Line("naming the next event; cells and menus start chains. Expressions in events may read campaign.var.<name> or area.var.<name>.");
        foreach (DefinitionType kind in EventTypes.All)
        {
            output.Line();
            output.Line($"{kind.Name}: {kind.Description}");
            WriteFields(output, kind.Fields, "  ");
            output.Line($"  Example: {kind.Example}");
        }

        return GoldboxCli.Ok;
    }

    private const string ImageExample = """{ "type": "asset", "id": "tavern", "media": "image", "file": "pictures/tavern.png" }""";

    private const string IllustratedExample = """{ "type": "asset", "id": "wylda", "media": "image", "file": "pictures/wylda.png", "sampling": "linear" }""";

    private const string AudioExample = """{ "type": "asset", "id": "creak", "media": "audio", "file": "sounds/creak.ogg" }""";

    private const string SheetExample = """{ "type": "asset", "id": "fire", "media": "sheet", "file": "pictures/fire.png", "frame_size": [64, 64], "animations": { "burn": { "frames": [0, 1, 2, 3], "fps": 8 } } }""";

    private static int MediaTopic(Output output)
    {
        if (output.Json)
        {
            output.WriteJson(new
            {
                media = Media.Types,
                sampling = new { modes = Media.SamplingModes, defaultMode = Media.SamplingModes[0] },
                slots = Media.Slots.Select(slot => new { name = slot.Name, description = slot.Description, accepts = slot.Accepts }),
                examples = new[] { ImageExample, IllustratedExample, SheetExample, AudioExample }.Select(example => System.Text.Json.JsonDocument.Parse(example).RootElement),
            });
            return GoldboxCli.Ok;
        }

        output.Line("An asset says what its file is (its media); a reference names the slot it fills, and the slot");
        output.Line("decides which media fit. Anything that shows a picture shows any visual media.");
        output.Line();
        output.Line("Media:");
        output.Line("  image  one picture; optional named \"regions\" (pixel rectangles) for uses that need them.");
        output.Line("  sheet  equal frames (\"frame_size\"), optionally animated; \"faces\", \"anchor\" and \"height\" let it stand in the world.");
        output.Line("  visual sampling  omit \"sampling\" or use \"nearest\" for pixel art; use \"linear\" for smoothly scaled illustrations.");
        output.Line($"  audio  a sound or music: {string.Join(", ", Media.AudioFormats)}.");
        output.Line();
        output.Line("Slots:");
        foreach (Media.Slot slot in Media.Slots)
        {
            output.Line($"  {slot.Name}: {slot.Description}");
            output.Line($"    accepts {slot.Accepts}.");
        }

        output.Line();
        output.Line($"Example image: {ImageExample}");
        output.Line($"Example illustrated image: {IllustratedExample}");
        output.Line($"Example sheet: {SheetExample}");
        output.Line($"Example audio: {AudioExample}");
        output.Line("Fields: `goldbox schema asset`.");
        return GoldboxCli.Ok;
    }

    private static int Expressions(Output output)
    {
        if (output.Json)
        {
            output.WriteJson(new
            {
                topics = ExpressionReference.Topics.Select(topic => new { topic = topic.Topic, text = topic.Text }),
                functions = ExpressionFunctions.All.Select(function => new { name = function.Name, signature = function.Signature, description = function.Description }),
                example = ExpressionReference.Example,
            });
            return GoldboxCli.Ok;
        }

        output.Line("Expressions are strings in definition fields, such as \"1d20 + self.str_to_hit\".");
        output.Line();
        foreach ((string topic, string text) in ExpressionReference.Topics)
        {
            output.Line($"  {topic,-12} {text}");
        }

        output.Line();
        output.Line("Functions:");
        foreach (ExpressionFunction function in ExpressionFunctions.All)
        {
            output.Line($"  {function.Signature,-22} {function.Description}");
        }

        output.Line();
        output.Line($"Example: {ExpressionReference.Example}");
        output.Line("Try one with `goldbox eval \"<expression>\" --module <path> --context '{\"self\": {...}}'`.");
        return GoldboxCli.Ok;
    }
}
