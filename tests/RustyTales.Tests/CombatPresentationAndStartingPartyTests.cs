using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyTales.Cli;
using RustyTales.Core.Campaigns;
using RustyTales.Core.Characters;
using RustyTales.Core.Combat;
using RustyTales.Core.Definitions;
using RustyTales.Core.Modules;
using RustyTales.Game;
using RustyTales.Game.Presentation;
using static RustyTales.Tests.TempModules;

namespace RustyTales.Tests;

/// <summary>Multi-style combat presentation modes and fixed <c>starting_party</c> campaigns.</summary>
public sealed class CombatPresentationAndStartingPartyTests
{
    [Fact]
    public void CampaignStartingPartyValidatesSizeAndRejectsDuplicates()
    {
        using TempModules scratch = new();
        string heroJson = CreateHeroNpcJson(scratch, "hero_ada", "Ada");
        string repoModules = Path.Combine(Rules.RepositoryRoot, "modules");

        // Duplicate NPC in starting_party fails validation.
        string dupCampaign = WriteCampaign(scratch, "dup", heroJson, """["hero_ada", "hero_ada"]""", min: 1, max: 2);
        ModuleSet dupSet = ModuleLoader.Load(dupCampaign, [scratch.Root, repoModules]);
        Assert.Contains(dupSet.Diagnostics, d => d.Rule == "campaign.starting-party" && d.JsonPath == "$.starting_party[1]");

        // Fewer starting members than party.min fails validation.
        string smallCampaign = WriteCampaign(scratch, "small", heroJson, """["hero_ada"]""", min: 2, max: 4);
        ModuleSet smallSet = ModuleLoader.Load(smallCampaign, [scratch.Root, repoModules]);
        Assert.Contains(smallSet.Diagnostics, d => d.Rule == "campaign.starting-party" && d.JsonPath == "$.starting_party");
    }

    [Fact]
    public void FixedStartingPartyRunsInCliWithoutPartyArgAndSkipsPartyCreationInGame()
    {
        using TempModules scratch = new();
        string heroJson = CreateHeroNpcJson(scratch, "hero_ada", "Ada");
        string repoModules = Path.Combine(Rules.RepositoryRoot, "modules");
        string campaignDir = WriteCampaign(scratch, "jrpg-trial", heroJson, """["hero_ada"]""", min: 1, max: 4, combatPresentation: "first_person");

        // CLI `tales play` needs no `--party` when `starting_party` is declared.
        string scriptPath = Path.Combine(scratch.Root, "walk.script");
        File.WriteAllText(scriptPath, "status\nforward\n");
        using StringWriter cliOut = new();
        int code = TalesCli.Run(["play", "--campaign", campaignDir, "--modules", repoModules, "--seed", "7", "--script", scriptPath], cliOut, scratch.Root);
        Assert.Equal(TalesCli.Ok, code);
        Assert.Contains("Ada", cliOut.ToString(), StringComparison.Ordinal);

        // Game single-player `open` skips Screen.Party straight into Screen.Play, and switches combat views cleanly.
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            string classic = EngineContentTests.Pack(Path.Combine(repoModules, "classic"), scratch);
            string art = EngineContentTests.Pack(Path.Combine(repoModules, "placeholder-art"), scratch);
            string camp = EngineContentTests.Pack(campaignDir, scratch);
            ModuleLibrary library = new(_ =>
            [
                ProductContentBundle.OpenContainer(engine.Content, classic),
                ProductContentBundle.OpenContainer(engine.Content, art),
                ProductContentBundle.OpenContainer(engine.Content, camp),
            ]);
            GameSession session = new(library);
            session.Refresh();
            string bundle = Assert.Single(session.Campaigns).Bundle;

            GameCommands.Run(session, engine, JsonDocument.Parse(JsonSerializer.Serialize(new { action = "open", campaign = bundle, seed = "3" })).RootElement);
            Assert.Equal(Screen.Play, session.Screen);
            Assert.Equal("Ada", Assert.Single(session.Runner!.State.Party).Name);

            // Stepping into the fight starts combat with the campaign's authored `first_person` presentation.
            session.Execute(engine, "forward");
            Assert.Equal(Screen.Combat, session.Screen);
            JsonObject projection = SessionProjection.Build(session);
            Assert.Equal("first_person", projection["fight"]!["presentation"]!.GetValue<string>());

            // Render all three combat presentations through SceneView and verify debug toggle.
            using SceneView view = new(engine, library);
            view.Show(session);

            TalesDebugModule debug = new(session, engine, () => view.Show(session));
            Assert.True(debug.CombatView("side_view").Succeeded);
            Assert.Equal("side_view", SessionProjection.Build(session)["fight"]!["presentation"]!.GetValue<string>());
            view.Show(session);

            Assert.True(debug.CombatView("tactical").Succeeded);
            Assert.Equal("tactical", SessionProjection.Build(session)["fight"]!["presentation"]!.GetValue<string>());
            view.Show(session);

            Assert.True(debug.CombatView("auto").Succeeded);
            Assert.Equal("first_person", SessionProjection.Build(session)["fight"]!["presentation"]!.GetValue<string>());
        });
    }

    [Fact]
    public void FirstPersonAndSideViewCameraPosesLookTowardTheStage()
    {
        CameraPose fp = CombatScene.PoseForFirstPerson(6, 4, 16.0 / 9.0);
        Assert.Equal(3f, fp.Position.X, 3);
        Assert.True(fp.Position.Z > 4f);
        Assert.InRange(fp.PitchDegrees, -10, 0);

        CameraPose sv = CombatScene.PoseForSideView(7, 4, 16.0 / 9.0);
        Assert.Equal(3.5f, sv.Position.X, 3);
        Assert.Equal(-12, sv.PitchDegrees, 3);
        Assert.True(sv.Position.Z > 4f);
    }

    private static string CreateHeroNpcJson(TempModules scratch, string npcId, string name)
    {
        string charPath = Path.Combine(scratch.Root, $"{npcId}-char.json");
        string npcPath = Path.Combine(scratch.Root, $"{npcId}.json");
        string classic = Path.Combine(Rules.RepositoryRoot, "modules", "classic");
        using StringWriter output = new();
        int created = TalesCli.Run(
            ["character", "new", "--module", classic, "--class", "fighter", "--race", "human", "--name", name, "--attributes", "str=15,dex=12,con=14,int=10,wis=10,cha=10", "--equipment", "long_sword,chain_mail", "--seed", "1", "--out", charPath],
            output,
            scratch.Root);
        Assert.Equal(TalesCli.Ok, created);
        int exported = TalesCli.Run(["character", "npc", charPath, "--module", classic, "--id", npcId, "--out", npcPath], output, scratch.Root);
        Assert.Equal(TalesCli.Ok, exported);
        return File.ReadAllText(npcPath);
    }

    private static string WriteCampaign(TempModules scratch, string id, string heroNpcJson, string startingPartyJson, int min, int max, string? combatPresentation = null)
    {
        string dir = Path.Combine(scratch.Root, id);
        scratch.Write($"{id}/module.json", $$"""
            {
              "format": 1,
              "id": "{{id}}",
              "kind": "campaign",
              "version": "0.1.0",
              "title": "Test {{id}}",
              "requires": [
                { "id": "classic", "version": "^0.1.0" },
                { "id": "placeholder-art", "version": "^0.1.0" }
              ],
              "provenance": "Original test fixture."
            }
            """);
        scratch.Write($"{id}/npcs/hero.json", heroNpcJson);
        string presentationField = combatPresentation is null ? "" : $", \"combat_presentation\": \"{combatPresentation}\"";
        scratch.Write($"{id}/campaign.json", $$"""
            {
              "type": "campaign",
              "id": "tale",
              "name": "Trial {{id}}",
              "start": { "area": "hall", "entry": "in" },
              "party": { "min": {{min}}, "max": {{max}} },
              "starting_party": {{startingPartyJson}}{{presentationField}}
            }
            """);
        scratch.Write($"{id}/areas/hall.json", """
            {
              "type": "area",
              "id": "hall",
              "name": "Hall",
              "wall_set": "placeholder-art:stone_wall",
              "map": ["+--+--+", "|     |", "+--+--+"],
              "cells": [{ "at": [1, 0], "event": "fight" }],
              "entries": { "in": { "at": [0, 0], "facing": "east" } }
            }
            """);
        scratch.Write($"{id}/events/fight.json", """
            {
              "type": "event",
              "id": "fight",
              "kind": "combat",
              "encounter": "classic:crypt_guard"
            }
            """);
        return dir;
    }
}
