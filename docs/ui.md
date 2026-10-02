# DOM companion

`src/ui/main.js` exports `mountProductUi`. Before presentation exists it is
a debug readout: it renders the `rusty.goldbox.session` projection (title,
party and play screens: campaigns, the party, the map, the waiting menu, the
log and notes) and claims the product's `goldbox.command` intent with
`goldbox.command.v1` payloads for its buttons and fields. Keys reach the
product through the Engine's key mappings, not through the DOM. The Engine draws
the first-person view in a window at the top left
(`FirstPersonView.Window` in the Game); the panel sits to its right and must
not cover it. The product project selects this directory and module for SDK
staging.

Keep only browser assets in `src/ui/`. The host admits every staged file by its
content type; documentation belongs under `docs/`.

Keep this lane to DOM presentation, accessibility and semantic actions. Game
state lives in C#; input delivery, projection transport, the canvas and
rendering belong to Engine. Intents are declared in the product project; keep
the C# (`RustyGoldboxProduct`) and DOM callers aligned when an action or field
changes. Dispose event listeners and subscriptions when the host unmounts the
UI.
