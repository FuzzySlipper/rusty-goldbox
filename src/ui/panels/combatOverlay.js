import { element } from '../dom.js';

const SVG_NS = 'http://www.w3.org/2000/svg';

function svgEl(tag, attrs = {}, text = null) {
  const node = document.createElementNS(SVG_NS, tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v !== undefined && v !== null) {
      node.setAttribute(k, String(v));
    }
  }
  if (text !== null) {
    node.textContent = text;
  }
  return node;
}

/**
 * Exact closed-form projection matching RustyTales.Game.Presentation.CombatScene's
 * orthographic (tactical) and perspective (first_person / side_view) camera math.
 * Returns normalized viewport coordinates { u, v } in [0..1], or null if behind camera.
 */
export function projectWorld(px, py, pz, fight, aspect) {
  const field = fight?.field;
  const width = field?.width ?? 6;
  const depth = field?.depth ?? 5;
  const presentation = fight?.presentation ?? 'tactical';
  const safeAspect = Math.max(0.25, aspect || 1.6);

  if (presentation === 'tactical') {
    const pitchDeg = fight?.camera?.pitch ?? 60;
    const fill = fight?.camera?.fill ?? 0.94;
    const pitch = (pitchDeg * Math.PI) / 180;
    const sinP = Math.sin(pitch);
    const cosP = Math.cos(pitch);

    const topY = (1.0 * cosP) + (0.35 * sinP);
    const botY = -depth * sinP;
    const midY = (topY + botY) / 2.0;

    // d = P - target, where target = (width/2, 0, 0) + up * midY and up = (0, cosP, -sinP)
    const dx = px - width / 2;
    const vy = (py * cosP - pz * sinP) - midY;

    const spanX = width + 0.7;
    const spanY = (depth * sinP) + (1.0 * cosP) + (0.35 * sinP);
    const orthoH = Math.max(spanY / fill, (spanX / safeAspect) / fill);
    const orthoW = orthoH * safeAspect;

    return {
      u: 0.5 + dx / orthoW,
      v: 0.5 - vy / orthoH,
    };
  }

  // Perspective camera (first_person or side_view)
  const isFirst = presentation === 'first_person';
  const pitchDeg = isFirst ? 4 : 12;
  const fovDeg = isFirst ? 36 : 34;
  const pitch = (pitchDeg * Math.PI) / 180;
  const sinP = Math.sin(pitch);
  const cosP = Math.cos(pitch);
  const tanY = Math.tan((fovDeg * Math.PI) / 360);
  const tanX = tanY * Math.max(0.4, safeAspect);

  let camX = width / 2;
  let camY;
  let camZ;
  if (isFirst) {
    const halfWidth = 1.5;
    const distForWidth = halfWidth / (0.88 * tanX);
    const distForHeight = 0.54 / (0.86 * tanY);
    const distance = Math.max(1.65, Math.max(distForWidth, distForHeight));
    const targetZ = depth * 0.66;
    camY = 0.52 + distance * sinP;
    camZ = targetZ + distance;
  } else {
    const halfWidth = Math.max(1.85, width * 0.36);
    const distForWidth = halfWidth / (0.90 * tanX);
    const distForHeight = 0.72 / (0.88 * tanY);
    const distance = Math.max(2.2, Math.max(distForWidth, distForHeight));
    const targetZ = depth * 0.68;
    camZ = Math.max(depth + 0.6, targetZ + distance);
    const dz = camZ - depth * 0.5;
    camY = 0.42 + dz * sinP;
  }

  const rx = px - camX;
  const ry = py - camY;
  const rz = pz - camZ;
  // Camera looks along forward = (0, -sinP, -cosP), up = (0, cosP, -sinP), right = (1, 0, 0)
  const vx = rx;
  const vy = ry * cosP - rz * sinP;
  const vz = -ry * sinP - rz * cosP;
  if (vz <= 0.05) {
    return null;
  }

  const ndcX = vx / (vz * tanX);
  const ndcY = vy / (vz * tanY);
  return {
    u: 0.5 + 0.5 * ndcX,
    v: 0.5 - 0.5 * ndcY,
  };
}

/** Simple line-of-sight check mirroring grid terrain sight blocking for click-site error messages. */
function hasGridLineOfSight(from, to, terrainByKey) {
  if (!from || !to) return true;
  let x0 = from.x;
  let y0 = from.y;
  const x1 = to.x;
  const y1 = to.y;
  const dx = Math.abs(x1 - x0);
  const dy = Math.abs(y1 - y0);
  const sx = x0 < x1 ? 1 : -1;
  const sy = y0 < y1 ? 1 : -1;
  let err = dx - dy;
  while (x0 !== x1 || y0 !== y1) {
    if ((x0 !== from.x || y0 !== from.y) && terrainByKey.get(`${x0},${y0}`)?.blocksSight) {
      return false;
    }
    const e2 = 2 * err;
    if (e2 > -dy) { err -= dy; x0 += sx; }
    if (e2 < dx) { err += dx; y0 += sy; }
  }
  return true;
}

function gridDistance(a, b, metric = 'chebyshev') {
  if (!a || !b) return 0;
  const dx = Math.abs(a.x - b.x);
  const dy = Math.abs(a.y - b.y);
  return metric === 'manhattan' ? dx + dy : Math.max(dx, dy);
}

/**
 * Interactive combat viewport overlay mounted inside `.gb-view-surface`:
 * - Projects an interactive tactical grid over the 3D floor when moving (click once to preview path, click again to confirm)
 * - Projects clickable unit reticles in tactical, first_person, and side_view modes
 * - Spawns floating error messages at the exact click site when an invalid square or unit is clicked
 * - Hosts the floating combat text layer over sprites (filled by combat feedback events)
 */
export function createCombatOverlay(surface, controls) {
  const svg = svgEl('svg', { class: 'gb-combat-svg', preserveAspectRatio: 'none' });
  const banner = element('div', { class: 'gb-combat-banner', hidden: 'hidden' });
  const floatLayer = element('div', { class: 'gb-float-layer', 'aria-hidden': 'true' });
  const root = element('div', { class: 'gb-combat-overlay', hidden: 'hidden' }, svg, banner, floatLayer);
  surface.append(root);

  let lastView = null;
  let hoverCellKey = null;

  /** Spawns a floating message at pixel coordinates inside the combat viewport. */
  const spawnFloatAt = (xPx, yPx, text, kind = 'error', delayMs = 0) => {
    const tag = element('div', { class: `gb-float-text gb-float-${kind}` }, text);
    tag.style.left = `${Math.round(xPx)}px`;
    tag.style.top = `${Math.round(yPx)}px`;
    if (delayMs > 0) {
      tag.style.animationDelay = `${delayMs}ms`;
    }
    floatLayer.append(tag);
    setTimeout(() => tag.remove(), 1450 + delayMs);
  };

  /** Spawns floating combat text anchored above a combatant's 3D sprite head. */
  const spawnOnMember = (memberId, text, kind, staggerIndex = 0) => {
    if (!lastView || lastView.screen !== 'combat') return false;
    const fight = lastView.fight;
    const member = (fight?.members ?? []).find((m) => m.id === memberId);
    if (!member?.worldPos) return false;
    const w = surface.clientWidth;
    const h = surface.clientHeight;
    if (w < 10 || h < 10) return false;

    const headY = fight.presentation === 'tactical' ? 1.05 : 0.92;
    const pt = projectWorld(member.worldPos.x, member.worldPos.y + headY, member.worldPos.z, fight, w / h);
    if (!pt) return false;
    const x = Math.max(36, Math.min(w - 36, pt.u * w));
    const y = Math.max(26, Math.min(h - 26, pt.v * h - staggerIndex * 20));
    spawnFloatAt(x, y, text, kind, staggerIndex * 110);
    return true;
  };

  const clientToLocal = (event) => {
    const rect = surface.getBoundingClientRect();
    return {
      x: Math.max(24, Math.min(rect.width - 24, event.clientX - rect.left)),
      y: Math.max(24, Math.min(rect.height - 24, event.clientY - rect.top)),
    };
  };

  const explainInvalidUnit = (fight, selection, actor, clickedMember) => {
    if (clickedMember.defeated || clickedMember.escaped) {
      return `${clickedMember.name} is already down`;
    }
    if (selection.unavailableAction) {
      const ua = selection.unavailableAction;
      if (ua.reason && !ua.reason.toLowerCase().includes('target')) {
        return ua.reason;
      }
      const kind = ua.targetKind;
      if ((kind === 'enemy' || kind === 'all_enemies') && clickedMember.side === actor?.side) {
        return 'Must target an enemy';
      }
      if ((kind === 'ally' || kind === 'hurt_ally' || kind === 'all_allies') && clickedMember.side !== actor?.side) {
        return 'Must target an ally';
      }
      if (kind === 'hurt_ally' && clickedMember.max !== null && clickedMember.value >= clickedMember.max) {
        return `${clickedMember.name} is at full health`;
      }
      if (ua.range !== null && ua.range !== undefined && actor?.position && clickedMember.position) {
        const dist = gridDistance(actor.position, clickedMember.position, fight.field?.metric);
        if (dist > ua.range) {
          return `Out of range (dist ${dist}, max ${ua.range})`;
        }
      }
      return ua.reason || `Cannot target ${clickedMember.name}`;
    }

    const action = selection.action;
    if (!action) {
      return 'Choose an action first';
    }
    const kind = action.targetKind;
    if ((kind === 'enemy' || kind === 'all_enemies') && clickedMember.side === actor?.side) {
      return 'Must target an enemy';
    }
    if ((kind === 'ally' || kind === 'hurt_ally' || kind === 'all_allies') && clickedMember.side !== actor?.side) {
      return 'Must target an ally';
    }
    if (kind === 'self' && clickedMember.id !== actor?.id) {
      return `${action.name} targets self only`;
    }
    if (kind === 'hurt_ally' && clickedMember.max !== null && clickedMember.value >= clickedMember.max) {
      return `${clickedMember.name} is at full health`;
    }
    if (action.range !== null && action.range !== undefined && actor?.position && clickedMember.position) {
      const dist = gridDistance(actor.position, clickedMember.position, fight.field?.metric);
      if (dist > action.range) {
        return `Out of range (dist ${dist}, max ${action.range})`;
      }
    }
    if (actor?.position && clickedMember.position && fight.field?.terrain?.length) {
      const terrainMap = new Map(fight.field.terrain.map((t) => [`${t.x},${t.y}`, t]));
      if (!hasGridLineOfSight(actor.position, clickedMember.position, terrainMap)) {
        return 'No line of sight';
      }
    }
    return `Invalid target for ${action.name}`;
  };

  const renderOverlay = () => {
    if (!lastView || lastView.screen !== 'combat') {
      root.hidden = true;
      root.removeAttribute('data-rusty-ui-interactive');
      svg.replaceChildren();
      banner.hidden = true;
      return;
    }

    const fight = lastView.fight;
    const w = surface.clientWidth;
    const h = surface.clientHeight;
    if (!fight || w < 10 || h < 10) {
      return;
    }

    root.hidden = false;
    root.setAttribute('data-rusty-ui-interactive', '');
    const aspect = w / h;
    svg.setAttribute('viewBox', `0 0 ${w} ${h}`);
    svg.replaceChildren();

    const members = fight.members ?? [];
    const decision = fight.decision ?? null;
    const actorId = decision?.actorId ?? fight.activeActorId ?? null;
    const actor = members.find((m) => m.id === actorId) ?? null;
    const selection = controls.getSelection();
    const activeAction = selection.action;
    const unavailableAction = selection.unavailableAction;
    const isMoveMode = Boolean(activeAction?.isMove && fight.presentation === 'tactical' && fight.field?.hasGrid);
    const isTargetingMode = Boolean(decision && !fight.done && ((activeAction && !activeAction.isMove) || unavailableAction));

    // Mode banner at top of viewport
    if (fight.done) {
      banner.hidden = true;
    } else if (isMoveMode) {
      banner.hidden = false;
      const preview = selection.previewMove;
      banner.textContent = preview
        ? `MOVE (${activeAction.name}) → (${preview.destination.x}, ${preview.destination.y}) · ${preview.cost} step${preview.cost === 1 ? '' : 's'} — click square again to confirm`
        : `MOVE (${activeAction.name}): Click a highlighted square to preview path, click again to move`;
      banner.dataset.mode = 'move';
    } else if (isTargetingMode) {
      banner.hidden = false;
      const label = activeAction?.name ?? unavailableAction?.name ?? 'Action';
      const rangeNote = (activeAction?.range ?? unavailableAction?.range) !== null && (activeAction?.range ?? unavailableAction?.range) !== undefined
        ? ` · range ${activeAction?.range ?? unavailableAction?.range}`
        : '';
      banner.textContent = unavailableAction
        ? `${label}${rangeNote} (${unavailableAction.reason}) — click a unit to inspect`
        : `TARGET ${label}${rangeNote}: Click a highlighted unit`;
      banner.dataset.mode = unavailableAction ? 'warn' : 'target';
    } else {
      banner.hidden = true;
    }

    const field = fight.field;
    const terrainByKey = new Map((field?.terrain ?? []).map((t) => [`${t.x},${t.y}`, t]));
    const memberByCell = new Map();
    for (const m of members) {
      if (!m.defeated && !m.escaped && m.position) {
        memberByCell.set(`${m.position.x},${m.position.y}`, m);
      }
    }

    // 1. Tactical overhead grid layer (projected 3D floor quads!)
    if (fight.presentation === 'tactical' && field?.hasGrid) {
      const freeMoveMap = new Map();
      if (isMoveMode) {
        for (const mv of activeAction.freeMoves ?? []) {
          freeMoveMap.set(`${mv.destination.x},${mv.destination.y}`, mv);
        }
      }

      const cellPolygonPoints = (cx, cy) => {
        const c0 = projectWorld(cx, 0.01, cy, fight, aspect);
        const c1 = projectWorld(cx + 1, 0.01, cy, fight, aspect);
        const c2 = projectWorld(cx + 1, 0.01, cy + 1, fight, aspect);
        const c3 = projectWorld(cx, 0.01, cy + 1, fight, aspect);
        if (!c0 || !c1 || !c2 || !c3) return null;
        return `${(c0.u * w).toFixed(1)},${(c0.v * h).toFixed(1)} ${(c1.u * w).toFixed(1)},${(c1.v * h).toFixed(1)} ${(c2.u * w).toFixed(1)},${(c2.v * h).toFixed(1)} ${(c3.u * w).toFixed(1)},${(c3.v * h).toFixed(1)}`;
      };

      const gridGroup = svgEl('g', { class: 'gb-grid-layer' });
      const hoverPathGroup = svgEl('g', { class: 'gb-hover-path-layer', 'pointer-events': 'none' });
      const drawPathInto = (container, move, isLocked) => {
        container.replaceChildren();
        if (!move || !actor?.position || !move.path?.length) return;
        const steps = [actor.position, ...move.path];
        const coords = steps
          .map((cell) => projectWorld(cell.x + 0.5, 0.04, cell.y + 0.5, fight, aspect))
          .filter(Boolean)
          .map((p) => [p.u * w, p.v * h]);
        if (coords.length < 2) return;

        container.append(svgEl('polyline', {
          points: coords.map(([px, py]) => `${px.toFixed(1)},${py.toFixed(1)}`).join(' '),
          class: isLocked ? 'gb-move-path gb-move-path-locked' : 'gb-move-path',
        }));
        for (let i = 1; i < coords.length; i++) {
          const [px, py] = coords[i];
          const isLast = i === coords.length - 1;
          container.append(svgEl('circle', {
            cx: px.toFixed(1),
            cy: py.toFixed(1),
            r: isLast ? 7 : 3.5,
            class: isLast ? 'gb-move-end' : 'gb-move-dot',
          }));
        }
        const [ex, ey] = coords[coords.length - 1];
        container.append(svgEl('rect', {
          x: (ex - 36).toFixed(1),
          y: (ey - 30).toFixed(1),
          width: 72,
          height: 18,
          rx: 4,
          class: 'gb-move-pill-bg',
        }));
        container.append(svgEl('text', {
          x: ex.toFixed(1),
          y: (ey - 17).toFixed(1),
          'text-anchor': 'middle',
          class: 'gb-move-pill-text',
        }, isLocked ? `✓ ${move.cost} step${move.cost === 1 ? '' : 's'}` : `${move.cost} step${move.cost === 1 ? '' : 's'}`));
      };

      for (let y = 0; y < field.depth; y++) {
        for (let x = 0; x < field.width; x++) {
          const key = `${x},${y}`;
          const pts = cellPolygonPoints(x, y);
          if (!pts) continue;
          const terrain = terrainByKey.get(key) ?? null;
          const occupant = memberByCell.get(key) ?? null;
          const isActorCell = actor?.position && actor.position.x === x && actor.position.y === y;
          const moveOption = isMoveMode ? freeMoveMap.get(key) ?? null : null;
          const isPreviewDest = Boolean(
            isMoveMode
            && selection.previewMove
            && selection.previewMove.destination.x === x
            && selection.previewMove.destination.y === y,
          );

          let cls = 'gb-grid-cell';
          if (isActorCell) cls += ' gb-grid-actor';
          if (isMoveMode) {
            if (isPreviewDest) cls += ' gb-grid-dest';
            else if (moveOption) cls += ' gb-grid-reach';
            else if (terrain && !terrain.passable) cls += ' gb-grid-blocked';
            else cls += ' gb-grid-dim';
          }

          const poly = svgEl('polygon', {
            points: pts,
            class: cls,
            'data-grid-x': x,
            'data-grid-y': y,
          });

          if (isMoveMode) {
            poly.addEventListener('mouseenter', () => {
              hoverCellKey = key;
              if (moveOption && !isPreviewDest) {
                poly.classList.add('gb-grid-hover');
                if (!selection.previewMove) {
                  drawPathInto(hoverPathGroup, moveOption, false);
                }
              }
            });
            poly.addEventListener('mouseleave', () => {
              poly.classList.remove('gb-grid-hover');
              if (hoverCellKey === key) {
                hoverCellKey = null;
                if (!selection.previewMove) {
                  hoverPathGroup.replaceChildren();
                }
              }
            });
            poly.addEventListener('click', (event) => {
              event.stopPropagation();
              const pt = clientToLocal(event);
              if (moveOption) {
                if (isPreviewDest) {
                  controls.confirmMove(moveOption);
                } else {
                  controls.setPreviewMove(moveOption);
                }
                return;
              }
              if (isActorCell) {
                spawnFloatAt(pt.x, pt.y, 'Already on this square', 'error');
              } else if (terrain && !terrain.passable) {
                spawnFloatAt(pt.x, pt.y, `Blocked by ${terrain.name}`, 'error');
              } else if (occupant) {
                spawnFloatAt(pt.x, pt.y, `Occupied by ${occupant.name}`, 'error');
              } else {
                const dist = actor?.position ? gridDistance(actor.position, { x, y }, field.metric) : null;
                spawnFloatAt(pt.x, pt.y, dist ? `Out of movement range (${dist} away)` : 'Out of movement range', 'error');
              }
            });
          }

          gridGroup.append(poly);
        }
      }
      svg.append(gridGroup);

      if (isMoveMode && selection.previewMove) {
        drawPathInto(hoverPathGroup, selection.previewMove, true);
      } else if (isMoveMode && hoverCellKey && freeMoveMap.has(hoverCellKey)) {
        drawPathInto(hoverPathGroup, freeMoveMap.get(hoverCellKey), false);
      }
      svg.append(hoverPathGroup);
    }

    // 2. Unit hitboxes & target reticles (all 3 combat modes: tactical, first_person, side_view)
    const legalTargetIds = new Set((activeAction?.targets ?? []).map((t) => t.id));
    const selectedTargetIds = new Set(selection.selectedTargets ?? []);
    const unitGroup = svgEl('g', { class: 'gb-unit-layer' });

    for (const member of members) {
      if (member.defeated || member.escaped || !member.worldPos) continue;
      // In first-person mode, only enemies stand in the 3D view.
      if (fight.presentation === 'first_person' && member.side === 0) continue;

      const basePt = projectWorld(member.worldPos.x, member.worldPos.y + 0.04, member.worldPos.z, fight, aspect);
      const midPt = projectWorld(member.worldPos.x, member.worldPos.y + 0.52, member.worldPos.z, fight, aspect);
      const headPt = projectWorld(member.worldPos.x, member.worldPos.y + 1.02, member.worldPos.z, fight, aspect);
      if (!basePt || !midPt || !headPt) continue;

      const cx = midPt.u * w;
      const cy = midPt.v * h;
      const bx = basePt.u * w;
      const by = basePt.v * h;
      const hitH = Math.max(36, Math.abs(by - headPt.v * h) * 1.15);
      const hitW = fight.presentation === 'tactical' ? Math.max(30, hitH * 0.72) : Math.max(54, hitH * 0.65);

      const isActing = member.id === actorId;
      const isLegal = isTargetingMode && !unavailableAction && legalTargetIds.has(member.id);
      const isSelected = selectedTargetIds.has(member.id);

      const g = svgEl('g', {
        class: `gb-unit-hit${isLegal ? ' gb-unit-legal' : ''}${isSelected ? ' gb-unit-selected' : ''}${isActing ? ' gb-unit-acting' : ''}`,
        'data-unit-id': member.id,
        // During tactical Move mode, let grid cells handle clicks cleanly so players can see "Occupied by X" on click.
        'pointer-events': isMoveMode ? 'none' : 'all',
      });

      // Subtle ground ring for active unit or legal target
      if (isActing || isLegal || isSelected) {
        const ringR = fight.presentation === 'tactical' ? Math.max(14, hitW * 0.44) : Math.max(22, hitW * 0.42);
        g.append(svgEl('ellipse', {
          cx: bx.toFixed(1),
          cy: by.toFixed(1),
          rx: ringR.toFixed(1),
          ry: (ringR * 0.48).toFixed(1),
          class: isSelected
            ? 'gb-ring-selected'
            : isLegal
              ? (member.side === 0 ? 'gb-ring-ally' : 'gb-ring-enemy')
              : 'gb-ring-active',
        }));
      }

      // Clickable transparent hitbox over the sprite
      const rect = svgEl('rect', {
        x: (cx - hitW / 2).toFixed(1),
        y: (cy - hitH / 2).toFixed(1),
        width: hitW.toFixed(1),
        height: hitH.toFixed(1),
        rx: 6,
        fill: 'transparent',
        class: 'gb-unit-box',
      });
      rect.append(svgEl('title', {}, `${member.name}${member.value !== null && member.value !== undefined ? ` (${member.value}${member.max !== null && member.max !== undefined ? `/${member.max}` : ''})` : ''}`));
      g.append(rect);

      if (!isMoveMode) {
        g.addEventListener('click', (event) => {
          event.stopPropagation();
          const pt = clientToLocal(event);
          if (fight.done || !decision) {
            return;
          }
          if (isLegal) {
            controls.pickTarget(member.id);
          } else {
            const msg = explainInvalidUnit(fight, selection, actor, member);
            spawnFloatAt(pt.x, pt.y, msg, 'error');
          }
        });
      }

      unitGroup.append(g);
    }
    svg.append(unitGroup);
  };

  const resized = new ResizeObserver(() => renderOverlay());
  resized.observe(surface);

  const render = (view) => {
    lastView = view;
    renderOverlay();
  };

  return {
    render,
    refresh: renderOverlay,
    spawnFloatAt,
    spawnOnMember,
    dispose: () => {
      resized.disconnect();
      root.remove();
    },
  };
}
