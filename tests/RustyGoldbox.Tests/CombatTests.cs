using System.Globalization;
using System.Text.RegularExpressions;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

/// <summary>
/// The combat loop under four ruleset shapes: classic (descending AC, one
/// action), ascend (ascending AC, critical tier, standard and move budget),
/// degrees (three actions, four degrees of success, basic saves) and
/// percentile (d100 roll-under, special and fumble tiers, active parry).
/// </summary>
public sealed class CombatTests
{
    private static string Fixture(string name) => Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", name);

    [Fact]
    public void ClassicCastersSpendSpellSlots()
    {
        using TempModules scratch = new();
        WriteCharacter(scratch, Rules.ClassicPath, "ada.json", new CreationRequest("Ada", "fighter", "human", Attributes: Scores(("str", 16), ("dex", 13), ("con", 15), ("int", 10), ("wis", 9), ("cha", 11))), "long_sword", "chain_mail", "shield");
        Golden.Verify("classic-spells.txt", CliTranscript.Run(scratch.Root,
            ["character", "new", "--module", Rules.ClassicPath, "--class", "magic_user", "--race", "human", "--name", "Mira", "--attributes", "str=9,dex=14,con=12,int=16,wis=10,cha=10", "--spells", "bless", "--out", "mira.json"],
            ["character", "new", "--module", Rules.ClassicPath, "--class", "magic_user", "--race", "human", "--name", "Mira", "--attributes", "str=9,dex=14,con=12,int=16,wis=10,cha=10", "--spells", "sleep,magic_missile", "--out", "mira.json"],
            // Mira memorises the first spell she knows in her one slot: sleep takes 2d4 rats of 4 hit dice or fewer.
            ["sim", "combat", "--module", Rules.ClassicPath, "--party", "ada.json,mira.json", "--encounter", "rat_pack", "--seed", "3"],
            // Skeletons are undead, so sleep has no one to take, and magic missile isn't memorised.
            ["sim", "combat", "--module", Rules.ClassicPath, "--party", "ada.json,mira.json", "--encounter", "crypt_guard", "--seed", "3"],
            // One slot holds one copy; memorising magic missile instead lets her cast it.
            ["character", "spells", "mira.json", "--module", Rules.ClassicPath, "--memorise", "sleep,magic_missile"],
            ["character", "spells", "mira.json", "--module", Rules.ClassicPath, "--memorise", "magic_missile"],
            ["sim", "combat", "--module", Rules.ClassicPath, "--party", "ada.json,mira.json", "--encounter", "crypt_guard", "--seed", "3"],
            // At level 3 she fires two missiles a casting, each at the weakest skeleton still standing, and memorises two castings.
            ["character", "level", "mira.json", "--module", Rules.ClassicPath, "--xp", "5001", "--seed", "1"],
            ["character", "spells", "mira.json", "--module", Rules.ClassicPath, "--memorise", "magic_missile,magic_missile"],
            ["sim", "combat", "--module", Rules.ClassicPath, "--party", "ada.json,mira.json", "--encounter", "crypt_guard", "--seed", "3"]));
    }

    [Fact]
    public void APreparedCopyIsCastOnceWhateverSlotsAreLeft()
    {
        ModuleSet set = ModuleLoader.Load(Fixture("ascend"), []);
        RuleSet rules = set.Rules!;
        List<ModuleDiagnostic> problems = [];
        Character ilse = WithDice(dice => CharacterRules.Create(rules, Character.StampsOf(set), new CreationRequest("Ilse", "adept", "folk", Attributes: Scores(("might", 9), ("grace", 12), ("grit", 16), ("wit", 16)), Features: ["lightning_reflexes"]), dice, problems))!;
        WithDice(dice => CharacterRules.AddExperience(rules, ilse, 3000, dice, problems, null, ["great_fortitude"]));
        Assert.True(CharacterRules.SetSpells(rules, ilse, ["flare"], problems));
        Assert.Empty(problems);

        // Two slots at level 3: without a plan she prepares flare twice; memorising one copy leaves a slot unused.
        Assert.Equal(["flare", "flare"], CharacterRules.MemorisedPlan(rules, ilse).Select(spell => spell.Id));
        Assert.True(CharacterRules.SetMemorised(rules, ilse, ["flare"], problems));
        Definition combat = rules.Find(DefinitionTypes.Combat, "standard", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, "brutes", out _)!;
        CombatResult result = WithDice(dice => CombatRunner.Run(rules, combat,
            [new CombatSide("Party", [Combatant.FromCharacter(rules, ilse)]), new CombatSide("Brutes", Encounters.Spawn(rules, encounter, dice))],
            dice, 6, encounter));

        List<string> lines = result.Facts.Select(fact => fact.Describe()).ToList();
        Assert.Single(lines, line => line.StartsWith("Ilse uses Flare", StringComparison.Ordinal));
        Assert.Contains("Ilse spends 1 1st circle slots (1 left).", lines);
        Assert.Empty(result.Sides[0].Members[0].Prepared);
    }

    [Fact]
    public void ClassicPartyFightsAnEncounter()
    {
        using TempModules scratch = new();
        WriteCharacter(scratch, Rules.ClassicPath, "ada.json", new CreationRequest("Ada", "fighter", "human", Attributes: Scores(("str", 16), ("dex", 13), ("con", 15), ("int", 10), ("wis", 9), ("cha", 11))), "long_sword", "chain_mail", "shield");
        WriteCharacter(scratch, Rules.ClassicPath, "brom.json", new CreationRequest("Brom", "cleric", "dwarf", Attributes: Scores(("str", 13), ("dex", 10), ("con", 14), ("int", 9), ("wis", 15), ("cha", 10))), "heavy_mace", "chain_mail");
        WriteCharacter(scratch, Rules.ClassicPath, "robin.json", new CreationRequest("Robin", "fighter", "human", Attributes: Scores(("str", 12), ("dex", 17), ("con", 13), ("int", 10), ("wis", 9), ("cha", 11))), "short_bow", "leather_armour");

        Golden.Verify("classic-combat.txt", CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", Rules.ClassicPath, "--party", "ada.json,brom.json", "--encounter", "crypt_guard", "--seed", "3"],
            ["sim", "combat", "--module", Rules.ClassicPath, "--party", "ada.json,brom.json", "--encounter", "ogre", "--seed", "1", "--runs", "200"],
            // The acolyte blesses its side and heals from its own two 1st level spells.
            ["sim", "combat", "--module", Rules.ClassicPath, "--party", "ada.json,brom.json", "--encounter", "crypt_cult", "--seed", "2"],
            // Ada and Brom charge; Robin shoots, at -2 beyond the bow's first 50 ft; the acolyte, badly hurt,
            // flees past two parting blows (+4) and gets away at the field's edge.
            ["sim", "combat", "--module", Rules.ClassicPath, "--party", "ada.json,brom.json,robin.json", "--encounter", "crypt_cult", "--seed", "7"]));
    }

    [Fact]
    public void AscendingArmourClassFightUsesCriticalsAndTwoBudgets()
    {
        using TempModules scratch = new();
        WriteCharacter(scratch, Fixture("ascend"), "kara.json", new CreationRequest("Kara", "warrior", "folk", Attributes: Scores(("might", 16), ("grace", 12), ("grit", 14), ("wit", 10)), Features: ["iron_will", "press_the_advantage"]), "longsword");
        WriteCharacter(scratch, Fixture("ascend"), "ilse.json", new CreationRequest("Ilse", "adept", "folk", Attributes: Scores(("might", 9), ("grace", 12), ("grit", 12), ("wit", 16)), Features: ["lightning_reflexes"]));
        WriteCharacter(scratch, Fixture("ascend"), "mender.json", new CreationRequest("Ilse", "adept", "folk", Attributes: Scores(("might", 9), ("grace", 12), ("grit", 16), ("wit", 16)), Features: ["lightning_reflexes"]), "chain_shirt");

        // Ilse has no weapon, so she hexes: the brute saves against her difficulty class.
        Golden.Verify("ascend-combat.txt", CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", Fixture("ascend"), "--party", "kara.json", "--encounter", "brutes", "--seed", "11"],
            ["sim", "combat", "--module", Fixture("ascend"), "--party", "kara.json,ilse.json", "--encounter", "brutes", "--seed", "20"],
            // Ember sets Kara burning for a rolled amount each turn until a save at the end of her turn ends it,
            // and fans the flames for 1d6 when she already burns.
            ["sim", "combat", "--module", Fixture("ascend"), "--party", "kara.json", "--encounter", "imps", "--seed", "1"],
            // The bullies' smash prefers the target with the best attack, so Kara falls; Ilse mends her back into
            // the fight, and a fallen bully's conditions still wear off (downed_conditions).
            ["sim", "combat", "--module", Fixture("ascend"), "--party", "kara.json,mender.json", "--encounter", "bullies", "--seed", "14"]));
    }

    [Fact]
    public void AMulticlassCharacterHasEveryClassesActionsOnce()
    {
        ModuleSet set = ModuleLoader.Load(Fixture("ascend"), []);
        List<ModuleDiagnostic> problems = [];
        Character character = WithDice(dice => CharacterRules.Create(set.Rules!, Character.StampsOf(set), new CreationRequest("Kara", "warrior", "folk", Attributes: Scores(("might", 16), ("grace", 12), ("grit", 14), ("wit", 12)), Features: ["second_wind", "improved_initiative"]), dice, problems))!;
        WithDice(dice => CharacterRules.AddExperience(set.Rules!, character, 3000, dice, problems, "adept", ["great_fortitude"]));
        Assert.Empty(problems);

        Combatant combatant = Combatant.FromCharacter(set.Rules!, character);

        // Warrior's uses first, then the adept's (its punch is the warrior's, so it appears once), then the features'.
        Assert.Equal(["Advance", "Aim", "Punch", "Mend", "Patch up", "Hex", "Second wind"], combatant.Uses.Select(use => use.Name));
        Evaluator evaluator = new(set.Rules!, null);
        // Warrior 1 and adept 2 each add their own progression.
        Assert.Equal(2m, evaluator.Stat(combatant.Creature, "attack_bonus").Number);
        Assert.Equal(3m, evaluator.Stat(combatant.Creature, "will_base").Number);
    }

    [Fact]
    public void DegreesFightUsesThreeActionsAndFourDegrees()
    {
        using TempModules scratch = new();
        WriteCharacter(scratch, Fixture("degrees"), "tor.json", new CreationRequest("Tor", "vanguard", "hillfolk",
            Features: ["stonehide", "sentry", "hill_toughness", "shield_ward"], Boosts: ["brawn", "insight", "stamina", "brawn", "brawn", "stamina", "finesse", "insight"]), "longblade", "buckler");
        WriteCharacter(scratch, Fixture("degrees"), "wren.json", new CreationRequest("Wren", "mystic", "sylvan",
            Features: ["duskwood", "scribe", "sylvan_step", "spark"], Boosts: ["insight", "insight", "finesse", "intellect", "finesse", "stamina", "brawn"]), "staff");

        // Natural 20s and 1s shift a degree; spark is a basic save the target rolls against Wren's spell DC.
        // In the second, Wren's scored soothe outranks her attacks once she is hurt enough.
        Golden.Verify("degrees-combat.txt", CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", Fixture("degrees"), "--party", "tor.json,wren.json", "--encounter", "raiders", "--seed", "3"],
            ["sim", "combat", "--module", Fixture("degrees"), "--party", "tor.json,wren.json", "--encounter", "raiders", "--seed", "2"]));
    }

    [Fact]
    public void WhatACreatureHasEquippedDecidesWhatWorks()
    {
        using TempModules scratch = new();
        CreationRequest tor = new("Tor", "vanguard", "hillfolk",
            Features: ["stonehide", "sentry", "hill_toughness", "shield_ward"], Boosts: ["brawn", "insight", "stamina", "brawn", "brawn", "stamina", "finesse", "insight"]);
        WriteCharacter(scratch, Fixture("degrees"), "shielded.json", tor, "longblade", "buckler");
        WriteCharacter(scratch, Fixture("degrees"), "bare.json", tor, "longblade");
        CreationRequest ilse = new("Ilse", "adept", "folk", Attributes: Scores(("might", 9), ("grace", 12), ("grit", 16), ("wit", 16)), Features: ["lightning_reflexes"]);
        WriteCharacter(scratch, Fixture("ascend"), "light.json", ilse, "chain_shirt");
        WriteCharacter(scratch, Fixture("ascend"), "heavy.json", ilse, "banded_mail");

        string Sim(string module, string party, string encounter) => CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", Fixture(module), "--party", party, "--encounter", encounter, "--seed", "3"]);

        // Degrees: shield ward's armour and the raise-shield reaction need a shield in hand.
        Assert.Contains("Tor reacts to", Sim("degrees", "shielded.json", "raiders"), StringComparison.Ordinal);
        Assert.DoesNotContain("Tor reacts to", Sim("degrees", "bare.json", "raiders"), StringComparison.Ordinal);
        decimal Ac(string file) => decimal.Parse(Regex.Match(CliTranscript.Run(scratch.Root, ["character", "show", file, "--module", Fixture("degrees")]), @"\bac\s+(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.Equal(Ac("bare.json") + 1, Ac("shielded.json"));
        // Ascend: an adept can't hex in heavy armour.
        Assert.Contains("Ilse uses Hex", Sim("ascend", "light.json", "bullies"), StringComparison.Ordinal);
        Assert.DoesNotContain("Ilse uses Hex", Sim("ascend", "heavy.json", "bullies"), StringComparison.Ordinal);
    }

    [Fact]
    public void FateConflictsAbsorbHitsWithStressAndConsequences()
    {
        using TempModules scratch = new();
        string fate = Path.Combine(Rules.RepositoryRoot, "modules", "fate-condensed");

        // No races or classes: skills by the pyramid (a priority names the top ten), three stunts, stress from Physique and Will.
        // Minor NPCs fall to any hit their stress can't hold; the ghoul takes consequences first.
        Golden.Verify("fate-conflict.txt", CliTranscript.Run(scratch.Root,
            ["character", "new", "--module", fate, "--name", "Ruth", "--priority", "fight,athletics,physique,notice,will,shoot,empathy,lore,provoke,stealth",
                "--feature", "heavy_hitter,tough_as_nails,quick_feet", "--out", "ruth.json"],
            ["character", "new", "--module", fate, "--name", "Ethan", "--priority", "shoot,notice,athletics,burglary,will,provoke,fight,crafts,stealth,deceive",
                "--feature", "deadeye,iron_will,cutting_words", "--out", "ethan.json"],
            ["character", "new", "--module", fate, "--name", "Nobody", "--class", "fighter"],
            ["sim", "combat", "--module", fate, "--party", "ruth.json,ethan.json", "--encounter", "cult_cell", "--seed", "1"],
            ["sim", "combat", "--module", fate, "--party", "ruth.json,ethan.json", "--encounter", "ghoul", "--seed", "2"]));
    }

    [Fact]
    public void UniversalD100FightsAreDodgedParriedAndMajorWoundsShock()
    {
        using TempModules scratch = new();
        string d100 = Path.Combine(Rules.RepositoryRoot, "modules", "universal-d100");

        // Characteristics are rolled (SIZ and INT at 2D6+6), a profession gives the skills, and gear brings armour and its penalties.
        string transcript = CliTranscript.Run(scratch.Root,
            ["character", "new", "--module", d100, "--name", "Aldric", "--feature", "soldier", "--seed", "4", "--out", "aldric.json"],
            ["character", "new", "--module", d100, "--name", "Mira", "--feature", "hunter", "--seed", "9", "--out", "mira.json"]);
        Equip(scratch, "aldric.json", "universal-d100", "broadsword", "heater_shield", "ring_armour");
        Equip(scratch, "mira.json", "universal-d100", "self_bow", "dagger", "soft_leather");

        // Each takes its best weapon; defences after the first in a round are at -30%; a major wound puts Mira in shock.
        Golden.Verify("universal-d100-combat.txt", transcript + CliTranscript.Run(scratch.Root,
            ["character", "show", "aldric.json", "--module", d100],
            ["sim", "combat", "--module", d100, "--party", "aldric.json,mira.json", "--encounter", "bandits", "--seed", "1"],
            ["sim", "combat", "--module", d100, "--party", "aldric.json,mira.json", "--encounter", "bear", "--seed", "2"]));
    }

    [Fact]
    public void ScifiDamageComesOffCharacteristicsUntilTwoAreGone()
    {
        using TempModules scratch = new();
        string scifi = Path.Combine(Rules.RepositoryRoot, "modules", "scifi-2d6");
        string transcript = CliTranscript.Run(scratch.Root,
            ["character", "new", "--module", scifi, "--name", "Vance", "--feature", "marine", "--priority", "end,dex,str,int,edu,soc", "--seed", "3", "--out", "vance.json"],
            ["character", "new", "--module", scifi, "--name", "Kira", "--feature", "mercenary", "--priority", "str,dex,end,int,edu,soc", "--seed", "5", "--out", "kira.json"]);
        Equip(scratch, "vance.json", "scifi-2d6", "rifle", "mesh");
        Equip(scratch, "kira.json", "scifi-2d6", "cutlass", "jack");

        // 2D6 + skill + DM against 8; damage is weapon dice + Effect - armour, off Endurance, then Strength or Dexterity;
        // the DMs fall with them, and two characteristics at 0 is unconscious. Pirates dodge (attacker -1).
        Golden.Verify("scifi-2d6-combat.txt", transcript + CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", scifi, "--party", "vance.json,kira.json", "--encounter", "boarders", "--seed", "1"]));
    }

    [Fact]
    public void FifthEditionPartiesFightWithDeathSavesAndSpells()
    {
        using TempModules scratch = new();
        string fifth = Path.Combine(Rules.RepositoryRoot, "modules", "fifth-srd");
        string[] New(string file, string name, string cls, string race, string priority, string features, string? spells = null) =>
            ["character", "new", "--module", fifth, "--class", cls, "--race", race, "--name", name, "--priority", priority, "--feature", features,
                .. (spells is null ? Array.Empty<string>() : ["--spells", spells]), "--out", file];

        // Level 1: a background and origin feat, a fighting style; level 5 by experience with a subclass at 3 and an Ability Score Improvement at 4.
        string transcript = CliTranscript.Run(scratch.Root,
            New("bram.json", "Bram", "fighter", "human", "str,con,dex,wis,cha,int", "soldier,savage_attacker,defense"),
            New("pip.json", "Pip", "rogue", "halfling", "dex,con,wis,int,cha,str", "criminal,alert"),
            New("hild.json", "Hild", "cleric", "dwarf", "wis,con,str,dex,cha,int", "acolyte,tough", "healing_word,cure_wounds,guiding_bolt,sacred_flame"),
            New("ilsa.json", "Ilsa", "wizard", "elf", "int,dex,con,wis,cha,str", "sage,alert", "magic_missile,fire_bolt"));
        Equip(scratch, "bram.json", "fifth-srd", "longsword", "chain_mail", "shield");
        Equip(scratch, "pip.json", "fifth-srd", "rapier", "shortbow", "leather");
        Equip(scratch, "hild.json", "fifth-srd", "mace", "scale_mail", "shield");
        Equip(scratch, "ilsa.json", "fifth-srd", "dagger");

        // At level 1 goblins drop characters to 0: death saves, Healing Word, Opportunity Attacks and the wizard's Shield.
        transcript += CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", fifth, "--party", "bram.json,pip.json,hild.json,ilsa.json", "--encounter", "goblin_ambush", "--seed", "4"],
            ["sim", "combat", "--module", fifth, "--party", "bram.json,pip.json,hild.json,ilsa.json", "--encounter", "wolf_pack", "--seed", "5"],
            ["character", "level", "bram.json", "--module", fifth, "--xp", "6500", "--class", "fighter", "--feature", "champion,asi_str"],
            ["character", "level", "ilsa.json", "--module", fifth, "--xp", "6500", "--class", "wizard", "--feature", "evoker,asi_int"],
            ["character", "spells", "ilsa.json", "--module", fifth, "--set", "fireball,scorching_ray,magic_missile,fire_bolt"]);

        // At level 5: two attacks, criticals on 19, Fireball and Scorching Ray; Undead Fortitude keeps zombies standing.
        Golden.Verify("fifth-srd-combat.txt", transcript + CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", fifth, "--party", "bram.json,ilsa.json", "--encounter", "restless_dead", "--seed", "1"]));
    }

    [Fact]
    public void ThreeActionPartiesFightWithDegreesDyingAndShields()
    {
        using TempModules scratch = new();
        string three = Path.Combine(Rules.RepositoryRoot, "modules", "three-action");
        string[] New(string file, string name, string cls, string race, string features, string boosts, string? spells = null) =>
            ["character", "new", "--module", three, "--class", cls, "--race", race, "--name", name, "--feature", features, "--boosts", boosts,
                .. (spells is null ? Array.Empty<string>() : ["--spells", spells]), "--out", file];

        // Boosts from ancestry, background, class and four free; a heritage, a background and the level 1 class choices.
        string transcript = CliTranscript.Run(scratch.Root,
            New("bram.json", "Bram", "fighter", "human", "skilled_human,warrior,sudden_charge", "str,con,str,dex,str,str,con,dex,wis"),
            New("pip.json", "Pip", "rogue", "halfling", "gutsy_halfling,criminal,thief,nimble_dodge", "con,dex,wis,dex,con,wis,int"),
            New("hild.json", "Hild", "cleric", "dwarf", "rock_dwarf,acolyte,warpriest", "str,wis,con,wis,con,str,dex", "heal,heal_wounds,bless,divine_lance,shield"),
            New("ilsa.json", "Ilsa", "wizard", "elf", "ancient_elf,scholar", "wis,int,dex,int,dex,con,wis", "force_barrage,ignition,shield"));
        Equip(scratch, "bram.json", "three-action", "longsword", "chain_mail", "steel_shield");
        Equip(scratch, "pip.json", "three-action", "rapier", "shortbow", "studded_leather");
        Equip(scratch, "hild.json", "three-action", "mace", "scale_mail", "wooden_shield");
        Equip(scratch, "ilsa.json", "three-action", "staff");

        // Three actions and the multiple attack penalty, flanking and sneak attack, Knockdown and Stand, raised shields
        // that last until the holder's next turn starts; bandits drop characters to dying, wounded makes it worse.
        transcript += CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", three, "--party", "bram.json,pip.json,hild.json,ilsa.json", "--encounter", "wolf_pack", "--seed", "5"],
            ["sim", "combat", "--module", three, "--party", "bram.json,pip.json,hild.json,ilsa.json", "--encounter", "highway_robbery", "--seed", "4"],
            ["character", "level", "bram.json", "--module", three, "--xp", "4000", "--class", "fighter", "--feature", "vicious_swing,toughness,snagging_strike", "--boosts", "str,con,dex,wis"],
            ["character", "level", "ilsa.json", "--module", three, "--xp", "4000", "--class", "wizard", "--feature", "widen_spell,toughness,counterspell", "--boosts", "int,con,dex,wis"],
            ["character", "spells", "ilsa.json", "--module", three, "--set", "fireball,blazing_bolt,force_barrage,ignition,shield"]);

        // At level 5: master proficiency, Vicious Swing, Fireball's basic Reflex save; zombies are weak to slashing.
        Golden.Verify("three-action-combat.txt", transcript + CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", three, "--party", "bram.json,ilsa.json", "--encounter", "shambling_dead", "--seed", "2"]));
    }

    [Fact]
    public void PoolsFightCountsSuccessesWithRerollsAndCancels()
    {
        using TempModules scratch = new();
        WriteCharacter(scratch, Fixture("pools"), "mags.json", new CreationRequest("Mags", "bruiser", "human", Attributes: Scores(("power", 4), ("finesse", 3), ("resolve", 2))), "club");
        // Bolt spends 2 of Mags's 4 willpower, a pool rather than slots, so Mags casts twice and then fights.
        string file = Path.Combine(scratch.Root, "mags.json");
        System.Text.Json.Nodes.JsonNode mags = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
        mags["spells"] = new System.Text.Json.Nodes.JsonArray("pools:bolt");
        File.WriteAllText(file, mags.ToJsonString());

        // Tens roll again and ones cancel successes (a thug botches below 0); the club's damage explodes on a 6.
        Golden.Verify("pools-combat.txt", CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", Fixture("pools"), "--party", "mags.json", "--encounter", "thugs", "--seed", "3"]));
    }

    [Fact]
    public void PercentileFightRollsUnderAndParries()
    {
        using TempModules scratch = new();
        WriteCharacter(scratch, Fixture("percentile"), "sten.json", new CreationRequest("Sten", "soldier", "human", Attributes: Scores(("body", 15), ("agility", 13), ("mind", 10))), "sword");

        Golden.Verify("percentile-combat.txt", CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", Fixture("percentile"), "--party", "sten.json", "--encounter", "wolves", "--seed", "13"],
            // A parry that succeeds by less than the attack lets 1 point through per 10 of the difference (round 6).
            ["sim", "combat", "--module", Fixture("percentile"), "--party", "sten.json", "--encounter", "wolves", "--seed", "2"],
            ["sim", "combat", "--module", Fixture("percentile"), "--party", "sten.json", "--encounter", "wolves", "--seed", "13", "--runs", "100", "--json"]));
    }

    [Theory]
    [InlineData("15", "5", "at-least", "great")]
    [InlineData("12", "5", "at-least", "success")]
    [InlineData("4", "5", "at-least", "failure")]
    [InlineData("1", "15", "at-least", "awful")]
    [InlineData("2", "10", "at-most", "special")]
    [InlineData("39", "40", "at-most", "success")]
    public void CheckTiersReadTheRollAndMargin(string roll, string target, string succeeds, string tier)
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/test.json", $$"""
            { "type": "check", "id": "test", "name": "Test", "roll": "{{roll}}", "target": "{{target}}", "succeeds": "{{succeeds}}",
              "tiers": [ { "name": "great", "when": "check.margin >= 10" }, { "name": "awful", "when": "check.margin <= -10" }, { "name": "special", "when": "check.roll <= floor(check.target / 5)" } ] }
            """);
        RuleSet rules = Rules.LoadValid(root);

        CheckResult result = new Evaluator(rules, null).Check(rules.Find(DefinitionTypes.Check, "test", out _)!, new Creature("self"), null);

        Assert.Equal(tier, result.Tier);
    }

    [Fact]
    public void DicePoolsCountSuccesses()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(Rules.WriteSmallRuleset(modules));
        CompiledExpression pool = rules.Compile("roll_count(6, 10, 8)", "rules", Roots.None);

        (decimal successes, DiceRoll roll) = WithDice(dice => (new Evaluator(rules, dice).Evaluate(pool, null, null).Number, dice.Rolls.Single()));

        Assert.Equal(roll.Faces.Count(face => face >= 8), successes);
        Assert.Equal(6, roll.Faces.Count);
    }

    [Fact]
    public void ConditionsTickLastAndStopActions()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition hexer = rules.Find(DefinitionTypes.Monster, "hexer", out _)!;
        Definition dummy = rules.Find(DefinitionTypes.Monster, "dummy", out _)!;

        CombatResult result = WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            return CombatRunner.Run(rules, combat,
            [
                new CombatSide("Hexers", [Combatant.FromMonster(rules, hexer, "Hexer", evaluator)]),
                new CombatSide("Dummies", [Combatant.FromMonster(rules, dummy, "Dummy", evaluator)]),
            ], dice, maxRounds: 10);
        });

        List<string> lines = result.Facts.Select(fact => fact.Describe()).ToList();
        Assert.Contains("Dummy is Hexed for 1 round.", lines);
        Assert.Contains("Dummy doesn't act (hexed).", lines);
        Assert.Contains("Dummy loses 3 hit points (7 left).", lines);
        Assert.Contains("Dummy is no longer Hexed.", lines);
        Assert.Equal("Hexers", result.Sides[result.Winner!.Value].Name);
    }

    [Fact]
    public void DurationsCoverTheHoldersNextTurnWhateverTheOrder()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/quick_dummy.json", """
            { "type": "monster", "id": "quick_dummy", "name": "Quick dummy", "tracks": { "hit_points": "10" }, "stats": { "str": "20" },
              "actions": [ { "action": "smite", "damage": "1" } ], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);

        List<string> lines = Fight(rules, "duel", "hexer", "quick_dummy", maxRounds: 2);

        // The dummy acts first in round 1, so the hex lands after its turn and must still cost it round 2.
        int hexed = lines.IndexOf("Quick dummy is Hexed for 1 round.");
        int round2 = lines.IndexOf("Round 2.");
        Assert.True(hexed >= 0 && hexed < round2);
        Assert.Contains("Quick dummy doesn't act (hexed).", lines.Skip(round2));
    }

    [Fact]
    public void ScoredUsesAreTakenWhenTheyScoreHighest()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/patch.json", """
            { "type": "action", "id": "patch", "name": "Patch", "cost": { "turn": 1 }, "target": "hurt_ally",
              "score": "if target.hit_points < 5 then 1 else -1", "always": [ { "op": "heal", "amount": "3" } ] }
            """);
        modules.Write("rules/medic.json", """
            { "type": "monster", "id": "medic", "name": "Medic", "tracks": { "hit_points": "10" }, "stats": { "str": "15" },
              "actions": [ { "action": "patch" }, { "action": "smite", "damage": "1" } ], "xp": 0 }
            """);
        modules.Write("rules/brute.json", """
            { "type": "monster", "id": "brute", "name": "Brute", "tracks": { "hit_points": "30" }, "stats": { "str": "5" },
              "actions": [ { "action": "smite", "damage": "3" } ], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);

        List<string> lines = Fight(rules, "duel", "medic", "brute", maxRounds: 3);

        // Patch is listed first but scores below smite until the medic is badly hurt (4 left after round 2).
        Assert.Equal(
            ["Medic uses Smite on Brute.", "Medic uses Smite on Brute.", "Medic uses Patch on Medic."],
            lines.Where(line => line.StartsWith("Medic uses", StringComparison.Ordinal)));
    }

    [Fact]
    public void ADownedCreaturesConditionsRunAtItsPlaceInTheOrder()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/bleeding.json", """
            { "type": "condition", "id": "bleeding", "name": "Bleeding", "modifiers": [], "each_turn": [ { "op": "damage", "amount": "1", "to": "self" } ] }
            """);
        modules.Write("rules/slash.json", """
            { "type": "action", "id": "slash", "name": "Slash", "cost": { "turn": 1 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "10" }, { "op": "apply_condition", "condition": "bleeding" } ] }
            """);
        modules.Write("rules/slasher.json", """
            { "type": "monster", "id": "slasher", "name": "Slasher", "tracks": { "hit_points": "50" }, "stats": { "str": "15" }, "actions": [ { "action": "slash" } ], "xp": 0 }
            """);
        modules.Write("rules/bystander.json", """
            { "type": "monster", "id": "bystander", "name": "Bystander", "tracks": { "hit_points": "50" }, "stats": { "str": "5" },
              "actions": [ { "action": "smite", "damage": "1" } ], "xp": 0 }
            """);
        modules.Write("rules/bleed_out.json", """
            { "type": "combat", "id": "bleed_out", "name": "Bleed out", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first",
              "initiative_each": "combat", "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points",
              "defeated": "self.hit_points <= 0", "downed_conditions": true }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "bleed_out", out _)!;

        CombatResult result = WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            return CombatRunner.Run(rules, combat,
            [
                new CombatSide("Slashers", [Combatant.FromMonster(rules, rules.Find(DefinitionTypes.Monster, "slasher", out _)!, "Slasher", evaluator)]),
                new CombatSide("Others",
                [
                    Combatant.FromMonster(rules, rules.Find(DefinitionTypes.Monster, "dummy", out _)!, "Dummy", evaluator),
                    Combatant.FromMonster(rules, rules.Find(DefinitionTypes.Monster, "bystander", out _)!, "Bystander", evaluator),
                ]),
            ], dice, 2);
        });

        // The dummy ties the bystander on initiative but is listed first: it falls to the first slash and bleeds at its own place, before the bystander acts.
        List<string> lines = result.Facts.Select(fact => fact.Describe()).ToList();
        int bleeds = lines.IndexOf("Dummy loses 1 hit points (-1 left).");
        Assert.True(bleeds > lines.IndexOf("Dummy is out of the fight."));
        Assert.True(bleeds < lines.IndexOf("Bystander uses Smite on Slasher."));
        Assert.Equal(2, lines.Count(line => line.StartsWith("Dummy loses 1 hit points", StringComparison.Ordinal)));
    }

    [Fact]
    public void SurprisedSidesLoseTheirFirstRound()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/ambush.json", """
            { "type": "combat", "id": "ambush", "name": "Ambush", "surprise": "1", "initiative": "self.str", "initiative_by": "side",
              "initiative_order": "highest-first", "initiative_each": "combat", "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points",
              "defeated": "self.hit_points <= 0" }
            """);
        RuleSet rules = Rules.LoadValid(root);

        List<string> lines = Fight(rules, "ambush", "hexer", "dummy", maxRounds: 2);

        Assert.Equal(["Hexers is surprised for 1 round.", "Dummies is surprised for 1 round."], lines.Take(2));
        int round2 = lines.IndexOf("Round 2.");
        Assert.Contains("Hexer doesn't act (surprised).", lines.Take(round2));
        Assert.Contains("Hexer uses Hex on Dummy.", lines.Skip(round2));
        Assert.Single(lines, line => line.Contains("for initiative", StringComparison.Ordinal) && line.StartsWith("Hexers", StringComparison.Ordinal));
    }

    [Fact]
    public void AFieldDeploysSidesAtOppositeEdgesAndCountsDistance()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/grid.json", """
            { "type": "combat", "id": "grid", "name": "Grid", "initiative": "self.str", "initiative_by": "side", "initiative_order": "highest-first", "initiative_each": "combat",
              "round_seconds": 6, "field": { "width": 6, "height": 3, "metric": "manhattan" }, "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        RuleSet rules = Rules.LoadValid(root);
        CombatField field = CombatField.Of(rules.Find(DefinitionTypes.Combat, "grid", out _)!)!;

        // The middle row first, then outward; a full column moves one in.
        Assert.Equal([new Cell(0, 1), new Cell(0, 0), new Cell(0, 2), new Cell(1, 1)], field.Deploy(0, 4));
        Assert.Equal([new Cell(5, 1), new Cell(5, 0)], field.Deploy(1, 2));
        Assert.Equal(6, field.Distance(new Cell(0, 0), new Cell(5, 1)));
        Assert.Equal(4, field.Neighbours(new Cell(2, 1)).Count());
        Assert.Equal(2, field.Neighbours(new Cell(0, 0)).Count());
    }

    [Fact]
    public void MovementGoesRoundObstaclesAndPaysForRoughGround()
    {
        using TempModules modules = new();
        string root = TerrainRuleset(modules);
        modules.Write("rules/walled.json", """
            { "type": "encounter", "id": "walled", "name": "Walled", "monsters": [ { "monster": "dummy", "count": "1" } ], "terrain": ["..#..", "..#..", "....."] }
            """);
        modules.Write("rules/muddy.json", """
            { "type": "encounter", "id": "muddy", "name": "Muddy", "monsters": [ { "monster": "dummy", "count": "1" } ], "terrain": ["..#..", "..#..", "..~.."] }
            """);
        RuleSet rules = Rules.LoadValid(root);

        // The wall leaves only the bottom row: five cells round it to stand next to the dummy.
        Assert.Contains("Walker moves 5 cells to (4, 2).", FightOn(rules, "walled", "walker"));
        // Mud costs 3 to enter, so 6 movement gets one cell short.
        Assert.Contains("Walker moves 4 cells to (3, 2).", FightOn(rules, "muddy", "walker"));
    }

    [Fact]
    public void RangedActionsNeedLineOfSight()
    {
        using TempModules modules = new();
        string root = TerrainRuleset(modules);
        modules.Write("rules/screened.json", """
            { "type": "encounter", "id": "screened", "name": "Screened", "monsters": [ { "monster": "dummy", "count": "1" } ], "terrain": ["..#..", "..#..", "..#.."] }
            """);
        modules.Write("rules/fenced.json", """
            { "type": "encounter", "id": "fenced", "name": "Fenced", "monsters": [ { "monster": "dummy", "count": "1" } ], "terrain": ["..=..", "..=..", "..=.."] }
            """);
        RuleSet rules = Rules.LoadValid(root);

        // A wall blocks the shot; a fence stops movement but not sight.
        Assert.Contains("Archer doesn't act (no action it can take).", FightOn(rules, "screened", "archer"));
        Assert.Contains("Archer uses Shoot on Dummy.", FightOn(rules, "fenced", "archer"));
        CombatField field = CombatField.Of(rules.Find(DefinitionTypes.Combat, "maze", out _)!, rules.Find(DefinitionTypes.Encounter, "screened", out _)!)!;
        Assert.False(field.CanSee(new Cell(0, 1), new Cell(4, 1)));
        Assert.True(field.CanSee(new Cell(0, 1), new Cell(1, 2)));
        Assert.Equal([new Cell(0, 1), new Cell(0, 0), new Cell(0, 2), new Cell(1, 1)], field.Deploy(0, 4));
    }

    [Fact]
    public void MovesStopInRangeAndSightOrKeepTheirDistance()
    {
        using TempModules modules = new();
        string root = TerrainRuleset(modules);
        modules.Write("rules/open.json", """{ "type": "encounter", "id": "open", "name": "Open", "monsters": [ { "monster": "dummy", "count": "1" } ] }""");
        modules.Write("rules/pillared.json", """
            { "type": "encounter", "id": "pillared", "name": "Pillared", "monsters": [ { "monster": "dummy", "count": "1" } ], "terrain": [".....", "...#.", "....."] }
            """);
        modules.Write("rules/close_in.json", """
            { "type": "action", "id": "close_in", "name": "Close in", "cost": { "turn": 1 }, "target": "enemy", "valid_target": "combat.distance > 2 or not combat.sight",
              "always": [ { "op": "move", "distance": "6", "within": "2" } ] }
            """);
        modules.Write("rules/back_off.json", """
            { "type": "action", "id": "back_off", "name": "Back off", "cost": { "turn": 1 }, "target": "enemy", "valid_target": "combat.distance < 3",
              "always": [ { "op": "move", "toward": "away", "distance": "6", "beyond": "3" } ] }
            """);
        modules.Write("rules/spotter.json", """
            { "type": "monster", "id": "spotter", "name": "Spotter", "tracks": { "hit_points": "50" }, "stats": { "str": "15" }, "actions": [ { "action": "close_in" } ], "xp": 0 }
            """);
        modules.Write("rules/skirmisher.json", """
            { "type": "monster", "id": "skirmisher", "name": "Skirmisher", "tracks": { "hit_points": "50" }, "stats": { "str": "15" },
              "actions": [ { "action": "back_off" }, { "action": "walk" } ], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);

        // Two cells short of the dummy is close enough...
        Assert.Contains("Spotter moves 2 cells to (2, 1).", FightOn(rules, "open", "spotter"));
        // ...unless a pillar blocks the line from there: round it to the nearest cell two away with a clear line.
        Assert.Contains("Spotter moves 4 cells to (3, 2).", FightOn(rules, "pillared", "spotter"));
        // Walks up, then backs off only until three away.
        Assert.Equal(["Skirmisher moves 3 cells to (3, 1).", "Skirmisher moves 2 cells to (1, 1)."],
            FightOn(rules, "open", "skirmisher", rounds: 2).Where(line => line.StartsWith("Skirmisher moves", StringComparison.Ordinal)));
    }

    [Fact]
    public void ADividedEffectMovesOnWhenItsTargetFalls()
    {
        using TempModules modules = new();
        string root = TerrainRuleset(modules);
        modules.Write("rules/pair.json", """{ "type": "encounter", "id": "pair", "name": "Pair", "monsters": [ { "monster": "dummy", "count": "2" } ] }""");
        modules.Write("rules/barrage.json", """
            { "type": "action", "id": "barrage", "name": "Barrage", "cost": { "turn": 1 }, "target": "enemy", "portions": "4", "always": [ { "op": "damage", "amount": "6" } ] }
            """);
        modules.Write("rules/gunner.json", """
            { "type": "monster", "id": "gunner", "name": "Gunner", "tracks": { "hit_points": "50" }, "stats": { "str": "15" }, "actions": [ { "action": "barrage" } ], "xp": 0 }
            """);
        modules.Write("rules/sweep.json", """
            { "type": "action", "id": "sweep", "name": "Sweep", "cost": { "turn": 1 }, "target": "all_enemies", "portions": "2", "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        ModuleSet set = ModuleLoader.Load(root, []);
        Assert.Equal(("action.portions", "$.portions"), set.Diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)).Single());
        modules.Write("rules/sweep.json", """{ "type": "action", "id": "sweep", "name": "Sweep", "cost": { "turn": 1 }, "target": "all_enemies", "always": [ { "op": "damage", "amount": "1" } ] }""");
        RuleSet rules = Rules.LoadValid(root);

        // Each portion goes to the enemy with the least left: two finish the first dummy, two the second.
        Assert.Equal(
            ["Barrage 1 of 4 goes to Dummy 1.", "Barrage 2 of 4 goes to Dummy 1.", "Barrage 3 of 4 goes to Dummy 2.", "Barrage 4 of 4 goes to Dummy 2."],
            FightOn(rules, "pair", "gunner").Where(line => line.StartsWith("Barrage", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFleeingCreatureDrawsPartingBlowsUnlessItWithdrawsAndEscapesAtTheEdge(bool provokes)
    {
        using TempModules modules = new();
        string root = TerrainRuleset(modules);
        modules.Write("rules/open.json", """{ "type": "encounter", "id": "open", "name": "Open", "monsters": [ { "monster": "coward", "count": "1" } ] }""");
        modules.Write("rules/run.json", $$"""
            { "type": "action", "id": "run", "name": "Run", "cost": { "turn": 1 }, "target": "enemy", "valid_target": "combat.distance <= 1",
              "always": [ { "op": "move", "toward": "away", "distance": "6", "escape": true, "provokes": {{(provokes ? "true" : "false")}} } ] }
            """);
        modules.Write("rules/coward.json", """
            { "type": "monster", "id": "coward", "name": "Coward", "tracks": { "hit_points": "50" }, "stats": { "str": "5" }, "actions": [ { "action": "run" } ], "xp": 0 }
            """);
        modules.Write("rules/swipe.json", """
            { "type": "action", "id": "swipe", "name": "Swipe", "cost": { "turn": 1 }, "target": "enemy", "check": "always_hits", "check_bonus": "3",
              "outcomes": { "success": [ { "op": "damage", "amount": "1" } ] } }
            """);
        modules.Write("rules/parting.json", """{ "type": "reaction", "id": "parting", "name": "Parting swipe", "trigger": "leaves_reach", "cost": { "reaction": 1 }, "use": { "action": "swipe" } }""");
        modules.Write("rules/maze.json", """
            { "type": "combat", "id": "maze", "name": "Maze", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "round",
              "round_seconds": 6, "field": { "width": 5, "height": 3, "metric": "manhattan" },
              "budget": [ { "id": "turn", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/guard.json", """
            { "type": "monster", "id": "guard", "name": "Guard", "tracks": { "hit_points": "50" }, "stats": { "str": "15" },
              "actions": [ { "action": "walk" } ], "reactions": ["parting"], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);

        List<string> lines = FightOn(rules, "open", "guard", rounds: 2);

        // The guard closes; the coward steps out of reach to the corner (a swipe at it unless it withdraws) and, cornered at the edge, gets away.
        Assert.Equal(provokes, lines.Contains("Guard reacts to Coward: Parting swipe."));
        Assert.Equal(provokes, lines.Contains("Guard rolls Always hits: 20 + 3 modifiers = 23 against 10: success."));
        Assert.Contains("Coward flees the field.", lines);
    }

    [Fact]
    public void TerrainIsCheckedAgainstTheField()
    {
        using TempModules modules = new();
        string root = TerrainRuleset(modules);
        modules.Write("rules/bad_key.json", """
            { "type": "combat", "id": "bad_key", "name": "Bad key", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "round",
              "round_seconds": 6, "field": { "width": 5, "height": 3, "terrain": { "##": { "name": "Double" }, "~": { "name": "Mud", "cost": 0 } } },
              "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/short.json", """
            { "type": "encounter", "id": "short", "name": "Short", "monsters": [ { "monster": "dummy", "count": "1" } ], "terrain": ["....", "....."] }
            """);
        modules.Write("rules/odd.json", """
            { "type": "encounter", "id": "odd", "name": "Odd", "monsters": [ { "monster": "dummy", "count": "1" } ], "terrain": [".....", "..?..", "....."] }
            """);

        ModuleSet set = ModuleLoader.Load(root, []);

        Assert.Equal(
            [
                ("combat.terrain", "bad_key.json", "$.field.terrain.##"),
                ("combat.terrain", "bad_key.json", "$.field.terrain.~.cost"),
                ("encounter.terrain", "odd.json", "$.terrain[1]"),
                ("encounter.terrain", "odd.json", "$.terrain[1]"),
                ("encounter.terrain", "short.json", "$.terrain"),
                ("encounter.terrain", "short.json", "$.terrain"),
            ],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, Path.GetFileName(diagnostic.File!), diagnostic.JsonPath!)).Order());
    }

    /// <summary>The duel ruleset on a 5 by 3 manhattan field with walls, fences and mud, a walker that closes to jab and an archer that shoots from where it stands.</summary>
    private static string TerrainRuleset(TempModules modules)
    {
        string root = DuelRuleset(modules);
        modules.Write("rules/maze.json", """
            { "type": "combat", "id": "maze", "name": "Maze", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "round",
              "round_seconds": 6, "field": { "width": 5, "height": 3, "metric": "manhattan", "terrain": {
                "#": { "name": "Wall", "passable": false, "blocks_sight": true },
                "=": { "name": "Fence", "passable": false },
                "~": { "name": "Mud", "cost": 3 } } },
              "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/walk.json", """
            { "type": "action", "id": "walk", "name": "Walk", "cost": { "turn": 1 }, "target": "enemy", "valid_target": "combat.nearest > 1", "always": [ { "op": "move", "distance": "6" } ] }
            """);
        modules.Write("rules/jab.json", """
            { "type": "action", "id": "jab", "name": "Jab", "cost": { "turn": 1 }, "target": "enemy", "range": "1", "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/shoot.json", """
            { "type": "action", "id": "shoot", "name": "Shoot", "cost": { "turn": 1 }, "target": "enemy", "range": "10", "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/walker.json", """
            { "type": "monster", "id": "walker", "name": "Walker", "tracks": { "hit_points": "50" }, "stats": { "str": "15" }, "actions": [ { "action": "jab" }, { "action": "walk" } ], "xp": 0 }
            """);
        modules.Write("rules/archer.json", """
            { "type": "monster", "id": "archer", "name": "Archer", "tracks": { "hit_points": "50" }, "stats": { "str": "15" }, "actions": [ { "action": "shoot" } ], "xp": 0 }
            """);
        return root;
    }

    /// <summary>Rounds of the maze combat (one by default): the monster against the encounter, on its terrain.</summary>
    private static List<string> FightOn(RuleSet rules, string encounterId, string monster, int rounds = 1)
    {
        Definition combat = rules.Find(DefinitionTypes.Combat, "maze", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, encounterId, out _)!;
        Definition fighter = rules.Find(DefinitionTypes.Monster, monster, out _)!;
        CombatResult result = WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            return CombatRunner.Run(rules, combat,
            [
                new CombatSide("Them", [Combatant.FromMonster(rules, fighter, fighter.Name, evaluator)]),
                new CombatSide("Dummies", Encounters.Spawn(rules, encounter, dice)),
            ], dice, rounds, encounter);
        });
        return result.Facts.Select(fact => fact.Describe()).ToList();
    }

    [Fact]
    public void AWoundedCreatureStrikesBackOnceAndReactionsDontChain()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/brawl.json", """
            { "type": "combat", "id": "brawl", "name": "Brawl", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "round",
              "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/spite.json", """
            { "type": "reaction", "id": "spite", "name": "Spite", "trigger": "damaged", "cost": { "reaction": 1 }, "when": "target.hit_points > 0", "use": { "action": "smite", "damage": "2" } }
            """);
        modules.Write("rules/brawler.json", """
            { "type": "monster", "id": "brawler", "name": "Brawler", "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ { "action": "smite", "damage": "1" } ], "reactions": ["spite"], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);

        List<string> lines = Fight(rules, "brawl", "brawler", "brawler", maxRounds: 1);

        // Each smite sets off the other brawler's spite once; the spite's own smite sets off nothing more.
        Assert.Equal(
            [
                "Brawler uses Smite on Brawler (2).",
                "Brawler (2) loses 1 hit points (19 left).",
                "Brawler (2) reacts to Brawler: Spite.",
                "Brawler (2) uses Smite on Brawler.",
                "Brawler loses 2 hit points (18 left).",
                "Brawler (2) uses Smite on Brawler.",
                "Brawler loses 1 hit points (17 left).",
                "Brawler reacts to Brawler (2): Spite.",
                "Brawler uses Smite on Brawler (2).",
                "Brawler (2) loses 2 hit points (17 left).",
            ],
            lines.SkipWhile(line => !line.Contains("uses", StringComparison.Ordinal)).Take(10));
    }

    [Fact]
    public void ACounterReactionAnswersAReactionButNothingAnswersIt()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/brawl.json", """
            { "type": "combat", "id": "brawl", "name": "Brawl", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "round",
              "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 }, { "id": "reaction", "per_turn": 2 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/spite.json", """
            { "type": "reaction", "id": "spite", "name": "Spite", "trigger": "damaged", "cost": { "reaction": 1 }, "counter": true, "use": { "action": "smite", "damage": "2" } }
            """);
        modules.Write("rules/brawler.json", """
            { "type": "monster", "id": "brawler", "name": "Brawler", "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ { "action": "smite", "damage": "1" } ], "reactions": ["spite"], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);

        List<string> lines = Fight(rules, "brawl", "brawler", "brawler", maxRounds: 1);

        // The first smite sets off a spite, which (a counter) sets off one spite back; that one sets off nothing, though budget is left.
        Assert.Equal(
            [
                "Brawler uses Smite on Brawler (2).",
                "Brawler (2) loses 1 hit points (19 left).",
                "Brawler (2) reacts to Brawler: Spite.",
                "Brawler (2) uses Smite on Brawler.",
                "Brawler loses 2 hit points (18 left).",
                "Brawler reacts to Brawler (2): Spite.",
                "Brawler uses Smite on Brawler (2).",
                "Brawler (2) loses 2 hit points (17 left).",
                "Brawler (2) uses Smite on Brawler.",
            ],
            lines.SkipWhile(line => !line.Contains("uses", StringComparison.Ordinal)).Take(9));
    }

    [Fact]
    public void ASideIsSurprisedByWhatItsLeadNotices()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/ambush.json", """
            { "type": "combat", "id": "ambush", "name": "Ambush", "surprise": "if self.str > target.str then 0 else 1", "surprise_lead": "self.str",
              "initiative": "self.str", "initiative_by": "side", "initiative_order": "highest-first", "initiative_each": "combat", "round_seconds": 6,
              "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition dummy = rules.Find(DefinitionTypes.Monster, "dummy", out _)!;
        Definition hexer = rules.Find(DefinitionTypes.Monster, "hexer", out _)!;

        // The watch's first member is a dummy, but its lead is the stronger hexer, who out-notices the lone dummy.
        CombatResult result = WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Definition combat = rules.Find(DefinitionTypes.Combat, "ambush", out _)!;
            return CombatRunner.Run(rules, combat,
            [
                new CombatSide("Watch", [Combatant.FromMonster(rules, dummy, "Sleepy", evaluator), Combatant.FromMonster(rules, hexer, "Sharp", evaluator)]),
                new CombatSide("Lone", [Combatant.FromMonster(rules, dummy, "Dummy", evaluator)]),
            ], dice, 1);
        });

        Assert.Equal("Lone is surprised for 1 round.", result.Facts[0].Describe());
        Assert.DoesNotContain(result.Facts, fact => fact.Describe() == "Watch is surprised for 1 round.");
    }

    [Fact]
    public void DefeatIsCheckedAfterEveryOperation()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/rout.json", """
            { "type": "combat", "id": "rout", "name": "Rout", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first",
              "initiative_each": "round", "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0 or self.hit < -5" }
            """);
        modules.Write("rules/crushed.json", """{ "type": "condition", "id": "crushed", "name": "Crushed", "modifiers": [ { "stat": "hit", "value": "-10" } ] }""");
        modules.Write("rules/crush.json", """
            { "type": "action", "id": "crush", "name": "Crush", "cost": { "turn": 1 }, "target": "enemy", "always": [ { "op": "apply_condition", "condition": "crushed" } ] }
            """);
        modules.Write("rules/crusher.json", """
            { "type": "monster", "id": "crusher", "name": "Crusher", "tracks": { "hit_points": "5" }, "stats": { "str": "15" }, "actions": [ { "action": "crush" } ], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);

        List<string> lines = Fight(rules, "rout", "crusher", "dummy", maxRounds: 3);

        Assert.Equal(["Dummy is Crushed.", "Dummy is out of the fight."], lines.SkipWhile(line => line != "Dummy is Crushed.").Take(2));
    }

    [Fact]
    public void FailuresDuringAFightNameTheirDefinition()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/smite.json", """
            { "type": "action", "id": "smite", "name": "Smite", "cost": { "turn": 1 }, "target": "enemy", "parameters": ["damage"],
              "always": [ { "op": "damage", "amount": "use.damage / (self.str - self.str)" } ] }
            """);
        RuleSet rules = Rules.LoadValid(root);

        RuleFailure failure = Assert.Throws<RuleFailure>(() => Fight(rules, "duel", "dummy", "dummy", maxRounds: 1));

        Assert.Equal(("combat.evaluate", "$.always[0].amount"), (failure.Diagnostic.Rule, failure.Diagnostic.JsonPath));
        Assert.EndsWith("smite.json", failure.Diagnostic.File, StringComparison.Ordinal);
        Assert.Contains("Division by zero", failure.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedNamesStayDistinctAcrossRuns()
    {
        using TempModules scratch = new();
        WriteCharacter(scratch, Fixture("percentile"), "sten.json", new CreationRequest("Sten", "soldier", "human", Attributes: Scores(("body", 15), ("agility", 13), ("mind", 10))), "sword");

        string transcript = CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", Fixture("percentile"), "--party", "sten.json,sten.json", "--encounter", "wolves", "--seed", "1", "--runs", "3"]);

        Assert.Contains("Sten (2) still fighting", transcript, StringComparison.Ordinal);
        Assert.Contains("[exit 0]", transcript, StringComparison.Ordinal);
    }

    [Fact]
    public void TracksAreSpentFlooredAndCapped()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/focus.json", """{ "type": "track", "id": "focus", "name": "Focus", "max": "2", "min": "0" }""");
        modules.Write("rules/ward.json", """{ "type": "track", "id": "ward", "name": "Ward", "max": "5", "restore_cap": "8", "start": "0" }""");
        modules.Write("rules/zap.json", """
            { "type": "action", "id": "zap", "name": "Zap", "cost": { "turn": 1 }, "target": "enemy", "available": "self.focus >= 1",
              "always": [ { "op": "damage", "amount": "2" }, { "op": "damage", "track": "focus", "amount": "5", "to": "self" } ] }
            """);
        modules.Write("rules/shield_up.json", """
            { "type": "action", "id": "shield_up", "name": "Shield up", "cost": { "turn": 1 }, "target": "self", "available": "self.ward < 8",
              "always": [ { "op": "heal", "track": "ward", "amount": "10" } ] }
            """);
        modules.Write("rules/zapper.json", """
            { "type": "monster", "id": "zapper", "name": "Zapper", "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ { "action": "shield_up" }, { "action": "zap" }, { "action": "smite", "damage": "1" } ], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);

        List<string> lines = Fight(rules, "duel", "zapper", "dummy", maxRounds: 3);

        Assert.Equal(
            [
                "Zapper uses Shield up on Zapper.",
                "Zapper regains 8 ward (8).",
                "Zapper uses Zap on Dummy.",
                "Dummy loses 2 hit points (8 left).",
                "Zapper loses 2 focus (0 left).",
                "Zapper uses Smite on Dummy.",
            ],
            lines.Where(line => line.StartsWith("Zapper uses", StringComparison.Ordinal) || line.Contains("ward", StringComparison.Ordinal)
                || line.Contains("focus", StringComparison.Ordinal) || line.StartsWith("Dummy loses", StringComparison.Ordinal)).Take(6));
    }

    [Fact]
    public void TracksBelowTheirFloorOrAboveTheirCapDontMove()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/focus.json", """{ "type": "track", "id": "focus", "name": "Focus", "max": "2", "min": "0", "start": "-1" }""");
        modules.Write("rules/ward.json", """{ "type": "track", "id": "ward", "name": "Ward", "max": "5", "restore_cap": "8", "start": "9" }""");
        modules.Write("rules/strain.json", """
            { "type": "action", "id": "strain", "name": "Strain", "cost": { "turn": 1 }, "target": "self",
              "always": [ { "op": "damage", "track": "focus", "amount": "3", "to": "self" }, { "op": "heal", "track": "ward", "amount": "3", "to": "self" } ] }
            """);
        modules.Write("rules/strainer.json", """
            { "type": "monster", "id": "strainer", "name": "Strainer", "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ { "action": "strain" } ], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);

        List<string> lines = Fight(rules, "duel", "strainer", "dummy", maxRounds: 1);

        Assert.Contains("Strainer loses 0 focus (-1 left).", lines);
        Assert.Contains("Strainer regains 0 ward (9).", lines);
    }

    [Fact]
    public void TrackProblemsAreFoundAtLoadOrNamed()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/mana.json", """{ "type": "track", "id": "mana", "name": "Mana" }""");
        modules.Write("rules/max_mana.json", """{ "type": "track", "id": "max_mana", "name": "Shadow", "max": "1" }""");
        modules.Write("rules/level.json", """{ "type": "track", "id": "level", "name": "Level", "max": "1" }""");

        ModuleSet set = ModuleLoader.Load(root, []);

        Assert.Equal(
            [("track.duplicate", "level.json"), ("track.duplicate", "max_mana.json"), ("track.max", "dummy.json"), ("track.max", "hexer.json"), ("track.max", "mana.json")],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, Path.GetFileName(diagnostic.File!))).Order());

        using TempModules loop = new();
        string looped = DuelRuleset(loop);
        loop.Write("rules/rage.json", """{ "type": "track", "id": "rage", "name": "Rage", "max": "self.max_rage + 1", "start": "self.hit_points" }""");
        RuleSet rules = Rules.LoadValid(looped);
        Creature self = new("self");
        self.Track("hit_points").Max = 4;

        ExpressionException error = Assert.Throws<ExpressionException>(() => new Evaluator(rules, null).Evaluate(rules.Compile("self.max_rage", "rules", Roots.Self), self, null));

        Assert.Contains("max_rage depends on itself", error.Message, StringComparison.Ordinal);
        Assert.Equal(4, new Evaluator(rules, null).Evaluate(rules.Compile("self.hit_points", "rules", Roots.Self), self, null).Number);
    }

    [Fact]
    public void ActionsAndUsesAreCheckedAtLoad()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/bad_cost.json", """{ "type": "action", "id": "bad_cost", "name": "Bad", "cost": { "mana": 1 }, "target": "self", "always": [ { "op": "heal", "amount": "1" } ] }""");
        modules.Write("rules/free.json", """{ "type": "action", "id": "free", "name": "Free", "cost": { "turn": 0 }, "target": "self", "always": [ { "op": "heal", "amount": "1" } ] }""");
        modules.Write("rules/no_check.json", """{ "type": "action", "id": "no_check", "name": "No check", "cost": { "turn": 1 }, "target": "enemy", "outcomes": { "success": [] } }""");
        modules.Write("rules/bad_tier.json", """{ "type": "action", "id": "bad_tier", "name": "Bad tier", "cost": { "turn": 1 }, "target": "enemy", "check": "always_hits", "outcomes": { "critical": [] } }""");
        modules.Write("rules/bad_op.json", """{ "type": "condition", "id": "bad_op", "name": "Bad op", "modifiers": [], "each_turn": [ { "op": "damage", "amount": "1", "to": "target" }, { "op": "explode" } ] }""");
        modules.Write("rules/user.json", """
            { "type": "monster", "id": "user", "name": "User", "class": "warrior", "level": 1, "tracks": { "hit_points": "5" },
              "actions": [ { "action": "smite" }, { "action": "smite", "damage": "1", "colour": "2" } ], "xp": 0 }
            """);

        ModuleSet set = ModuleLoader.Load(root, []);

        Assert.Equal(
            [
                ("definition.field-value", "bad_op.json", "$.each_turn[0].to"),
                ("definition.operation", "bad_op.json", "$.each_turn[1].op"),
            ],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, Path.GetFileName(diagnostic.File!), diagnostic.JsonPath!)));
        modules.Write("rules/bad_op.json", """{ "type": "condition", "id": "bad_op", "name": "Bad op", "modifiers": [] }""");

        ModuleSet again = ModuleLoader.Load(root, []);

        Assert.Equal(
            [
                ("action.cost", "bad_cost.json", "$.cost.mana"),
                ("action.cost", "free.json", "$.cost"),
                ("action.outcomes", "bad_tier.json", "$.outcomes.critical"),
                ("action.outcomes", "no_check.json", "$"),
                ("action.outcomes", "no_check.json", "$.outcomes"),
                ("use.parameter", "user.json", "$.actions[0]"),
                ("use.parameter", "user.json", "$.actions[1].colour"),
            ],
            again.Diagnostics.Select(diagnostic => (diagnostic.Rule, Path.GetFileName(diagnostic.File!), diagnostic.JsonPath!)).Order());
    }

    /// <summary>
    /// A small ruleset without dice in its formulas: the hexer always hits and
    /// hexes (no actions, 3 damage a turn, 1 round); the dummy just takes it.
    /// </summary>
    private static string DuelRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/combat.json", """
            { "type": "combat", "id": "duel", "name": "Duel", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "round",
              "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/always_hits.json", """{ "type": "check", "id": "always_hits", "name": "Always hits", "roll": "20", "target": "10", "succeeds": "at-least" }""");
        modules.Write("rules/hexed.json", """
            { "type": "condition", "id": "hexed", "name": "Hexed", "modifiers": [], "prevents_actions": true,
              "each_turn": [ { "op": "damage", "amount": "3", "to": "self" } ] }
            """);
        modules.Write("rules/hex.json", """
            { "type": "action", "id": "hex", "name": "Hex", "cost": { "turn": 1 }, "target": "enemy", "available": "true", "check": "always_hits",
              "outcomes": { "success": [ { "op": "apply_condition", "condition": "hexed", "rounds": "1" } ] } }
            """);
        modules.Write("rules/smite.json", """
            { "type": "action", "id": "smite", "name": "Smite", "cost": { "turn": 1 }, "target": "enemy", "parameters": ["damage"],
              "always": [ { "op": "damage", "amount": "use.damage" } ] }
            """);
        modules.Write("rules/hexer.json", """
            { "type": "monster", "id": "hexer", "name": "Hexer", "class": "warrior", "level": 1, "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ { "action": "hex" }, { "action": "smite", "damage": "1" } ], "xp": 0 }
            """);
        modules.Write("rules/dummy.json", """
            { "type": "monster", "id": "dummy", "name": "Dummy", "class": "warrior", "level": 1, "tracks": { "hit_points": "10" }, "stats": { "str": "5" },
              "actions": [ { "action": "smite", "damage": "1" } ], "xp": 0 }
            """);
        return root;
    }

    private static List<string> Fight(RuleSet rules, string combatId, string first, string second, int maxRounds)
    {
        Definition combat = rules.Find(DefinitionTypes.Combat, combatId, out _)!;
        Definition a = rules.Find(DefinitionTypes.Monster, first, out _)!;
        Definition b = rules.Find(DefinitionTypes.Monster, second, out _)!;
        CombatResult result = WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            return CombatRunner.Run(rules, combat, Encounters.Distinct(
            [
                new CombatSide(a.Name + "s", [Combatant.FromMonster(rules, a, a.Name, evaluator)]),
                new CombatSide(b.Name.Replace("y", "ie", StringComparison.Ordinal) + "s", [Combatant.FromMonster(rules, b, b.Name, evaluator)]),
            ]), dice, maxRounds);
        });
        return result.Facts.Select(fact => fact.Describe()).ToList();
    }

    private static void WriteCharacter(TempModules scratch, string module, string file, CreationRequest request, params string[] equipment)
    {
        ModuleSet set = ModuleLoader.Load(module, []);
        List<ModuleDiagnostic> problems = [];
        Character character = WithDice(dice => CharacterRules.Create(set.Rules!, Character.StampsOf(set), request, dice, problems))!;
        Assert.Empty(problems);
        character.Equipment.AddRange(equipment.Select(id => set.Rules!.Find(DefinitionTypes.Item, id, out _)!));
        File.WriteAllText(Path.Combine(scratch.Root, file), CharacterFile.ToJson(character));
    }

    /// <summary>Gives a saved character these items of the module, replacing what it carries.</summary>
    private static void Equip(TempModules scratch, string file, string module, params string[] items)
    {
        string path = Path.Combine(scratch.Root, file);
        System.Text.Json.Nodes.JsonNode node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        node["equipment"] = new System.Text.Json.Nodes.JsonArray(items.Select(item => (System.Text.Json.Nodes.JsonNode)$"{module}:{item}"!).ToArray());
        File.WriteAllText(path, node.ToJsonString());
    }

    private static Dictionary<string, decimal> Scores(params (string Id, decimal Score)[] scores) => scores.ToDictionary(score => score.Id, score => score.Score);

    private static T WithDice<T>(Func<DiceRoller, T> work)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(1, "tests"));
            return work(new DiceRoller(engine.Random, stream));
        });
    }
}
