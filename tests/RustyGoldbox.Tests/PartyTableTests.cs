using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Game;

namespace RustyGoldbox.Tests;

/// <summary>Who may do what at a hosted table, checked where guest actions enter the Game.</summary>
public sealed class PartyTableTests
{
    private const uint Host = PartyTable.HostMember;
    private const uint Ann = 2;
    private const uint Bo = 3;

    [Fact]
    public void SeatsMakeTheirOwnCharactersTheLeaderMovesAndFightersAnswerOnlyToTheirOwners()
    {
        using TempModules scratch = new();
        List<string> containers = new[] { "classic", "placeholder-art", "sample-crypt" }
            .Select(id => EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
            .ToList();
        List<string> script = File.ReadAllLines(CampaignTests.Script("crypt.script"))
            .Select(line => line.Split('#')[0].Trim())
            .Where(line => line.Length > 0)
            .ToList();

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = new(new ModuleLibrary(_ => containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList()));
            session.Refresh();
            Run(session, engine, JsonSerializer.Serialize(new { action = "open", campaign = Assert.Single(session.Campaigns).Bundle, seed = "12" }));
            PartyTable table = new();
            session.Table = table;
            table.Join(Host, "host-key", "Hana");
            table.Join(Ann, "ann-key", "Ann");
            table.Join(Bo, "bo-key", "Bo");

            // Each player makes their own character, and only they can change it.
            Roll(session, engine, Ann, "Ann", "classic:fighter");
            Roll(session, engine, Bo, "Bo", "classic:cleric");
            int annIndex = session.Party.FindIndex(member => member.Name == "Ann");
            int boIndex = session.Party.FindIndex(member => member.Name == "Bo");
            Assert.Equal(Ann, table.OwnerOf(annIndex));
            Assert.Equal(Bo, table.OwnerOf(boIndex));
            Assert.Equal("That character isn't yours.", For(session, engine, Ann, new { action = "equip", member = boIndex, item = "classic:heavy_mace" }));
            Assert.Null(For(session, engine, Bo, new { action = "equip", member = boIndex, item = "classic:heavy_mace" }));
            Assert.Null(For(session, engine, Ann, new { action = "equip", member = annIndex, item = "classic:long_sword" }));
            Assert.Equal("Only the host can do that.", For(session, engine, Ann, new { action = "begin" }));
            Run(session, engine, """{ "action": "begin" }""");
            Assert.Equal(Screen.Play, session.Screen);

            // The leader moves the party; the lead passes and falls back to the host when its holder leaves.
            Assert.Equal("Only the leader moves the party and deals for it.", For(session, engine, Ann, new { action = "play", command = "forward" }));
            Assert.Null(For(session, engine, Ann, new { action = "play", command = "look" }));
            Assert.Null(For(session, engine, Host, new { action = "pass-lead", to = Ann }));
            Assert.Equal(Ann, table.Leader);
            Assert.Null(For(session, engine, Ann, new { action = "play", command = "left" }));
            Assert.Null(For(session, engine, Ann, new { action = "play", command = "right" }));
            Run(session, engine, """{ "action": "play", "command": "left" }""");
            Assert.Equal("Only the leader moves the party and deals for it.", Assert.Single(session.Notes));
            table.Leave(Ann);
            Assert.Equal(Host, table.Leader);
            Assert.Equal("The lead can only pass to a player who is here.", For(session, engine, Host, new { action = "pass-lead", to = Ann }));
            table.Join(Ann + 10, "ann-key", "Ann");
            Assert.Equal(Ann + 10, table.SeatOf(Ann + 10)!.Member);
            Assert.Equal(Ann + 10, table.OwnerOf(annIndex));

            // Character commands belong to the character's owner.
            Assert.Equal("That character isn't yours.", For(session, engine, Bo, new { action = "play", command = $"use {annIndex + 1} classic:dagger" }));

            // Walk to the first fight as the leader.
            foreach (string command in script)
            {
                Run(session, engine, JsonSerializer.Serialize(new { action = "play", command }));
                if (session.Screen == Screen.Combat)
                {
                    break;
                }
            }

            CombatDecision decision = Assert.IsType<CombatDecision>(session.Combat!.PendingDecision);
            PendingCombatantSource fighter = session.CombatMetadata!.Participants.Single(participant => participant.Id == decision.ActorId);
            uint owner = table.OwnerOf(fighter.PartyIndex!.Value);
            uint other = owner == Bo ? Ann + 10 : Bo;
            table.ViewSent(session, 7);
            Assert.Equal("That fighter isn't yours.", For(session, engine, other, new { action = "combat-end-turn", actor = decision.ActorId }, seen: 7));
            Assert.StartsWith("The party has moved on", For(session, engine, owner, new { action = "combat-end-turn", actor = decision.ActorId }, seen: 6), StringComparison.Ordinal);
            Assert.Null(For(session, engine, owner, new { action = "combat-end-turn", actor = decision.ActorId }, seen: 7));
            Assert.NotEqual(decision.Id, session.Combat!.PendingDecision?.Id);
        });
    }

    [Fact]
    public void DroppingACharacterKeepsEveryOtherSeatOnItsOwn()
    {
        PartyTable table = new();
        table.Join(Ann, "a", "Ann");
        table.Join(Bo, "b", "Bo");
        table.Claim(Ann, 0);
        table.Claim(Bo, 1);
        table.Claim(Ann, 2);

        table.Dropped(0);

        Assert.Equal(Bo, table.OwnerOf(0));
        Assert.Equal(Ann, table.OwnerOf(1));
        Assert.Equal(Host, table.OwnerOf(2));
    }

    private static void Roll(GameSession session, IEngineContext engine, uint member, string name, string characterClass)
    {
        for (int attempt = 0; attempt < 50 && session.Party.All(character => character.Name != name); attempt++)
        {
            Assert.Null(For(session, engine, member, new { action = "roll", name, race = "classic:human", @class = characterClass }));
        }
    }

    private static string? For(GameSession session, IEngineContext engine, uint member, object action, ulong seen = ulong.MaxValue)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(action));
        return GameCommands.RunFor(session, engine, member, document.RootElement, seen);
    }

    private static void Run(GameSession session, IEngineContext engine, string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        GameCommands.Run(session, engine, document.RootElement);
    }
}
