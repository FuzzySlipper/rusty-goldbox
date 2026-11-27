(() => {
  const SVG_NS = "http://www.w3.org/2000/svg";

  const state = {
    graph: null,
    activeTab: "overview",
    selectedAreaId: null,
    selectedCell: null, // { areaId, x, y }
    selectedDoor: null, // { areaId, doorId }
    selectedEventId: null,
    mapZoom: 1,
    playerView: false,
    showReachability: true,
    showCoords: true,
    issueSeverity: "all",
    eventSearch: "",
    eventScope: "all",
    eventKind: "all",
    sceneGroup: "all",
    areaSearch: "",
  };

  const KIND_ICON = {
    combat: "⚔",
    menu: "💬",
    teleport: "🌀",
    shop: "🛒",
    temple: "✚",
    training: "🎓",
    rest: "⛺",
    end: "⚑",
    text: "📜",
    branch: "⑂",
    check: "🎲",
    open: "🔑",
    give: "🎁",
    take: "✋",
    treasure: "💰",
    set: "⚙",
    join: "🤝",
    dismiss: "👋",
    experience: "★",
    perception: "👁",
    effect: "✨",
    milestone: "🏆",
    improve: "📈",
    spell_reward: "📖",
  };

  const KIND_COLOR = {
    combat: "#eb5d5d",
    menu: "#64a5f6",
    teleport: "#b184f5",
    shop: "#3dc4ab",
    temple: "#3dc4ab",
    training: "#3dc4ab",
    rest: "#3dc4ab",
    end: "#4ad68c",
    text: "#9aa4b6",
    branch: "#e4a94c",
    check: "#e4a94c",
    open: "#f0a43b",
    give: "#e4a94c",
    treasure: "#e4a94c",
  };

  const FACING_ARROW = { north: "▲", east: "▶", south: "▼", west: "◀" };

  function el(tag, attrs = {}, text = null) {
    const node = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs)) {
      if (v === null || v === undefined) continue;
      if (k === "class") node.className = v;
      else node.setAttribute(k, String(v));
    }
    if (text !== null) node.textContent = text;
    return node;
  }

  function svgEl(tag, attrs = {}, text = null) {
    const node = document.createElementNS(SVG_NS, tag);
    for (const [k, v] of Object.entries(attrs)) {
      if (v === null || v === undefined) continue;
      node.setAttribute(k, String(v));
    }
    if (text !== null) node.textContent = text;
    return node;
  }

  function shortId(id) {
    if (!id) return "";
    const parts = id.split(":");
    return parts[parts.length - 1];
  }

  function assetUrl(qualifiedId) {
    if (!qualifiedId || !qualifiedId.includes(":")) return null;
    const [mod, id] = qualifiedId.split(":", 2);
    return `/api/asset/${encodeURIComponent(mod)}/${encodeURIComponent(id)}`;
  }

  function findAsset(qualifiedId) {
    return state.graph?.assets?.find((a) => a.id === qualifiedId) || null;
  }

  async function loadGraph(reload = false) {
    const status = document.getElementById("reload-status");
    status.textContent = reload ? "reloading…" : "loading…";
    try {
      const res = await fetch(reload ? "/api/reload" : "/api/graph", {
        method: reload ? "POST" : "GET",
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      state.graph = await res.json();
      if (!state.selectedAreaId && state.graph.areas.length > 0) {
        state.selectedAreaId = state.graph.areas[0].id;
      } else if (state.selectedAreaId && !state.graph.areas.some((a) => a.id === state.selectedAreaId)) {
        state.selectedAreaId = state.graph.areas[0]?.id || null;
      }
      populateFilters();
      renderAll();
      const now = new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
      status.textContent = `loaded ${now}`;
    } catch (err) {
      status.textContent = `error: ${err.message}`;
    }
  }

  function switchTab(tab) {
    state.activeTab = tab;
    document.querySelectorAll(".tab").forEach((btn) => {
      btn.classList.toggle("active", btn.dataset.tab === tab);
    });
    document.querySelectorAll(".view").forEach((view) => {
      view.classList.toggle("active", view.id === `view-${tab}`);
    });
    if (tab === "overview") renderOverview();
    if (tab === "maps") renderMaps();
    if (tab === "events") renderEvents();
    if (tab === "issues") renderIssues();
  }

  function populateFilters() {
    const kindSelect = document.getElementById("event-kind-filter");
    const prevKind = state.eventKind;
    kindSelect.innerHTML = `<option value="all">All kinds</option>`;
    const kinds = [...new Set((state.graph.events || []).map((e) => e.kind))].sort();
    for (const k of kinds) {
      kindSelect.appendChild(el("option", { value: k }, `${KIND_ICON[k] || "•"} ${k}`));
    }
    kindSelect.value = kinds.includes(prevKind) ? prevKind : "all";

    const sceneSelect = document.getElementById("scene-group-filter");
    const prevScene = state.sceneGroup;
    sceneSelect.innerHTML = `<option value="all">Show all matching</option>`;
    const scenes = [...new Set((state.graph.events || []).map((e) => e.sceneId).filter(Boolean))].sort();
    for (const s of scenes) {
      sceneSelect.appendChild(el("option", { value: s }, `Scene: ${s}`));
    }
    sceneSelect.value = scenes.includes(prevScene) ? prevScene : "all";
  }

  function renderAll() {
    renderHeader();
    renderOverview();
    renderMaps();
    renderEvents();
    renderIssues();
  }

  function renderHeader() {
    const g = state.graph;
    document.title = `${g.module.title} — Rusty Tales Workbench`;
    document.getElementById("module-title").textContent = g.module.title;
    document.getElementById("module-kind").textContent = g.module.kind;
    document.getElementById("module-version").textContent = `${g.module.id} ${g.module.version}`;
    const validPill = document.getElementById("module-valid");
    validPill.textContent = g.module.isValid ? "valid" : "invalid";
    validPill.className = `pill ${g.module.isValid ? "pill-ok" : "pill-err"}`;
    document.getElementById("module-subtitle").textContent = g.module.location;

    document.getElementById("tab-maps-count").textContent = String(g.counts.areas);
    document.getElementById("tab-events-count").textContent = String(g.counts.events);

    const issuesBadge = document.getElementById("tab-issues-count");
    issuesBadge.textContent = String(g.counts.issues);
    const hasErr = g.issues.some((i) => i.severity === "error");
    const hasWarn = g.issues.some((i) => i.severity === "warning");
    issuesBadge.className = `badge ${hasErr ? "badge-err" : hasWarn ? "badge-warn" : "badge-ok"}`;
  }

  // ==================== TAB 1: FLOW OVERVIEW ====================
  function renderOverview() {
    const g = state.graph;
    if (!g) return;

    document.getElementById("overview-summary").textContent =
      `${g.counts.areas} areas · ${g.navigationLinks.length} links · ${g.counts.events} events (${g.counts.scenes} lowered scenes) · ${g.counts.doors} declared doors · ${g.counts.secretDoors} secret doors`;

    // Render Campaign & Module facts rail
    const facts = document.getElementById("campaign-facts");
    facts.innerHTML = "";
    const addFact = (k, v) => {
      facts.appendChild(el("dt", {}, k));
      facts.appendChild(el("dd", {}, String(v ?? "—")));
    };
    if (g.campaign) {
      addFact("Campaign", `${g.campaign.name} (${g.campaign.localId})`);
      addFact("Start", `${shortId(g.campaign.startArea)} [${g.campaign.startEntry}]`);
      addFact("Intro", g.campaign.introEvent ? shortId(g.campaign.introEvent) : "none");
      addFact("Party", `${g.campaign.partyMin}–${g.campaign.partyMax}${g.campaign.hasStartingParty ? " (fixed)" : ""}`);
      if (g.campaign.combatPresentation) addFact("Combat", g.campaign.combatPresentation);
      if (g.campaign.skin) addFact("Skin", g.campaign.skin);
    } else {
      addFact("Module kind", g.module.kind);
      addFact("Campaign", "none (non-campaign module)");
    }
    addFact("Encounters", g.counts.encounters);
    addFact("Monsters / Items", `${g.counts.monsters} / ${g.counts.items}`);
    addFact("NPCs / Vars", `${g.counts.npcs} / ${g.counts.variables}`);
    addFact("Assets / Figures", `${g.counts.assets} / ${g.counts.figures}`);

    const depList = document.getElementById("dependency-list");
    depList.innerHTML = "";
    if (g.dependencies.length === 0) {
      depList.appendChild(el("li", {}, "No required dependencies."));
    } else {
      for (const dep of g.dependencies) {
        depList.appendChild(el("li", {}, `${dep.id} ${dep.version} (${dep.kind})`));
      }
    }

    const railIssues = document.getElementById("rail-issues-list");
    railIssues.innerHTML = "";
    document.getElementById("rail-issues-count").textContent = String(g.issues.length);
    if (g.issues.length === 0) {
      railIssues.appendChild(el("li", {}, "✓ No validation or reachability issues."));
    } else {
      for (const issue of g.issues.slice(0, 8)) {
        const li = el("li", { class: "clickable" });
        const badge = el("span", { class: `pill pill-${issue.severity === "error" ? "err" : issue.severity === "warning" ? "warn" : "info"}` }, issue.code);
        li.appendChild(badge);
        li.appendChild(el("div", { style: "margin-top:4px;font-size:11px;color:var(--text-dim)" }, issue.message));
        li.addEventListener("click", () => jumpToIssue(issue));
        railIssues.appendChild(li);
      }
    }

    // Build SVG Area Navigation Graph
    const svg = document.getElementById("overview-svg");
    svg.innerHTML = "";

    if (g.areas.length === 0) {
      svg.setAttribute("viewBox", "0 0 640 280");
      svg.appendChild(svgEl("text", { x: 40, y: 80, fill: "#9aa4b6", "font-size": 14 },
        "This module has no area definitions. Inspect events, assets, or diagnostics in the other tabs."));
      return;
    }

    const defs = svgEl("defs");
    const addMarker = (id, color) => {
      const m = svgEl("marker", { id, viewBox: "0 0 10 10", refX: 9, refY: 5, markerWidth: 7, markerHeight: 7, orient: "auto-start-reverse" });
      m.appendChild(svgEl("path", { d: "M 0 1 L 10 5 L 0 9 z", fill: color }));
      defs.appendChild(m);
    };
    addMarker("arrow-tp", "#b184f5");
    addMarker("arrow-key", "#e4a94c");
    svg.appendChild(defs);

    // Compute BFS depth from start area for clean left-to-right layered layout
    const depth = new Map();
    const startArea = g.areas.find((a) => a.isStartArea) || g.areas[0];
    const adj = new Map(g.areas.map((a) => [a.id, []]));
    for (const link of g.navigationLinks) {
      if (link.kind === "teleport" && adj.has(link.fromArea) && link.fromArea !== link.toArea) {
        adj.get(link.fromArea).push(link.toArea);
      }
    }
    depth.set(startArea.id, 0);
    const q = [startArea.id];
    while (q.length > 0) {
      const cur = q.shift();
      const d = depth.get(cur);
      for (const next of adj.get(cur) || []) {
        if (!depth.has(next)) {
          depth.set(next, d + 1);
          q.push(next);
        }
      }
    }
    let maxDepth = Math.max(0, ...depth.values());
    for (const a of g.areas) {
      if (!depth.has(a.id)) depth.set(a.id, maxDepth + 1);
    }

    const columns = new Map();
    for (const a of g.areas) {
      const col = depth.get(a.id);
      if (!columns.has(col)) columns.set(col, []);
      columns.get(col).push(a);
    }

    const CARD_W = 248;
    const CARD_H = 112;
    const COL_GAP = 176;
    const ROW_GAP = 42;
    const PAD = 56;
    const pos = new Map();
    let maxX = 800;
    let maxY = 480;

    for (const [col, list] of [...columns.entries()].sort((a, b) => a[0] - b[0])) {
      list.forEach((area, row) => {
        const x = PAD + col * (CARD_W + COL_GAP);
        const y = PAD + row * (CARD_H + ROW_GAP);
        pos.set(area.id, { x, y, cx: x + CARD_W / 2, cy: y + CARD_H / 2 });
        maxX = Math.max(maxX, x + CARD_W + PAD);
        maxY = Math.max(maxY, y + CARD_H + PAD + 40);
      });
    }

    svg.setAttribute("width", String(maxX));
    svg.setAttribute("height", String(maxY));
    svg.setAttribute("viewBox", `0 0 ${maxX} ${maxY}`);

    // Draw navigation edges first
    const edgeCountBetween = new Map();
    for (const link of g.navigationLinks) {
      const from = pos.get(link.fromArea);
      const to = pos.get(link.toArea);
      if (!from || !to) continue;

      const pairKey = `${link.fromArea}->${link.toArea}:${link.kind}`;
      const dupIdx = edgeCountBetween.get(pairKey) || 0;
      edgeCountBetween.set(pairKey, dupIdx + 1);

      const isKey = link.kind === "key_source";
      const color = isKey ? "#e4a94c" : "#b184f5";
      const marker = isKey ? "url(#arrow-key)" : "url(#arrow-tp)";

      let pathD;
      let labelX;
      let labelY;
      if (link.fromArea === link.toArea) {
        const loopOff = 28 + dupIdx * 24;
        const sx = from.x + CARD_W - 24;
        const sy = from.y;
        pathD = `M ${sx} ${sy} C ${sx + 40} ${sy - loopOff - 20}, ${from.x + 24} ${sy - loopOff - 20}, ${from.x + 24} ${sy}`;
        labelX = from.cx;
        labelY = sy - loopOff - 12;
      } else {
        const goRight = to.cx >= from.cx;
        const sx = goRight ? from.x + CARD_W : from.x;
        const sy = from.cy + (dupIdx - 0.5) * 18;
        const ex = goRight ? to.x : to.x + CARD_W;
        const ey = to.cy + (dupIdx - 0.5) * 18;
        const dx = Math.max(48, Math.abs(ex - sx) * 0.45);
        const curveY = (sy + ey) / 2 + (goRight ? -1 : 1) * (dupIdx * 22 + (isKey ? 28 : 0));
        const c1x = goRight ? sx + dx : sx - dx;
        const c2x = goRight ? ex - dx : ex + dx;
        pathD = `M ${sx} ${sy} C ${c1x} ${curveY}, ${c2x} ${curveY}, ${ex} ${ey}`;
        labelX = (sx + ex) / 2;
        labelY = curveY - 6;
      }

      const edgeGroup = svgEl("g", { class: "svg-edge" });
      const path = svgEl("path", {
        d: pathD,
        fill: "none",
        stroke: color,
        "stroke-width": isKey ? 1.8 : 2.2,
        "stroke-dasharray": isKey ? "6 4" : null,
        "marker-end": marker,
      });
      edgeGroup.appendChild(path);
      const rawLabel = link.kind === "teleport" && link.toEntry
        ? `${link.toEntry} (${shortId(link.viaEvent)})`
        : link.label;
      const pillText = rawLabel.length > 26 ? rawLabel.slice(0, 25) + "…" : rawLabel;
      const pillW = Math.max(44, pillText.length * 6.5 + 14);
      edgeGroup.appendChild(svgEl("rect", {
        x: labelX - pillW / 2,
        y: labelY - 12,
        width: pillW,
        height: 16,
        rx: 4,
        fill: "#0f1217",
        stroke: color,
        "stroke-opacity": "0.45",
        "stroke-width": 1,
      }));
      edgeGroup.appendChild(svgEl("text", {
        x: labelX,
        y: labelY,
        fill: color,
        "font-size": 10.5,
        "font-family": "var(--font-mono)",
        "text-anchor": "middle",
      }, pillText));
      edgeGroup.addEventListener("click", () => {
        if (link.viaEvent) {
          const evt = g.events.find((e) => e.id === link.viaEvent);
          if (evt) inspectEvent(evt);
        }
      });
      svg.appendChild(edgeGroup);
    }

    // Draw Area Cards
    for (const area of g.areas) {
      const p = pos.get(area.id);
      const isDeadEnd = g.areas.length > 1 && area.reachable && area.outgoingAreaCount === 0 && !area.hasEndEvent;
      const borderColor = !area.reachable
        ? "#eb5d5d"
        : area.isStartArea
          ? "#e4a94c"
          : area.hasEndEvent
            ? "#3dc4ab"
            : isDeadEnd
              ? "#f0a43b"
              : "#424f68";

      const grp = svgEl("g", {
        class: `svg-node${state.selectedAreaId === area.id ? " selected" : ""}`,
        transform: `translate(${p.x}, ${p.y})`,
      });

      grp.appendChild(svgEl("rect", {
        class: "node-box",
        width: CARD_W,
        height: CARD_H,
        rx: 10,
        fill: "#1a202c",
        stroke: borderColor,
        "stroke-width": area.isStartArea || !area.reachable ? 2.2 : 1.5,
      }));

      // Top accent bar
      grp.appendChild(svgEl("rect", {
        x: 0,
        y: 0,
        width: CARD_W,
        height: 6,
        rx: 4,
        fill: borderColor,
      }));

      const badge = !area.reachable ? "✕ UNREACHABLE" : area.isStartArea ? "★ START" : area.hasEndEvent ? "⚑ ENDING" : isDeadEnd ? "⚠ DEAD END" : "";
      grp.appendChild(svgEl("text", { x: 14, y: 28, fill: "#ece6d9", "font-size": 13.5, "font-weight": 700 }, area.name));
      grp.appendChild(svgEl("text", { x: 14, y: 45, fill: "#9aa4b6", "font-size": 11, "font-family": "var(--font-mono)" }, `${area.localId} · ${area.width}×${area.height}`));

      if (badge) {
        grp.appendChild(svgEl("text", {
          x: CARD_W - 12,
          y: 26,
          fill: borderColor,
          "font-size": 10,
          "font-weight": 700,
          "text-anchor": "end",
        }, badge));
      }

      const evtCount = area.cells.filter((c) => c.eventId).length;
      const combatCount = area.cells.filter((c) => c.eventKind === "combat").length;
      const unreachCells = area.cells.filter((c) => c.reachability === "unreachable").length;
      grp.appendChild(svgEl("text", { x: 14, y: 72, fill: "#cbd5e1", "font-size": 11 },
        `Entries: ${area.entries.length}   Doors: ${area.doors.length}   Triggers: ${evtCount}`));
      grp.appendChild(svgEl("text", { x: 14, y: 92, fill: unreachCells > 0 ? "#eb5d5d" : "#9aa4b6", "font-size": 11 },
        `${combatCount > 0 ? `⚔ ${combatCount} combat · ` : ""}${unreachCells > 0 ? `${unreachCells} walled cells` : "dbl-click for 2D map"}`));

      grp.addEventListener("click", () => {
        state.selectedAreaId = area.id;
        renderOverview();
        inspectArea(area);
      });
      grp.addEventListener("dblclick", () => {
        state.selectedAreaId = area.id;
        switchTab("maps");
      });
      svg.appendChild(grp);
    }
  }

  // ==================== TAB 2: 2D AREA MAPS ====================
  function renderMaps() {
    const g = state.graph;
    if (!g) return;

    const list = document.getElementById("area-list");
    list.innerHTML = "";
    const query = state.areaSearch.trim().toLowerCase();
    const areas = g.areas.filter((a) => !query || a.name.toLowerCase().includes(query) || a.id.toLowerCase().includes(query));

    for (const area of areas) {
      const li = el("li", { class: `item-row${state.selectedAreaId === area.id ? " active" : ""}` });
      const top = el("div", { class: "item-row-top" });
      const title = `${area.isStartArea ? "★ " : ""}${area.name}`;
      top.appendChild(el("span", { class: "item-row-name" }, title));
      const areaIssues = g.issues.filter((i) => i.areaId === area.id).length;
      if (!area.reachable) {
        top.appendChild(el("span", { class: "badge badge-err" }, "unreach"));
      } else if (areaIssues > 0) {
        top.appendChild(el("span", { class: "badge badge-warn" }, String(areaIssues)));
      }
      li.appendChild(top);
      li.appendChild(el("div", { class: "item-row-sub" }, `${area.width}×${area.height} · ${area.entries.length} entries · ${area.cells.filter((c) => c.eventId).length} triggers`));
      li.addEventListener("click", () => {
        state.selectedAreaId = area.id;
        state.selectedCell = null;
        state.selectedDoor = null;
        renderMaps();
        inspectArea(area);
      });
      list.appendChild(li);
    }

    const area = g.areas.find((a) => a.id === state.selectedAreaId);
    const svg = document.getElementById("map-svg");
    svg.innerHTML = "";
    if (!area) {
      document.getElementById("active-area-title").textContent = "No area selected";
      document.getElementById("active-area-meta").textContent = "";
      return;
    }

    document.getElementById("active-area-title").textContent = `${area.name} (${area.id})`;
    document.getElementById("active-area-meta").textContent =
      `${area.width} × ${area.height} · ${area.entries.length} entries · ${area.doors.length} declared doors${area.wallSet ? ` · wall_set ${shortId(area.wallSet)}` : ""}`;

    const CELL = Math.round(64 * state.mapZoom);
    const PAD = 38;
    const W = PAD * 2 + area.width * CELL;
    const H = PAD * 2 + area.height * CELL;
    svg.setAttribute("width", String(W));
    svg.setAttribute("height", String(H));
    svg.setAttribute("viewBox", `0 0 ${W} ${H}`);

    // Hatch pattern for unreachable cells
    const defs = svgEl("defs");
    const pat = svgEl("pattern", { id: "hatch-unreach", width: 8, height: 8, patternUnits: "userSpaceOnUse", patternTransform: "rotate(45)" });
    pat.appendChild(svgEl("rect", { width: 8, height: 8, fill: "#2e171d" }));
    pat.appendChild(svgEl("line", { x1: 0, y1: 0, x2: 0, y2: 8, stroke: "#6e2934", "stroke-width": 2 }));
    defs.appendChild(pat);
    svg.appendChild(defs);

    // Axis coordinates along top and left
    if (state.showCoords) {
      for (let x = 0; x < area.width; x++) {
        svg.appendChild(svgEl("text", {
          x: PAD + x * CELL + CELL / 2,
          y: PAD - 12,
          fill: "#6f7a8d",
          "font-size": 11,
          "font-family": "var(--font-mono)",
          "text-anchor": "middle",
        }, String(x)));
      }
      for (let y = 0; y < area.height; y++) {
        svg.appendChild(svgEl("text", {
          x: PAD - 14,
          y: PAD + y * CELL + CELL / 2 + 4,
          fill: "#6f7a8d",
          "font-size": 11,
          "font-family": "var(--font-mono)",
          "text-anchor": "middle",
        }, String(y)));
      }
    }

    const entryMap = new Map();
    for (const entry of area.entries) {
      const key = `${entry.x},${entry.y}`;
      if (!entryMap.has(key)) entryMap.set(key, []);
      entryMap.get(key).push(entry);
    }

    // 1. Cells
    for (const cell of area.cells) {
      const cx = PAD + cell.x * CELL;
      const cy = PAD + cell.y * CELL;
      const isSelected = state.selectedCell && state.selectedCell.areaId === area.id && state.selectedCell.x === cell.x && state.selectedCell.y === cell.y;

      let fill = "#1b2230";
      if (state.showReachability) {
        if (cell.reachability === "locked") fill = "#332819";
        else if (cell.reachability === "secret") fill = "#271d3a";
        else if (cell.reachability === "unreachable") fill = "url(#hatch-unreach)";
      }

      const cellG = svgEl("g", { class: `svg-cell${isSelected ? " selected" : ""}` });
      cellG.appendChild(svgEl("rect", {
        class: "cell-rect",
        x: cx + 1,
        y: cy + 1,
        width: CELL - 2,
        height: CELL - 2,
        rx: 3,
        fill,
        stroke: isSelected ? "#e4a94c" : "#242c3b",
        "stroke-width": isSelected ? 2.4 : 1,
      }));

      const entriesHere = entryMap.get(`${cell.x},${cell.y}`) || [];
      if (entriesHere.length > 0) {
        const primary = entriesHere[0];
        const tag = `${primary.isCampaignStart ? "★" : ""}${FACING_ARROW[primary.facing] || ""}`;
        cellG.appendChild(svgEl("text", {
          x: cx + 6,
          y: cy + 15,
          fill: primary.isCampaignStart ? "#e4a94c" : "#3dc4ab",
          "font-size": Math.max(10, Math.round(12 * state.mapZoom)),
          "font-weight": 700,
        }, tag));
      }

      if (cell.propSprite) {
        cellG.appendChild(svgEl("text", {
          x: cx + CELL - 7,
          y: cy + 15,
          fill: "#e4a94c",
          "font-size": Math.max(9, Math.round(11 * state.mapZoom)),
          "text-anchor": "end",
        }, "◆"));
      }

      if (cell.eventId) {
        const icon = KIND_ICON[cell.eventKind] || "!";
        const color = KIND_COLOR[cell.eventKind] || "#ece6d9";
        cellG.appendChild(svgEl("text", {
          x: cx + CELL / 2,
          y: cy + CELL / 2 + 6,
          fill: color,
          "font-size": Math.max(13, Math.round(18 * state.mapZoom)),
          "text-anchor": "middle",
        }, icon));

        if (CELL >= 56) {
          const shortEvt = shortId(cell.eventId);
          const lbl = shortEvt.length > 9 ? shortEvt.slice(0, 8) + "…" : shortEvt;
          cellG.appendChild(svgEl("text", {
            x: cx + CELL / 2,
            y: cy + CELL - 6,
            fill: "#c9d2e3",
            "font-size": 9,
            "font-family": "var(--font-mono)",
            "text-anchor": "middle",
          }, lbl));
        }
      } else if (entriesHere.length > 0 && CELL >= 56) {
        const lbl = entriesHere[0].name.slice(0, 9);
        cellG.appendChild(svgEl("text", {
          x: cx + CELL / 2,
          y: cy + CELL - 6,
          fill: "#3dc4ab",
          "font-size": 9,
          "font-family": "var(--font-mono)",
          "text-anchor": "middle",
        }, lbl));
      }

      const tipLines = [`[${cell.x}, ${cell.y}] (${cell.reachability})`];
      if (cell.zone) tipLines.push(`zone: ${cell.zone}`);
      if (entriesHere.length) tipLines.push(`entry: ${entriesHere.map((e) => `${e.name} (${e.facing})`).join(", ")}`);
      if (cell.eventId) tipLines.push(`event: ${cell.eventId} (${cell.eventKind})`);
      if (cell.propSprite) tipLines.push(`prop: ${cell.propSprite}`);
      cellG.appendChild(svgEl("title", {}, tipLines.join("\n")));

      cellG.addEventListener("click", () => {
        state.selectedCell = { areaId: area.id, x: cell.x, y: cell.y };
        state.selectedDoor = null;
        renderMaps();
        inspectCell(area, cell, entriesHere);
      });
      svg.appendChild(cellG);
    }

    // Index declared doors by canonical edge so edges render locked status & click targets
    const doorByEdge = new Map();
    const canonicalKey = (x, y, facing) => {
      if (facing === "south") return `${x},${y + 1},north`;
      if (facing === "east") return `${x + 1},${y},west`;
      return `${x},${y},${facing}`;
    };
    for (const door of area.doors) {
      doorByEdge.set(canonicalKey(door.x, door.y, door.facing), door);
    }

    // 2. Horizontal edges
    for (let y = 0; y <= area.height; y++) {
      for (let x = 0; x < area.width; x++) {
        let edge = area.horizontalEdges[y][x];
        if (edge === "open") continue;
        if (state.playerView && edge === "secret") edge = "wall";
        const door = doorByEdge.get(`${x},${y},north`) || null;
        drawMapEdge(svg, area, PAD + x * CELL, PAD + y * CELL, PAD + (x + 1) * CELL, PAD + y * CELL, edge, door);
      }
    }

    // 3. Vertical edges
    for (let y = 0; y < area.height; y++) {
      for (let x = 0; x <= area.width; x++) {
        let edge = area.verticalEdges[y][x];
        if (edge === "open") continue;
        if (state.playerView && edge === "secret") edge = "wall";
        const door = doorByEdge.get(`${x},${y},west`) || null;
        drawMapEdge(svg, area, PAD + x * CELL, PAD + y * CELL, PAD + x * CELL, PAD + (y + 1) * CELL, edge, door);
      }
    }
  }

  function drawMapEdge(svg, area, x1, y1, x2, y2, edge, door) {
    if (edge === "wall") {
      svg.appendChild(svgEl("line", {
        x1, y1, x2, y2,
        stroke: "#98a6bf",
        "stroke-width": 4,
        "stroke-linecap": "square",
      }));
      return;
    }

    const g = svgEl("g", { class: door ? "svg-door" : "" });
    const mx = (x1 + x2) / 2;
    const my = (y1 + y2) / 2;

    if (edge === "secret") {
      g.appendChild(svgEl("line", {
        x1, y1, x2, y2,
        stroke: "#b184f5",
        "stroke-width": 4,
        "stroke-dasharray": "6 4",
      }));
      g.appendChild(svgEl("circle", { cx: mx, cy: my, r: 8, fill: "#271d3a", stroke: "#b184f5", "stroke-width": 1.4 }));
      g.appendChild(svgEl("text", { x: mx, y: my + 3.5, fill: "#d9c2ff", "font-size": 9.5, "font-weight": 700, "text-anchor": "middle" }, "S"));
      g.appendChild(svgEl("title", {}, "Secret door"));
    } else if (edge === "door") {
      const isLocked = door?.locked === true;
      const color = isLocked ? "#eb5d5d" : "#d99a4e";
      g.appendChild(svgEl("line", {
        x1, y1, x2, y2,
        stroke: color,
        "stroke-width": 5,
        "stroke-linecap": "round",
      }));
      if (door) {
        g.appendChild(svgEl("circle", { cx: mx, cy: my, r: 9, fill: "#131720", stroke: color, "stroke-width": 1.6 }));
        g.appendChild(svgEl("text", { x: mx, y: my + 3.5, fill: color, "font-size": 10, "text-anchor": "middle" }, isLocked ? "🔒" : "D"));
        g.appendChild(svgEl("title", {}, `Door '${door.id}' (${isLocked ? "locked" : "unlocked"})`));
        g.addEventListener("click", (ev) => {
          ev.stopPropagation();
          state.selectedDoor = { areaId: area.id, doorId: door.id };
          state.selectedCell = null;
          inspectDoor(area, door);
        });
      }
    }

    svg.appendChild(g);
  }

  // ==================== TAB 3: EVENTS & SCENES ====================
  function renderEvents() {
    const g = state.graph;
    if (!g) return;

    const query = state.eventSearch.trim().toLowerCase();
    const filtered = g.events.filter((e) => {
      if (state.eventScope === "unreachable" && e.reachable) return false;
      if (state.eventScope === "scenes" && !e.sceneId) return false;
      if (state.eventKind !== "all" && e.kind !== state.eventKind) return false;
      if (state.sceneGroup !== "all" && e.sceneId !== state.sceneGroup) return false;
      if (query && !e.id.toLowerCase().includes(query) && !e.summary.toLowerCase().includes(query) && !(e.sceneId || "").toLowerCase().includes(query)) return false;
      return true;
    });

    const list = document.getElementById("event-list");
    list.innerHTML = "";
    for (const evt of filtered) {
      const li = el("li", { class: `item-row${state.selectedEventId === evt.id ? " active" : ""}` });
      const top = el("div", { class: "item-row-top" });
      top.appendChild(el("span", { class: "item-row-name" }, `${KIND_ICON[evt.kind] || "•"} ${evt.localId}`));
      if (!evt.reachable) {
        top.appendChild(el("span", { class: "badge badge-err" }, "unreach"));
      } else if (evt.sceneId) {
        top.appendChild(el("span", { class: "badge" }, "scene"));
      }
      li.appendChild(top);
      li.appendChild(el("div", { class: "item-row-sub" }, evt.summary));
      li.addEventListener("click", () => {
        state.selectedEventId = evt.id;
        renderEvents();
        inspectEvent(evt);
      });
      list.appendChild(li);
    }

    document.getElementById("events-graph-title").textContent =
      state.sceneGroup !== "all" ? `Scene Flow: ${state.sceneGroup}` : "Event Flow Graph";
    document.getElementById("events-graph-meta").textContent =
      `Showing ${filtered.length} of ${g.events.length} events`;

    renderEventsSvg(filtered);
  }

  function renderEventsSvg(events) {
    const g = state.graph;
    const svg = document.getElementById("events-svg");
    svg.innerHTML = "";

    if (events.length === 0) {
      svg.setAttribute("viewBox", "0 0 600 240");
      svg.appendChild(svgEl("text", { x: 32, y: 64, fill: "#9aa4b6", "font-size": 13 }, "No events match the current filter."));
      return;
    }

    const visibleIds = new Set(events.map((e) => e.id));
    const edges = g.eventEdges.filter((ed) => visibleIds.has(ed.from) && visibleIds.has(ed.to));

    // Topological / BFS layering for readable left-to-right flow
    const inDeg = new Map(events.map((e) => [e.id, 0]));
    const outMap = new Map(events.map((e) => [e.id, []]));
    for (const ed of edges) {
      inDeg.set(ed.to, (inDeg.get(ed.to) || 0) + 1);
      outMap.get(ed.from).push(ed.to);
    }

    const layer = new Map();
    const queue = [];
    for (const e of events) {
      if (inDeg.get(e.id) === 0 || e.triggers.length > 0) {
        layer.set(e.id, 0);
        queue.push(e.id);
      }
    }
    if (queue.length === 0) {
      layer.set(events[0].id, 0);
      queue.push(events[0].id);
    }
    while (queue.length > 0) {
      const cur = queue.shift();
      const d = layer.get(cur);
      for (const next of outMap.get(cur) || []) {
        if (!layer.has(next)) {
          layer.set(next, d + 1);
          queue.push(next);
        }
      }
    }
    for (const e of events) {
      if (!layer.has(e.id)) layer.set(e.id, 0);
    }

    const cols = new Map();
    for (const e of events) {
      const col = layer.get(e.id);
      if (!cols.has(col)) cols.set(col, []);
      cols.get(col).push(e);
    }

    const NODE_W = 220;
    const NODE_H = 62;
    const COL_GAP = 148;
    const ROW_GAP = 24;
    const PAD = 32;
    const pos = new Map();
    let maxX = 760;
    let maxY = 420;

    for (const [col, list] of [...cols.entries()].sort((a, b) => a[0] - b[0])) {
      list.forEach((evt, row) => {
        const x = PAD + col * (NODE_W + COL_GAP);
        const y = PAD + row * (NODE_H + ROW_GAP);
        pos.set(evt.id, { x, y, cx: x + NODE_W / 2, cy: y + NODE_H / 2 });
        maxX = Math.max(maxX, x + NODE_W + PAD);
        maxY = Math.max(maxY, y + NODE_H + PAD);
      });
    }

    svg.setAttribute("width", String(maxX));
    svg.setAttribute("height", String(maxY));
    svg.setAttribute("viewBox", `0 0 ${maxX} ${maxY}`);

    const defs = svgEl("defs");
    const m = svgEl("marker", { id: "arrow-evt", viewBox: "0 0 10 10", refX: 9, refY: 5, markerWidth: 6, markerHeight: 6, orient: "auto-start-reverse" });
    m.appendChild(svgEl("path", { d: "M 0 1 L 10 5 L 0 9 z", fill: "#6f7a8d" }));
    defs.appendChild(m);
    svg.appendChild(defs);

    for (const ed of edges) {
      const from = pos.get(ed.from);
      const to = pos.get(ed.to);
      if (!from || !to) continue;
      const forward = to.x >= from.x;
      const sx = forward ? from.x + NODE_W : from.x;
      const sy = from.cy;
      const ex = forward ? to.x : to.x + NODE_W;
      const ey = to.cy;
      const dx = Math.max(36, Math.abs(ex - sx) * 0.4);
      const pathD = forward
        ? `M ${sx} ${sy} C ${sx + dx} ${sy}, ${ex - dx} ${ey}, ${ex} ${ey}`
        : `M ${sx} ${sy} C ${sx - dx} ${sy - 36}, ${ex + dx} ${ey - 36}, ${ex} ${ey}`;
      const edgeColor = ed.kind === "win" || ed.kind === "success" ? "#3dc4ab"
        : ed.kind === "lose" || ed.kind === "failure" || ed.kind === "refused" ? "#eb5d5d"
        : ed.kind === "choice" ? "#64a5f6"
        : "#6f7a8d";
      svg.appendChild(svgEl("path", {
        d: pathD,
        fill: "none",
        stroke: edgeColor,
        "stroke-width": 1.6,
        "marker-end": "url(#arrow-evt)",
      }));
      if (ed.label && ed.label !== "next") {
        const lbl = ed.label.length > 20 ? ed.label.slice(0, 19) + "…" : ed.label;
        const lx = (sx + ex) / 2;
        const ly = (sy + ey) / 2 - 4;
        const lw = Math.max(36, lbl.length * 6 + 12);
        svg.appendChild(svgEl("rect", {
          x: lx - lw / 2,
          y: ly - 10.5,
          width: lw,
          height: 14,
          rx: 3,
          fill: "#0f1217",
          "fill-opacity": "0.92",
        }));
        svg.appendChild(svgEl("text", {
          x: lx,
          y: ly,
          fill: edgeColor,
          "font-size": 9.5,
          "font-family": "var(--font-mono)",
          "text-anchor": "middle",
        }, lbl));
      }
    }

    for (const evt of events) {
      const p = pos.get(evt.id);
      const accent = !evt.reachable ? "#eb5d5d" : (KIND_COLOR[evt.kind] || "#6f7a8d");
      const grp = svgEl("g", {
        class: `svg-node${state.selectedEventId === evt.id ? " selected" : ""}`,
        transform: `translate(${p.x}, ${p.y})`,
      });
      grp.appendChild(svgEl("rect", {
        class: "node-box",
        width: NODE_W,
        height: NODE_H,
        rx: 7,
        fill: "#1a202c",
        stroke: state.selectedEventId === evt.id ? "#e4a94c" : accent,
        "stroke-width": state.selectedEventId === evt.id ? 2.4 : 1.4,
      }));
      grp.appendChild(svgEl("rect", { x: 0, y: 0, width: 5, height: NODE_H, rx: 3, fill: accent }));
      grp.appendChild(svgEl("text", { x: 12, y: 21, fill: "#ece6d9", "font-size": 11.5, "font-weight": 700 },
        `${KIND_ICON[evt.kind] || "•"} ${evt.localId.length > 24 ? evt.localId.slice(0, 23) + "…" : evt.localId}`));
      grp.appendChild(svgEl("text", { x: 12, y: 39, fill: "#9aa4b6", "font-size": 10 },
        evt.summary.length > 31 ? evt.summary.slice(0, 30) + "…" : evt.summary));
      const footer = evt.sceneId ? `scene: ${evt.sceneId}/${evt.sceneNode}` : evt.kind;
      grp.appendChild(svgEl("text", { x: 12, y: 54, fill: "#6f7a8d", "font-size": 9.5, "font-family": "var(--font-mono)" },
        footer.length > 32 ? footer.slice(0, 31) + "…" : footer));

      grp.addEventListener("click", () => {
        state.selectedEventId = evt.id;
        renderEvents();
        inspectEvent(evt);
      });
      svg.appendChild(grp);
    }
  }

  // ==================== TAB 4: DIAGNOSTICS ====================
  function renderIssues() {
    const g = state.graph;
    if (!g) return;

    const filtered = g.issues.filter((i) => state.issueSeverity === "all" || i.severity === state.issueSeverity);
    const errCount = g.issues.filter((i) => i.severity === "error").length;
    const warnCount = g.issues.filter((i) => i.severity === "warning").length;
    const infoCount = g.issues.filter((i) => i.severity === "info").length;
    document.getElementById("issues-summary").textContent =
      `${errCount} errors · ${warnCount} warnings · ${infoCount} info`;

    const tbody = document.getElementById("issues-tbody");
    tbody.innerHTML = "";
    if (filtered.length === 0) {
      const tr = el("tr");
      tr.appendChild(el("td", { colspan: "5", style: "color:var(--teal);text-align:center;padding:24px" }, "✓ No issues in this filter."));
      tbody.appendChild(tr);
      return;
    }

    for (const issue of filtered) {
      const tr = el("tr");
      const sevClass = issue.severity === "error" ? "pill-err" : issue.severity === "warning" ? "pill-warn" : "pill-info";
      const tdSev = el("td");
      tdSev.appendChild(el("span", { class: `pill ${sevClass}` }, issue.severity));
      tr.appendChild(tdSev);
      tr.appendChild(el("td", { style: "font-family:var(--font-mono);color:var(--gold)" }, issue.code));
      tr.appendChild(el("td", {}, issue.message));
      const loc = [issue.file, issue.jsonPath].filter(Boolean).join(" ");
      tr.appendChild(el("td", { style: "font-family:var(--font-mono);font-size:11px;color:var(--text-dim)" }, loc || "—"));
      const tdAct = el("td");
      if (issue.areaId || issue.eventId) {
        const btn = el("button", { class: "btn btn-sm" }, "Jump →");
        btn.addEventListener("click", () => jumpToIssue(issue));
        tdAct.appendChild(btn);
      } else {
        tdAct.textContent = "—";
      }
      tr.appendChild(tdAct);
      tbody.appendChild(tr);
    }
  }

  function jumpToIssue(issue) {
    const g = state.graph;
    if (issue.areaId) {
      const area = g.areas.find((a) => a.id === issue.areaId);
      if (area) {
        state.selectedAreaId = area.id;
        switchTab("maps");
        if (issue.cell && issue.cell.length === 2) {
          const cell = area.cells.find((c) => c.x === issue.cell[0] && c.y === issue.cell[1]);
          if (cell) {
            state.selectedCell = { areaId: area.id, x: cell.x, y: cell.y };
            renderMaps();
            inspectCell(area, cell, area.entries.filter((e) => e.x === cell.x && e.y === cell.y));
            return;
          }
        }
        inspectArea(area);
        return;
      }
    }
    if (issue.eventId) {
      const evt = g.events.find((e) => e.id === issue.eventId);
      if (evt) {
        state.selectedEventId = evt.id;
        switchTab("events");
        inspectEvent(evt);
      }
    }
  }

  // ==================== INSPECTOR DRAWER ====================
  function setInspector(kind, title, sections) {
    document.getElementById("inspector-kind").textContent = kind;
    document.getElementById("inspector-title").textContent = title;
    const body = document.getElementById("inspector-body");
    body.innerHTML = "";
    for (const sec of sections) body.appendChild(sec);
  }

  function makeKvSection(title, rows) {
    const sec = el("div", { class: "insp-section" });
    sec.appendChild(el("h4", {}, title));
    const dl = el("dl", { class: "kv-list" });
    for (const [k, v] of rows) {
      if (v === null || v === undefined || v === "") continue;
      dl.appendChild(el("dt", {}, k));
      dl.appendChild(el("dd", {}, String(v)));
    }
    sec.appendChild(dl);
    return sec;
  }

  function makeAssetPreviewSection(label, qualifiedAssetId) {
    const asset = findAsset(qualifiedAssetId);
    const url = assetUrl(qualifiedAssetId);
    if (!url) return null;
    const sec = el("div", { class: "insp-section" });
    sec.appendChild(el("h4", {}, `${label}: ${qualifiedAssetId}`));
    const wrap = el("div", { class: "asset-preview" });
    if (asset?.media === "audio") {
      const audio = el("audio", { controls: "controls", src: url });
      wrap.appendChild(audio);
    } else {
      const img = el("img", { src: url, alt: qualifiedAssetId });
      if (asset?.sampling === "linear") img.style.imageRendering = "auto";
      wrap.appendChild(img);
    }
    if (asset) {
      wrap.appendChild(el("div", { style: "font-size:11px;color:var(--text-dim);font-family:var(--font-mono)" },
        `${asset.file} · ${asset.media} · ${asset.sampling}`));
    }
    sec.appendChild(wrap);
    return sec;
  }

  function makeJsonSection(qualifiedId, defType) {
    const sec = el("div", { class: "insp-section" });
    sec.appendChild(el("h4", {}, "Definition JSON"));
    const pre = el("pre", { class: "json-block" }, "Loading…");
    sec.appendChild(pre);
    fetch(`/api/definition?id=${encodeURIComponent(qualifiedId)}${defType ? `&type=${encodeURIComponent(defType)}` : ""}`)
      .then((r) => r.json())
      .then((data) => {
        if (!data.ok) {
          pre.textContent = data.error || "Not found";
          return;
        }
        pre.textContent = JSON.stringify(data.json, null, 2);
      })
      .catch((err) => {
        pre.textContent = `Error loading definition: ${err.message}`;
      });
    return sec;
  }

  function inspectArea(area) {
    const sections = [];
    const actionsSec = el("div", { class: "insp-section" });
    actionsSec.appendChild(el("h4", {}, "Actions"));
    const actRow = el("div", { class: "insp-actions" });
    const openMapBtn = el("button", { class: "btn btn-primary btn-sm" }, "Open 2D Map →");
    openMapBtn.addEventListener("click", () => {
      state.selectedAreaId = area.id;
      switchTab("maps");
    });
    actRow.appendChild(openMapBtn);
    actionsSec.appendChild(actRow);
    sections.push(actionsSec);

    sections.push(makeKvSection("Area Details", [
      ["ID", area.id],
      ["Dimensions", `${area.width} × ${area.height}`],
      ["Reachable", area.reachable ? "Yes" : "No (unreachable from start)"],
      ["Entries", area.entries.map((e) => `${e.name} [${e.x},${e.y}]`).join(", ")],
      ["Wall Set", area.wallSet || "none"],
      ["File", area.file],
    ]));

    if (area.wallSet) {
      const preview = makeAssetPreviewSection("Wall Set", area.wallSet);
      if (preview) sections.push(preview);
    }

    sections.push(makeJsonSection(area.id, "area"));
    setInspector("AREA", area.name, sections);
  }

  function inspectCell(area, cell, entriesHere) {
    const sections = [];
    sections.push(makeKvSection("Cell Metadata", [
      ["Area", `${area.name} (${area.localId})`],
      ["Coords", `[${cell.x}, ${cell.y}]`],
      ["Reachability", cell.reachability],
      ["Zone", cell.zone],
      ["Entries", entriesHere.map((e) => `${e.name} (${e.facing}${e.isCampaignStart ? ", campaign start" : ""})`).join(", ")],
      ["Trigger", cell.eventId ? `${cell.eventId} (${cell.eventKind})` : "none"],
      ["Trigger Flags", cell.eventId ? `${cell.once ? "once" : "repeatable"}${cell.facing ? `, facing ${cell.facing}` : ""}` : null],
      ["Prop", cell.propSprite ? `${cell.propSprite}${cell.propHidden ? ` (hidden: ${cell.propHidden})` : ""}` : null],
      ["Backdrop", cell.backdrop],
    ]));

    if (cell.eventId) {
      const evt = state.graph.events.find((e) => e.id === cell.eventId);
      const actSec = el("div", { class: "insp-section" });
      actSec.appendChild(el("h4", {}, "Triggered Event"));
      actSec.appendChild(el("div", { style: "margin-bottom:8px;font-size:12px" }, cell.eventSummary || cell.eventId));
      const row = el("div", { class: "insp-actions" });
      if (evt) {
        const btnEvt = el("button", { class: "btn btn-primary btn-sm" }, "Inspect Event →");
        btnEvt.addEventListener("click", () => {
          state.selectedEventId = evt.id;
          inspectEvent(evt);
        });
        row.appendChild(btnEvt);
        if (evt.teleportArea) {
          const btnTp = el("button", { class: "btn btn-sm" }, `Jump to ${shortId(evt.teleportArea)} →`);
          btnTp.addEventListener("click", () => {
            state.selectedAreaId = evt.teleportArea;
            state.selectedCell = null;
            renderMaps();
            const target = state.graph.areas.find((a) => a.id === evt.teleportArea);
            if (target) inspectArea(target);
          });
          row.appendChild(btnTp);
        }
      }
      actSec.appendChild(row);
      sections.push(actSec);
    }

    if (cell.backdrop) {
      const p = makeAssetPreviewSection("Backdrop", cell.backdrop);
      if (p) sections.push(p);
    }
    if (cell.propSprite) {
      const p = makeAssetPreviewSection("Prop Sprite", cell.propSprite);
      if (p) sections.push(p);
    }

    setInspector("MAP CELL", `${area.name} [${cell.x}, ${cell.y}]`, sections);
  }

  function inspectDoor(area, door) {
    const sections = [];
    sections.push(makeKvSection("Door Details", [
      ["Door ID", door.id],
      ["Area", `${area.name} (${area.localId})`],
      ["Edge", `[${door.x}, ${door.y}] facing ${door.facing}`],
      ["Status", door.locked ? "Locked" : "Unlocked"],
      ["Reachable", door.reachable ? "Yes" : "No"],
      ["Key Item", door.keyItem || "none"],
      ["Key Sources", door.keySourceEvents.length ? door.keySourceEvents.join(", ") : "none"],
      ["Pick Check", door.pickCheck || "none"],
      ["Force Check", door.forceCheck || "none"],
      ["Unlock Event", door.unlockEvent || "none"],
      ["Opened By", door.openedByEvents.length ? door.openedByEvents.join(", ") : "none"],
    ]));
    setInspector("DOOR", door.id, sections);
  }

  function inspectEvent(evt) {
    const sections = [];
    const outgoing = state.graph.eventEdges.filter((e) => e.from === evt.id);

    sections.push(makeKvSection("Event Details", [
      ["ID", evt.id],
      ["Kind", `${KIND_ICON[evt.kind] || "•"} ${evt.kind}`],
      ["Reachable", evt.reachable ? "Yes" : "No (unreachable)"],
      ["Scene Origin", evt.sceneId ? `${evt.sceneId} → node '${evt.sceneNode}'` : null],
      ["Encounter", evt.encounter],
      ["Teleport", evt.teleportArea ? `${evt.teleportArea} [${evt.teleportEntry}]` : null],
      ["Open Door", evt.openDoor],
      ["File", `${evt.file} (${evt.jsonPath})`],
    ]));

    if (evt.triggers.length > 0 || outgoing.length > 0 || evt.teleportArea) {
      const linkSec = el("div", { class: "insp-section" });
      linkSec.appendChild(el("h4", {}, "Connections"));
      const actRow = el("div", { class: "insp-actions" });

      for (const trig of evt.triggers) {
        if (trig.areaId && trig.x !== null && trig.y !== null) {
          const b = el("button", { class: "btn btn-sm" }, `Map: ${shortId(trig.areaId)} [${trig.x},${trig.y}]`);
          b.addEventListener("click", () => {
            const area = state.graph.areas.find((a) => a.id === trig.areaId);
            if (!area) return;
            state.selectedAreaId = area.id;
            state.selectedCell = { areaId: area.id, x: trig.x, y: trig.y };
            switchTab("maps");
            const cell = area.cells.find((c) => c.x === trig.x && c.y === trig.y);
            if (cell) inspectCell(area, cell, area.entries.filter((e) => e.x === cell.x && e.y === cell.y));
          });
          actRow.appendChild(b);
        }
      }

      if (evt.teleportArea) {
        const b = el("button", { class: "btn btn-primary btn-sm" }, `Destination: ${shortId(evt.teleportArea)} →`);
        b.addEventListener("click", () => {
          state.selectedAreaId = evt.teleportArea;
          state.selectedCell = null;
          switchTab("maps");
          const area = state.graph.areas.find((a) => a.id === evt.teleportArea);
          if (area) inspectArea(area);
        });
        actRow.appendChild(b);
      }

      for (const edge of outgoing) {
        const target = state.graph.events.find((e) => e.id === edge.to);
        if (!target) continue;
        const b = el("button", { class: "btn btn-sm" }, `${edge.label} → ${target.localId}`);
        b.addEventListener("click", () => {
          state.selectedEventId = target.id;
          renderEvents();
          inspectEvent(target);
        });
        actRow.appendChild(b);
      }

      linkSec.appendChild(actRow);
      sections.push(linkSec);
    }

    if (evt.picture) {
      const p = makeAssetPreviewSection("Picture", evt.picture);
      if (p) sections.push(p);
    }
    if (evt.sound) {
      const p = makeAssetPreviewSection("Sound", evt.sound);
      if (p) sections.push(p);
    }
    if (evt.music) {
      const p = makeAssetPreviewSection("Music", evt.music);
      if (p) sections.push(p);
    }

    sections.push(makeJsonSection(evt.id, "event"));
    setInspector("EVENT", evt.localId, sections);
  }

  // ==================== WIRE UP CONTROLS ====================
  document.querySelectorAll(".tab").forEach((btn) => {
    btn.addEventListener("click", () => switchTab(btn.dataset.tab));
  });

  document.getElementById("btn-reload").addEventListener("click", () => loadGraph(true));

  document.getElementById("area-search").addEventListener("input", (e) => {
    state.areaSearch = e.target.value;
    renderMaps();
  });

  document.getElementById("chk-player-view").addEventListener("change", (e) => {
    state.playerView = e.target.checked;
    renderMaps();
  });
  document.getElementById("chk-reachability").addEventListener("change", (e) => {
    state.showReachability = e.target.checked;
    renderMaps();
  });
  document.getElementById("chk-coords").addEventListener("change", (e) => {
    state.showCoords = e.target.checked;
    renderMaps();
  });

  const updateZoom = (next) => {
    state.mapZoom = Math.min(2.2, Math.max(0.5, Number(next.toFixed(2))));
    document.getElementById("btn-map-zoom-reset").textContent = `${Math.round(state.mapZoom * 100)}%`;
    renderMaps();
  };
  document.getElementById("btn-map-zoom-out").addEventListener("click", () => updateZoom(state.mapZoom - 0.15));
  document.getElementById("btn-map-zoom-reset").addEventListener("click", () => updateZoom(1));
  document.getElementById("btn-map-zoom-in").addEventListener("click", () => updateZoom(state.mapZoom + 0.15));

  document.getElementById("event-search").addEventListener("input", (e) => {
    state.eventSearch = e.target.value;
    renderEvents();
  });
  document.getElementById("event-scope-filter").addEventListener("change", (e) => {
    state.eventScope = e.target.value;
    renderEvents();
  });
  document.getElementById("event-kind-filter").addEventListener("change", (e) => {
    state.eventKind = e.target.value;
    renderEvents();
  });
  document.getElementById("scene-group-filter").addEventListener("change", (e) => {
    state.sceneGroup = e.target.value;
    renderEvents();
  });

  document.querySelectorAll("#issue-filter-pills .pill-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      state.issueSeverity = btn.dataset.severity;
      document.querySelectorAll("#issue-filter-pills .pill-btn").forEach((b) => b.classList.toggle("active", b === btn));
      renderIssues();
    });
  });

  loadGraph(false);
})();
