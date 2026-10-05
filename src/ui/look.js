/**
 * The Game's look: one stylesheet of theme variables (a skin overrides the
 * colours and adds its tile, frame and button faces) and the panel layout.
 * Layout ratios arrive as CSS variables set by layout.js (--gb-main-rows,
 * --gb-side-width, --gb-text-scale, --gb-control-scale, --gb-portrait-scale);
 * the arrangement and screen arrive as data attributes on the frame.
 */

export const BASE_LOOK = `
[data-goldbox-panel] {
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
  --gb-foe: #c97ad6;
  --gb-font: ui-monospace, "DejaVu Sans Mono", "Cascadia Mono", Consolas, monospace;
  position: absolute; inset: 0; container-type: size; overflow: hidden;
  color: var(--gb-text); font-family: var(--gb-font);
}
[data-goldbox-panel] *, [data-goldbox-panel] *::before, [data-goldbox-panel] *::after { box-sizing: border-box; }
[data-goldbox-panel] [hidden] { display: none !important; }
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
.gb-sheet { overflow: auto; padding: .6em 1em; width: min(100%, 72em); justify-self: center; }
.gb-sheet h1 { margin: 0 0 .3em; font-size: 1.4em; color: var(--gb-accent); }
.gb-sheet h2 { margin: .8em 0 .3em; font-size: 1.05em; color: var(--gb-accent); }

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

[data-goldbox-panel] button {
  font: inherit; cursor: pointer; border: 1px solid var(--gb-border); background: var(--gb-button); color: var(--gb-button-text);
  padding: .15em .55em; box-shadow: inset 0 -2px 0 color-mix(in srgb, #000 40%, transparent);
}
[data-goldbox-panel] button:hover:not(:disabled) { border-color: var(--gb-accent); }
[data-goldbox-panel] button:disabled { opacity: .35; cursor: default; }
[data-goldbox-panel] :focus-visible { outline: 2px solid var(--gb-accent); outline-offset: 1px; }
[data-goldbox-panel] input, [data-goldbox-panel] select { background: var(--gb-inset); color: var(--gb-text); border: 1px solid var(--gb-border); padding: .1em .3em; }

/* Status line */
.gb-status { grid-area: status; flex-direction: row; align-items: center; gap: 1.2em; padding: .15em .6em; white-space: nowrap; overflow: hidden; }
.gb-status .gb-where { color: var(--gb-accent); overflow: hidden; text-overflow: ellipsis; min-width: 0; }
.gb-status .gb-spacer { flex: 1; }
.gb-compass { display: inline-grid; place-items: center; width: 1.5em; height: 1.5em; border: 1px solid var(--gb-border); border-radius: 50%; color: var(--gb-accent); font-size: .8em; flex: none; }

/* The view: the Engine draws here, so the panel is clear; its shadow paints the gaps around the other panels. */
.gb-view { grid-area: view; background: transparent; box-shadow: 0 0 0 100vmax var(--gb-inset); }
.gb-view-surface { position: absolute; inset: 0; }

/* The log: a page of text under the view */
.gb-log { grid-area: log; }
.gb-log .gb-tab { flex: none; align-self: flex-start; margin: .25em .4em 0; padding: .1em .8em; border: 2px solid var(--gb-border); border-bottom: 0; background: var(--gb-page); color: var(--gb-page-text); }
.gb-log .gb-page { flex: 1; min-height: 0; overflow: auto; background: var(--gb-page); color: var(--gb-page-text); border-top: 2px solid var(--gb-border); padding: .3em .7em; font-size: 1.1em; }
.gb-log .gb-page p { margin: 0 0 .15em; white-space: pre-wrap; }
.gb-log .gb-page p.gb-old { color: color-mix(in srgb, var(--gb-page-text) 55%, var(--gb-page)); }
.gb-log .gb-page p.gb-note { color: var(--gb-page-accent); }
.gb-log .gb-tabs { flex: none; display: flex; gap: .2em; }
.gb-log .gb-tabs .gb-tab[aria-pressed="false"] { opacity: .7; }
.gb-log .gb-page .gb-choice.gb-selected { font-weight: bold; }
.gb-owner { font-size: .75em; color: var(--gb-accent); }
.gb-invitation { display: block; max-width: 100%; overflow-wrap: anywhere; user-select: all; text-align: left; font: .8em monospace; }
.gb-owner.gb-away { color: var(--gb-muted); }
.gb-log .gb-page .gb-choice { display: block; width: 100%; text-align: left; background: none; border: 0; box-shadow: none; padding: .05em 0; color: var(--gb-page-link); }
.gb-log .gb-page .gb-choice:hover { text-decoration: underline; }
.gb-log .gb-page .gb-choice b { color: var(--gb-page-accent); margin-inline-end: .5em; }
.gb-log .gb-prompt { flex: none; display: flex; gap: .4em; align-items: center; padding: .15em .6em; border-top: 2px solid var(--gb-border); background: var(--gb-page); color: var(--gb-page-link); }
.gb-log .gb-prompt input { flex: 1; min-width: 0; background: none; border: 0; color: var(--gb-page-text); padding: 0; }

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
.gb-map pre { flex: 1; min-height: 0; margin: 0; padding: .3em; overflow: hidden; line-height: 1; font-size: .85em; display: grid; place-items: center; }
.gb-foes { list-style: none; margin: 0; padding: .3em .5em; overflow: auto; display: grid; gap: .25em; align-content: start; }
.gb-foes li { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 0 .4em; align-items: center; }
.gb-foes li.gb-down { opacity: .45; text-decoration: line-through; }
.gb-foes li.gb-acting .gb-who { color: var(--gb-accent); }
.gb-foes .gb-who { color: var(--gb-foe); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.gb-foes .gb-bar { grid-column: 1 / -1; }

/* Controls */
.gb-controls { flex: 0 1 auto; min-height: 0; max-height: min(42cqh, 28em); flex-direction: row; gap: .4em; padding: .4em; align-items: start; font-size: calc(1em * var(--gb-control-scale)); overflow: hidden; }
.gb-pad { display: grid; grid-template-columns: repeat(3, 2em); grid-auto-rows: 2em; gap: .15em; flex: none; }
.gb-pad button { padding: 0; }
.gb-commands { flex: 1; display: flex; flex-direction: column; gap: .2em; min-width: 0; min-height: 0; overflow: auto; }
.gb-commands button { text-align: left; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.gb-combat-summary { display: flex; flex-wrap: wrap; gap: .15em .35em; line-height: 1.25; }
.gb-combat-label { color: var(--gb-muted); font-size: .8em; letter-spacing: .06em; text-transform: uppercase; margin-top: .15em; }
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
`;

/** A skin's stylesheet: its colours as theme variables, its panel tile under them, its frame and button faces cut into nine. */
export function skinLook(skin) {
  if (!skin) {
    return '';
  }

  const imageRendering = (picture) => picture?.sampling === 'linear' ? 'auto' : 'pixelated';
  const colors = Object.entries(skin.colors ?? {}).map(([name, value]) => `--gb-${name.replace('_', '-')}:${value};`).join('');
  let css = `[data-goldbox-panel]{${colors}}`;
  if (skin.panel?.url) {
    css += '[data-goldbox-panel] .gb-panel:not(.gb-view){background:linear-gradient(var(--gb-background),var(--gb-background)),'
      + `url("${skin.panel.url}") 0 0 / ${skin.panel.width * 2}px ${skin.panel.height * 2}px repeat;image-rendering:${imageRendering(skin.panel)}}`;
  }

  if (skin.frame?.picture?.url) {
    const width = skin.frame.slice * 2;
    css += `[data-goldbox-panel] .gb-panel{border:${width}px solid transparent;box-shadow:none;`
      + `border-image:url("${skin.frame.picture.url}") ${skin.frame.slice} / ${width}px stretch}`
      + `[data-goldbox-panel] .gb-panel{image-rendering:${imageRendering(skin.frame.picture)}}`
      + '[data-goldbox-panel] .gb-view{box-shadow:0 0 0 100vmax var(--gb-inset)}';
  }

  if (skin.button?.picture?.url) {
    const width = skin.button.slice;
    css += `[data-goldbox-panel] button{border:${width}px solid transparent;box-shadow:none;`
      + `border-image:url("${skin.button.picture.url}") ${skin.button.slice} fill / ${width}px stretch;image-rendering:${imageRendering(skin.button.picture)}}`;
  }

  return css;
}
