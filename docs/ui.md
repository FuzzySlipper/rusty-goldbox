# DOM companion

`src/ui/main.js` exports `mountProductUi`. Before presentation exists it is
a debug readout: it renders the `rusty.goldbox.session` projection (title,
party, play and combat screens: campaigns, the party, the map, the waiting
menu, the fight's combatants and log, and notes) and claims the product's `goldbox.command` intent with
`goldbox.command.v1` payloads for its buttons and fields. Keys reach the
product through the Engine's key mappings, not through the DOM. The Engine draws
the first-person view in a window at the top left
(`SceneView.Window` in the Game); the panel sits to its right and must
not cover it. The product project selects this directory and module for SDK
staging.

Each campaign on the title screen lists the extensions the player may add
(`extensions`: installed extension modules built on its ruleset that it
doesn't require), each with a checkbox; Open sends the checked IDs with the
`open` action. The party screen names the extensions loaded, and a save
records them, so loading it adds them again.

The party screen asks for what a roll needs from the projection's `creation`
(the default creation's method, attributes, grants and boosts), each class's
first-level `grants`, and every feature's kind and boosts: a select for each
feature a grant takes and, under creation by boosts, for each boost that
offers a choice, in the order Core takes them (race, creation features, class,
creation). Core checks the roll; its notes say what to fix.

When `creation.lifepath` is present, the party projection also carries
`lifepath` with its careers and available skill tables. The party controls
send `lifepath`, `careers`, `terms`, `skillTables` and `benefits` in the same
`roll` command; one selected skill or benefit table can repeat for all rolls
that policy produces, while explicit sequences remain available. Core performs the actual throws and returns each member's
`age`, `lifepath` and `careerTerms`, including roll objects, selected tables,
benefit choices, results and `benefitsLost` for a failed term. The DOM displays
that ledger after the character is rolled and owns none of the career state.

Portraits and icons are media objects the projection carries
(`portraitPicture`, the portrait chooser's `picture`, `iconPicture`: an Engine
UI image `url`, the image's `width` and `height`, and a sheet's `frame` size),
all drawn by one `picture` function: an image as an `<img>`, a sheet's frame
cropped from it, pixel-sharp, playing the `animation` the projection names
(its frames, fps and loop) on one page-wide ticker that keeps each picture's
clock across re-renders. They show on party members and the chosen
portrait on the party screen, a roster strip (portrait, name, tracks,
experience) in play, and icons in the combat lists. A missing picture draws
nothing. The portrait chooser offers assets tagged `portrait`.

Each member in the party and play projections carries the spells it knows and
the ones it could (`spells`, `castable`); the panel shows a checkbox per
castable spell on both screens and sends the whole checked list as a `spells`
action, which Core checks. A member whose class prepares spells also shows
what it memorises and has left today (`memorisable`, `memorised`,
`memorisedChosen`, `prepared`), with + and − per spell that send the whole
list as a `memorise` action: prepared at once while making the party, at the
next rest that prepares spells in play. In play each member shows its experience; one with
a level waiting (`levelReady`) gets a Level up button that sends the play
command `level <n>`, and a level that needs choices is taken by typing them in
the command box (`level 2 --feature weapon_focus`), the refusal listing what
is open. A dual-classed member whose former class waits (`formerClasses`) gets
a button to call on it or set it aside (`former <n> on|off`).

Keep only browser assets in `src/ui/`. The host admits every staged file by its
content type; documentation belongs under `docs/`.

Keep this lane to DOM presentation, accessibility and semantic actions. Game
state lives in C#; input delivery, projection transport, the canvas and
rendering belong to Engine. Intents are declared in the product project; keep
the C# (`RustyGoldboxProduct`) and DOM callers aligned when an action or field
changes. Dispose event listeners and subscriptions when the host unmounts the
UI.

Music and sound volume sliders sit under every screen. They are made once,
so a drag isn't interrupted by re-renders, take their values from the
projection's `volumes` when not being dragged, and send a `volume` action
(`bus` music or sound, `volume` from 0 to 1) on change.

The look is a stylesheet of theme variables (`--gb-background`, `--gb-text`,
`--gb-muted`, `--gb-accent`, `--gb-border`, `--gb-inset`, `--gb-button`,
`--gb-button-text`) on the `[data-goldbox-panel]` element. The projection's
`skin` (the player's pick, else the open campaign's) overrides them and adds
its panel tile under the background colour, its frame and button faces as
nine-slice `border-image`s and its title art in place of the heading; the
stylesheet is rewritten only when the skin changes. A Skin select beside the
volume sliders lists the projection's `skins` and sends a `skin` action (an
ID, or null for the campaign's own).
