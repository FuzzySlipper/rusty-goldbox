using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Game;

/// <summary>
/// The debug readout of a <see cref="GameSession"/>: what the DOM companion
/// shows and the choices it can claim intents for. Built as JSON, then
/// copied into the Engine's structured UI value.
/// </summary>
internal static partial class SessionProjection
{
    /// <param name="imageUrls">Where the panels can show an image asset of a module set, when it can be shown.</param>
    public static JsonObject Build(GameSession session, Func<ModuleSet, Definition, string?>? imageUrls = null)
    {
        imageUrls ??= (_, _) => null;
        Func<Definition, string?> imageUrl = asset => session.Set is ModuleSet set ? imageUrls(set, asset) : null;
        JsonObject projection = new()
        {
            ["screen"] = session.Screen.ToString().ToLowerInvariant(),
            ["status"] = Status(session),
            ["notes"] = Strings(session.Notes),
            ["skin"] = session.ActiveSkin is var (skinSet, skin) ? Skin(skinSet, skin, asset => imageUrls(skinSet, asset)) : null,
            ["skins"] = new JsonArray(session.Skins.Select(choice => (JsonNode)new JsonObject { ["id"] = choice.Id, ["name"] = choice.Name }).ToArray()),
            ["skinPicked"] = session.PickedSkin?.Choice.Id,
            ["volumes"] = new JsonObject { ["music"] = session.MusicVolume, ["sound"] = session.SoundVolume },
            // The panels' proportions in force, whether the player set their own, and what may be set.
            ["layout"] = new JsonObject(session.Layout.Select(part => KeyValuePair.Create(part.Key, (JsonNode?)part.Value))),
            ["layoutPicked"] = session.LayoutPicked is not null,
            ["uiScale"] = session.UiScale,
            ["layoutParts"] = new JsonArray(SkinLayout.Parts.Select(part => (JsonNode)new JsonObject
            {
                ["id"] = part.Name,
                ["name"] = part.Label,
                ["min"] = part.Minimum,
                ["max"] = part.Maximum,
            }).ToArray()),
            ["modules"] = Modules(session.Installer),
            // Hosted play: seats, who plays what and the leader, and which member this player is.
            ["table"] = session.Table?.ToJson(),
            ["you"] = session.Table is null ? null : session.LocalMember,
            ["campaigns"] = new JsonArray(session.Campaigns.Select(campaign => (JsonNode)new JsonObject
            {
                ["bundle"] = campaign.Bundle,
                ["id"] = campaign.Id,
                ["title"] = campaign.Title,
                ["version"] = campaign.Version.ToString(),
                ["extensions"] = new JsonArray(campaign.Extensions.Select(extension => (JsonNode)new JsonObject
                {
                    ["id"] = extension.Id,
                    ["title"] = extension.Title,
                    ["version"] = extension.Version.ToString(),
                }).ToArray()),
            }).ToArray()),
        };

        if (session.Screen != Screen.Title)
        {
            projection["seed"] = session.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            projection["extensions"] = new JsonArray(session.Set!.Extensions.Select(id => (JsonNode)id).ToArray());
        }

        if (session.Screen == Screen.Party)
        {
            RuleSet rules = session.Set!.Rules!;
            JsonElement size = session.Campaign!.Json.GetProperty("party");
            projection["partySize"] = new JsonObject { ["min"] = size.GetProperty("min").GetInt32(), ["max"] = size.GetProperty("max").GetInt32() };
            projection["races"] = Choices(rules, DefinitionTypes.Race);
            projection["classes"] = Choices(rules, DefinitionTypes.Class);
            Creation(rules, projection);
            projection["items"] = Choices(rules, DefinitionTypes.Item);
            projection["portraits"] = new JsonArray(rules.OfType(DefinitionTypes.Asset)
                .Where(asset => asset.Json.TryGetProperty("tags", out JsonElement tags) && tags.EnumerateArray().Any(tag => tag.GetString() == "portrait"))
                .Select(asset => (JsonNode)new JsonObject { ["id"] = asset.QualifiedId, ["name"] = asset.Id, ["picture"] = Picture(rules, asset, imageUrl) }).ToArray());
            projection["party"] = new JsonArray(session.Party.Select(character => (JsonNode)Member(rules, character, imageUrl)).ToArray());
        }

        if (session.Screen == Screen.Combat)
        {
            projection["fight"] = LiveFight(session, session.Combat!, imageUrl);
        }

        if (session.Screen == Screen.Play)
        {
            CampaignRunner runner = session.Runner!;
            CampaignState state = runner.State;
            projection["position"] = new JsonObject
            {
                ["area"] = state.Area.QualifiedId,
                ["name"] = state.Area.Name,
                ["x"] = state.X,
                ["y"] = state.Y,
                ["facing"] = Facings.Name(state.Facing),
            };
            projection["map"] = Map(runner, state);
            projection["menu"] = new JsonArray(runner.MenuOptions().Select(option => (JsonNode)new JsonObject
            {
                ["number"] = option.Number,
                ["label"] = option.Label,
            }).ToArray());
            projection["commands"] = CampaignRunner.CommandList;
            projection["elapsedDays"] = state.ElapsedDays;
            projection["training"] = runner.Training()?.Text;
            projection["shop"] = runner.Shop() is ShopFact shop ? new JsonObject
            {
                ["text"] = shop.Text,
                ["balances"] = Balances(shop.Balances),
                ["stock"] = Offers(shop.Stock),
                ["carried"] = Offers(shop.Carried),
                ["buyingCurrency"] = shop.BuyingCurrency?.QualifiedId,
                ["buyingMaxValue"] = shop.MaxBuyValue,
            } : null;
            projection["temple"] = runner.Temple() is TempleFact temple ? new JsonObject
            {
                ["text"] = temple.Text,
                ["services"] = new JsonArray(temple.Services.Select(service => (JsonNode)new JsonObject
                {
                    ["number"] = service.Number,
                    ["label"] = service.Label,
                    ["currency"] = service.Currency.QualifiedId,
                    ["prices"] = new JsonArray(service.Prices.Select(price => (JsonNode)JsonValue.Create(price)!).ToArray()),
                }).ToArray()),
            } : null;
            projection["party"] = new JsonArray(state.Party.Select(character => (JsonNode)Member(session.Set!.Rules!, character, imageUrl, runner)).ToArray());
            projection["inventory"] = new JsonArray(state.Inventory
                .GroupBy(item => item.QualifiedId)
                .Select(items => (JsonNode)new JsonObject
                {
                    ["id"] = items.Key,
                    ["name"] = items.First().Name,
                    ["count"] = items.Count(),
                    ["usable"] = items.First().Json.TryGetProperty("use", out _),
                }).ToArray());
            projection["ended"] = state.Ended;
            projection["log"] = Strings(session.Log);
            projection["picture"] = state.Picture is Definition shown ? Picture(session.Set!.Rules!, shown, imageUrl) : null;
            projection["music"] = state.Music?.QualifiedId;
            projection["viewEvent"] = state.ViewEvent?.QualifiedId;
            projection["viewOptions"] = new JsonArray(runner.CurrentViews().Select(view => (JsonNode)Presentation(session.Set!.Rules!, view, imageUrl)).ToArray());
            projection["view"] = state.ViewedCharacter is Character viewed
                ? runner.ViewFor(viewed) is ViewPresentation selected
                    ? new JsonObject
                    {
                        ["member"] = state.Party.IndexOf(viewed) + 1,
                        ["who"] = viewed.Name,
                        ["mode"] = selected.Mode,
                        ["text"] = selected.Text,
                        ["picture"] = selected.Picture is Definition selectedPicture ? Picture(session.Set!.Rules!, selectedPicture, imageUrl) : null,
                    }
                    : null
                : null;
        }

        return projection;
    }

    /// <summary>Copies a JSON value into the Engine's node, edge and UTF-8 arrays.</summary>
    public static UiValue ToUiValue(JsonNode root)
    {
        List<StructuredValueNode> nodes = [];
        List<uint> edges = [];
        List<byte> text = [];
        uint index = Add(root, null, nodes, edges, text);
        return new UiValue(nodes.ToArray(), edges.ToArray(), index, text.ToArray());
    }

    private static uint Add(JsonNode? node, string? key, List<StructuredValueNode> nodes, List<uint> edges, List<byte> text)
    {
        (uint keyOffset, uint keyLength) = key is null ? (0u, 0u) : Text(key, text);
        StructuredValueNode value;
        switch (node)
        {
            case null:
                value = new(StructuredValueKind.Null, 0, 0, keyOffset, keyLength, 0, 0, 0, 0);
                break;
            case JsonObject members:
                List<uint> fields = members.Select(member => Add(member.Value, member.Key, nodes, edges, text)).ToList();
                value = new(StructuredValueKind.Object, 0, 0, keyOffset, keyLength, 0, 0, (uint)edges.Count, (uint)fields.Count);
                edges.AddRange(fields);
                break;
            case JsonArray elements:
                List<uint> items = elements.Select(element => Add(element, null, nodes, edges, text)).ToList();
                value = new(StructuredValueKind.Array, 0, 0, keyOffset, keyLength, 0, 0, (uint)edges.Count, (uint)items.Count);
                edges.AddRange(items);
                break;
            default:
                value = node.GetValueKind() switch
                {
                    JsonValueKind.String => StringNode(node.GetValue<string>(), keyOffset, keyLength, text),
                    JsonValueKind.Number => new(StructuredValueKind.Number, 0, double.Parse(node.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture), keyOffset, keyLength, 0, 0, 0, 0),
                    JsonValueKind.True => new(StructuredValueKind.Bool, 1, 0, keyOffset, keyLength, 0, 0, 0, 0),
                    JsonValueKind.False => new(StructuredValueKind.Bool, 0, 0, keyOffset, keyLength, 0, 0, 0, 0),
                    _ => new(StructuredValueKind.Null, 0, 0, keyOffset, keyLength, 0, 0, 0, 0),
                };
                break;
        }

        nodes.Add(value);
        return (uint)nodes.Count - 1;
    }

    private static StructuredValueNode StringNode(string value, uint keyOffset, uint keyLength, List<byte> text)
    {
        (uint offset, uint length) = Text(value, text);
        return new(StructuredValueKind.String, 0, 0, keyOffset, keyLength, offset, length, 0, 0);
    }

    private static (uint Offset, uint Length) Text(string value, List<byte> text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        uint offset = (uint)text.Count;
        text.AddRange(bytes);
        return (offset, (uint)bytes.Length);
    }

    private static string Status(GameSession session)
    {
        return session.Screen switch
        {
            Screen.Title => $"{session.Campaigns.Count} campaign(s) available",
            Screen.Party => $"{session.Campaign!.Name}: making a party ({session.Party.Count})",
            Screen.Combat when session.Combat is CombatObservation { Phase: CombatPhase.Ended } ended => $"{session.Campaign!.Name}: {Outcome(ended)}",
            Screen.Combat when session.Combat is CombatObservation live => $"{session.Campaign!.Name}: fighting{(live.ActiveActorId is string actor ? $" · {actor}" : "")}",
            _ => session.Runner!.State.Ended
                ? $"{session.Campaign!.Name}: the adventure is over"
                : $"{session.Campaign!.Name}: {session.Runner.State.Area.Name} [{session.Runner.State.X}, {session.Runner.State.Y}] facing {Facings.Name(session.Runner.State.Facing)}",
        };
    }

    private static JsonObject LiveFight(GameSession session, CombatObservation observation, Func<Definition, string?> imageUrl)
    {
        CampaignRunner runner = session.Runner!;
        PendingCombatState pending = session.CombatMetadata!;
        RuleSet rules = session.Set!.Rules!;
        Definition track = rules.Reference(pending.Combat, "$.track");
        Dictionary<string, (PendingCombatantSource Source, PendingFightMember? Member)> metadata = [];
        for (int index = 0; index < pending.Participants.Count; index++)
        {
            PendingCombatantSource source = pending.Participants[index];
            metadata[source.Id] = (source, index < pending.Members.Count ? pending.Members[index] : null);
        }

        string? decisionActorId = observation.PendingDecision?.ActorId;
        string? turnActorId = observation.ActiveActorId;
        string? highlightedActorId = decisionActorId ?? turnActorId;
        JsonArray members = new(observation.Combatants.Select(member => LiveMember(session, observation, member, highlightedActorId, track, metadata, imageUrl)).ToArray());
        JsonObject fight = new()
        {
            ["encounter"] = pending.Encounter.Name,
            ["track"] = track.Name,
            ["trackId"] = track.QualifiedId,
            ["phase"] = observation.Phase.ToString().ToLowerInvariant(),
            ["round"] = observation.Round,
            ["activeActorId"] = highlightedActorId,
            ["decisionActorId"] = decisionActorId,
            ["turnActorId"] = turnActorId,
            ["done"] = observation.Phase == CombatPhase.Ended,
            ["outcome"] = observation.Phase == CombatPhase.Ended ? Outcome(observation) : null,
            ["winner"] = observation.Winner,
            ["fledSide"] = observation.FledSide,
            ["members"] = members,
            ["decision"] = observation.PendingDecision is CombatDecision decision ? Decision(decision, rules) : null,
            ["log"] = Strings(observation.Facts.TakeLast(14).Select(fact => fact.Describe())),
        };
        return fight;
    }

    private static JsonObject LiveMember(GameSession session, CombatObservation observation, CombatantObservation member, string? highlightedActorId, Definition track, Dictionary<string, (PendingCombatantSource Source, PendingFightMember? Member)> metadata, Func<Definition, string?> imageUrl)
    {
        RuleSet rules = session.Set!.Rules!;
        if (!metadata.TryGetValue(member.Id, out (PendingCombatantSource Source, PendingFightMember? Member) info))
        {
            throw new InvalidOperationException($"Live combat observation contains unknown combatant ID '{member.Id}'.");
        }
        decimal? value = member.Tracks.TryGetValue(track.Id, out decimal? current)
            ? current
            : member.Tracks.TryGetValue(track.QualifiedId, out decimal? qualifiedCurrent) ? qualifiedCurrent : null;
        Definition? kind = info.Member?.MonsterId is string monsterId
            ? rules.Find(DefinitionTypes.Monster, monsterId, out _)
            : info.Member?.ClassId is string classId ? rules.Find(DefinitionTypes.Class, classId, out _) : null;
        Definition? icon = kind is not null && rules.Icons.TryGetValue(kind, out Definition? shown) ? shown : null;
        Definition? portrait = info.Source.PartyIndex is int partyIndex && partyIndex < session.Runner!.State.Party.Count
            ? session.Runner.State.Party[partyIndex].Portrait
            : null;
        return new JsonObject
        {
            ["id"] = member.Id,
            ["name"] = member.Name,
            ["side"] = member.Side,
            ["controller"] = member.Controller.ToString().ToLowerInvariant(),
            ["defeated"] = member.Defeated,
            ["escaped"] = member.Escaped,
            ["acting"] = member.Id == highlightedActorId,
            ["turning"] = member.Id == observation.ActiveActorId,
            ["value"] = value is decimal valueNumber ? (double)valueNumber : null,
            ["max"] = info.Member?.Max is decimal maximum ? (double)maximum : null,
            ["budget"] = new JsonObject(member.Budget.Select(entry => KeyValuePair.Create(entry.Key, (JsonNode?)entry.Value))),
            ["position"] = member.Position is Cell position ? new JsonObject { ["x"] = position.X, ["y"] = position.Y } : null,
            ["icon"] = icon?.QualifiedId,
            ["iconPicture"] = icon is null ? null : Picture(rules, icon, imageUrl),
            ["portraitPicture"] = portrait is null ? null : Picture(rules, portrait, imageUrl),
        };
    }

    private static JsonObject Decision(CombatDecision decision, RuleSet rules)
    {
        return new JsonObject
        {
            ["id"] = decision.Id,
            ["kind"] = decision.Kind.ToString().ToLowerInvariant(),
            ["actorId"] = decision.ActorId,
            ["round"] = decision.Round,
            ["actions"] = new JsonArray(decision.Actions.Select(choice => ActionChoice(choice, rules)).ToArray()),
            ["moves"] = new JsonArray(decision.Moves.Select(MoveChoice).ToArray()),
            ["canEndTurn"] = decision.CanEndTurn,
            ["operationOwner"] = decision.OperationOwner,
            ["operationPath"] = decision.OperationPath,
            ["actionId"] = decision.ActionId,
            ["maximumTargets"] = decision.MaximumTargets,
            ["options"] = decision.Options is null ? null : new JsonArray(decision.Options.Select(DecisionOption).ToArray()),
            ["check"] = decision.Check is CombatCheckState check ? new JsonObject
            {
                ["id"] = check.CheckId,
                ["name"] = rules.Find(DefinitionTypes.Check, check.CheckId, out _)?.Name ?? check.CheckId,
                ["roll"] = (double)check.Roll, ["bonus"] = (double)check.Bonus,
                ["modifier"] = (double)check.Modifier, ["total"] = (double)check.Total,
                ["target"] = (double)check.Target, ["margin"] = (double)check.Margin,
                ["success"] = check.Success, ["tier"] = check.Tier,
            } : null,
        };
    }

    private static JsonObject ActionChoice(CombatActionChoice choice, RuleSet rules)
    {
        return new JsonObject
        {
            ["id"] = choice.Id,
            ["actionId"] = choice.ActionId,
            ["name"] = choice.Name,
            ["spellId"] = choice.SpellId,
            ["cost"] = new JsonObject(choice.Cost.Select(entry => KeyValuePair.Create(entry.Key, (JsonNode?)entry.Value))),
            ["spellCosts"] = choice.SpellCosts is null ? null : new JsonArray(choice.SpellCosts.Select(entry => (JsonNode)new JsonObject
            {
                ["trackId"] = entry.Key,
                ["name"] = rules.TryTrack(entry.Key, out Definition? track, out _) ? track!.Name : entry.Key,
                ["cost"] = (double)entry.Value,
            }).ToArray()),
            ["targetKind"] = choice.TargetKind,
            ["targetMode"] = choice.TargetMode,
            ["portionCount"] = choice.PortionCount,
            ["targets"] = new JsonArray(choice.Targets.Select(target => new JsonObject
            {
                ["id"] = target.Id, ["name"] = target.Name, ["side"] = target.Side,
                ["defeated"] = target.Defeated, ["escaped"] = target.Escaped,
                ["track"] = target.Track is decimal current ? (double)current : null,
                ["max"] = target.MaximumTrack is decimal maximum ? (double)maximum : null,
                ["position"] = target.Position is Cell position ? new JsonObject { ["x"] = position.X, ["y"] = position.Y } : null,
            }).ToArray()),
            ["moves"] = new JsonArray(choice.Moves.Select(MoveChoice).ToArray()),
        };
    }

    private static JsonObject MoveChoice(CombatMoveChoice move) => new()
    {
        ["destination"] = new JsonObject { ["x"] = move.Destination.X, ["y"] = move.Destination.Y },
        ["path"] = new JsonArray(move.Path.Select(cell => (JsonNode)new JsonObject { ["x"] = cell.X, ["y"] = cell.Y }).ToArray()),
        ["cost"] = move.Cost,
        ["targetIds"] = move.TargetIds is null ? null : new JsonArray(move.TargetIds.Select(targetId => (JsonNode)targetId).ToArray()),
    };

    private static JsonObject DecisionOption(CombatDecisionOption option) => new()
    {
        ["id"] = option.Id, ["name"] = option.Name, ["kind"] = option.Kind,
        ["qualifiedId"] = option.QualifiedId, ["targetId"] = option.TargetId,
        ["trackId"] = option.TrackId, ["cost"] = (double)option.Cost,
        ["bonus"] = option.Bonus is decimal bonus ? (double)bonus : null,
        ["reroll"] = option.Reroll, ["score"] = option.Score is decimal score ? (double)score : null,
    };

    private static string Outcome(CombatObservation observation)
    {
        return observation.Winner is int winner
            ? winner == 0 ? "The party won." : "The party lost."
            : observation.FledSide is int fled ? fled == 0 ? "The party fled." : "The foes fled." : "The fight ended.";
    }

    private static JsonObject Modules(ModuleInstaller installer)
    {
        string library = InstalledModules.DefaultDirectory();
        Dictionary<string, InstalledSources.Entry> record = InstalledSources.Read(library);
        return new JsonObject
        {
            ["activity"] = installer.Activity,
            ["received"] = installer.Received,
            ["expected"] = installer.Expected,
            ["previewSource"] = installer.PreviewSource,
            ["preview"] = new JsonArray(installer.Preview.Select(offer => (JsonNode)new JsonObject
            {
                ["id"] = offer.Id,
                ["version"] = offer.Version,
                ["kind"] = offer.Kind,
                ["title"] = offer.Title,
                ["provenance"] = offer.Provenance,
                ["requires"] = Strings(offer.Requires),
            }).ToArray()),
            ["updates"] = new JsonArray(installer.Updates.Select(update => (JsonNode)new JsonObject
            {
                ["id"] = update.Id,
                ["installed"] = update.Installed,
                ["available"] = update.Available,
                ["source"] = update.Source,
            }).ToArray()),
            ["installed"] = new JsonArray(InstalledModules.In(library, [])
                .Select(Path.GetFileName)
                .Select(name => InstalledName().Match(name!))
                .Where(match => match.Success)
                .Select(match => (JsonNode)new JsonObject
                {
                    ["id"] = match.Groups[1].Value,
                    ["version"] = match.Groups[2].Value,
                    ["source"] = record.TryGetValue(match.Groups[1].Value, out InstalledSources.Entry? entry) ? entry.Releases : null,
                }).ToArray()),
            ["messages"] = Strings(installer.Messages),
        };
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(.+)-(\d+\.\d+\.\d+)\.rpak$")]
    private static partial System.Text.RegularExpressions.Regex InstalledName();

    private static JsonArray Strings(IEnumerable<string> lines) => new(lines.Select(line => (JsonNode)line).ToArray());

    private static JsonArray Offers(IReadOnlyList<ShopOffer> offers)
    {
        return new JsonArray(offers.Select(offer => (JsonNode)new JsonObject
        {
            ["number"] = offer.Number,
            ["id"] = offer.Item.QualifiedId,
            ["name"] = offer.Item.Name,
            ["price"] = offer.Price,
            ["currency"] = offer.Currency.QualifiedId,
            ["holder"] = offer.Holder,
            ["remaining"] = offer.Remaining,
            ["sellable"] = offer.Sellable,
            ["refusalReason"] = offer.RefusalReason,
        }).ToArray());
    }

    private static JsonObject Balances(IReadOnlyDictionary<Definition, decimal> balances)
    {
        JsonObject result = [];
        foreach ((Definition currency, decimal amount) in balances)
        {
            result[currency.QualifiedId] = amount;
        }

        return result;
    }

    private static JsonArray Choices(RuleSet rules, DefinitionType type)
    {
        return new JsonArray(rules.OfType(type).Select(definition => (JsonNode)new JsonObject
        {
            ["id"] = definition.QualifiedId,
            ["name"] = definition.Name,
            ["boosts"] = Boosts(definition),
        }).ToArray());
    }

    /// <summary>
    /// What the party screen needs to ask for at a roll: the default creation's
    /// method, attributes, grants and boosts, each class's first-level grants,
    /// and every feature with its kind and boosts. Core checks the roll itself.
    /// </summary>
    private static void Creation(RuleSet rules, JsonObject projection)
    {
        if (CharacterRules.DefaultCreation(rules) is not Definition creation)
        {
            return;
        }

        projection["creation"] = CreationChoice(rules, creation);
        projection["creations"] = new JsonArray(rules.OfType(DefinitionTypes.CharacterCreation).Select(definition => (JsonNode)CreationChoice(rules, definition)).ToArray());
        if (creation.Json.TryGetProperty("lifepath", out _)
            && rules.Reference(creation, "$.lifepath") is Definition lifepath)
        {
            projection["lifepath"] = new JsonObject
            {
                ["id"] = lifepath.QualifiedId,
                ["name"] = lifepath.Name,
                ["startAge"] = lifepath.Json.GetProperty("start_age").GetInt32(),
                ["maxTerms"] = lifepath.Json.GetProperty("max_terms").GetInt32(),
                ["careers"] = new JsonArray(CharacterRules.LifepathCareers(lifepath).Select(career => (JsonNode)new JsonObject
                {
                    ["id"] = career.Id,
                    ["name"] = career.Name,
                    ["skillTables"] = new JsonArray(career.SkillTables.Select(table => (JsonNode)table).ToArray()),
                    ["benefitKinds"] = new JsonArray(career.BenefitKinds.Select(kind => (JsonNode)kind).ToArray()),
                }).ToArray()),
            };
        }
        foreach (JsonNode? entry in projection["classes"]!.AsArray())
        {
            Definition characterClass = rules.Find(DefinitionTypes.Class, entry!["id"]!.GetValue<string>(), out _)!;
            entry["grants"] = Grants(CharacterRules.FirstLevelChoices(rules, characterClass));
        }

        projection["features"] = new JsonArray(rules.OfType(DefinitionTypes.Feature).Select(feature => (JsonNode)new JsonObject
        {
            ["id"] = feature.QualifiedId,
            ["name"] = feature.Name,
            ["kind"] = feature.Json.GetProperty("kind").GetString(),
            ["boosts"] = Boosts(feature),
        }).ToArray());
    }

    private static JsonArray Grants(List<Grant> grants)
    {
        return new JsonArray(grants.Select(grant => (JsonNode)new JsonObject
        {
            ["kinds"] = new JsonArray(grant.Kinds.Select(kind => (JsonNode)JsonValue.Create(kind)!).ToArray()),
            ["count"] = grant.Count,
        }).ToArray());
    }

    private static JsonObject CreationChoice(RuleSet rules, Definition creation)
    {
        JsonObject choice = new()
        {
            ["id"] = creation.QualifiedId,
            ["name"] = creation.Name,
            ["method"] = creation.Json.TryGetProperty("method", out JsonElement method) ? method.GetString() : "roll",
            ["attributes"] = new JsonArray(creation.Json.GetProperty("attributes").EnumerateArray().Select(attribute => (JsonNode)JsonValue.Create(attribute.GetString())!).ToArray()),
            ["grants"] = Grants(CharacterRules.CreationChoices(creation)),
            ["boosts"] = Boosts(creation),
        };
        JsonObject AttributeDetail(string id)
        {
            Definition definition = rules.Stats[id].Definition;
            JsonObject detail = new()
            {
                ["id"] = id,
                ["name"] = definition.Name,
            };
            if (definition.Json.TryGetProperty("min", out JsonElement minimum))
            {
                detail["min"] = minimum.GetDecimal();
            }

            if (definition.Json.TryGetProperty("max", out JsonElement maximum))
            {
                detail["max"] = maximum.GetDecimal();
            }

            return detail;
        }

        string[] attributeIds = creation.Json.GetProperty("attributes").EnumerateArray().Select(attribute => attribute.GetString()!).ToArray();
        choice["attributeDetails"] = new JsonArray(attributeIds.Select(id => (JsonNode)AttributeDetail(id)).ToArray());
        JsonElement ownRolls = creation.Json.TryGetProperty("attribute_rolls", out JsonElement givenRolls) ? givenRolls : default;
        bool HasOwnRoll(string id) => ownRolls.ValueKind == JsonValueKind.Object && ownRolls.TryGetProperty(id, out _);
        choice["arrangeableAttributes"] = new JsonArray(attributeIds
            .Where(id => !HasOwnRoll(id))
            .Select(id => (JsonNode)AttributeDetail(id))
            .ToArray());
        if (creation.Json.TryGetProperty("array", out JsonElement array))
        {
            choice["array"] = JsonNode.Parse(array.GetRawText());
        }

        if (creation.Json.TryGetProperty("assignment", out JsonElement assignment))
        {
            choice["assignment"] = assignment.GetString();
        }

        if (creation.Json.TryGetProperty("base", out JsonElement baseScore))
        {
            choice["base"] = baseScore.GetDecimal();
        }

        if (creation.Json.TryGetProperty("budget", out JsonElement budget))
        {
            choice["budget"] = budget.GetDecimal();
        }

        if (creation.Json.TryGetProperty("costs", out _)
            && rules.Reference(creation, "$.costs") is Definition costs
            && costs.Json.TryGetProperty("rows", out JsonElement rows))
        {
            choice["costs"] = new JsonObject
            {
                ["id"] = costs.QualifiedId,
                ["rows"] = JsonNode.Parse(rows.GetRawText()),
            };
        }

        if (creation.Json.TryGetProperty("skill_points", out JsonElement skillPoints))
        {
            choice["skillPoints"] = JsonNode.Parse(skillPoints.GetRawText());
        }

        return choice;
    }

    /// <summary>A definition's boosts as lists of the attributes each may raise; an empty list is any attribute.</summary>
    private static JsonArray Boosts(Definition definition)
    {
        if (!definition.Json.TryGetProperty("boosts", out JsonElement boosts))
        {
            return [];
        }

        return new JsonArray(boosts.EnumerateArray().Select(boost => (JsonNode)new JsonArray(
            boost.TryGetProperty("from", out JsonElement from)
                ? from.EnumerateArray().Select(attribute => (JsonNode)JsonValue.Create(attribute.GetString())!).ToArray()
                : [])).ToArray());
    }

    /// <summary>A skin as the panels apply it: its colours, and its pictures as media objects with their slices.</summary>
    private static JsonObject Skin(ModuleSet set, Definition skin, Func<Definition, string?> imageUrl)
    {
        RuleSet rules = set.Rules!;
        JsonObject colors = [];
        if (skin.Json.TryGetProperty("colors", out JsonElement given))
        {
            foreach (JsonProperty color in given.EnumerateObject())
            {
                colors[color.Name] = color.Value.GetString();
            }
        }

        JsonObject? Nine(string part) => skin.Json.TryGetProperty(part, out JsonElement nine)
            ? new JsonObject { ["picture"] = Picture(rules, rules.Reference(skin, $"$.{part}.picture"), imageUrl), ["slice"] = nine.GetProperty("slice").GetInt32() }
            : null;
        JsonObject? Whole(string part) => skin.Json.TryGetProperty(part, out _) ? Picture(rules, rules.Reference(skin, $"$.{part}"), imageUrl) : null;
        return new JsonObject
        {
            ["id"] = skin.QualifiedId,
            ["name"] = skin.Json.GetProperty("name").GetString(),
            ["colors"] = colors,
            ["panel"] = Whole("panel"),
            ["frame"] = Nine("frame"),
            ["button"] = Nine("button"),
            ["title"] = Whole("title"),
        };
    }

    /// <summary>
    /// How the panels show an asset in a picture slot, whatever its media: the
    /// image URL, the image's pixel size, and for a sheet the size of a frame
    /// and the first animation it plays. Null when the image can't be shown.
    /// </summary>
    private static JsonObject? Picture(RuleSet rules, Definition asset, Func<Definition, string?> imageUrl)
    {
        if (imageUrl(asset) is not string url)
        {
            return null;
        }

        (int width, int height) = rules.ImageSizes[asset];
        JsonArray? frame = Media.MediaOf(asset) == "sheet" && asset.Json.GetProperty("frame_size") is JsonElement size
            ? new JsonArray(size[0].GetInt32(), size[1].GetInt32())
            : null;
        JsonObject? animation = null;
        if (frame is not null && asset.Json.TryGetProperty("animations", out JsonElement animations) && animations.EnumerateObject().FirstOrDefault() is { Value.ValueKind: JsonValueKind.Object } first)
        {
            animation = new JsonObject
            {
                ["frames"] = new JsonArray(first.Value.GetProperty("frames").EnumerateArray().Select(played => (JsonNode)played.GetInt32()).ToArray()),
                ["fps"] = first.Value.GetProperty("fps").GetDouble(),
                ["loop"] = !first.Value.TryGetProperty("loop", out JsonElement repeat) || repeat.GetBoolean(),
            };
        }

        return new JsonObject
        {
            ["url"] = url,
            ["width"] = width,
            ["height"] = height,
            ["frame"] = frame,
            ["animation"] = animation,
            ["sampling"] = Media.SamplingOf(asset),
        };
    }

    /// <summary>
    /// A member's tracks as values for bars and text. A vital track is one a
    /// combat is fought on, so a portrait can show it.
    /// </summary>
    private static JsonArray SheetValues(RuleSet rules, Character character)
    {
        Evaluator evaluator = new(rules, null);
        Creature creature = character.ToCreature();
        return new JsonArray(rules.OfType(DefinitionTypes.Derived)
            .Where(stat => stat.Json.TryGetProperty("show_on_sheet", out JsonElement shown) && shown.GetBoolean())
            .Select(stat => (JsonNode)new JsonObject
            {
                ["name"] = stat.Name,
                ["value"] = evaluator.Stat(creature, stat.Id).ToString(),
            }).ToArray());
    }

    private static JsonArray Tracks(RuleSet rules, Character character)
    {
        HashSet<Definition> fought = rules.OfType(DefinitionTypes.Combat)
            .Select(combat => rules.Reference(combat, "$.track"))
            .ToHashSet();
        return new JsonArray(CharacterSheet.Tracks(rules, character).Select(track => (JsonNode)new JsonObject
        {
            ["id"] = track.Track.QualifiedId,
            ["name"] = track.Track.Name,
            ["current"] = track.Current is decimal current ? (double)current : null,
            ["max"] = track.Max is decimal max ? (double)max : null,
            ["vital"] = fought.Contains(track.Track),
        }).ToArray());
    }

    private static JsonArray Spells(IEnumerable<Definition> spells)
    {
        return new JsonArray(spells.Select(spell => (JsonNode)new JsonObject { ["id"] = spell.QualifiedId, ["name"] = spell.Name }).ToArray());
    }

    private static JsonObject Member(RuleSet rules, Character character, Func<Definition, string?> imageUrl, CampaignRunner? runner = null)
    {
        List<ModuleDiagnostic> skillProblems = [];
        SkillPointOptions? skillPoints = CharacterRules.GetSkillPointOptions(rules, character, skillProblems);
        ViewPresentation? view = runner?.ViewFor(character);
        return new JsonObject
        {
            ["name"] = character.Name,
            ["npc"] = character.Npc?.QualifiedId,
            ["race"] = character.Race?.Name,
            ["class"] = character.ClassLevels().Count > 1 ? character.ClassText : character.Class?.Name,
            ["level"] = character.Level,
            ["tracks"] = Tracks(rules, character),
            ["attributes"] = Strings(character.Attributes.Select(attribute => $"{attribute.Key} {Number(attribute.Value)}")),
            ["derived"] = SheetValues(rules, character),
            ["conditions"] = Strings(character.Conditions.Select(condition => condition.Name)),
            ["skillPoints"] = skillPoints is null ? null : new JsonObject
            {
                ["profession"] = (double)skillPoints.Profession,
                ["personal"] = (double)skillPoints.Personal,
                ["committed"] = character.SkillPointsCommitted,
                ["skills"] = new JsonArray(skillPoints.Skills.Select(skill => (JsonNode)new JsonObject
                {
                    ["id"] = skill.Skill,
                    ["base"] = (double)skill.Base,
                    ["current"] = (double)skill.Current,
                    ["profession"] = skill.ProfessionAllowed,
                }).ToArray()),
            },
            ["skillAllocations"] = new JsonArray(character.SkillAllocations.Values.Select(allocation => (JsonNode)new JsonObject
            {
                ["id"] = allocation.Skill,
                ["profession"] = (double)allocation.Profession,
                ["personal"] = (double)allocation.Personal,
            }).ToArray()),
            ["features"] = Strings(character.Features.Select(feature => feature.Name)),
            ["equipment"] = new JsonArray(character.Equipment.Select(item => (JsonNode)new JsonObject { ["id"] = item.QualifiedId, ["name"] = item.Name }).ToArray()),
            ["spells"] = new JsonArray(character.Spells.Select(spell => (JsonNode)new JsonObject { ["id"] = spell.QualifiedId, ["name"] = spell.Name }).ToArray()),
            ["memorisable"] = Spells(character.Spells.Where(spell => CharacterRules.NeedsPreparing(rules, character, spell))),
            ["memorised"] = Spells(CharacterRules.MemorisedPlan(rules, character)),
            ["memorisedChosen"] = character.Memorised.Count > 0,
            ["prepared"] = Spells(CharacterRules.PreparedLeft(rules, character)),
            ["castable"] = new JsonArray(CharacterRules.CastableSpells(rules, character).Select(spell => (JsonNode)new JsonObject { ["id"] = spell.QualifiedId, ["name"] = spell.Name }).ToArray()),
            ["balances"] = Balances(character.Balances, rules),
            ["experience"] = (double)character.Experience,
            ["levelReady"] = CharacterRules.ReadyToLevel(rules, character),
            ["formerClasses"] = !character.HasDormantClasses() ? null : character.UsesFormerClasses ? "called" : "waiting",
            ["portrait"] = character.Portrait?.QualifiedId,
            ["portraitPicture"] = character.Portrait is Definition portrait ? Picture(rules, portrait, imageUrl) : null,
            ["perception"] = character.Perception is PerceptionState perception ? new JsonObject
            {
                ["scope"] = perception.Scope,
                ["mode"] = perception.Mode,
            } : null,
            ["view"] = view is ViewPresentation selected ? Presentation(rules, selected, imageUrl) : null,
            ["age"] = character.Lifepath is null ? null : character.Age,
            ["lifepath"] = character.Lifepath?.QualifiedId,
            ["careerTerms"] = new JsonArray(character.CareerTerms.Select(term => new JsonObject
            {
                ["career"] = term.Career,
                ["number"] = term.Number,
                ["ageBefore"] = term.AgeBefore,
                ["ageAfter"] = term.AgeAfter,
                ["rankBefore"] = term.RankBefore,
                ["rankAfter"] = term.RankAfter,
                ["qualification"] = LifepathRoll(term.Qualification),
                ["survival"] = LifepathRoll(term.Survival),
                ["commission"] = LifepathRoll(term.Commission),
                ["advancement"] = LifepathRoll(term.Advancement),
                ["aging"] = LifepathRoll(term.Aging),
                ["ended"] = term.Ended,
                ["benefitsLost"] = term.BenefitsLost,
                ["choices"] = new JsonArray(term.Choices.Select(choice => (JsonNode)choice).ToArray()),
                ["results"] = new JsonArray(term.Results.Select(result => (JsonNode)result).ToArray()),
            }).ToArray()),
        };
    }

    private static JsonObject Presentation(RuleSet rules, ViewPresentation view, Func<Definition, string?> imageUrl)
    {
        return new JsonObject
        {
            ["mode"] = view.Mode,
            ["text"] = view.Text,
            ["picture"] = view.Picture is Definition picture ? Picture(rules, picture, imageUrl) : null,
        };
    }

    private static JsonObject? LifepathRoll(LifepathRoll? roll)
    {
        return roll is null ? null : new JsonObject
        {
            ["kind"] = roll.Kind,
            ["roll"] = roll.Roll,
            ["modifier"] = roll.Modifier,
            ["total"] = roll.Total,
            ["target"] = roll.Target,
            ["success"] = roll.Success,
        };
    }

    private static JsonObject Balances(IReadOnlyDictionary<string, decimal> balances, RuleSet rules)
    {
        JsonObject result = [];
        foreach ((string currency, decimal amount) in balances.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            result[rules.Currencies.TryGetValue(currency, out Definition? definition) ? definition.QualifiedId : currency] = amount;
        }

        return result;
    }

    /// <summary>The area as the CLI's player view draws it, with the party as an arrow.</summary>
    private static string Map(CampaignRunner runner, CampaignState state)
    {
        AreaMap map = runner.PlayerMap(state.Area);
        string arrow = state.Facing switch
        {
            Facing.North => "^^",
            Facing.East => ">>",
            Facing.South => "vv",
            _ => "<<",
        };
        return map.Render(new Dictionary<(int X, int Y), string> { [(state.X, state.Y)] = arrow }, playerView: true);
    }

    private static string Number(decimal? value) => value?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "-";
}
