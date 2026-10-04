# Premise and creative brief

## The promise

Blackapple is a quiet pig-farming village at the edge of a fairy-haunted
forest. Its people have lived for generations under an uneasy accommodation
with the wood and the old powers beneath the nearby Faehill. A fortnight ago,
seven children followed a rhyme to a magic mirror. An ancient Brugh Elf took
them below and sent seven goatfolk doubles back in their place. The doubles
look like the children but behave with escalating cruelty, while the real
children have been made servants in an underground court.

The party arrives when the village is trying to explain the impossible without
accusing its own families. They can build trust through ordinary help, trace
the children's disappearance through conflicting testimony, choose a mirror
route, enter the Brugh, and decide what rescue means when the court's glamour
shows each adventurer a different world. The return is a second investigation:
the party must account for each child and each double, face the village's fear,
and decide whether survival, truth, reconciliation, or a costly bargain is the
best ending available.

The adventure is about attention and consequences. A useful clue may come from
a tavern song, a child's overheard memory, a harmless-looking shopkeeper, a
forest creature, or a room that looks beautiful only to some of the party. A
fight can solve a local danger and still damage trust. Retreat can preserve
people and routes. The players should feel that they solved a web of
relationships rather than cleared a sequence of rooms.

## Source facts retained

These are facts of the supplied Release 21 adventure, paraphrased for design
use and mapped in [source-mapping.md](source-mapping.md):

* Blackapple has population 408, lies beside an ancient wood, and is built
  around pig farming and a taboo against cutting living trees.
* The old story links the village's prosperity to Ludann, an enchanted
  squirrel, a black apple, and a pact to leave the forest enough dead wood.
  The truth of the story is uncertain in the source.
* The village's children used a mirror and a rhyme to call the Elf Lord. Seven
  real children are captive in the Brugh; seven ibix wear pumpkin-shell masks
  and imitate them.
* The source's central dungeon is a three-level underground Brugh reached only
  through the Figwort Manor or Hen's Teeth mirrors after the Elf Lord activates
  the coatroom route. The C27 mirrors are the route out.
* The Brugh's glamour makes the underground court look like a marble palace to
  some characters and a filthy complex to others. The source resolves this
  separately for each adventurer on entry.
* The missing children work in different Brugh rooms, and their gold ankle
  threads maintain the charm. Removing a thread breaks that charm; the children
  want to go home but remember the journey only hazily.
* The Elf Lord is capricious and dangerous, Moth-in-Water is his loud jester and
  lieutenant, and the court includes fairies, ibix, animals, mushroom people,
  and other odd guests. The source warns that several powerful beings are
  opportunities for role-play or evasion rather than balanced boss fights.
* Return takes one day in the mortal world in the source, and the source
  suggests that a party may initially treat the experience as a dream. The
  adaptation retains the compressed time as a story effect and makes the
  player's rescued-child accounting explicit.

## Adaptation decisions

* **Rules:** the campaign depends on `fifth-srd` (SRD 5.2.1). Basic Fantasy
  class names, statistics, XP rows, and monster numbers are not copied. A
  conversion uses the ruleset's level 1–3 characters, ordinary checks,
  conditions, rests, shops, temples, training, and combat definitions. The
  source's unusual fiction remains even when a numerical implementation is
  different.
* **Party:** the authored opening is 4–6 active player characters. The party
  may recruit Wylda before entering the Brugh, but no child is a combatant.
  Master Ned can accompany a rescue if the party frees him. The product may
  support 12 members elsewhere; that platform fact is not a promise that this
  adventure is balanced for 12.
* **Progression:** the party begins at level 1 and is expected to reach level 2
  after the investigation and optional work, then level 3 by the conclusion or
  immediately after a full rescue. Advancement is a story reward for evidence,
  difficult rescues, and negotiated solutions, not a requirement to farm random
  encounters. A party that retreats receives recovery and can continue.
* **Length:** the core path has six to ten sessions or roughly 8–12 hours. The
  village can be revisited after every expedition. Optional scenes are
  meaningful alternate clues or resources, not filler map squares.
* **Per-member perception:** on entering the Brugh, each active member receives
  one Engine-backed `fifthsave_int` check from the data-only `blackapple-fae`
  extension. Its intended definition shape is a fifth-srd-style check:
  `roll: "1d20"`, `bonus: "self.int_save"`, `target: "10"`,
  `succeeds: "at-least"`; `int_save` is the extension's Intelligence-save
  derived value (`self.int_mod` plus the fifth-srd proficiency when the
  character's class grants Intelligence saves). Success stores `truth`; failure
  stores `glamour` in `state.glamour_truth` for that member until the party
  leaves. The check uses Engine Random through the normal evaluator and is made
  once per entry, not by hashing or alternating members. Every keyed room that
  benefits from the contrast supplies both descriptions. The mode changes what
  the character can notice and how the scene is presented; it does not create
  separate maps, duplicate enemies, or contradictory exits. Member-keyed check
  storage and view projection remain the concrete perception gap tracked by
  child task #9362; child and double campaign facts use the scalar variables in
  [README.md](README.md).
* **Mirrors:** the Figwort Manor and Hen's Teeth mirrors are one-way entry
  routes once the Elf Lord opens C1. In this digital adaptation, the prepared
  external mirror becomes an open doorway at midnight for a ten-minute ingress
  window; C1 is entry only. C27's return mirrors lead to the two mundane
  mirrors and are the only exit. The player chooses an entry route when both
  are available; route choice changes who can help and which return scene is
  easiest, but does not lock the party out of the core rescue.
* **C13 dinner and the pit:** the source has four reaction outcomes: an
  immediate attack throws the whole party into C18; an unfavorable reaction
  throws one disrespectful member (and may pull the others in); a favorable
  reaction sends Moth-in-Water with the party to C8 or C9; and a very favorable
  reaction adds an ibix thrown into the pit for entertainment. The digital
  adaptation keeps these branches. The whole party remains in the one Core
  area position: the unfavorable single-member result is a focused rescue
  vignette in which the party can intervene, and a failed intervention throws
  the whole party into C18. It never creates a fake split map or an unpersisted
  per-member location. Any supported member condition or damage is applied by
  the ordinary ruleset operation; otherwise the vignette is narrative only.
* **Children and doubles:** child identity is tracked separately from the
  corresponding ibix status. Exposing a double is evidence; it is not proof that
  the real child has been rescued. Children may be freed quietly, recovered
  after a failed approach, or left captive. A return scene accounts for all
  seven rather than awarding one generic rescue flag.
* **Art:** the visual direction is original illustrated dark fairytale art:
  ink-and-wash or painterly textures, readable silhouettes, warm village light,
  and paired palace/underground compositions that share geometry. No excluded
  source illustration is used as an image-edit input or shipped in the product.

## Audience, tone, and table care

The intended audience is older teens and adults who enjoy exploratory fantasy,
social mysteries, and consequences that do not reduce to a single combat
victory. The tone is eerie and folkloric, with absurd courtly comedy from
Moth-in-Water, fauns, bad games, and impossible food. Comedy relieves tension;
it does not make child captivity or disease a joke.

Content notes belong in the campaign's opening projection: child abduction and
coercive enchantment; identity replacement; disease and visible sores; an
unethical historical asylum treatment; implied torture; death threats; and
creatures whose magic can induce despair. The digital adaptation uses a
fictional, non-graphic description of illness and gives players a way to leave
or skip a distressing scene. It does not require a player to role-play medical
trauma, disclose personal experience, or accept a real-world diagnostic claim.
The source's self-harm implication in the ward-pixie effect is not presented as
a required player action; the adaptation converts it to a dangerous despair
condition with an immediate safety and recovery prompt.

## Play loop

1. **Arrive and belong:** learn the village through a safe inn, a toll, a meal,
   and one ordinary act of help. The party gains its first lead without being
   forced into a fight.
2. **Compare stories:** visit parents, services, the sanitarium, the tavern,
   cemetery, manor, and forest leads. Different people know different pieces;
   the party chooses whom to trust.
3. **Choose a route:** take the Figwort invitation, use Flynn's Hen's Teeth,
   bargain with a fairy, or return after preparing. Wylda's recruitment and
   the A4 cure route are optional resources.
4. **Read two worlds:** cross the mirror, resolve per-member glamour/truth, and
   use mismatched descriptions as clues. Social, stealth, exploration, and
   combat approaches should all advance the rescue.
5. **Rescue and recover:** free children by removing their threads, release
   useful prisoners such as Master Ned, retreat through C27, and treat wounds,
   fear, and compromised trust before the return.
6. **Account and decide:** return to parents and villagers, expose or contain
   remaining doubles, and choose the ending the party's actions have earned.

## Conversion guardrails

The campaign may add original connective scenes, checks, rewards, and recovery
choices. Those are adaptation decisions and must be labelled as such. It must
not add a new generic dungeon in place of the Brugh, flatten the dual
perception, turn the Elf Lord into a mandatory boss, or use the source's
excluded artwork. Any new primitive runtime behavior must be raised to Core;
the campaign files remain data and do not carry code.
