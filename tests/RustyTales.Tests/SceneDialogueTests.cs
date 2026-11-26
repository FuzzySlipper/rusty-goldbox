using System.Text.Json;
using RustyTales.Cli;
using RustyTales.Core.Definitions;
using RustyTales.Core.Modules;
using static RustyTales.Tests.TempModules;

namespace RustyTales.Tests;

public sealed class SceneDialogueTests
{
    [Fact]
    public void MultiNpcTownInvestigationScenePlaysDeterministicallyThroughTopicsChecksFlagsAndServices()
    {
        using TempModules modules = new();
        WriteInvestigationCampaign(modules);

        string campaignDir = Path.Combine(modules.Root, "town");
        ModuleSet set = ModuleLoader.Load(campaignDir, []);
        Assert.Empty(set.Diagnostics);

        // Two compact scene definitions lower deterministically into a full validated event graph
        // (far smaller and clearer than hand-wiring 14+ separate event definitions).
        List<Definition> loweredEvents = set.Rules!.OfType(DefinitionTypes.Event)
            .Where(evt => evt.Id.StartsWith("innkeeper", StringComparison.Ordinal) || evt.Id.StartsWith("constable", StringComparison.Ordinal))
            .ToList();
        Assert.True(loweredEvents.Count >= 12, $"Expected >=12 lowered events from the 2 scenes, got {loweredEvents.Count}.");

        string scriptPath = Path.Combine(modules.Root, "investigation.txt");
        File.WriteAllText(scriptPath, """
            # First visit the Constable without evidence -> turned away
            forward
            # Move east to Bram the Innkeeper
            forward
            # Ask about the missing scout (sets heard_rumor, loops back to topics)
            choose 1
            # Visit the outfitter shop handoff, buy a lantern, leave -> returns to Bram
            choose 3
            buy 1
            leave
            # Press Bram (persuade check succeeds -> sets has_evidence, gives cellar_key, returns to topics)
            choose 2
            # Leave Bram
            choose 3
            # Return west to the Constable with evidence -> awards XP, sets case_solved, victory
            back
            """);

        (int code1, string run1) = Run(
            modules.Root,
            "play",
            "--campaign", campaignDir,
            "--modules", modules.Root,
            "--script", scriptPath,
            "--seed", "7",
            "--fail-on-refusal",
            "--json");
        Assert.True(code1 == TalesCli.Ok, run1);

        (int code2, string run2) = Run(
            modules.Root,
            "play",
            "--campaign", campaignDir,
            "--modules", modules.Root,
            "--script", scriptPath,
            "--seed", "7",
            "--fail-on-refusal",
            "--json");
        Assert.Equal(TalesCli.Ok, code2);
        Assert.Equal(run1, run2);

        using (JsonDocument doc = JsonDocument.Parse(run1))
        {
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), run1);
            Assert.True(doc.RootElement.GetProperty("ended").GetBoolean(), run1);
            Assert.Contains("Bram: She took the north road at dusk.", run1, StringComparison.Ordinal);
            Assert.Contains("Bram: Alright! Take the cellar key.", run1, StringComparison.Ordinal);
            Assert.Contains("Constable Vale: That settles it — the case is solved!", run1, StringComparison.Ordinal);
        }

        // Verify mid-dialogue save and resume preserves lowered event identity cleanly.
        string savePath = Path.Combine(modules.Root, "mid_dialogue.json");
        string part1 = Path.Combine(modules.Root, "part1.txt");
        string part2 = Path.Combine(modules.Root, "part2.txt");
        File.WriteAllText(part1, """
            forward
            forward
            choose 1
            """);
        File.WriteAllText(part2, """
            choose 2
            choose 3
            back
            """);

        (int saveCode, string saveOut) = Run(
            modules.Root,
            "play",
            "--campaign", campaignDir,
            "--modules", modules.Root,
            "--script", part1,
            "--save", savePath,
            "--seed", "7",
            "--fail-on-refusal",
            "--json");
        Assert.True(saveCode == TalesCli.Ok, saveOut);

        (int resumeCode, string resumeOut) = Run(
            modules.Root,
            "play",
            "--campaign", campaignDir,
            "--modules", modules.Root,
            "--load", savePath,
            "--script", part2,
            "--fail-on-refusal",
            "--json");
        Assert.True(resumeCode == TalesCli.Ok, resumeOut);
        using (JsonDocument resumed = JsonDocument.Parse(resumeOut))
        {
            Assert.True(resumed.RootElement.GetProperty("ok").GetBoolean(), resumeOut);
            Assert.True(resumed.RootElement.GetProperty("ended").GetBoolean(), resumeOut);
            Assert.Contains("Constable Vale: That settles it — the case is solved!", resumeOut, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SceneStaticChecksCatchReachabilitySoftlocksLoopsAndBadTargets()
    {
        using TempModules modules = new();
        Rules.WriteSmallRuleset(modules);
        modules.Module("art", "assets");
        string campaign = modules.Module("camp", "campaign", requires: string.Join(", ", Require("rules", "*"), Require("art", "*")));
        modules.Write("camp/base.json", """
            [
              { "type": "area", "id": "room", "name": "Room", "map": [ "+--+", "|  |", "+--+" ],
                "entries": { "start": { "at": [0, 0], "facing": "north" } } },
              { "type": "campaign", "id": "camp", "name": "Camp", "start": { "area": "room", "entry": "start" }, "party": { "min": 1, "max": 4 } },
              { "type": "variable", "id": "flag", "value_type": "boolean", "initial": "false" }
            ]
            """);
        modules.Write("camp/bad_scenes.json", """
            [
              {
                "type": "scene",
                "id": "softlock_scene",
                "start": "ask",
                "nodes": {
                  "ask": {
                    "prompt": "Pick?",
                    "choices": [ { "label": "Only guarded", "when": "campaign.var.flag" } ]
                  }
                }
              },
              {
                "type": "scene",
                "id": "orphan_scene",
                "start": "entry",
                "nodes": {
                  "entry": { "say": "Hello." },
                  "orphan": { "say": "Never reached." }
                }
              },
              {
                "type": "scene",
                "id": "loop_scene",
                "start": "a",
                "nodes": {
                  "a": { "say": "Ping", "goto": "b" },
                  "b": { "say": "Pong", "goto": "a" }
                }
              },
              {
                "type": "scene",
                "id": "bad_target_scene",
                "start": "entry",
                "nodes": {
                  "entry": { "goto": "nowhere" }
                }
              }
            ]
            """);

        ModuleSet set = ModuleLoader.Load(campaign, []);

        Assert.Equal(
            [
                ("scene.loop", "$[2].nodes.a"),
                ("scene.menu", "$[0].nodes.ask.choices"),
                ("scene.node-not-found", "$[3].nodes.entry.goto"),
                ("scene.unreachable", "$[1].nodes.orphan"),
            ],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)).Order());
    }

    [Fact]
    public void SceneDiagnosticsMapRuleSetErrorsBackToExactSceneJsonPaths()
    {
        using TempModules modules = new();
        Rules.WriteSmallRuleset(modules);
        modules.Module("art", "assets");
        string campaign = modules.Module("camp", "campaign", requires: string.Join(", ", Require("rules", "*"), Require("art", "*")));
        modules.Write("camp/base.json", """
            [
              { "type": "area", "id": "room", "name": "Room", "map": [ "+--+", "|  |", "+--+" ],
                "entries": { "start": { "at": [0, 0], "facing": "north" } } },
              { "type": "campaign", "id": "camp", "name": "Camp", "start": { "area": "room", "entry": "start" }, "party": { "min": 1, "max": 4 } },
              { "type": "variable", "id": "clues", "value_type": "number", "initial": "0" }
            ]
            """);
        modules.Write("camp/scenes.json", """
            [
              { "type": "event", "id": "ok_event", "kind": "text", "text": "Ok." },
              {
                "type": "scene",
                "id": "broken_npc",
                "start": "topics",
                "nodes": {
                  "topics": {
                    "prompt": "Ask?",
                    "choices": [
                      { "label": "Missing var", "set": { "no_such_var": "true" } },
                      { "label": "Wrong type", "set": { "clues": "true" } },
                      { "label": "Missing ext", "event": "missing_event" },
                      { "label": "Try check", "goto": "press" }
                    ]
                  },
                  "press": {
                    "check": { "check": "rules:no_such_check", "member": 1 }
                  }
                }
              }
            ]
            """);

        ModuleSet set = ModuleLoader.Load(campaign, []);

        Assert.All(set.Diagnostics, diagnostic =>
        {
            Assert.Equal("camp", diagnostic.Module);
            Assert.Equal("scenes.json", Path.GetFileName(diagnostic.File!));
        });
        Assert.Equal(
            [
                ("expression.type", "$[1].nodes.topics.choices[1].set.clues"),
                ("reference.not-found", "$[1].nodes.press.check.check"),
                ("reference.not-found", "$[1].nodes.topics.choices[0].set.no_such_var"),
                ("reference.not-found", "$[1].nodes.topics.choices[2].event"),
            ],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)).Order());
    }

    [Fact]
    public void SceneSupportsExtendsPatchAndSchemaDiscovery()
    {
        using TempModules modules = new();
        Rules.WriteSmallRuleset(modules);
        modules.Module("art", "assets");
        modules.Module("shared", "extension", requires: Require("rules", "*"));
        modules.Write("shared/base_scene.json", """
            {
              "type": "scene",
              "id": "merchant_base",
              "speaker": "Merchant",
              "start": "greet",
              "nodes": {
                "greet": { "say": "Fine wares for travellers!" }
              }
            }
            """);

        string campaign = modules.Module("camp", "campaign", requires: string.Join(", ", Require("rules", "*"), Require("art", "*"), Require("shared", "*")));
        modules.Write("camp/world.json", """
            [
              { "type": "area", "id": "room", "name": "Room", "map": [ "+--+", "|  |", "+--+" ],
                "entries": { "start": { "at": [0, 0], "facing": "north" } } },
              { "type": "campaign", "id": "camp", "name": "Camp", "start": { "area": "room", "entry": "start" }, "party": { "min": 1, "max": 4 } },
              { "type": "patch", "id": "rename_merchant", "target_type": "scene", "target": "shared:merchant_base",
                "set": { "speaker": "Master Grocer" } },
              { "type": "scene", "id": "local_merchant", "extends": "shared:merchant_base", "speaker": "Hilda" }
            ]
            """);

        ModuleSet set = ModuleLoader.Load(campaign, []);
        Assert.Empty(set.Diagnostics);

        Definition baseLowered = set.Rules!.Find(DefinitionTypes.Event, "shared:merchant_base", out _)!;
        Definition childLowered = set.Rules.Find(DefinitionTypes.Event, "camp:local_merchant", out _)!;
        Assert.Equal("Master Grocer: Fine wares for travellers!", baseLowered.Json.GetProperty("text").GetString());
        Assert.Equal("Hilda: Fine wares for travellers!", childLowered.Json.GetProperty("text").GetString());

        (int code, string schema) = Run(modules.Root, "schema", "scene", "--json");
        Assert.Equal(TalesCli.Ok, code);
        using JsonDocument doc = JsonDocument.Parse(schema);
        List<string> fields = doc.RootElement.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()!).ToList();
        Assert.Contains("speaker", fields);
        Assert.Contains("start", fields);
        Assert.Contains("nodes", fields);
    }

    private static void WriteInvestigationCampaign(TempModules modules)
    {
        Rules.WriteSmallRuleset(modules);
        modules.Write("rules/extras.json", """
            [
              { "type": "economy", "id": "standard", "sell_fraction": 0.5 },
              { "type": "check", "id": "persuade", "name": "Persuade", "roll": "1d20", "bonus": "self.str", "target": "5", "succeeds": "at-least" },
              { "type": "item", "id": "lantern", "name": "Brass lantern", "kind": "gear", "cost": 5, "currency": "gold", "weight": 1 },
              { "type": "item", "id": "cellar_key", "name": "Cellar key", "kind": "key", "cost": 0, "currency": "gold", "weight": 0 },
              {
                "type": "character-creation",
                "id": "standard",
                "name": "Standard",
                "attributes": ["str"],
                "method": "array",
                "array": [14],
                "default": true
              }
            ]
            """);
        modules.Module("art", "assets");
        modules.Module("town", "campaign", requires: string.Join(", ", Require("rules", "*"), Require("art", "*")));
        modules.Write("town/world.json", """
            [
              {
                "type": "campaign",
                "id": "town",
                "name": "Lantern Watch",
                "start": { "area": "square", "entry": "gate" },
                "party": { "min": 1, "max": 4 },
                "starting_party": ["investigator"]
              },
              {
                "type": "npc",
                "id": "investigator",
                "character": {
                  "name": "Rowan",
                  "creation": "rules:standard",
                  "levels": [ { "class": "rules:warrior", "gain": 10 } ],
                  "experience": 0,
                  "class_experience": { "rules:warrior": 0 },
                  "attributes": { "str": 14 },
                  "tracks": { "hit_points": { "current": 10, "max": 10 } },
                  "balances": { "gold": 20 },
                  "equipment": [],
                  "conditions": []
                }
              },
              { "type": "variable", "id": "heard_rumor", "value_type": "boolean", "initial": "false" },
              { "type": "variable", "id": "has_evidence", "value_type": "boolean", "initial": "false" },
              { "type": "variable", "id": "case_solved", "value_type": "boolean", "initial": "false" },
              {
                "type": "area",
                "id": "square",
                "name": "Market Square",
                "map": [
                  "+--+--+--+",
                  "|        |",
                  "+--+--+--+"
                ],
                "cells": [
                  { "at": [1, 0], "event": "constable" },
                  { "at": [2, 0], "event": "innkeeper" }
                ],
                "entries": { "gate": { "at": [0, 0], "facing": "east" } }
              },
              {
                "type": "event",
                "id": "outfitter_shop",
                "kind": "shop",
                "text": "The inn's supply counter is open.",
                "items": [ { "item": "rules:lantern" } ],
                "next": "innkeeper__topics"
              }
            ]
            """);

        modules.Write("town/scenes.json", """
            [
              {
                "type": "scene",
                "id": "innkeeper",
                "speaker": "Bram",
                "start": "greet",
                "nodes": {
                  "greet": {
                    "branch": [ { "when": "campaign.var.case_solved", "goto": "after_case" } ],
                    "say": "Storm's rolling in. What brings you to the Hearth?",
                    "goto": "topics"
                  },
                  "after_case": {
                    "say": "Drinks are on the house for the town's saviours!"
                  },
                  "topics": {
                    "prompt": "What do you ask Bram?",
                    "choices": [
                      {
                        "label": "Ask about the missing scout",
                        "say": "She took the north road at dusk.",
                        "set": { "heard_rumor": "true" },
                        "goto": "topics"
                      },
                      {
                        "label": "Press him about the cellar key",
                        "when": "campaign.var.heard_rumor and not campaign.var.has_evidence",
                        "goto": "press"
                      },
                      {
                        "label": "Browse supplies",
                        "event": "outfitter_shop"
                      },
                      {
                        "label": "Leave",
                        "say": "Safe roads, friends."
                      }
                    ]
                  },
                  "press": {
                    "check": {
                      "check": "rules:persuade",
                      "member": 1,
                      "on_success": "confess",
                      "on_failure": "stonewall"
                    }
                  },
                  "confess": {
                    "say": "Alright! Take the cellar key.",
                    "set": { "has_evidence": "true" },
                    "give": [ { "item": "rules:cellar_key" } ],
                    "goto": "topics"
                  },
                  "stonewall": {
                    "say": "I don't know what you mean.",
                    "goto": "topics"
                  }
                }
              },
              {
                "type": "scene",
                "id": "constable",
                "speaker": "Constable Vale",
                "start": "talk",
                "nodes": {
                  "talk": {
                    "branch": [ { "when": "campaign.var.has_evidence", "goto": "solve" } ],
                    "say": "Bring me solid proof from the inn before I make an arrest."
                  },
                  "solve": {
                    "say": "That settles it — the case is solved!",
                    "set": { "case_solved": "true" },
                    "xp": { "amount": "50" },
                    "end": "Order returns to Market Square."
                  }
                }
              }
            ]
            """);
    }

    private static (int Code, string Output) Run(string workingDirectory, params string[] args)
    {
        using StringWriter output = new();
        int code = TalesCli.Run(args, output, workingDirectory);
        return (code, output.ToString());
    }
}
