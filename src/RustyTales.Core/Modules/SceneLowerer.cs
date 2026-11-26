using System.Text.Json;
using System.Text.Json.Nodes;
using RustyTales.Core.Definitions;

namespace RustyTales.Core.Modules;

/// <summary>
/// Statically validates a <c>scene</c> definition's node graph (target existence,
/// reachability, unguarded menus, and unconditional loops) and lowers it
/// deterministically into standard <c>event</c> definitions whose diagnostics
/// map back to the exact <c>scene</c> file and JSON path.
/// </summary>
internal static class SceneLowerer
{
    private static readonly string[] TerminalFields = ["choices", "check", "combat", "teleport", "goto", "event", "end"];
    private static readonly string[] EffectFields = ["set", "give", "take", "open", "xp", "treasure"];
    private static readonly string[] ActionFields = ["branch", "say", "set", "give", "take", "open", "xp", "treasure", "choices", "check", "combat", "teleport", "goto", "event", "end"];

    public static List<Definition> Lower(Definition scene, List<ModuleDiagnostic> diagnostics)
    {
        int errorsBefore = diagnostics.Count;
        string start = scene.Json.GetProperty("start").GetString()!;
        JsonElement nodesElement = scene.Json.GetProperty("nodes");

        Dictionary<string, JsonElement> nodes = new(StringComparer.Ordinal);
        foreach (JsonProperty property in nodesElement.EnumerateObject())
        {
            if (!DefinitionIds.IsValid(property.Name))
            {
                Error(scene, diagnostics, "scene.node-id", $"$.nodes.{property.Name}",
                    $"'{property.Name}' is not a valid scene node ID. Use {DefinitionIds.FormatDescription}.");
                continue;
            }

            nodes[property.Name] = property.Value;
        }

        if (nodes.Count == 0)
        {
            Error(scene, diagnostics, "scene.nodes", "$.nodes", "A scene needs at least one node in \"nodes\".");
            return [];
        }

        if (!nodes.ContainsKey(start))
        {
            Error(scene, diagnostics, "scene.start", "$.start",
                $"Start node '{start}' is not in \"nodes\". Nodes: {string.Join(", ", nodes.Keys)}.");
            return [];
        }

        Dictionary<string, List<string>> edges = nodes.Keys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);
        Dictionary<string, string?> directGoto = [];

        foreach ((string nodeId, JsonElement node) in nodes)
        {
            string nodePath = $"$.nodes.{nodeId}";
            ValidateNode(scene, nodeId, node, nodePath, nodes, edges[nodeId], directGoto, diagnostics);
        }

        if (diagnostics.Count > errorsBefore)
        {
            return [];
        }

        // Reachability check from start.
        HashSet<string> reachable = [start];
        Queue<string> queue = new();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            foreach (string next in edges[current])
            {
                if (reachable.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        foreach (string nodeId in nodes.Keys.Where(id => !reachable.Contains(id)))
        {
            Error(scene, diagnostics, "scene.unreachable", $"$.nodes.{nodeId}",
                $"Node '{nodeId}' in scene '{scene.Id}' cannot be reached from start node '{start}'. Link to it or remove it.");
        }

        // Unconditional non-pausing loop check.
        HashSet<string> reportedLoop = new(StringComparer.Ordinal);
        foreach (string nodeId in nodes.Keys)
        {
            if (!directGoto.ContainsKey(nodeId) || reportedLoop.Contains(nodeId))
            {
                continue;
            }

            List<string> trail = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            string? cursor = nodeId;
            while (cursor is not null && directGoto.TryGetValue(cursor, out string? next))
            {
                if (!seen.Add(cursor))
                {
                    int cycleStart = trail.IndexOf(cursor);
                    List<string> cycle = trail.Skip(cycleStart).Append(cursor).ToList();
                    foreach (string member in cycle)
                    {
                        reportedLoop.Add(member);
                    }

                    Error(scene, diagnostics, "scene.loop", $"$.nodes.{cursor}",
                        $"Scene nodes form an unconditional non-pausing loop ({string.Join(" -> ", cycle)}). Add choices, a branch exit, or end the chain.");
                    break;
                }

                trail.Add(cursor);
                cursor = next;
            }
        }

        if (diagnostics.Count > errorsBefore)
        {
            return [];
        }

        string? defaultSpeaker = scene.Json.TryGetProperty("speaker", out JsonElement speakerEl) ? speakerEl.GetString() : null;
        string? scenePicture = scene.Json.TryGetProperty("picture", out JsonElement pictureEl) ? pictureEl.GetString() : null;

        List<Definition> lowered = [];
        foreach ((string nodeId, JsonElement node) in nodes)
        {
            LowerNode(scene, start, nodeId, node, defaultSpeaker, nodeId == start ? scenePicture : null, lowered, diagnostics);
        }

        return lowered;
    }

    private static void ValidateNode(
        Definition scene,
        string nodeId,
        JsonElement node,
        string nodePath,
        Dictionary<string, JsonElement> nodes,
        List<string> outEdges,
        Dictionary<string, string?> directGoto,
        List<ModuleDiagnostic> diagnostics)
    {
        if (!ActionFields.Any(field => node.TryGetProperty(field, out _)))
        {
            Error(scene, diagnostics, "scene.empty-node", nodePath,
                $"Node '{nodeId}' does nothing. Give it \"say\", \"choices\", an effect, \"branch\", \"check\", \"combat\", \"teleport\", \"goto\", \"event\" or \"end\".");
            return;
        }

        List<string> terminals = TerminalFields.Where(field => node.TryGetProperty(field, out _)).ToList();
        if (terminals.Count > 1)
        {
            Error(scene, diagnostics, "scene.node-terminal", nodePath,
                $"Node '{nodeId}' has more than one continuation/terminal action ({string.Join(", ", terminals)}). Use at most one of: {string.Join(", ", TerminalFields)}.");
        }

        bool hasBranch = node.TryGetProperty("branch", out JsonElement branch);
        if (hasBranch)
        {
            if (branch.GetArrayLength() == 0)
            {
                Error(scene, diagnostics, "scene.branch", $"{nodePath}.branch", "A branch list needs at least one condition entry.");
            }

            for (int i = 0; i < branch.GetArrayLength(); i++)
            {
                JsonElement entry = branch[i];
                string entryPath = $"{nodePath}.branch[{i}]";
                bool hasGoto = entry.TryGetProperty("goto", out JsonElement branchGoto);
                bool hasEvent = entry.TryGetProperty("event", out _);
                if (hasGoto == hasEvent)
                {
                    Error(scene, diagnostics, "scene.branch-target", entryPath, "A branch entry needs exactly one of \"goto\" (a local node) or \"event\" (an external event).");
                }
                else if (hasGoto)
                {
                    CheckLocalTarget(scene, branchGoto.GetString()!, $"{entryPath}.goto", nodes, outEdges, diagnostics);
                }
            }
        }

        bool hasPrompt = node.TryGetProperty("prompt", out _);
        bool hasChoices = node.TryGetProperty("choices", out JsonElement choices);
        bool hasSay = node.TryGetProperty("say", out _);
        bool hasEffects = EffectFields.Any(field => node.TryGetProperty(field, out _));

        if (hasPrompt && !hasChoices)
        {
            Error(scene, diagnostics, "scene.choices", $"{nodePath}.prompt", "\"prompt\" only applies to a node with \"choices\".");
        }

        if (hasChoices)
        {
            if (choices.GetArrayLength() == 0)
            {
                Error(scene, diagnostics, "scene.choices", $"{nodePath}.choices", "A choices list needs at least one choice.");
            }
            else if (choices.EnumerateArray().All(choice => choice.TryGetProperty("when", out _)))
            {
                Error(scene, diagnostics, "scene.menu", $"{nodePath}.choices",
                    "Every choice has a \"when\", so the dialogue could offer nothing and leave the party stuck. Give at least one choice no \"when\".");
            }

            if (!hasPrompt && (!hasSay || hasEffects))
            {
                Error(scene, diagnostics, "scene.choices", nodePath,
                    "Add \"prompt\" for the choices question (required when \"say\" is omitted or effects run before \"choices\").");
            }

            for (int i = 0; i < choices.GetArrayLength(); i++)
            {
                JsonElement choice = choices[i];
                string choicePath = $"{nodePath}.choices[{i}]";
                List<string> choiceTargets = new[] { "goto", "event", "end" }.Where(field => choice.TryGetProperty(field, out _)).ToList();
                if (choiceTargets.Count > 1)
                {
                    Error(scene, diagnostics, "scene.choice-target", choicePath,
                        $"A choice takes at most one of \"goto\", \"event\" or \"end\", but has {string.Join(", ", choiceTargets)}.");
                }

                if (choice.TryGetProperty("goto", out JsonElement choiceGoto))
                {
                    CheckLocalTarget(scene, choiceGoto.GetString()!, $"{choicePath}.goto", nodes, outEdges, diagnostics);
                }
            }
        }

        if (node.TryGetProperty("take", out JsonElement take))
        {
            CheckTargetPair(scene, take, $"{nodePath}.take", "on_refused", "on_refused_event", nodes, outEdges, diagnostics);
        }

        if (node.TryGetProperty("check", out JsonElement check))
        {
            CheckTargetPair(scene, check, $"{nodePath}.check", "on_success", "on_success_event", nodes, outEdges, diagnostics);
            CheckTargetPair(scene, check, $"{nodePath}.check", "on_failure", "on_failure_event", nodes, outEdges, diagnostics);
        }

        if (node.TryGetProperty("combat", out JsonElement combat))
        {
            CheckTargetPair(scene, combat, $"{nodePath}.combat", "on_win", "on_win_event", nodes, outEdges, diagnostics);
            CheckTargetPair(scene, combat, $"{nodePath}.combat", "on_lose", "on_lose_event", nodes, outEdges, diagnostics);
            CheckTargetPair(scene, combat, $"{nodePath}.combat", "on_flee", "on_flee_event", nodes, outEdges, diagnostics);
        }

        if (node.TryGetProperty("teleport", out JsonElement teleport))
        {
            CheckTargetPair(scene, teleport, $"{nodePath}.teleport", "goto", "event", nodes, outEdges, diagnostics);
        }

        if (node.TryGetProperty("goto", out JsonElement nodeGoto))
        {
            string target = nodeGoto.GetString()!;
            CheckLocalTarget(scene, target, $"{nodePath}.goto", nodes, outEdges, diagnostics);
            bool hasTakeRefusal = node.TryGetProperty("take", out JsonElement takeEl)
                && (takeEl.TryGetProperty("on_refused", out _) || takeEl.TryGetProperty("on_refused_event", out _));
            if (!hasBranch && !hasTakeRefusal)
            {
                directGoto[nodeId] = target;
            }
        }
    }

    private static void CheckTargetPair(
        Definition scene,
        JsonElement owner,
        string ownerPath,
        string localField,
        string eventField,
        Dictionary<string, JsonElement> nodes,
        List<string> outEdges,
        List<ModuleDiagnostic> diagnostics)
    {
        bool hasLocal = owner.TryGetProperty(localField, out JsonElement localValue);
        bool hasEvent = owner.TryGetProperty(eventField, out _);
        if (hasLocal && hasEvent)
        {
            Error(scene, diagnostics, "scene.target", ownerPath,
                $"Give at most one of \"{localField}\" (local node) or \"{eventField}\" (external event), not both.");
            return;
        }

        if (hasLocal)
        {
            CheckLocalTarget(scene, localValue.GetString()!, $"{ownerPath}.{localField}", nodes, outEdges, diagnostics);
        }
    }

    private static void CheckLocalTarget(
        Definition scene,
        string target,
        string path,
        Dictionary<string, JsonElement> nodes,
        List<string> outEdges,
        List<ModuleDiagnostic> diagnostics)
    {
        if (!nodes.ContainsKey(target))
        {
            Error(scene, diagnostics, "scene.node-not-found", path,
                $"There is no node '{target}' in scene '{scene.Id}'. Nodes: {string.Join(", ", nodes.Keys)}. Use \"event\" to jump to an external event.");
            return;
        }

        outEdges.Add(target);
    }

    private static void LowerNode(
        Definition scene,
        string startNode,
        string nodeId,
        JsonElement node,
        string? defaultSpeaker,
        string? defaultPicture,
        List<Definition> lowered,
        List<ModuleDiagnostic> diagnostics)
    {
        string nodePath = $"$.nodes.{nodeId}";
        string entryId = EntryId(scene.Id, startNode, nodeId);
        string nodeBase = $"{scene.Id}__{nodeId}";
        List<StepBuilder> steps = [];
        List<StepBuilder> extraSteps = [];

        bool hasBranch = node.TryGetProperty("branch", out JsonElement branch);
        bool hasSay = node.TryGetProperty("say", out JsonElement sayElement);
        bool hasEffects = EffectFields.Any(field => node.TryGetProperty(field, out _));
        bool hasChoices = node.TryGetProperty("choices", out JsonElement choicesElement);
        bool hasPrompt = node.TryGetProperty("prompt", out JsonElement promptElement);
        bool foldSayIntoMenu = hasSay && hasChoices && !hasPrompt && !hasEffects;

        string? nodeSpeaker = node.TryGetProperty("speaker", out JsonElement speakerEl) ? speakerEl.GetString() : defaultSpeaker;
        string? picture = node.TryGetProperty("picture", out JsonElement picEl) ? picEl.GetString() : defaultPicture;
        string? picturePath = node.TryGetProperty("picture", out _) ? $"{nodePath}.picture" : "$.picture";
        string? sound = node.TryGetProperty("sound", out JsonElement soundEl) ? soundEl.GetString() : null;
        string? music = node.TryGetProperty("music", out JsonElement musicEl) ? musicEl.GetString() : null;
        bool mediaAttached = false;

        if (hasBranch)
        {
            JsonObject obj = new() { ["type"] = "event", ["kind"] = "branch" };
            JsonArray branchesArray = new();
            Dictionary<string, FieldOrigin> origins = new() { ["$"] = Origin(scene, $"{nodePath}.branch") };
            for (int i = 0; i < branch.GetArrayLength(); i++)
            {
                JsonElement b = branch[i];
                string bPath = $"{nodePath}.branch[{i}]";
                string nextId = b.TryGetProperty("goto", out JsonElement g)
                    ? EntryId(scene.Id, startNode, g.GetString()!)
                    : b.GetProperty("event").GetString()!;
                branchesArray.Add(new JsonObject
                {
                    ["when"] = b.GetProperty("when").GetString()!,
                    ["next"] = nextId,
                });
                origins[$"$.branches[{i}].when"] = Origin(scene, $"{bPath}.when");
                origins[$"$.branches[{i}].next"] = Origin(scene, b.TryGetProperty("goto", out _) ? $"{bPath}.goto" : $"{bPath}.event");
            }

            obj["branches"] = branchesArray;
            steps.Add(new StepBuilder(obj, origins, IsBranch: true));
        }

        if (hasSay && !foldSayIntoMenu)
        {
            JsonObject obj = new()
            {
                ["type"] = "event",
                ["kind"] = "text",
                ["text"] = FormatLine(nodeSpeaker, sayElement.GetString()!),
            };
            Dictionary<string, FieldOrigin> origins = new()
            {
                ["$"] = Origin(scene, nodePath),
                ["text"] = Origin(scene, $"{nodePath}.say"),
            };
            AttachMedia(scene, obj, origins, picture, picturePath, sound, $"{nodePath}.sound", music, $"{nodePath}.music");
            mediaAttached = true;
            steps.Add(new StepBuilder(obj, origins));
        }

        if (node.TryGetProperty("set", out JsonElement setMap))
        {
            AppendSetSteps(scene, setMap, $"{nodePath}.set", steps);
        }

        if (node.TryGetProperty("give", out JsonElement giveList))
        {
            AppendGiveSteps(scene, giveList, $"{nodePath}.give", steps);
        }

        if (node.TryGetProperty("take", out JsonElement take))
        {
            string takePath = $"{nodePath}.take";
            JsonObject obj = new()
            {
                ["type"] = "event",
                ["kind"] = "take",
                ["item"] = take.GetProperty("item").GetString()!,
            };
            Dictionary<string, FieldOrigin> origins = new()
            {
                ["$"] = Origin(scene, takePath),
                ["item"] = Origin(scene, $"{takePath}.item"),
            };
            if (take.TryGetProperty("count", out JsonElement count))
            {
                obj["count"] = count.GetInt32();
                origins["count"] = Origin(scene, $"{takePath}.count");
            }

            if (ResolveTarget(scene.Id, startNode, take, "on_refused", "on_refused_event", takePath) is (string refusedId, string refusedPath))
            {
                obj["on_refused"] = refusedId;
                origins["on_refused"] = Origin(scene, refusedPath);
            }

            steps.Add(new StepBuilder(obj, origins));
        }

        if (node.TryGetProperty("open", out JsonElement openDoor))
        {
            JsonObject obj = new()
            {
                ["type"] = "event",
                ["kind"] = "open",
                ["door"] = openDoor.GetString()!,
            };
            Dictionary<string, FieldOrigin> origins = new()
            {
                ["$"] = Origin(scene, $"{nodePath}.open"),
                ["door"] = Origin(scene, $"{nodePath}.open"),
            };
            if (!mediaAttached)
            {
                AttachMedia(scene, obj, origins, picture, picturePath, sound, $"{nodePath}.sound", music, $"{nodePath}.music");
                mediaAttached = true;
            }

            steps.Add(new StepBuilder(obj, origins));
        }

        if (node.TryGetProperty("xp", out JsonElement xp))
        {
            string xpPath = $"{nodePath}.xp";
            JsonObject obj = new()
            {
                ["type"] = "event",
                ["kind"] = "experience",
                ["amount"] = JsonNode.Parse(xp.GetProperty("amount").GetRawText()),
            };
            Dictionary<string, FieldOrigin> origins = new()
            {
                ["$"] = Origin(scene, xpPath),
                ["amount"] = Origin(scene, $"{xpPath}.amount"),
            };
            if (xp.TryGetProperty("each", out JsonElement each))
            {
                obj["each"] = each.GetBoolean();
                origins["each"] = Origin(scene, $"{xpPath}.each");
            }

            if (xp.TryGetProperty("text", out JsonElement xpText))
            {
                obj["text"] = xpText.GetString()!;
                origins["text"] = Origin(scene, $"{xpPath}.text");
            }

            steps.Add(new StepBuilder(obj, origins));
        }

        if (node.TryGetProperty("treasure", out JsonElement treasure))
        {
            string trPath = $"{nodePath}.treasure";
            JsonObject obj = new() { ["type"] = "event", ["kind"] = "treasure" };
            Dictionary<string, FieldOrigin> origins = new() { ["$"] = Origin(scene, trPath) };
            if (treasure.TryGetProperty("currency", out JsonElement currency))
            {
                obj["currency"] = currency.GetString()!;
                origins["currency"] = Origin(scene, $"{trPath}.currency");
            }

            if (treasure.TryGetProperty("amount", out JsonElement amount))
            {
                obj["amount"] = JsonNode.Parse(amount.GetRawText());
                origins["amount"] = Origin(scene, $"{trPath}.amount");
            }

            if (treasure.TryGetProperty("items", out JsonElement items))
            {
                obj["items"] = JsonNode.Parse(items.GetRawText());
                origins["items"] = Origin(scene, $"{trPath}.items");
            }

            steps.Add(new StepBuilder(obj, origins));
        }

        string? tailNext = null;
        string? tailNextPath = null;

        if (hasChoices)
        {
            string promptText = hasPrompt
                ? promptElement.GetString()!
                : FormatLine(nodeSpeaker, sayElement.GetString()!);
            JsonObject menuObj = new()
            {
                ["type"] = "event",
                ["kind"] = "menu",
                ["text"] = promptText,
            };
            Dictionary<string, FieldOrigin> menuOrigins = new()
            {
                ["$"] = Origin(scene, nodePath),
                ["text"] = Origin(scene, hasPrompt ? $"{nodePath}.prompt" : $"{nodePath}.say"),
                ["options"] = Origin(scene, $"{nodePath}.choices"),
            };
            if (!mediaAttached)
            {
                AttachMedia(scene, menuObj, menuOrigins, picture, picturePath, sound, $"{nodePath}.sound", music, $"{nodePath}.music");
                mediaAttached = true;
            }

            JsonArray options = new();
            for (int ci = 0; ci < choicesElement.GetArrayLength(); ci++)
            {
                JsonElement choice = choicesElement[ci];
                string choicePath = $"{nodePath}.choices[{ci}]";
                JsonObject opt = new() { ["label"] = choice.GetProperty("label").GetString()! };
                menuOrigins[$"$.options[{ci}].label"] = Origin(scene, $"{choicePath}.label");
                if (choice.TryGetProperty("when", out JsonElement when))
                {
                    opt["when"] = JsonNode.Parse(when.GetRawText());
                    menuOrigins[$"$.options[{ci}].when"] = Origin(scene, $"{choicePath}.when");
                }

                List<StepBuilder> choiceSteps = [];
                if (choice.TryGetProperty("say", out JsonElement cSay))
                {
                    string? cSpeaker = choice.TryGetProperty("speaker", out JsonElement cSpk) ? cSpk.GetString() : nodeSpeaker;
                    choiceSteps.Add(new StepBuilder(
                        new JsonObject
                        {
                            ["type"] = "event",
                            ["kind"] = "text",
                            ["text"] = FormatLine(cSpeaker, cSay.GetString()!),
                        },
                        new Dictionary<string, FieldOrigin>
                        {
                            ["$"] = Origin(scene, choicePath),
                            ["text"] = Origin(scene, $"{choicePath}.say"),
                        }));
                }

                if (choice.TryGetProperty("set", out JsonElement cSet))
                {
                    AppendSetSteps(scene, cSet, $"{choicePath}.set", choiceSteps);
                }

                if (choice.TryGetProperty("give", out JsonElement cGive))
                {
                    AppendGiveSteps(scene, cGive, $"{choicePath}.give", choiceSteps);
                }

                if (choice.TryGetProperty("open", out JsonElement cOpen))
                {
                    choiceSteps.Add(new StepBuilder(
                        new JsonObject
                        {
                            ["type"] = "event",
                            ["kind"] = "open",
                            ["door"] = cOpen.GetString()!,
                        },
                        new Dictionary<string, FieldOrigin>
                        {
                            ["$"] = Origin(scene, $"{choicePath}.open"),
                            ["door"] = Origin(scene, $"{choicePath}.open"),
                        }));
                }

                if (choice.TryGetProperty("end", out JsonElement cEnd))
                {
                    choiceSteps.Add(new StepBuilder(
                        new JsonObject
                        {
                            ["type"] = "event",
                            ["kind"] = "end",
                            ["text"] = cEnd.GetString()!,
                        },
                        new Dictionary<string, FieldOrigin>
                        {
                            ["$"] = Origin(scene, $"{choicePath}.end"),
                            ["text"] = Origin(scene, $"{choicePath}.end"),
                        }));
                }

                string? choiceCont = null;
                string? choiceContPath = null;
                if (choice.TryGetProperty("goto", out JsonElement cGoto))
                {
                    choiceCont = EntryId(scene.Id, startNode, cGoto.GetString()!);
                    choiceContPath = $"{choicePath}.goto";
                }
                else if (choice.TryGetProperty("event", out JsonElement cEvent))
                {
                    choiceCont = cEvent.GetString()!;
                    choiceContPath = $"{choicePath}.event";
                }

                if (choiceSteps.Count > 0)
                {
                    string choiceBase = $"{nodeBase}__c{ci}";
                    AssignIdsAndChain(scene, choiceSteps, choiceBase, choiceBase, choiceCont, choiceContPath);
                    extraSteps.AddRange(choiceSteps);
                    opt["next"] = choiceBase;
                    menuOrigins[$"$.options[{ci}].next"] = Origin(scene, choicePath);
                }
                else if (choiceCont is not null)
                {
                    opt["next"] = choiceCont;
                    menuOrigins[$"$.options[{ci}].next"] = Origin(scene, choiceContPath!);
                }

                options.Add(opt);
            }

            menuObj["options"] = options;
            steps.Add(new StepBuilder(menuObj, menuOrigins));
        }
        else if (node.TryGetProperty("check", out JsonElement check))
        {
            string checkPath = $"{nodePath}.check";
            JsonObject obj = new()
            {
                ["type"] = "event",
                ["kind"] = "check",
                ["check"] = check.GetProperty("check").GetString()!,
                ["member"] = check.TryGetProperty("member", out JsonElement m) ? m.GetInt32() : 1,
            };
            Dictionary<string, FieldOrigin> origins = new()
            {
                ["$"] = Origin(scene, checkPath),
                ["check"] = Origin(scene, $"{checkPath}.check"),
                ["member"] = Origin(scene, check.TryGetProperty("member", out _) ? $"{checkPath}.member" : checkPath),
            };
            if (check.TryGetProperty("modifier", out JsonElement mod))
            {
                obj["modifier"] = JsonNode.Parse(mod.GetRawText());
                origins["modifier"] = Origin(scene, $"{checkPath}.modifier");
            }

            if (ResolveTarget(scene.Id, startNode, check, "on_success", "on_success_event", checkPath) is (string okId, string okPath))
            {
                obj["on_success"] = okId;
                origins["on_success"] = Origin(scene, okPath);
            }

            if (ResolveTarget(scene.Id, startNode, check, "on_failure", "on_failure_event", checkPath) is (string failId, string failPath))
            {
                obj["on_failure"] = failId;
                origins["on_failure"] = Origin(scene, failPath);
            }

            steps.Add(new StepBuilder(obj, origins));
        }
        else if (node.TryGetProperty("combat", out JsonElement combat))
        {
            string combatPath = $"{nodePath}.combat";
            JsonObject obj = new()
            {
                ["type"] = "event",
                ["kind"] = "combat",
                ["encounter"] = combat.GetProperty("encounter").GetString()!,
            };
            Dictionary<string, FieldOrigin> origins = new()
            {
                ["$"] = Origin(scene, combatPath),
                ["encounter"] = Origin(scene, $"{combatPath}.encounter"),
            };
            if (combat.TryGetProperty("combat", out JsonElement combatDef))
            {
                obj["combat"] = combatDef.GetString()!;
                origins["combat"] = Origin(scene, $"{combatPath}.combat");
            }

            if (ResolveTarget(scene.Id, startNode, combat, "on_win", "on_win_event", combatPath) is (string winId, string winPath))
            {
                obj["on_win"] = winId;
                origins["on_win"] = Origin(scene, winPath);
            }

            if (ResolveTarget(scene.Id, startNode, combat, "on_lose", "on_lose_event", combatPath) is (string loseId, string losePath))
            {
                obj["on_lose"] = loseId;
                origins["on_lose"] = Origin(scene, losePath);
            }

            if (ResolveTarget(scene.Id, startNode, combat, "on_flee", "on_flee_event", combatPath) is (string fleeId, string fleePath))
            {
                obj["on_flee"] = fleeId;
                origins["on_flee"] = Origin(scene, fleePath);
            }

            if (!mediaAttached)
            {
                AttachMedia(scene, obj, origins, picture, picturePath, sound, $"{nodePath}.sound", music, $"{nodePath}.music");
                mediaAttached = true;
            }

            steps.Add(new StepBuilder(obj, origins));
        }
        else if (node.TryGetProperty("teleport", out JsonElement teleport))
        {
            string tpPath = $"{nodePath}.teleport";
            JsonObject obj = new()
            {
                ["type"] = "event",
                ["kind"] = "teleport",
                ["area"] = teleport.GetProperty("area").GetString()!,
                ["entry"] = teleport.GetProperty("entry").GetString()!,
            };
            Dictionary<string, FieldOrigin> origins = new()
            {
                ["$"] = Origin(scene, tpPath),
                ["area"] = Origin(scene, $"{tpPath}.area"),
                ["entry"] = Origin(scene, $"{tpPath}.entry"),
            };
            if (ResolveTarget(scene.Id, startNode, teleport, "goto", "event", tpPath) is (string afterId, string afterPath))
            {
                obj["next"] = afterId;
                origins["next"] = Origin(scene, afterPath);
            }

            steps.Add(new StepBuilder(obj, origins));
        }
        else if (node.TryGetProperty("end", out JsonElement endText))
        {
            JsonObject obj = new()
            {
                ["type"] = "event",
                ["kind"] = "end",
                ["text"] = endText.GetString()!,
            };
            Dictionary<string, FieldOrigin> origins = new()
            {
                ["$"] = Origin(scene, $"{nodePath}.end"),
                ["text"] = Origin(scene, $"{nodePath}.end"),
            };
            steps.Add(new StepBuilder(obj, origins));
        }
        else if (node.TryGetProperty("goto", out JsonElement gotoEl))
        {
            tailNext = EntryId(scene.Id, startNode, gotoEl.GetString()!);
            tailNextPath = $"{nodePath}.goto";
        }
        else if (node.TryGetProperty("event", out JsonElement eventEl))
        {
            tailNext = eventEl.GetString()!;
            tailNextPath = $"{nodePath}.event";
        }

        if (steps.Count == 0 && tailNext is not null)
        {
            JsonObject redirect = new()
            {
                ["type"] = "event",
                ["kind"] = "branch",
                ["branches"] = new JsonArray(new JsonObject { ["when"] = "true", ["next"] = tailNext }),
            };
            Dictionary<string, FieldOrigin> origins = new()
            {
                ["$"] = Origin(scene, tailNextPath!),
                ["$.branches[0].next"] = Origin(scene, tailNextPath!),
            };
            steps.Add(new StepBuilder(redirect, origins, IsBranch: true));
            tailNext = null;
        }

        AssignIdsAndChain(scene, steps, entryId, nodeBase, tailNext, tailNextPath);

        foreach (StepBuilder step in steps.Concat(extraSteps))
        {
            using JsonDocument document = JsonDocument.Parse(step.Json.ToJsonString());
            Definition? built = DefinitionReader.Read(
                document.RootElement,
                scene.Module,
                scene.File,
                diagnostics,
                scene.RootPath,
                step.Origins);
            if (built is not null)
            {
                lowered.Add(built);
            }
        }
    }

    private static void AssignIdsAndChain(
        Definition scene,
        List<StepBuilder> steps,
        string firstId,
        string prefix,
        string? tailNext,
        string? tailNextPath)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            steps[i].Json["id"] = i == 0 ? firstId : $"{prefix}__{i}";
        }

        for (int i = 0; i < steps.Count; i++)
        {
            string? nextId = i + 1 < steps.Count ? steps[i + 1].Json["id"]!.GetValue<string>() : tailNext;
            if (nextId is null)
            {
                continue;
            }

            string linkField = steps[i].IsBranch ? "otherwise" : "next";
            steps[i].Json[linkField] = nextId;
            if (i + 1 >= steps.Count && tailNextPath is not null)
            {
                steps[i].Origins[linkField] = Origin(scene, tailNextPath);
            }
        }
    }

    private static void AppendSetSteps(Definition scene, JsonElement setMap, string basePath, List<StepBuilder> steps)
    {
        foreach (JsonProperty entry in setMap.EnumerateObject())
        {
            string entryPath = $"{basePath}.{entry.Name}";
            JsonObject obj = new()
            {
                ["type"] = "event",
                ["kind"] = "set",
                ["variable"] = entry.Name,
                ["value"] = JsonNode.Parse(entry.Value.GetRawText()),
            };
            Dictionary<string, FieldOrigin> origins = new()
            {
                ["$"] = Origin(scene, entryPath),
                ["variable"] = Origin(scene, entryPath),
                ["value"] = Origin(scene, entryPath),
            };
            steps.Add(new StepBuilder(obj, origins));
        }
    }

    private static void AppendGiveSteps(Definition scene, JsonElement giveList, string basePath, List<StepBuilder> steps)
    {
        for (int i = 0; i < giveList.GetArrayLength(); i++)
        {
            JsonElement item = giveList[i];
            string entryPath = $"{basePath}[{i}]";
            JsonObject obj = new()
            {
                ["type"] = "event",
                ["kind"] = "give",
                ["item"] = item.GetProperty("item").GetString()!,
            };
            Dictionary<string, FieldOrigin> origins = new()
            {
                ["$"] = Origin(scene, entryPath),
                ["item"] = Origin(scene, $"{entryPath}.item"),
            };
            if (item.TryGetProperty("count", out JsonElement count))
            {
                obj["count"] = count.GetInt32();
                origins["count"] = Origin(scene, $"{entryPath}.count");
            }

            steps.Add(new StepBuilder(obj, origins));
        }
    }

    private static void AttachMedia(
        Definition scene,
        JsonObject target,
        Dictionary<string, FieldOrigin> origins,
        string? picture,
        string picturePath,
        string? sound,
        string soundPath,
        string? music,
        string musicPath)
    {
        if (picture is not null)
        {
            target["picture"] = picture;
            origins["picture"] = Origin(scene, picturePath);
        }

        if (sound is not null)
        {
            target["sound"] = sound;
            origins["sound"] = Origin(scene, soundPath);
        }

        if (music is not null)
        {
            target["music"] = music;
            origins["music"] = Origin(scene, musicPath);
        }
    }

    private static (string TargetId, string SourcePath)? ResolveTarget(
        string sceneId,
        string startNode,
        JsonElement owner,
        string localField,
        string eventField,
        string ownerPath)
    {
        if (owner.TryGetProperty(localField, out JsonElement localValue))
        {
            return (EntryId(sceneId, startNode, localValue.GetString()!), $"{ownerPath}.{localField}");
        }

        if (owner.TryGetProperty(eventField, out JsonElement eventValue))
        {
            return (eventValue.GetString()!, $"{ownerPath}.{eventField}");
        }

        return null;
    }

    private static string EntryId(string sceneId, string startNode, string nodeId) =>
        string.Equals(nodeId, startNode, StringComparison.Ordinal) ? sceneId : $"{sceneId}__{nodeId}";

    private static string FormatLine(string? speaker, string text) =>
        string.IsNullOrWhiteSpace(speaker) ? text : $"{speaker}: {text}";

    private static FieldOrigin Origin(Definition scene, string scenePath)
    {
        (string module, string file, string jsonPath) = scene.DiagnosticLocation(scenePath);
        return new FieldOrigin(module, file, "$", null, AttributeErrorsToOrigin: true, ExactPath: jsonPath);
    }

    private static void Error(Definition scene, List<ModuleDiagnostic> diagnostics, string rule, string path, string message)
    {
        (string module, string file, string jsonPath) = scene.DiagnosticLocation(path);
        diagnostics.Add(new ModuleDiagnostic(rule, message, module, file, jsonPath));
    }

    private sealed record StepBuilder(JsonObject Json, Dictionary<string, FieldOrigin> Origins, bool IsBranch = false);
}
