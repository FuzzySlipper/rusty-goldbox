# DOM companion

`src/ui/main.js` exports `mountProductUi`. Before presentation exists it is
a debug readout: it observes the
`rusty.goldbox.status` projection and shows its `status` text. The product
project selects this directory and module for SDK staging.

Keep only browser assets in `src/ui/`. The host admits every staged file by its
content type; documentation belongs under `docs/`.

Keep this lane to DOM presentation, accessibility and semantic actions. Game
state lives in C#; input delivery, projection transport, the canvas and
rendering belong to Engine. When the UI starts submitting intents, declare
them in the product project and keep the C# and DOM callers aligned. Dispose
event listeners and subscriptions when the host unmounts the UI.
