/**
 * The Game's look: one stylesheet of theme variables (a skin overrides the
 * colours and adds its tile, frame and button faces) and the panel layout.
 * Layout ratios arrive as CSS variables set by layout.js (--gb-main-rows,
 * --gb-side-width, --gb-text-scale, --gb-control-scale, --gb-portrait-scale);
 * the arrangement and screen arrive as data attributes on the frame.
 */

export const BASE_LOOK = `
[data-tales-panel] {
  --gb-background: #15131c;
  --gb-inset: #0b0a10;
  --gb-text: #ebe3cc;
  --gb-muted: #948c78;
  --gb-accent: #e6b450;
  --gb-border: #7a6440;
  --gb-button: #262131;
  --gb-button-text: #ebe3cc;
  --gb-page: #e9e1cb;
  --gb-page-text: #1b1712;
  --gb-page-accent: #8a4b0f;
  --gb-page-link: #1f3f86;
  --gb-good: #7fc46a;
  --gb-warn: #e6b450;
  --gb-bad: #e0604f;
  --gb-foe: #d58ce0;
  --gb-font: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
  --gb-prose-font: Georgia, "Iowan Old Style", "Liberation Serif", serif;
  --gb-mono-font: ui-monospace, "DejaVu Sans Mono", "Cascadia Mono", Consolas, monospace;
  position: absolute; inset: 0; container-type: size; overflow: hidden;
  color: var(--gb-text); font-family: var(--gb-font);
}
[data-tales-panel] *, [data-tales-panel] *::before, [data-tales-panel] *::after { box-sizing: border-box; }
[data-tales-panel] [hidden] { display: none !important; }
.gb-frame {
  --gb-gap: clamp(3px, .7cqmin, 8px);
  position: absolute; inset: 0; display: grid; gap: var(--gb-gap); padding: var(--gb-gap);
  font-size: calc(clamp(11px, 1.9cqmin, 22px) * var(--gb-text-scale) * var(--rusty-ui-scale, 1)); line-height: 1.2;
}
.gb-frame button, .gb-frame input, .gb-frame select { font: inherit; }

/* Play and combat. Standard: the view over the log; portraits, map and controls at the side. */
.gb-frame[data-arrangement="standard"] {
  grid-template-columns: minmax(0, 1fr) calc(var(--gb-side-width) * 1cqw);
  grid-template-rows: auto var(--gb-main-rows);
  grid-template-areas: "status status" "view side" "log side";
}
/* Ultrawide: portraits take their own column. */
.gb-frame[data-arrangement="ultrawide"] {
  grid-template-columns: calc(var(--gb-side-width) * .55cqw) minmax(0, 1fr) calc(var(--gb-side-width) * .7cqw);
  grid-template-rows: auto var(--gb-main-rows);
  grid-template-areas: "status status status" "portraits view map" "portraits log controls";
}
/* Tall: one column; the map rides on the view, portraits become a strip. */
.gb-frame[data-arrangement="tall"] {
  grid-template-columns: minmax(0, 1fr);
  grid-template-rows: auto var(--gb-main-rows) auto auto;
  grid-template-areas: "status" "view" "log" "controls" "portraits";
}
/* Title and party screens: one framed sheet over the whole stage (after the arrangements, which it overrides). */
.gb-frame[data-screen="title"], .gb-frame[data-screen="party"] { background: var(--gb-inset); grid-template: minmax(0, 1fr) / minmax(0, 1fr); grid-template-areas: none; }
.gb-sheet { overflow: auto; padding: .9em 1.25em 1.2em; width: min(100%, 76em); justify-self: center; gap: .75em; }
.gb-sheet-body { display: flex; flex-direction: column; gap: .85em; }
.gb-notes { flex: none; color: var(--gb-accent); max-height: 7.5em; overflow: auto; margin: 0; padding: .45em .85em .45em 1.75em; border: 1px solid color-mix(in srgb, var(--gb-accent) 65%, transparent); border-left: 3px solid var(--gb-accent); background: color-mix(in srgb, var(--gb-accent) 10%, var(--gb-inset)); border-radius: 3px; overflow-wrap: anywhere; font-size: .92em; line-height: 1.35; }
.gb-notes[hidden] { display: none; }
.gb-sheet h1 { margin: 0; font-size: 1.55em; letter-spacing: .02em; color: var(--gb-accent); line-height: 1.15; }
.gb-sheet h2 { margin: 0; font-size: 1.02em; letter-spacing: .06em; text-transform: uppercase; color: var(--gb-accent); }
.gb-sheet h3 { margin: .4em 0 .25em; font-size: .82em; letter-spacing: .08em; text-transform: uppercase; color: var(--gb-muted); }

/* Shared sheet structure: header, cards, badges, drawers, forms and steppers */
.gb-sheet-header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: .6em 1em; padding-bottom: .65em; border-bottom: 1px solid color-mix(in srgb, var(--gb-border) 55%, transparent); }
.gb-sheet-title { display: flex; flex-direction: column; gap: .25em; min-width: 0; }
.gb-sheet-sub { color: var(--gb-muted); font-size: .9em; }
.gb-header-actions { display: flex; flex-wrap: wrap; gap: .45em; align-items: center; }
.gb-section-head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: .4em .75em; }
.gb-card-box { display: flex; flex-direction: column; gap: .6em; padding: .75em .85em; background: color-mix(in srgb, var(--gb-inset) 60%, var(--gb-background)); border: 1px solid color-mix(in srgb, var(--gb-border) 70%, transparent); border-radius: 4px; }
.gb-card-heading { margin: 0; font-size: .92em; letter-spacing: .08em; text-transform: uppercase; color: var(--gb-accent); }
.gb-subbox { display: flex; flex-direction: column; gap: .45em; padding: .55em .65em; background: color-mix(in srgb, var(--gb-inset) 75%, transparent); border: 1px solid color-mix(in srgb, var(--gb-border) 42%, transparent); border-radius: 3px; }
.gb-hint-bar { padding: .4em .6em; border-radius: 3px; background: color-mix(in srgb, var(--gb-accent) 9%, var(--gb-inset)); border: 1px solid color-mix(in srgb, var(--gb-accent) 30%, transparent); color: var(--gb-text); font-size: .88em; }
.gb-inline-warn { padding: .35em .55em; border-radius: 3px; background: color-mix(in srgb, var(--gb-bad) 14%, var(--gb-inset)); border: 1px solid color-mix(in srgb, var(--gb-bad) 60%, transparent); color: var(--gb-bad); font-size: .88em; }
.gb-divider { height: 1px; background: color-mix(in srgb, var(--gb-border) 40%, transparent); margin: .15em 0; }
.gb-badge-row { display: flex; flex-wrap: wrap; align-items: center; gap: .3em; }
.gb-pill { display: inline-flex; align-items: center; padding: .12em .48em; border-radius: 999px; font-size: .78em; line-height: 1.25; background: color-mix(in srgb, var(--gb-border) 28%, var(--gb-inset)); border: 1px solid color-mix(in srgb, var(--gb-border) 55%, transparent); color: var(--gb-text); white-space: nowrap; }
.gb-pill-accent { background: color-mix(in srgb, var(--gb-accent) 18%, var(--gb-inset)); border-color: color-mix(in srgb, var(--gb-accent) 65%, transparent); color: var(--gb-accent); }
.gb-pill-good { background: color-mix(in srgb, var(--gb-good) 16%, var(--gb-inset)); border-color: color-mix(in srgb, var(--gb-good) 58%, transparent); color: var(--gb-good); }
.gb-pill-bad { background: color-mix(in srgb, var(--gb-bad) 18%, var(--gb-inset)); border-color: color-mix(in srgb, var(--gb-bad) 62%, transparent); color: var(--gb-bad); }
.gb-check-pill { display: inline-flex; align-items: center; gap: .3em; padding: .18em .5em; border-radius: 3px; font-size: .85em; background: color-mix(in srgb, var(--gb-inset) 75%, transparent); border: 1px solid color-mix(in srgb, var(--gb-border) 45%, transparent); cursor: pointer; }
.gb-compact-list { margin: 0; padding-left: 1.2em; display: flex; flex-direction: column; gap: .3em; font-size: .92em; }

/* Primary and utility buttons */
[data-tales-panel] .gb-sheet button.gb-btn-primary { background: color-mix(in srgb, var(--gb-accent) 24%, var(--gb-button)); border-color: var(--gb-accent); color: var(--gb-accent); font-weight: 700; padding: .3em .95em; outline: 1px solid color-mix(in srgb, var(--gb-accent) 75%, transparent); outline-offset: -1px; }
[data-tales-panel] .gb-sheet button.gb-btn-primary:hover:not(:disabled) { background: color-mix(in srgb, var(--gb-accent) 36%, var(--gb-button)); }
[data-tales-panel] .gb-sheet button.gb-btn-subtle { font-size: .84em; padding: .14em .5em; opacity: .82; }
[data-tales-panel] .gb-sheet button.gb-btn-xs { font-size: .78em; padding: .1em .45em; }
[data-tales-panel] .gb-sheet button.gb-icon-btn { padding: .14em .45em; line-height: 1.1; }
[data-tales-panel] .gb-sheet button.gb-tag-btn { font-size: .8em; padding: .1em .45em; border-radius: 999px; border-image: none; background: color-mix(in srgb, var(--gb-border) 28%, var(--gb-inset)); border: 1px solid color-mix(in srgb, var(--gb-border) 55%, transparent); }

/* Collapsible utility drawers (Title co-op, modules, and display/audio settings) */
.gb-drawer { border: 1px solid color-mix(in srgb, var(--gb-border) 60%, transparent); border-radius: 4px; background: color-mix(in srgb, var(--gb-inset) 55%, var(--gb-background)); }
.gb-drawer > summary { cursor: pointer; padding: .5em .75em; font-size: .86em; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; color: var(--gb-accent); list-style: none; display: flex; align-items: center; justify-content: space-between; user-select: none; }
.gb-drawer > summary::after { content: "▸"; color: var(--gb-muted); font-size: 1em; transition: transform .15s ease; }
.gb-drawer[open] > summary::after { content: "▾"; color: var(--gb-accent); }
.gb-drawer[open] > summary { border-bottom: 1px solid color-mix(in srgb, var(--gb-border) 40%, transparent); }
.gb-drawer-body { display: flex; flex-direction: column; gap: .55em; padding: .65em .75em; }
.gb-settings-drawer { margin-top: .15em; }

/* Settings slider grid (works in both sheet drawer and in-game menu overlay) */
.gb-settings { display: flex; flex-direction: column; gap: .5em; }
.gb-slider-grid { display: grid; gap: .45em .9em; grid-template-columns: repeat(auto-fit, minmax(min(100%, 15em), 1fr)); }
.gb-slider-field { display: grid; grid-template-columns: minmax(6.5em, auto) minmax(5em, 1fr) 3.4em; align-items: center; gap: .45em; font-size: .9em; }
.gb-slider-field.gb-slider-select { grid-template-columns: minmax(6.5em, auto) minmax(8em, 1fr); }
.gb-slider-label { color: var(--gb-text); white-space: nowrap; }
.gb-slider-field input[type="range"] { width: 100%; min-width: 0; accent-color: var(--gb-accent); margin: 0; padding: 0; border: 0; background: transparent; }
.gb-slider-value { text-align: right; color: var(--gb-muted); font-family: var(--gb-mono-font); font-size: .88em; font-variant-numeric: tabular-nums; }

/* Form fields */
.gb-field-grid { display: grid; gap: .55em .75em; grid-template-columns: repeat(auto-fit, minmax(min(100%, 13.5em), 1fr)); align-items: start; }
.gb-field { display: flex; flex-direction: column; gap: .25em; min-width: 0; }
.gb-field-label { font-size: .76em; font-weight: 700; letter-spacing: .07em; text-transform: uppercase; color: var(--gb-muted); }
.gb-field input, .gb-field select, .gb-field .gb-select, .gb-field textarea { width: 100%; min-width: 0; }
[data-tales-panel] textarea { background: var(--gb-inset); color: var(--gb-text); border: 1px solid var(--gb-border); padding: .35em .45em; font-family: var(--gb-mono-font); font-size: .82em; resize: vertical; }

/* Custom theme-matched DOM dropdown (replaces offset/unstyled native CEF <select> popups) */
.gb-select { position: relative; display: inline-flex; min-width: 0; max-width: 100%; }
.gb-select.gb-select-open { z-index: 65; }
[data-tales-panel] .gb-select > button.gb-select-trigger {
  width: 100%; min-width: 0; display: flex; align-items: center; justify-content: space-between; gap: .5em;
  background: var(--gb-inset); color: var(--gb-text); border: 1px solid var(--gb-border); border-image: none;
  box-shadow: none; border-radius: 3px; padding: .24em .5em; text-align: left; font: inherit; line-height: 1.3;
  cursor: pointer; image-rendering: auto;
}
[data-tales-panel] .gb-select > button.gb-select-trigger:hover:not(:disabled),
[data-tales-panel] .gb-select.gb-select-open > button.gb-select-trigger {
  border-color: var(--gb-accent); background: color-mix(in srgb, var(--gb-accent) 10%, var(--gb-inset));
}
[data-tales-panel] .gb-select > button.gb-select-trigger:disabled { opacity: .5; cursor: default; }
.gb-select-label { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.gb-select-caret { flex: none; color: var(--gb-muted); font-size: .85em; }
.gb-select.gb-select-open .gb-select-caret { color: var(--gb-accent); }
.gb-select-menu {
  position: absolute; left: 0; top: calc(100% + 3px); min-width: 100%; max-width: min(28em, 90vw);
  max-height: 15.5em; overflow-y: auto; z-index: 70;
  background: color-mix(in srgb, var(--gb-inset) 94%, #000);
  border: 1px solid var(--gb-accent); border-radius: 4px; padding: 3px;
  box-shadow: 0 10px 28px rgba(0, 0, 0, .72);
  display: flex; flex-direction: column; gap: 2px;
}
.gb-select-menu[hidden] { display: none; }
.gb-select.gb-select-up .gb-select-menu { top: auto; bottom: calc(100% + 3px); }
[data-tales-panel] .gb-select-menu > button.gb-select-option {
  width: 100%; display: block; text-align: left; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  background: transparent; color: var(--gb-text); border: 1px solid transparent; border-image: none;
  box-shadow: none; border-radius: 3px; padding: .26em .55em; font: inherit; line-height: 1.3;
  cursor: pointer; image-rendering: auto;
}
[data-tales-panel] .gb-select-menu > button.gb-select-option:hover:not(:disabled),
[data-tales-panel] .gb-select-menu > button.gb-select-option.gb-active:not(:disabled) {
  background: color-mix(in srgb, var(--gb-accent) 22%, var(--gb-inset)); border-color: color-mix(in srgb, var(--gb-accent) 55%, transparent); color: var(--gb-text);
}
[data-tales-panel] .gb-select-menu > button.gb-select-option.gb-selected {
  background: color-mix(in srgb, var(--gb-accent) 28%, var(--gb-inset)); color: var(--gb-accent); font-weight: 700;
}
[data-tales-panel] .gb-select-menu > button.gb-select-option.gb-disabled,
[data-tales-panel] .gb-select-menu > button.gb-select-option:disabled {
  opacity: .48; cursor: not-allowed; color: var(--gb-muted);
}
.gb-portrait-head .gb-select { width: auto; min-width: 9em; max-width: 13em; font-size: .86em; }
.gb-stat-chip.gb-stat-fail { border-color: var(--gb-bad); background: color-mix(in srgb, var(--gb-bad) 14%, var(--gb-inset)); }
.gb-stat-chip.gb-stat-fail strong { color: var(--gb-bad); }
.gb-stat-chip.gb-stat-ok { border-color: color-mix(in srgb, var(--gb-good) 58%, transparent); }
.gb-stat-card-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(5.4em, 1fr)); gap: .38em; }
.gb-stat-card {
  display: flex; flex-direction: column; align-items: center; gap: .12em; padding: .35em .3em;
  border-radius: 4px; background: var(--gb-inset); border: 1px solid color-mix(in srgb, var(--gb-border) 55%, transparent);
  font-family: var(--gb-mono-font); font-variant-numeric: tabular-nums;
}
.gb-stat-card small { font-size: .72em; color: var(--gb-muted); letter-spacing: .06em; }
.gb-stat-card strong { font-size: 1.24em; line-height: 1.1; color: var(--gb-text); }
.gb-stat-card .gb-stat-sub { font-size: .7em; color: var(--gb-muted); }
.gb-stat-card.gb-stat-prime { border-color: color-mix(in srgb, var(--gb-accent) 72%, transparent); }
.gb-stat-card.gb-stat-prime strong { color: var(--gb-accent); }
.gb-stat-card.gb-stat-fail { border-color: var(--gb-bad); background: color-mix(in srgb, var(--gb-bad) 15%, var(--gb-inset)); }
.gb-stat-card.gb-stat-fail strong, .gb-stat-card.gb-stat-fail .gb-stat-sub { color: var(--gb-bad); }
.gb-stat-card.gb-stat-ok { border-color: color-mix(in srgb, var(--gb-good) 55%, transparent); }
.gb-inline-input { display: flex; gap: .3em; align-items: center; }
.gb-inline-input input { flex: 1; min-width: 0; }

/* Title screen dashboard */
.gb-title-grid { display: grid; gap: .9em; grid-template-columns: minmax(0, 1.55fr) minmax(17em, 1fr); align-items: start; }
@media (max-width: 860px) { .gb-title-grid { grid-template-columns: minmax(0, 1fr); } }
.gb-title-main, .gb-title-side { display: flex; flex-direction: column; gap: .65em; min-width: 0; }
.gb-campaign-grid { display: grid; gap: .65em; grid-template-columns: repeat(auto-fit, minmax(min(100%, 19em), 1fr)); }
.gb-campaign-card { justify-content: space-between; min-height: 8.5em; transition: border-color .12s ease; }
.gb-campaign-card:hover { border-color: var(--gb-accent); }
.gb-campaign-head { display: flex; flex-direction: column; gap: .3em; }
.gb-sheet .gb-campaign-title { margin: 0; font-size: 1.08em; letter-spacing: .01em; text-transform: none; color: var(--gb-text); }
.gb-campaign-desc { margin: 0; font-size: .86em; line-height: 1.38; color: var(--gb-muted); }
.gb-portrait-head select { width: auto; max-width: 12em; font-size: .86em; }
.gb-campaign-ext { display: flex; flex-direction: column; gap: .25em; padding-top: .25em; border-top: 1px dashed color-mix(in srgb, var(--gb-border) 45%, transparent); }
.gb-campaign-foot { display: flex; justify-content: flex-end; align-items: center; padding-top: .2em; }

/* Party & character creation workbench */
.gb-party-grid { display: grid; gap: .9em; grid-template-columns: minmax(0, 1.25fr) minmax(0, 1fr); align-items: start; }
@media (max-width: 900px) { .gb-party-grid { grid-template-columns: minmax(0, 1fr); } }
.gb-forge-sections { display: flex; flex-direction: column; gap: .6em; }
.gb-forge-foot { display: flex; flex-direction: column; gap: .4em; padding-top: .25em; border-top: 1px solid color-mix(in srgb, var(--gb-border) 45%, transparent); }
.gb-roll-btn { width: 100%; padding: .42em 1em !important; font-size: 1.02em; }

/* Portrait picker strip */
.gb-portrait-strip { display: flex; flex-wrap: wrap; gap: .35em; align-items: center; }
[data-tales-panel] .gb-portrait-strip button.gb-portrait-tile { display: inline-grid; place-items: center; padding: 2px; min-width: 54px; min-height: 54px; background: var(--gb-inset); border: 2px solid color-mix(in srgb, var(--gb-border) 65%, transparent); border-image: none; border-radius: 3px; box-shadow: none; }
[data-tales-panel] .gb-portrait-strip button.gb-portrait-tile.gb-selected { border-color: var(--gb-accent); box-shadow: 0 0 0 1px var(--gb-accent); }
[data-tales-panel] .gb-portrait-strip button.gb-portrait-none { font-size: .78em; color: var(--gb-muted); padding: 0 .5em; }

/* Stat builders, steppers, boosts, and skills */
.gb-stat-rows { display: flex; flex-direction: column; gap: .35em; }
.gb-stat-row { display: grid; grid-template-columns: minmax(0, 1fr) auto auto; align-items: center; gap: .45em; padding: .22em .35em; border-radius: 3px; background: color-mix(in srgb, var(--gb-background) 65%, transparent); }
.gb-stat-row.gb-priority-row { grid-template-columns: auto minmax(0, 1fr) auto; }
.gb-stat-row-label { display: flex; flex-wrap: wrap; align-items: center; gap: .25em; min-width: 0; font-size: .92em; }
.gb-priority-badge { min-width: 4.2em; justify-content: center; font-family: var(--gb-mono-font); }
.gb-cost-note { font-family: var(--gb-mono-font); font-size: .82em; min-width: 5.5em; text-align: right; font-variant-numeric: tabular-nums; }
.gb-stepper { display: inline-flex; align-items: center; gap: .2em; }
.gb-stepper input[type="number"] { width: 3.6em; text-align: center; font-family: var(--gb-mono-font); font-variant-numeric: tabular-nums; }
[data-tales-panel] button.gb-step-btn { min-width: 1.85em; padding: .1em .35em; text-align: center; font-family: var(--gb-mono-font); }

.gb-stat-strip { display: flex; flex-wrap: wrap; gap: .3em; }
.gb-stat-chip { display: inline-flex; align-items: baseline; gap: .3em; padding: .16em .48em; border-radius: 3px; background: var(--gb-inset); border: 1px solid color-mix(in srgb, var(--gb-border) 50%, transparent); font-family: var(--gb-mono-font); font-variant-numeric: tabular-nums; font-size: .88em; }
.gb-stat-chip small { color: var(--gb-muted); font-size: .82em; }
.gb-stat-chip strong { color: var(--gb-text); }
.gb-stat-chip.gb-stat-prime { border-color: color-mix(in srgb, var(--gb-accent) 70%, transparent); }
.gb-stat-chip.gb-stat-prime strong { color: var(--gb-accent); }

.gb-boost-group { display: flex; flex-direction: column; gap: .25em; padding-top: .3em; border-top: 1px dashed color-mix(in srgb, var(--gb-border) 35%, transparent); }
.gb-boost-head { display: flex; flex-wrap: wrap; align-items: center; gap: .35em; font-size: .88em; }
.gb-boost-row { display: flex; flex-wrap: wrap; gap: .35em; }
.gb-boost-slot { display: inline-flex; align-items: center; gap: .25em; padding: .12em .25em; border-radius: 3px; border: 1px solid transparent; }
.gb-boost-slot.gb-conflict { border-color: var(--gb-bad); background: color-mix(in srgb, var(--gb-bad) 15%, transparent); }

.gb-skill-row { display: grid; grid-template-columns: minmax(0, 1fr) auto auto; align-items: center; gap: .4em; padding: .2em .3em; border-radius: 3px; background: color-mix(in srgb, var(--gb-background) 60%, transparent); font-size: .88em; }
.gb-skill-cell { display: inline-flex; align-items: center; gap: .25em; }

/* Party roster cards */
.gb-roster-col { display: flex; flex-direction: column; gap: .65em; min-width: 0; }
.gb-slot-pips { display: inline-flex; gap: .25em; }
.gb-slot-pip { width: .85em; height: .45em; border-radius: 2px; background: color-mix(in srgb, var(--gb-border) 35%, var(--gb-inset)); border: 1px solid color-mix(in srgb, var(--gb-border) 60%, transparent); }
.gb-slot-pip.gb-required { border-color: var(--gb-accent); }
.gb-slot-pip.gb-filled { background: var(--gb-good); border-color: var(--gb-good); }
.gb-empty-roster { align-items: center; text-align: center; padding: 1.5em 1em; }
.gb-member-card { gap: .5em; }
.gb-member-head { display: flex; align-items: flex-start; gap: .6em; }
.gb-member-portrait { width: 56px; height: 56px; flex: none; display: grid; place-items: center; overflow: hidden; background: var(--gb-inset); border: 2px solid var(--gb-border); border-radius: 3px; }
.gb-portrait-fallback { font-size: 1.4em; font-weight: 700; color: var(--gb-accent); }
.gb-member-ident { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: .25em; }
.gb-member-name-row { display: flex; flex-wrap: wrap; align-items: baseline; gap: .35em; }
.gb-member-name { font-size: 1.04em; color: var(--gb-text); }
.gb-member-gear { display: flex; flex-direction: column; gap: .3em; padding-top: .3em; border-top: 1px dashed color-mix(in srgb, var(--gb-border) 35%, transparent); }

/* Character stable & workbench mode tabs */
.gb-tab-bar { display: flex; flex-wrap: wrap; gap: .35em; border-bottom: 1px solid color-mix(in srgb, var(--gb-border) 45%, transparent); padding-bottom: .45em; }
[data-tales-panel] .gb-tab-bar button.gb-tab-btn { font-size: .86em; padding: .24em .75em; border-radius: 3px; border-image: none; background: color-mix(in srgb, var(--gb-inset) 70%, transparent); border: 1px solid color-mix(in srgb, var(--gb-border) 55%, transparent); color: var(--gb-muted); }
[data-tales-panel] .gb-tab-bar button.gb-tab-btn.gb-selected { background: color-mix(in srgb, var(--gb-accent) 20%, var(--gb-inset)); border-color: var(--gb-accent); color: var(--gb-accent); font-weight: 700; }
.gb-stable-list { display: flex; flex-direction: column; gap: .45em; }
.gb-stable-row { display: flex; align-items: center; justify-content: space-between; gap: .55em; padding: .4em .5em; border-radius: 3px; background: color-mix(in srgb, var(--gb-background) 70%, transparent); border: 1px solid color-mix(in srgb, var(--gb-border) 45%, transparent); }
.gb-stable-portrait { width: 42px; height: 42px; flex: none; display: grid; place-items: center; overflow: hidden; background: var(--gb-inset); border: 1px solid var(--gb-border); border-radius: 3px; }
.gb-stable-info { flex: 1; min-width: 0; font-size: .9em; }
.gb-stable-origin { font-size: .82em; }

.gb-side { grid-area: side; display: flex; flex-direction: column; gap: var(--gb-gap); min-width: 0; min-height: 0; }
.gb-frame[data-arrangement="ultrawide"] .gb-side, .gb-frame[data-arrangement="tall"] .gb-side { display: contents; }
.gb-frame[data-arrangement="ultrawide"] .gb-portraits { grid-area: portraits; }
.gb-frame[data-arrangement="ultrawide"] .gb-map { grid-area: map; }
.gb-frame[data-arrangement="ultrawide"] .gb-controls { grid-area: controls; }
.gb-frame[data-arrangement="tall"] .gb-portraits { grid-area: portraits; }
.gb-frame[data-arrangement="tall"] .gb-controls { grid-area: controls; }
.gb-frame[data-arrangement="tall"] .gb-map { grid-area: view; justify-self: end; align-self: start; width: clamp(80px, 32%, 260px); max-height: 60%; margin: .4em; z-index: 2; }
.gb-overlay { grid-area: view; }
.gb-frame[data-arrangement="tall"] .gb-overlay { grid-area: 2 / 1 / 4 / 2; }

/* Panels */
.gb-panel {
  position: relative; min-width: 0; min-height: 0; display: flex; flex-direction: column;
  background: var(--gb-background); border: 2px solid var(--gb-border);
  box-shadow: inset 0 0 0 1px var(--gb-inset);
}
.gb-panel > h2 {
  margin: 0; padding: .2em .6em; font-size: .78em; font-weight: 700; letter-spacing: .1em; text-transform: uppercase;
  color: var(--gb-accent); border-bottom: 1px solid color-mix(in srgb, var(--gb-border) 60%, transparent);
  display: flex; justify-content: space-between; gap: 1em; flex: none;
}
.gb-panel > h2 small { color: var(--gb-muted); letter-spacing: .03em; text-transform: none; font-weight: 400; }
.gb-row { display: flex; flex-wrap: wrap; gap: .3em; align-items: center; margin: .25em 0; }
.gb-muted { color: var(--gb-muted); }
.gb-picture { image-rendering: pixelated; display: block; }
.gb-picture[data-gb-sampling="linear"] { image-rendering: auto; }
.gb-picture-sheet-image { display: block; position: absolute; max-width: none; image-rendering: pixelated; }
.gb-picture[data-gb-sampling="linear"] > .gb-picture-sheet-image { image-rendering: auto; }
img.gb-picture { object-fit: contain; }
.gb-fill { width: 100%; height: auto; }

[data-tales-panel] button {
  font: inherit; cursor: pointer; border: 1px solid var(--gb-border); background: var(--gb-button); color: var(--gb-button-text);
  padding: .15em .55em; box-shadow: inset 0 -2px 0 color-mix(in srgb, #000 40%, transparent);
}
[data-tales-panel] button:hover:not(:disabled) { border-color: var(--gb-accent); }
[data-tales-panel] button:disabled { opacity: .35; cursor: default; }
[data-tales-panel] :focus-visible { outline: 2px solid var(--gb-accent); outline-offset: 1px; }
[data-tales-panel] input, [data-tales-panel] select { background: var(--gb-inset); color: var(--gb-text); border: 1px solid var(--gb-border); padding: .1em .3em; }

/* Status line */
.gb-status { grid-area: status; flex-direction: row; align-items: center; gap: 1.2em; padding: .15em .6em; white-space: nowrap; overflow: hidden; }
.gb-status .gb-where { color: var(--gb-accent); overflow: hidden; text-overflow: ellipsis; min-width: 0; }
.gb-status .gb-spacer { flex: 1; }
.gb-compass { display: inline-grid; place-items: center; width: 1.5em; height: 1.5em; border: 1px solid var(--gb-border); border-radius: 50%; color: var(--gb-accent); font-size: .8em; flex: none; }

/* The view: the Engine draws here, so the panel is clear; its shadow paints the gaps around the other panels. */
.gb-view { grid-area: view; background: transparent; box-shadow: 0 0 0 100vmax var(--gb-inset); }
.gb-view-surface { position: absolute; inset: 0; }

/* The log: a journal page under the view */
.gb-log { grid-area: log; }
.gb-log .gb-tab { flex: none; align-self: flex-start; margin: .25em .4em 0; padding: .15em .85em; border: 2px solid var(--gb-border); border-bottom: 0; background: var(--gb-page); color: var(--gb-page-text); font-size: .85em; font-weight: 600; letter-spacing: .04em; }
.gb-log .gb-cmd-toggle { font-family: var(--gb-mono-font); background: var(--gb-button); color: var(--gb-muted); border-color: var(--gb-border); padding: .15em .55em; }
.gb-log .gb-cmd-toggle[aria-pressed="true"] { background: var(--gb-page); color: var(--gb-page-accent); }
.gb-log .gb-tabs .gb-spacer { flex: 1; }
.gb-log .gb-page { flex: 1; min-height: 0; overflow: auto; background: var(--gb-page); color: var(--gb-page-text); border-top: 2px solid var(--gb-border); padding: .45em .85em; font-family: var(--gb-prose-font); font-size: 1.08em; line-height: 1.42; }
.gb-log .gb-page p { margin: 0 0 .35em; white-space: pre-wrap; }
.gb-log .gb-page p.gb-old { color: color-mix(in srgb, var(--gb-page-text) 56%, var(--gb-page)); }
.gb-log .gb-page p.gb-note { color: var(--gb-page-accent); font-family: var(--gb-font); font-size: .92em; font-weight: 600; }
.gb-log .gb-tabs { flex: none; display: flex; gap: .2em; align-items: flex-end; }
.gb-log .gb-tabs .gb-tab[aria-pressed="false"] { opacity: .72; }
.gb-log .gb-page .gb-choice.gb-selected { font-weight: 700; border-color: var(--gb-page-accent); }
.gb-owner { font-size: .75em; color: var(--gb-accent); }
.gb-invitation { display: block; max-width: 100%; overflow-wrap: anywhere; user-select: all; text-align: left; font: .8em var(--gb-mono-font); }
.gb-owner.gb-away { color: var(--gb-muted); }
.gb-log .gb-page .gb-choice { display: flex; align-items: baseline; gap: .55em; width: 100%; text-align: left; font-family: var(--gb-font); font-size: .94em; background: color-mix(in srgb, var(--gb-page-text) 6%, var(--gb-page)); border: 1px solid color-mix(in srgb, var(--gb-page-text) 20%, transparent); border-radius: 3px; box-shadow: none; padding: .28em .55em; margin: .25em 0; color: var(--gb-page-link); }
.gb-log .gb-page .gb-choice:hover { background: color-mix(in srgb, var(--gb-page-accent) 12%, var(--gb-page)); border-color: var(--gb-page-accent); }
.gb-log .gb-page .gb-choice b { display: inline-grid; place-items: center; min-width: 1.45em; padding: 0 .25em; border-radius: 2px; background: var(--gb-page-accent); color: var(--gb-page); font-family: var(--gb-mono-font); font-size: .85em; }
.gb-log .gb-prompt { flex: none; display: flex; gap: .45em; align-items: center; padding: .22em .7em; border-top: 2px solid var(--gb-border); background: var(--gb-inset); color: var(--gb-accent); font-family: var(--gb-mono-font); font-size: .9em; }
.gb-log .gb-prompt input { flex: 1; min-width: 0; background: none; border: 0; color: var(--gb-text); padding: 0; font-family: var(--gb-mono-font); }

/* Portraits. The roster viewport owns scrolling; its page controls keep a
   larger party reachable without shrinking cards into unreadable tiles. */
.gb-portraits { min-height: 0; }
.gb-roster-nav { flex: none; display: flex; align-items: center; justify-content: space-between; gap: .4em; padding: .15em .3em 0; }
.gb-roster-nav button { min-width: 2em; padding: 0 .3em; line-height: 1.1; }
.gb-roster-page { flex: 1; color: var(--gb-muted); text-align: center; font-size: .8em; font-variant-numeric: tabular-nums; }
.gb-roster-scroll { flex: 1 1 auto; min-height: 0; overflow: auto; overscroll-behavior: contain; scrollbar-gutter: stable; }
.gb-portraits ol { list-style: none; margin: 0; padding: .3em; display: grid; gap: .3em; min-height: min-content;
  grid-template-columns: repeat(auto-fill, minmax(calc(4.6em * var(--gb-portrait-scale)), 1fr)); align-content: start; }
.gb-frame[data-arrangement="tall"] .gb-portraits ol { grid-template-columns: repeat(auto-fill, minmax(calc(3.8em * var(--gb-portrait-scale)), 1fr)); }
.gb-card { display: flex; flex-direction: column; width: 100%; min-width: 0; border: 2px solid var(--gb-border); background: var(--gb-inset); padding: 0; text-align: center; box-shadow: none; }
.gb-card.gb-acting { outline: 2px solid var(--gb-accent); outline-offset: 1px; }
.gb-card.gb-down { filter: grayscale(1) brightness(.6); }
.gb-card .gb-face { aspect-ratio: 1; display: grid; place-items: center; overflow: hidden; font-size: 1.6em; color: var(--gb-muted); }
.gb-card .gb-face > * { width: 100%; height: 100%; }
.gb-card .gb-name { padding: .05em .2em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; background: color-mix(in srgb, var(--gb-border) 55%, #000); color: var(--gb-text); }
.gb-card .gb-flag { color: var(--gb-accent); }
.gb-card .gb-amount { font-size: .85em; color: var(--gb-muted); font-variant-numeric: tabular-nums; }
.gb-bar { height: .3em; background: var(--gb-inset); flex: none; }
.gb-bar i { display: block; height: 100%; }

/* Map, or foes in combat */
.gb-map { flex: 1; min-height: 4em; }
.gb-frame[data-screen="combat"][data-arrangement="standard"] .gb-map { min-height: 10em; }
.gb-map-wrap { flex: 1; min-height: 0; padding: .45em; display: grid; place-items: center; overflow: hidden; }
.gb-map-svg { width: 100%; height: 100%; max-width: 100%; max-height: 100%; display: block; }
.gb-map-cell { fill: color-mix(in srgb, var(--gb-text) 7%, var(--gb-inset)); stroke: color-mix(in srgb, var(--gb-border) 22%, transparent); stroke-width: .75; }
.gb-map-here { fill: color-mix(in srgb, var(--gb-accent) 22%, var(--gb-inset)); stroke: color-mix(in srgb, var(--gb-accent) 55%, transparent); }
.gb-map-wall { stroke: color-mix(in srgb, var(--gb-text) 82%, var(--gb-border)); stroke-width: 3; stroke-linecap: round; }
.gb-map-door { stroke: var(--gb-accent); stroke-width: 3.5; stroke-linecap: butt; }
.gb-map-player { fill: var(--gb-accent); stroke: var(--gb-inset); stroke-width: 1; }
.gb-foes { list-style: none; margin: 0; padding: .35em .45em; overflow: auto; display: grid; gap: .3em; align-content: start; }
.gb-foes li.gb-down { opacity: .45; text-decoration: line-through; }
.gb-foes li.gb-acting .gb-who { color: var(--gb-accent); }
.gb-foe-row { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: .15em .45em; align-items: center; width: 100%; text-align: left; padding: .22em .35em; border-radius: 2px; border: 1px solid transparent; background: transparent; box-shadow: none; }
.gb-foes button.gb-foe-targetable { background: color-mix(in srgb, var(--gb-button) 65%, transparent); border-color: color-mix(in srgb, var(--gb-border) 60%, transparent); }
.gb-foes button.gb-foe-targetable:hover { border-color: var(--gb-accent); background: color-mix(in srgb, var(--gb-accent) 14%, var(--gb-button)); }
.gb-foes .gb-who { color: var(--gb-foe); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; font-weight: 600; }
.gb-foes .gb-num { font-family: var(--gb-mono-font); font-variant-numeric: tabular-nums; font-size: .88em; }
.gb-foes .gb-bar { grid-column: 1 / -1; }

/* Controls */
.gb-controls { flex: 0 1 auto; min-height: 0; max-height: min(42cqh, 28em); flex-direction: row; gap: .45em; padding: .45em; align-items: start; font-size: calc(1em * var(--gb-control-scale)); overflow: hidden; }
.gb-pad { display: grid; grid-template-columns: repeat(3, 2.1em); grid-auto-rows: 2.1em; gap: .2em; flex: none; }
.gb-pad button { padding: 0; }
.gb-commands { flex: 1; display: flex; flex-direction: column; gap: .25em; min-width: 0; min-height: 0; overflow: auto; }
.gb-commands button { text-align: left; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.gb-combat-summary { display: flex; flex-wrap: wrap; align-items: baseline; gap: .2em .45em; line-height: 1.3; padding-bottom: .2em; border-bottom: 1px solid color-mix(in srgb, var(--gb-border) 45%, transparent); }
.gb-combat-stat { font-family: var(--gb-mono-font); font-size: .88em; color: var(--gb-good); }
.gb-combat-budget { font-family: var(--gb-mono-font); font-size: .82em; color: var(--gb-accent); padding: .05em .4em; border: 1px solid color-mix(in srgb, var(--gb-accent) 45%, transparent); border-radius: 2px; }
.gb-combat-check { width: 100%; font-family: var(--gb-mono-font); font-size: .85em; color: var(--gb-text); }
.gb-combat-label { color: var(--gb-muted); font-size: .74em; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; margin-top: .2em; }
.gb-commands .gb-selected { border-color: var(--gb-accent); color: var(--gb-accent); }
.gb-commands .gb-down { opacity: .45; text-decoration: line-through; }
.gb-frame[data-arrangement="tall"] .gb-commands { flex-direction: row; flex-wrap: wrap; }
.gb-frame[data-arrangement="tall"] .gb-combat-summary { width: 100%; }

/* Overlays: shop, temple, trainer, a character, the game menu */
.gb-overlay { z-index: 3; margin: clamp(4px, 3%, 32px); overflow: hidden; box-shadow: 0 0 0 4px var(--gb-inset), 0 12px 40px #000c; }
.gb-overlay .gb-body { flex: 1; min-height: 0; overflow: auto; padding: .4em .7em; }
.gb-overlay .gb-columns { display: grid; gap: .5em 1em; grid-template-columns: repeat(auto-fit, minmax(min(100%, 14em), 1fr)); align-content: start; }
.gb-overlay h3 { margin: .3em 0 .2em; font-size: .8em; letter-spacing: .1em; text-transform: uppercase; color: var(--gb-muted); }
.gb-overlay table { width: 100%; border-collapse: collapse; font-variant-numeric: tabular-nums; }
.gb-overlay td { padding: .1em .2em; border-bottom: 1px dotted color-mix(in srgb, var(--gb-border) 40%, transparent); vertical-align: middle; }
.gb-overlay td.gb-num { text-align: right; color: var(--gb-muted); white-space: nowrap; }
.gb-overlay td.gb-act { text-align: right; width: 1%; white-space: nowrap; }
.gb-overlay .gb-close { box-shadow: none; border: 0; background: none; color: var(--gb-muted); padding: 0 .3em; font-size: 1.6em; line-height: .8; }
.gb-overlay label { display: inline-flex; gap: .3em; align-items: center; margin-inline-end: .8em; }

/* In-game Workbench Debug Surface overlay */
.gb-workbench-backdrop { position: fixed; inset: 0; z-index: 40; background: rgba(7, 9, 13, 0.78); display: flex; align-items: center; justify-content: center; padding: 14px; font-family: system-ui, -apple-system, sans-serif; }
.gb-workbench-backdrop[hidden] { display: none; }
.gb-wb-shell { width: min(96vw, 1380px); height: min(92vh, 860px); background: #0f1217; color: #ece6d9; border: 1px solid #3c465c; border-radius: 10px; display: flex; flex-direction: column; overflow: hidden; box-shadow: 0 24px 64px rgba(0, 0, 0, 0.75); }
.gb-wb-header { display: flex; align-items: center; gap: 10px; padding: 8px 12px; background: #171b23; border-bottom: 1px solid #2c3444; flex-wrap: wrap; }
.gb-wb-title { font-size: 13px; color: #e4a94c; letter-spacing: 0.02em; }
.gb-wb-live { font-family: ui-monospace, Menlo, monospace; font-size: 11px; color: #3dc4ab; background: rgba(61, 196, 171, 0.14); border: 1px solid rgba(61, 196, 171, 0.4); border-radius: 999px; padding: 2px 9px; }
.gb-wb-status { font-family: ui-monospace, Menlo, monospace; font-size: 11px; padding: 2px 8px; border-radius: 999px; background: #222836; color: #9aa4b6; }
.gb-wb-status[data-state="online"] { background: rgba(61, 196, 171, 0.16); color: #3dc4ab; }
.gb-wb-status[data-state="offline"] { background: rgba(235, 93, 93, 0.16); color: #eb5d5d; }
.gb-wb-input, .gb-wb-select, .gb-wb-note { background: #0d1016; color: #ece6d9; border: 1px solid #343d50; border-radius: 5px; padding: 4px 8px; font-size: 12px; }
.gb-wb-input { width: 195px; font-family: ui-monospace, Menlo, monospace; font-size: 11px; }
.gb-wb-pinbar { display: flex; align-items: center; gap: 8px; padding: 7px 12px; background: #141820; border-bottom: 1px solid #2c3444; flex-wrap: wrap; }
.gb-wb-pinlabel { font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.05em; color: #9aa4b6; }
.gb-wb-select { max-width: 240px; font-family: ui-monospace, Menlo, monospace; font-size: 11px; }
.gb-wb-note { flex: 1; min-width: 180px; }
.gb-wb-feedback { font-family: ui-monospace, Menlo, monospace; font-size: 11px; color: #e4a94c; }
[data-tales-panel] .gb-workbench-backdrop .gb-wb-shell button { border: 1px solid #3f4b63; border-image: none; box-shadow: none; background: #1f2530; color: #ece6d9; padding: 4px 10px; border-radius: 5px; font-size: 11.5px; font-weight: 600; cursor: pointer; image-rendering: auto; }
[data-tales-panel] .gb-workbench-backdrop .gb-wb-shell button:hover { background: #2a3242; }
[data-tales-panel] .gb-workbench-backdrop .gb-wb-shell .gb-wb-btn-fix { border-color: #eb5d5d; color: #eb5d5d; background: rgba(235, 93, 93, 0.14); }
[data-tales-panel] .gb-workbench-backdrop .gb-wb-shell .gb-wb-btn-q { border-color: #64a5f6; color: #64a5f6; background: rgba(100, 165, 246, 0.14); }
[data-tales-panel] .gb-workbench-backdrop .gb-wb-shell .gb-wb-btn-ok { border-color: #3dc4ab; color: #3dc4ab; background: rgba(61, 196, 171, 0.14); }
.gb-wb-body { flex: 1; min-height: 0; position: relative; display: flex; }
.gb-wb-iframe { width: 100%; height: 100%; border: 0; background: #0f1217; }
.gb-wb-offline { margin: auto; max-width: 480px; padding: 24px; background: #171b23; border: 1px solid #2c3444; border-radius: 10px; text-align: center; }
.gb-wb-offline h3 { margin: 0 0 8px 0; color: #e4a94c; }
.gb-wb-offline p { margin: 0 0 10px 0; color: #9aa4b6; font-size: 12.5px; }
.gb-wb-code { margin: 0 0 14px 0; padding: 8px 12px; background: #0c0f14; border: 1px solid #2c3444; border-radius: 6px; color: #ece6d9; font-family: ui-monospace, Menlo, monospace; font-size: 12px; }
`;

/** A skin's stylesheet: its colours as theme variables, its panel tile under them, its frame and button faces cut into nine. */
export function skinLook(skin) {
  if (!skin) {
    return '';
  }

  const imageRendering = (picture) => picture?.sampling === 'linear' ? 'auto' : 'pixelated';
  const colors = Object.entries(skin.colors ?? {}).map(([name, value]) => `--gb-${name.replace('_', '-')}:${value};`).join('');
  let css = `[data-tales-panel]{${colors}}`;
  if (skin.panel?.url) {
    css += '[data-tales-panel] .gb-panel:not(.gb-view){background:linear-gradient(var(--gb-background),var(--gb-background)),'
      + `url("${skin.panel.url}") 0 0 / ${skin.panel.width * 2}px ${skin.panel.height * 2}px repeat;image-rendering:${imageRendering(skin.panel)}}`;
  }

  if (skin.frame?.picture?.url) {
    const width = skin.frame.slice * 2;
    css += `[data-tales-panel] .gb-panel{border:${width}px solid transparent;box-shadow:none;`
      + `border-image:url("${skin.frame.picture.url}") ${skin.frame.slice} / ${width}px stretch}`
      + `[data-tales-panel] .gb-panel{image-rendering:${imageRendering(skin.frame.picture)}}`
      + '[data-tales-panel] .gb-view{box-shadow:0 0 0 100vmax var(--gb-inset)}';
  }

  if (skin.button?.picture?.url) {
    const width = skin.button.slice;
    css += `[data-tales-panel] button{border:${width}px solid transparent;box-shadow:none;`
      + `border-image:url("${skin.button.picture.url}") ${skin.button.slice} fill / ${width}px stretch;image-rendering:${imageRendering(skin.button.picture)}}`;
  }

  return css;
}
