import { element, picture } from '../dom.js';
import { bar } from './members.js';

const SVG_NS = 'http://www.w3.org/2000/svg';

function svgElement(tag, attrs = {}, ...children) {
  const node = document.createElementNS(SVG_NS, tag);
  for (const [name, value] of Object.entries(attrs)) {
    if (value !== undefined && value !== null) {
      node.setAttribute(name, String(value));
    }
  }
  node.append(...children);
  return node;
}

/**
 * Parses the AreaMap.Render ASCII grid into a crisp vector SVG minimap:
 * tiled cells, solid walls, distinct doorway bars, and an oriented party arrow.
 */
function renderMinimapSvg(svg, ascii) {
  const lines = (ascii ?? '').split('\n').filter((line) => line.length > 0);
  if (lines.length < 3 || lines.length % 2 === 0 || (lines[0].length - 1) % 3 !== 0) {
    svg.replaceChildren();
    return;
  }

  const cols = (lines[0].length - 1) / 3;
  const rows = (lines.length - 1) / 2;
  const cell = 24;
  const pad = 8;
  const width = cols * cell + pad * 2;
  const height = rows * cell + pad * 2;
  svg.setAttribute('viewBox', `0 0 ${width} ${height}`);

  const floors = [];
  const walls = [];
  const doors = [];
  const markers = [];

  for (let y = 0; y < rows; y++) {
    const cellLine = lines[y * 2 + 1];
    for (let x = 0; x < cols; x++) {
      const cx = pad + x * cell;
      const cy = pad + y * cell;
      const interior = cellLine.slice(x * 3 + 1, x * 3 + 3);
      const isPlayer = interior === '^^' || interior === '>>' || interior === 'vv' || interior === '<<';
      floors.push(svgElement('rect', {
        x: cx + 1,
        y: cy + 1,
        width: cell - 2,
        height: cell - 2,
        rx: 2,
        class: isPlayer ? 'gb-map-cell gb-map-here' : 'gb-map-cell',
      }));

      if (isPlayer) {
        const angle = interior === '^^' ? 0 : interior === '>>' ? 90 : interior === 'vv' ? 180 : 270;
        const mx = cx + cell / 2;
        const my = cy + cell / 2;
        markers.push(svgElement('polygon', {
          points: `${mx},${my - 6.5} ${mx + 5.5},${my + 5.5} ${mx},${my + 2.5} ${mx - 5.5},${my + 5.5}`,
          transform: angle === 0 ? undefined : `rotate(${angle} ${mx} ${my})`,
          class: 'gb-map-player',
        }));
      }
    }
  }

  const addEdge = (kind, x1, y1, x2, y2) => {
    if (kind === 'wall') {
      walls.push(svgElement('line', { x1, y1, x2, y2, class: 'gb-map-wall' }));
    } else if (kind === 'door') {
      // Wall stubs framing an accent-colored door bar in the center.
      const mx1 = x1 + (x2 - x1) * 0.22;
      const my1 = y1 + (y2 - y1) * 0.22;
      const mx2 = x1 + (x2 - x1) * 0.78;
      const my2 = y1 + (y2 - y1) * 0.78;
      walls.push(svgElement('line', { x1, y1, x2: mx1, y2: my1, class: 'gb-map-wall' }));
      walls.push(svgElement('line', { x1: mx2, y1: my2, x2, y2, class: 'gb-map-wall' }));
      doors.push(svgElement('line', { x1: mx1, y1: my1, x2: mx2, y2: my2, class: 'gb-map-door' }));
    }
  };

  // Horizontal edges
  for (let y = 0; y <= rows; y++) {
    const edgeLine = lines[y * 2];
    const py = pad + y * cell;
    for (let x = 0; x < cols; x++) {
      const token = edgeLine.slice(x * 3 + 1, x * 3 + 3);
      if (token === '--' || token === 'SS') {
        addEdge('wall', pad + x * cell, py, pad + (x + 1) * cell, py);
      } else if (token === 'DD') {
        addEdge('door', pad + x * cell, py, pad + (x + 1) * cell, py);
      }
    }
  }

  // Vertical edges
  for (let y = 0; y < rows; y++) {
    const cellLine = lines[y * 2 + 1];
    const py = pad + y * cell;
    for (let x = 0; x <= cols; x++) {
      const ch = cellLine[x * 3];
      const px = pad + x * cell;
      if (ch === '|' || ch === 'S') {
        addEdge('wall', px, py, px, py + cell);
      } else if (ch === 'D') {
        addEdge('door', px, py, px, py + cell);
      }
    }
  }

  svg.replaceChildren(...floors, ...walls, ...doors, ...markers);
}

/** The side column's free space: a rendered vector minimap in play, the other sides of the fight in combat. */
export function createMap(onPickTarget = null) {
  const title = element('h2', {});
  const map = svgElement('svg', {
    id: 'rusty-tales-map',
    class: 'gb-map-svg',
    role: 'img',
    'aria-label': 'Map',
    preserveAspectRatio: 'xMidYMid meet',
  });
  const mapWrap = element('div', { class: 'gb-map-wrap' }, map);
  const foes = element('ul', { class: 'gb-foes' });
  const node = element('section', { class: 'gb-panel gb-map', 'data-rusty-ui-interactive': '' }, title, mapWrap, foes);
  let lastAscii = null;

  const render = (view) => {
    const combat = view.screen === 'combat';
    mapWrap.hidden = combat;
    foes.hidden = !combat;
    if (!combat) {
      title.textContent = view.position?.name ?? 'Map';
      const ascii = view.map ?? '';
      if (ascii !== lastAscii) {
        lastAscii = ascii;
        renderMinimapSvg(map, ascii);
      }
      return;
    }

    const others = (view.fight?.members ?? []).filter((member) => member.side !== 0);
    const standing = others.filter((member) => !member.defeated).length;
    const legalTargets = new Set(
      (view.fight?.decision?.actions ?? []).flatMap((action) => (action.targets ?? []).map((target) => target.id)),
    );
    title.replaceChildren(
      view.fight?.encounter ?? 'Foes',
      element('small', {}, `${standing} standing${view.fight?.track ? ` · ${view.fight.track}` : ''}`),
    );
    foes.replaceChildren(...others.map((member) => {
      const clickable = Boolean(onPickTarget && !member.defeated && member.id && legalTargets.has(member.id));
      const row = element(clickable ? 'button' : 'div', {
        ...(clickable ? { type: 'button', title: `Target ${member.name}` } : {}),
        class: `gb-foe-row${clickable ? ' gb-foe-targetable' : ''}`,
      },
        element('span', {}, ...picture(member.iconPicture, '', 18)),
        element('span', { class: 'gb-who' }, member.name),
        element('span', { class: 'gb-muted gb-num' }, `${member.value}${member.max === null ? '' : `/${member.max}`}`),
        bar(member.value, member.max));
      if (clickable) {
        row.addEventListener('click', () => onPickTarget(member.id));
      }
      return element('li', {
        class: `${member.defeated ? 'gb-down' : ''}${member.acting ? ' gb-acting' : ''}`,
      }, row);
    }));
  };

  return { node, render, dispose: () => {} };
}
