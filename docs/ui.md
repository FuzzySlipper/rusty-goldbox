# DOM companion

`src/ui/main.js` exports `mountProductUi`. It renders the
`rusty.goldbox.session` projection and claims the product's `goldbox.command`
intent with `goldbox.command.v1` payloads for its buttons and fields. Keys
reach the product through the Engine's key mappings, not through the DOM. The
product project selects this directory and module for SDK staging; the entry
imports the other modules here by relative path.

## Panel layout

Play and combat share one frame of panels, a CSS grid over the whole window
(`.gb-frame`). The Engine draws the world in the **view** panel, which the
frame leaves clear; its shadow paints the gaps between the other panels.
Whenever the view panel moves or resizes, the UI sends its inner rectangle as
fractions of the window from its top left (`{ action: "layout", view: { x, y,
width, height } }`), and the camera draws there. This is presentation only and
stands in until the Engine can anchor a camera to an element (rusty-engine
#9317). Clicks
on the view reach the Engine canvas; every other panel is marked
`data-rusty-ui-interactive` so it takes its own pointer input.

Each panel has a sizing class that says how it takes space as the window
changes shape:

| Class | Panel | Behaviour |
| --- | --- | --- |
| hero | view (`.gb-view`) | Shares the main column with the log by the log share |
| flow | log (`panels/log.js`) | Under the view: the play or fight log as a page of text, the last action's notes, the waiting menu as numbered choices, and the `Party:` command line |
| count-driven | portraits (`panels/portraits.js`) | Cards sized by party size and the portrait scale; in play a card opens that member's sheet, in combat it shows the party's side and who acts |
| fill | map (`panels/map.js`) | The area map in play; the other sides of the fight in combat |
| strip | status (`panels/status.js`), controls (`panels/controls.js`) | Fixed thickness: where the party is and the ≡ Menu; the movement pad and the commands that fit what the party is doing |
| overlay | `panels/overlays.js` | Covers the view: the game menu (save, quit, sound and look) or a member's sheet when the player opens one, else the shop, temple or trainer the party is at |

`layout.js` picks one of three arrangements from the window's aspect ratio:
**standard** (the view over the log, then a side column of portraits, map and
controls), **ultrawide** (portraits get their own column) and **tall** (one
column, the map riding on the view's corner). It sets the layout's ratios
(log share, side width, text, control and portrait scales) as CSS variables on
the panel root, so one stylesheet serves every shape. The ratios are built-in
defaults (`DEFAULT_LAYOUT`).

The title and party screens are one framed sheet over the whole window, with
the menu's sound and look settings at the foot.

Which overlay the player opened is the UI's only state of its own, and it is
presentation: game state stays in the projection.

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
all drawn by one `picture` function in `dom.js`: an image as an `<img>`, a
sheet's frame cropped from it, pixel-sharp, playing the `animation` the
projection names (its frames, fps and loop) on one page-wide ticker that keeps
each picture's clock across re-renders. A picture sized `'fill'` takes its
container's width, so portrait cards scale with the panel. A missing picture
draws nothing; a card without a portrait shows the member's initial. Fight
members on the party's side carry the member's `portraitPicture`. The portrait
chooser offers assets tagged `portrait`.

A member's `tracks` are values (`id`, `name`, `current`, `max`, and `vital`
for a track some combat is fought on); a portrait card's bar shows the vital
track, else the first with a maximum.

Each member in the party and play projections carries the spells it knows and
the ones it could (`spells`, `castable`); the panel shows a checkbox per
castable spell on the party screen and in a member's sheet in play, and sends the whole
checked list as a `spells` action, which Core checks. A member whose class prepares spells also shows
what it memorises and has left today (`memorisable`, `memorised`,
`memorisedChosen`, `prepared`), with + and − per spell that send the whole
list as a `memorise` action: prepared at once while making the party, at the
next rest that prepares spells in play. In play a member's sheet shows its experience; a
card marks a level waiting (`levelReady`) and the sheet has a Level up button that sends the play
command `level <n>`, and a level that needs choices is taken by typing them in
the command box (`level 2 --feature weapon_focus`), the refusal listing what
is open. A dual-classed member whose former class waits (`formerClasses`) gets
a sheet button to call on it or set it aside (`former <n> on|off`).

Keep only browser assets in `src/ui/`. The host admits every staged file by its
content type; documentation belongs under `docs/`.

Keep this lane to DOM presentation, accessibility and semantic actions. Game
state lives in C#; input delivery, projection transport, the canvas and
rendering belong to Engine. Intents are declared in the product project; keep
the C# (`RustyGoldboxProduct`) and DOM callers aligned when an action or field
changes. Dispose event listeners and subscriptions when the host unmounts the
UI.

Music and sound volume sliders and the skin select sit in the ≡ Menu in play
and at the foot of the title and party screens. They are made once, so a drag
isn't interrupted by re-renders, take their values from the projection's
`volumes` when not being dragged, and send a `volume` action (`bus` music or
sound, `volume` from 0 to 1) on change.

The look is one stylesheet (`look.js`) of theme variables (`--gb-background`,
`--gb-text`, `--gb-muted`, `--gb-accent`, `--gb-border`, `--gb-inset`,
`--gb-button`, `--gb-button-text`, and for the log page `--gb-page`,
`--gb-page-text`, `--gb-page-accent`, `--gb-page-link`) on the
`[data-goldbox-panel]` root. The projection's `skin` (the player's pick, else
the open campaign's) overrides them and adds its panel tile under every panel
but the view, its frame and button faces as nine-slice `border-image`s and its
title art in place of the heading; the stylesheet is rewritten only when the
skin changes. The Skin select lists the projection's `skins` and sends a
`skin` action (an ID, or null for the campaign's own).
