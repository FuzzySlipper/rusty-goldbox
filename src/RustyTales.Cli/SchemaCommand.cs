using System.Text.Json;
using RustyTales.Core.Authoring;
using RustyTales.Core.Definitions;
using RustyTales.Core.Expressions;
using RustyTales.Core.Modules;
using RustyTales.Core.Rules;

namespace RustyTales.Cli;

/// <summary><c>tales schema [topic]</c>: the module format reference.</summary>
internal static class SchemaCommand
{
    private const string ManifestExample = """
        {
          "format": 1,
          "id": "my-campaign",
          "kind": "campaign",
          "version": "0.1.0",
          "title": "My campaign",
          "requires": [ { "id": "classic", "version": "^0.1.0" }, { "id": "crypt-art", "version": "*", "releases": "github:alice/crypt-art" } ],
          "provenance": "Original content.",
          "releases": "github:alice/my-campaign"
        }
        """;

    private const string WorkspaceExample = """
        {
          "modules": ["modules", "../shared-rules"],
          "authoring": {
            "modules": ["modules/blackapple-brugh", "modules/blackapple-art"],
            "staging": ".tales/staged",
            "exports": "exports"
          }
        }
        """;

    public static int Run(IEnumerable<string> args, Output output, string workingDirectory = ".")
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--module", "--modules", "--extension"], []);
        if (error is null && parsed.Positionals.Count > 1)
        {
            error = "Usage: tales schema [type | kits | workspace | module | expressions | operations | events | media | live-combat] | tales schema --module <path> [--modules <dir>]... [--extension <id>]...";
        }

        string? moduleArg = parsed.Single("--module");
        if (error is null && moduleArg is not null && parsed.Positionals.Count > 0)
        {
            error = "Pass either a schema topic or --module <path>, not both.";
        }

        if (error is null && moduleArg is null && (parsed.All("--modules").Count > 0 || parsed.All("--extension").Count > 0))
        {
            error = "--modules and --extension require --module <path>.";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        if (moduleArg is not null)
        {
            return ScopedModule(moduleArg, parsed, output, workingDirectory);
        }

        string? topic = parsed.Positionals.Count == 0 ? null : parsed.Positionals[0];
        if (topic is null)
        {
            return Overview(output);
        }

        if (topic == "kits")
        {
            return Kits(output);
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

        if (topic == "live-combat")
        {
            return LiveCombat(output);
        }

        DefinitionType? type = DefinitionTypes.Find(topic);
        if (type is null)
        {
            string types = string.Join(", ", DefinitionTypes.All.Select(definition => definition.Name));
            return output.UsageError($"'{topic}' is not a schema topic. Topics: {types}, kits, workspace, module, expressions, operations, events, media, live-combat.");
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
                topics = new[] { "kits", "workspace", "module", "expressions", "operations", "events", "media", "live-combat" },
                scopedDiscovery = "tales schema --module <path> [--modules <dir>]... [--json]",
            });
            return TalesCli.Ok;
        }

        output.Line("A module is a directory with module.json and .json definition files.");
        output.Line("Each definition file holds one definition object or an array of definitions, each with \"type\" (below) and \"id\".");
        output.Line("Definitions may inherit top-level fields with \"extends\", and extension/campaign modules may override required modules' definitions with \"type\": \"patch\".");
        output.Line("Run `tales schema <topic>` for details, or `tales schema --module <path>` for a scoped summary of one ruleset or campaign.");
        output.Line();
        output.Line("Definition types:");
        foreach (DefinitionType type in DefinitionTypes.All)
        {
            output.Line($"  {type.Name,-19} {type.Description}");
        }

        output.Line();
        output.Line("Other topics:");
        output.Line("  kits                Modular genre starter kits for `tales workspace new --kit <id>`.");
        output.Line("  module              The module.json manifest.");
        output.Line("  workspace           The tales.json authoring workspace.");
        output.Line("  expressions         The expression language and its functions.");
        output.Line("  operations          What actions and conditions can do: damage, heal, conditions, checks.");
        output.Line("  events              Campaign event kinds and their fields.");
        output.Line("  media               Asset media kinds, sampling, and slot compatibility.");
        output.Line("  live-combat         Script commands and JSON observations for a suspended fight.");
        return TalesCli.Ok;
    }

    private static int Kits(Output output)
    {
        if (output.Json)
        {
            output.WriteJson(new
            {
                kits = GenreKits.All.Select(kit => new
                {
                    id = kit.Id,
                    title = kit.Title,
                    summary = kit.Summary,
                    combatPresentation = kit.CombatPresentation,
                    partyStyle = kit.PartyStyle,
                    highlights = kit.Highlights,
                    patterns = kit.Patterns,
                    scaffold = $"tales workspace new <dir> --kit {kit.Id}",
                }),
            });
            return TalesCli.Ok;
        }

        output.Line("Genre starter kits scaffold a complete, validated workspace (scoped ruleset, starter art,");
        output.Line("example campaign, smoke playtest script, and kit guide):");
        foreach (GenreKit kit in GenreKits.All)
        {
            output.Line();
            output.Line($"{kit.Id}: {kit.Title}");
            output.Line($"  {kit.Summary}");
            output.Line($"  combat presentation: {kit.CombatPresentation}");
            output.Line($"  party style:         {kit.PartyStyle}");
            output.Line($"  scaffold:            tales workspace new <dir> --kit {kit.Id}");
            output.Line("  highlights:");
            foreach (string highlight in kit.Highlights)
            {
                output.Line($"    - {highlight}");
            }

            output.Line("  reusable patterns:");
            foreach (string pattern in kit.Patterns)
            {
                output.Line($"    - {pattern}");
            }
        }

        return TalesCli.Ok;
    }

    private static int ScopedModule(string moduleArg, Arguments parsed, Output output, string workingDirectory)
    {
        string path = Path.GetFullPath(moduleArg, workingDirectory);
        List<string> searchDirectories = parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList();
        ModuleSet set = ModuleSets.Load(path, searchDirectories, ModuleSets.Extensions(parsed));
        if (!set.IsValid || set.Rules is null)
        {
            return output.ModuleErrors(set);
        }

        RuleSet rules = set.Rules;
        LoadedModule? rulesetModule = set.LoadOrder.FirstOrDefault(module => module.Manifest.Kind == ModuleKind.Ruleset);
        Definition? campaign = rules.OfType(DefinitionTypes.Campaign).FirstOrDefault();
        List<Definition> combats = rules.OfType(DefinitionTypes.Combat).ToList();
        string presentation = campaign?.Json.TryGetProperty("combat_presentation", out JsonElement campPres) == true
            ? campPres.GetString()!
            : combats.FirstOrDefault()?.Json.TryGetProperty("presentation", out JsonElement combPres) == true
                ? combPres.GetString()!
                : "tactical";

        var attributes = rules.OfType(DefinitionTypes.Attribute).Select(def => new
        {
            id = def.Id,
            name = def.Name,
            min = def.Json.GetProperty("min").GetInt32(),
            max = def.Json.GetProperty("max").GetInt32(),
        }).ToList();

        var derived = rules.OfType(DefinitionTypes.Derived).Select(def => new
        {
            id = def.Id,
            name = def.Name,
            type = ExprTypes.Name(rules.Stats[def.Id].Type),
            showOnSheet = def.Json.TryGetProperty("show_on_sheet", out JsonElement show) && show.GetBoolean(),
        }).ToList();

        var tracks = rules.OfType(DefinitionTypes.Track).Select(def => new
        {
            id = def.Id,
            name = def.Name,
            fromLevels = def.Json.TryGetProperty("from_levels", out JsonElement fl) && fl.GetBoolean(),
        }).ToList();

        var currencies = rules.OfType(DefinitionTypes.Currency).Select(def => new { id = def.Id, name = def.Name }).ToList();
        var classes = rules.OfType(DefinitionTypes.Class).Select(def => new { id = def.QualifiedId, name = def.Name, levels = def.Json.GetProperty("levels").GetArrayLength() }).ToList();
        var races = rules.OfType(DefinitionTypes.Race).Select(def => new { id = def.QualifiedId, name = def.Name }).ToList();
        var checks = rules.OfType(DefinitionTypes.Check).Select(def => new { id = def.QualifiedId, name = def.Name, succeeds = def.Json.GetProperty("succeeds").GetString()! }).ToList();
        var conditions = rules.OfType(DefinitionTypes.Condition).Select(def => new { id = def.QualifiedId, name = def.Name }).ToList();
        var actions = rules.OfType(DefinitionTypes.Action).Select(def => new
        {
            id = def.QualifiedId,
            name = def.Name,
            target = def.Json.GetProperty("target").GetString()!,
            parameters = def.Json.TryGetProperty("parameters", out JsonElement parameters)
                ? parameters.EnumerateArray().Select(entry => entry.GetString()!).ToList()
                : [],
        }).ToList();
        var items = rules.OfType(DefinitionTypes.Item).Select(def => new
        {
            id = def.QualifiedId,
            name = def.Name,
            kind = def.Json.GetProperty("kind").GetString()!,
            cost = def.Json.GetProperty("cost").GetDecimal(),
            currency = def.Json.GetProperty("currency").GetString()!,
        }).ToList();
        var spells = rules.OfType(DefinitionTypes.Spell).Select(def => new { id = def.QualifiedId, name = def.Name }).ToList();
        var monsters = rules.OfType(DefinitionTypes.Monster).Select(def => new { id = def.QualifiedId, name = def.Name, xp = def.Json.GetProperty("xp").GetInt32() }).ToList();
        var resting = rules.OfType(DefinitionTypes.Resting).Select(def => new { id = def.QualifiedId, unit = def.Json.GetProperty("unit").GetString()! }).ToList();
        var assets = rules.OfType(DefinitionTypes.Asset).Select(def => new
        {
            id = def.QualifiedId,
            media = Media.MediaOf(def),
            slots = Media.Slots.Where(slot => Media.Problem(slot.Name, def) is null).Select(slot => slot.Name).ToList(),
        }).ToList();
        var skins = rules.OfType(DefinitionTypes.Skin).Select(def => new { id = def.QualifiedId, name = def.Name }).ToList();
        var variables = rules.Variables.Values.Concat(rules.AreaVariables.Values).Select(def => new
        {
            id = def.Id,
            scope = def.Json.TryGetProperty("scope", out JsonElement scope) ? scope.GetString()! : "campaign",
            type = def.Json.GetProperty("value_type").GetString()!,
        }).ToList();

        if (output.Json)
        {
            output.WriteJson(new
            {
                ok = true,
                module = new { id = set.Root!.Id, kind = ModuleKinds.Name(set.Root.Kind), version = set.Root.Version.ToString() },
                ruleset = rulesetModule?.Manifest.Id,
                combatPresentation = presentation,
                hasStartingParty = campaign?.Json.TryGetProperty("starting_party", out _) == true,
                combats = combats.Select(def => new
                {
                    id = def.QualifiedId,
                    hasField = def.Json.TryGetProperty("field", out _),
                    budgets = def.Json.GetProperty("budget").EnumerateArray().Select(entry => entry.GetProperty("id").GetString()!).ToList(),
                    track = def.Json.GetProperty("track").GetString()!,
                }),
                attributes,
                derived,
                tracks,
                currencies,
                classes,
                races,
                checks,
                conditions,
                actions,
                items,
                spells,
                monsters,
                resting,
                assets,
                skins,
                variables,
                authoringTips = new[]
                {
                    "Group related definitions into JSON arrays in a single .json file (e.g. events.json, monsters.json).",
                    "Use \"extends\": \"<id>\" or \"<module>:<id>\" to derive variants without copying unchanged fields.",
                    "Use \"type\": \"patch\" in an extension or campaign module to override fields on a required module's definition.",
                },
            });
            return TalesCli.Ok;
        }

        output.Line($"Scoped schema for {set.Root!.Id} ({ModuleKinds.Name(set.Root.Kind)}, ruleset: {rulesetModule?.Manifest.Id ?? "none"})");
        output.Line($"  combat presentation: {presentation}");
        if (campaign is not null)
        {
            string partyMode = campaign.Json.TryGetProperty("starting_party", out JsonElement sp)
                ? $"fixed starting_party ({sp.GetArrayLength()} NPCs)"
                : $"custom party ({campaign.Json.GetProperty("party").GetProperty("min").GetInt32()}..{campaign.Json.GetProperty("party").GetProperty("max").GetInt32()})";
            output.Line($"  party mode:          {partyMode}");
        }

        WriteIdList(output, "attributes", attributes.Select(a => $"{a.id} ({a.min}..{a.max})"));
        WriteIdList(output, "derived", derived.Select(d => d.showOnSheet ? $"{d.id} [{d.type}, sheet]" : $"{d.id} [{d.type}]"));
        WriteIdList(output, "tracks", tracks.Select(t => t.fromLevels ? $"{t.id} (from_levels)" : t.id));
        WriteIdList(output, "currencies", currencies.Select(c => c.id));
        WriteIdList(output, "classes", classes.Select(c => $"{c.id} ({c.levels} lvls)"));
        WriteIdList(output, "races", races.Select(r => r.id));
        WriteIdList(output, "checks", checks.Select(c => $"{c.id} ({c.succeeds})"));
        WriteIdList(output, "conditions", conditions.Select(c => c.id));
        WriteIdList(output, "actions", actions.Select(a => a.parameters.Count == 0 ? $"{a.id} [{a.target}]" : $"{a.id} [{a.target}; params: {string.Join(", ", a.parameters)}]"));
        WriteIdList(output, "items", items.Select(i => $"{i.id} ({i.kind}, {i.cost} {i.currency})"));
        WriteIdList(output, "spells", spells.Select(s => s.id));
        WriteIdList(output, "monsters", monsters.Select(m => $"{m.id} ({m.xp} xp)"));
        WriteIdList(output, "resting", resting.Select(r => $"{r.id} ({r.unit})"));
        WriteIdList(output, "assets", assets.Select(a => $"{a.id} [{a.media}: {string.Join("/", a.slots)}]"));
        WriteIdList(output, "skins", skins.Select(s => s.id));
        WriteIdList(output, "variables", variables.Select(v => $"{v.scope}.var.{v.id} ({v.type})"));
        output.Line();
        output.Line("Authoring tips: use multi-definition JSON arrays, \"extends\" for variants, and \"type\": \"patch\" for overrides.");
        return TalesCli.Ok;
    }

    private static void WriteIdList(Output output, string label, IEnumerable<string> entries)
    {
        List<string> list = entries.ToList();
        if (list.Count == 0)
        {
            return;
        }

        output.Line($"  {label,-12} {string.Join(", ", list)}");
    }

    private static int Type(Output output, DefinitionType type)
    {
        List<Field> fields = [.. DefinitionType.FieldsFor(type)];
        if (output.Json)
        {
            output.WriteJson(new
            {
                name = type.Name,
                description = type.Description,
                fields = fields.Select(FieldJson),
                example = System.Text.Json.JsonDocument.Parse(type.Example).RootElement,
            });
            return TalesCli.Ok;
        }

        output.Line($"{type.Name}: {type.Description}");
        output.Line();
        output.Line("Fields:");
        WriteFields(output, fields, "  ");
        output.Line();
        output.Line("Example:");
        output.Line(type.Example.Trim());
        return TalesCli.Ok;
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
            return TalesCli.Ok;
        }

        output.Line($"{ManifestReader.FileName}: the module manifest. Every field is required except {string.Join(", ", ManifestReader.OptionalFields)}.");
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
        return TalesCli.Ok;
    }

    private static int Workspace(Output output)
    {
        if (output.Json)
        {
            output.WriteJson(new
            {
                name = "workspace",
                file = "tales.json",
                fields = new object[]
                {
                    new
                    {
                        name = "modules",
                        required = true,
                        kind = "array of directory paths",
                        description = "Dependency search directories, relative to tales.json or absolute. Existing module commands use this list.",
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
                editableDirectories = RustyTales.Core.Authoring.Workspace.EditableDirectoryNames,
                generatedDirectories = new[] { ".tales/staged", "exports" },
                commands = new object[]
                {
                    new { name = "workspace build", description = "Validate each explicit authored module and replace staging with its runtime files." },
                    new { name = "workspace export", description = "Build first, then pack each staged module independently into exports as an Engine container." },
                    new { name = "workspace install", description = "Build first, then pack each staged module into the Game's module library, where the title screen's Refresh finds it." },
                },
                example = System.Text.Json.JsonDocument.Parse(WorkspaceExample).RootElement,
            });
            return TalesCli.Ok;
        }

        output.Line("tales.json workspace: dependency paths plus an optional authoring layout.");
        output.Line();
        output.Line("Fields:");
        output.Line("  modules (required): array of dependency search directories, relative to tales.json or absolute.");
        output.Line("  authoring (optional): object describing authored runtime module sources and generated outputs.");
        output.Line("    modules (required): explicit module source directories; canon, prompts and art stay outside them.");
        output.Line("    staging (required): generated clean staging directory, commonly .tales/staged.");
        output.Line("    exports (required): generated exported container directory, commonly exports.");
        output.Line("Run `tales workspace build` to validate and stage only authoring.modules; run `tales workspace export` to pack each staged module independently, or `tales workspace install` to pack them into the Game's module library.");
        output.Line("Build reports included runtime files and unresolved dependencies as JSON. A generated root must stay outside module sources and editable roots before it can be cleaned.");
        output.Line();
        output.Line("Editable roots created by `tales workspace new`: canon, art/references, art/accepted, art/rejected, prompts, scripts.");
        output.Line("These roots are discoverable conventions; only runtime module directories and their required licence/provenance files are distributed.");
        output.Line();
        output.Line("Example:");
        output.Line(WorkspaceExample.Trim());
        return TalesCli.Ok;
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
            return TalesCli.Ok;
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

        return TalesCli.Ok;
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
            return TalesCli.Ok;
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

        return TalesCli.Ok;
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
            return TalesCli.Ok;
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
        output.Line("Fields: `tales schema asset`.");
        return TalesCli.Ok;
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
            return TalesCli.Ok;
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
        output.Line("Try one with `tales eval \"<expression>\" --module <path> --context '{\"self\": {...}}'`.");
        return TalesCli.Ok;
    }

    private const string LiveCombatExample = """
        {
          "command": "combat action side-1-member-1 classic:melee_attack --target side-2-member-1",
          "combat": {
            "accepted": true,
            "phase": "AwaitingAction",
            "round": 1,
            "activeActorId": "side-1-member-1",
            "pendingDecision": {
              "id": "decision:side-1-member-1:1:0",
              "kind": "Action",
              "actorId": "side-1-member-1",
              "actions": [
                {
                  "id": "side-1-member-1/use/0/classic:melee_attack",
                  "actionId": "classic:melee_attack",
                  "name": "Melee attack",
                  "cost": { "action": 1 },
                  "targets": [
                    { "id": "side-2-member-1", "name": "Target", "side": 1, "defeated": false, "escaped": false, "position": { "x": 2, "y": 1 } }
                  ],
                  "moves": []
                }
              ]
            },
            "combatants": [
              { "id": "side-1-member-1", "side": 0, "controller": "Manual", "position": { "x": 1, "y": 1 } }
            ]
          }
        }
        """;

    private static int LiveCombat(Output output)
    {
        if (output.Json)
        {
            output.WriteJson(new
            {
                name = "live-combat",
                description = "The read-only observation and command surface for a campaign combat suspended between turns.",
                commands = new[]
                {
                    new { command = "combat inspect", description = "Observe phase, active actor, legal actions, targets, moves, budgets and tracks without advancing." },
                    new { command = "combat control <actor-id> auto|manual", description = "Change the controller for one stable Core actor ID." },
                    new { command = "combat action <actor-id> <action-id> [target-id...] [--target <id>]... [--targets <id>,...] [--path <x,y;x,y>]", description = "Submit one legal Core action and explicit targets/path." },
                    new { command = "combat move <actor-id> <action-id> <target-id> <x,y;x,y>", description = "Submit an authored movement action and path exactly as written." },
                    new { command = "combat end-turn <actor-id>", description = "End the active actor's turn." },
                    new { command = "combat decide <decision-id> [option-id]", description = "Accept or decline a pending interrupt/post-roll option." },
                    new { command = "combat auto-step", description = "Advance the active manual actor through one automatic Core turn, then restore manual control." },
                },
                path = "A path is a semicolon-separated list of x,y cells. Each offered move lists targetIds; choose a path associated with the action's first selected target. A mismatched pair is refused before movement, spending or random price commitment. IDs and choices come from combat.pendingDecision; action IDs are stable machine IDs and do not include display names. CLI does not resolve rules.",
                spellCosts = "SpellCosts contains known or committed resource prices. A random price remains null until a legal spell is selected, then is retained across save/resume. An accepted selection can report an unaffordable price without casting or spending; inspect the updated choice before retrying.",
                example = System.Text.Json.JsonDocument.Parse(LiveCombatExample).RootElement,
                trace = "tales play ... --trace adds a side-effect-free decision projection; observation never rolls dice.",
            });
            return TalesCli.Ok;
        }

        output.Line("Live combat commands are script lines under `tales play`:");
        output.Line($"  {CombatScript.Usage}");
        output.Line();
        output.Line("Use `combat inspect` to discover stable actor/action/target IDs. Paths are semicolon-separated x,y cells; submit the copied machine IDs.");
        output.Line("Each offered move lists targetIds. Choose the target first and use its associated path; mismatched pairs are refused before movement, spending or random price commitment.");
        output.Line("`--combat-control manual` suspends the party at a decision; enemies remain automatic. The default is automatic.");
        output.Line("`combat auto-step` assists one manual turn and restores manual control; use `combat control <actor-id> auto` for a persistent takeover.");
        output.Line("Add `--trace` for a side-effect-free decision projection; inspection and trace do not roll dice.");
        output.Line("SpellCosts contains known or committed prices; a random price stays null until a legal spell is selected. An accepted unaffordable price leaves resources unchanged, and retries use the same saved quote.");
        output.Line();
        output.Line("Example JSON observation:");
        output.Line(LiveCombatExample.Trim());
        return TalesCli.Ok;
    }
}
