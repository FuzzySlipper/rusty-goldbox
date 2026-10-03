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

Portraits and icons are media objects the projection carries
(`portraitPicture`, the portrait chooser's `picture`, `iconPicture`: an Engine
UI image `url`, the image's `width` and `height`, and a sheet's `frame` size),
all drawn by one `picture` function: an image as an `<img>`, a sheet's first
frame cropped from it, pixel-sharp. They show on party members and the chosen
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
