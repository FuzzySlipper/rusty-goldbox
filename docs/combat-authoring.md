# Authoring tactical combat

Tactical combat has two data layers. The ruleset owns what a creature is
legally allowed to do: actions, costs, ranges, targets, checks, resources and
operations. A behavior profile owns the policy for choosing among those legal
uses: guards, priorities, target and destination preferences, short action
sequences and the fallback when a plan no longer applies. A profile cannot
make an unavailable action legal and it cannot change an action's effect.

Start with the live format reference:

```bash
goldbox schema combat-behavior
goldbox schema action
goldbox schema combat
```

Put a reusable profile in a ruleset or extension module that requires the
ruleset which supplies its action references. A campaign assigns a profile by
reference on its combat, monsters or NPCs, or requires an extension that owns
campaign-specific profiles. A profile is a definition with ordered rules. A
rule may have a boolean `when`, a `priority` or `score`, and a short list of
steps. Each step names an existing action use and may select a target or move
first through its destination; a profile has a fixed sequence and never loops.
Use parameters supply values such as damage, range, or a resource choice. Keep
each sequence short and explicit. Do not encode loops, scripts, or assumptions
about a particular C# class.

For example, a ranged skirmisher can keep its distance while an enemy is near,
then retreat and shoot when that enemy closes:

```json
{
  "type": "combat-behavior",
  "id": "skirmish",
  "name": "Skirmish at range",
  "rules": [
    {
      "when": "combat.distance <= 2",
      "priority": 20,
      "steps": [
        {
          "destination": { "kind": "away", "distance": "4" },
          "action": { "action": "tactical-bestiaire:withdraw" },
          "target": "enemy"
        }
      ],
      "fallback": "next"
    },
    {
      "priority": 10,
      "steps": [
        { "action": { "action": "classic:missile_attack", "damage": "1d6", "increment": "6" }, "target": "enemy" }
      ],
      "fallback": "end-turn"
    }
  ]
}
```

This example uses the data-only `tactical-bestiaire:withdraw` action, whose
existing `move` operation makes the `away` destination executable before the
missile step. A profile that uses only a non-moving action should omit
`destination` or add a separate authored movement step.

The exact fields and legal destination kinds are those printed by `goldbox
schema combat-behavior`; the example illustrates the ownership boundary rather than
adding a new primitive. Reuse one mechanical action in several profiles when
the creatures should differ only in policy. For example, one profile can
prefer the nearest target while another gives injured allies priority, even
though both use the same healing or missile action.

An action that every combatant may use belongs on the combat definition's
`actions` list. Define the action in the owning ruleset or extension, then add
its qualified ID there; the combat owner appends that legal use after each
creature's own uses. Its `cost` must name a budget declared by that combat, so
a shared movement action can use a `standard` or three-action budget without
special casing the ruleset.

Attach the profile to a creature through the behavior field documented by the
schema. Enemies use their authored profile by default. An NPC may also set
`control` to `"automatic"` or `"manual"` to choose its default controller when
it joins the party. An explicit player or campaign controller choice wins for
the current fight and is retained by a live continuation; without `control`,
the campaign's combat default applies. A manual choice must still use the
shared legal-action resolver.

Test a profile in layers:

1. Validate the root module and every direct dependency. Read the diagnostic's
   module, file, JSON path and rule when an expression, reference, parameter,
   or destination is wrong.
2. Inspect the resolved set to confirm that action references point to the
   intended ruleset and that figures point to logical asset IDs.
3. Run a deterministic simulation with a fixed seed. Use a scenario that
   makes the policy choice visible: a ranged creature outside and inside its
   preferred distance, a damaged ally for support, or a phase condition that
   changes the first eligible rule.
4. Run the same module from a packed container. Keep the ruleset, extension,
   campaign and assets independently packable; use `--modules` to make the
   containers available and confirm there is no source-directory fallback.
5. Run the campaign script when the profile is assigned to an NPC or
   companion. Capture both the autonomous turn and the manual override, then
   save and resume if the profile has committed state.

On a grid, `party_start` and `monsters_start` are deployment anchors. The
field expands each side around its anchor, so a campaign that permits twelve
members needs twelve distinct passable cells on the party side after its
encounter terrain is applied. The original `tactical-expedition` example uses
a 10 by 6 field, keeps its two anchors clear, and exercises the twelve-member
deployment together with the optional Ivy companion.

Use an original fixture to check a second ruleset shape, such as a three-action
budget or a shared zones field. The fixture must carry a `PROVENANCE.md` that
states that its rules, names and numbers are original and identifies the
shape it exercises; it must not copy published text or tables. A first-party
module that adapts licensed rules carries that licence's complete text and
notices instead. Do not put setting names or Product Identity into an open
ruleset module.

When a profile cannot express the intended plan, first check whether the
action is legal and whether the profile has a guard, target, destination,
fallback and finite step sequence. A missing expression or reference is an
authoring error. A genuinely new effect, operation, expression function,
combat hook, or state owner is Core work and needs a focused primitive with a
second ruleset-shaped fixture. Do not add a local script or duplicate the
combat loop in a module.

Useful end-to-end commands are:

```bash
dotnet run --project src/RustyGoldbox.Cli -- schema combat-behavior
dotnet run --project src/RustyGoldbox.Cli -- module validate modules/tactical-bestiaire --modules modules
dotnet run --project src/RustyGoldbox.Cli -- module inspect modules/tactical-bestiaire combat-behavior
dotnet run --project src/RustyGoldbox.Cli -- sim combat --module modules/tactical-expedition \
  --modules modules --party hero.json --encounter tactical-expedition:gallery --seed 17 --trace
dotnet run --project src/RustyGoldbox.Cli -- module pack modules/tactical-bestiaire \
  --output /tmp/tactical-bestiaire-0.1.0.rpak --modules modules
```

