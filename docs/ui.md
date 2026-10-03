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

The party screen asks for what a roll needs from the projection's `creation`
(the default creation's method, attributes, grants and boosts), each class's
first-level `grants`, and every feature's kind and boosts: a select for each
feature a grant takes and, under creation by boosts, for each boost that
offers a choice, in the order Core takes them (race, creation features, class,
creation). Core checks the roll; its notes say what to fix.

Each member in the party and play projections carries the spells it knows and
the ones it could (`spells`, `castable`); the panel shows a checkbox per
castable spell on both screens and sends the whole checked list as a `spells`
action, which Core checks.

Keep only browser assets in `src/ui/`. The host admits every staged file by its
content type; documentation belongs under `docs/`.

Keep this lane to DOM presentation, accessibility and semantic actions. Game
state lives in C#; input delivery, projection transport, the canvas and
rendering belong to Engine. Intents are declared in the product project; keep
the C# (`RustyGoldboxProduct`) and DOM callers aligned when an action or field
changes. Dispose event listeners and subscriptions when the host unmounts the
UI.
