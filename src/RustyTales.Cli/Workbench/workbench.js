(() => {
  const SVG_NS = "http://www.w3.org/2000/svg";

  const state = {
    graph: null,
    reviews: [],
    reviewCounts: { total: 0, open: 0, needsFix: 0, question: 0, approved: 0, stale: 0, resolved: 0 },
    reviewStorePath: "",
    reviewFilter: "all",
    activeTab: "overview",
    selectedAreaId: null,
    selectedCell: null, // { areaId, x, y }
    selectedDoor: null, // { areaId, doorId }
    selectedEventId: null,
    lastInspector: null, // () => void to refresh inspector after pin edits
    mapZoom: 1,
    showTilemap: true,
    playerView: false,
    showReachability: true,
    showCoords: true,
    sheetImages: new Map(),
    issueSeverity: "all",
    eventSearch: "",
    eventScope: "all",
    eventKind: "all",
    sceneGroup: "all",
    areaSearch: "",
    assetCategory: "all",
    assetReviewFilter: "all",
    assetSearch: "",
    assetUnusedOnly: false,
    selectedGalleryKey: null, // "asset:<id>" or "skin:<id>"
    spriteAnimPlayers: [],
    embedMode: new URLSearchParams(window.location.search).get("embed") === "1",
    liveState: null, // { screen, module, campaign, area, areaName, x, y, facing, activeEvent, activeActor }
    playtest: {
      status: null,
      busy: false,
      iframeUrl: null,
      pollTimer: null,
    },
    puppets: {
      status: null,
      busy: false,
      iframeUrl: null,
    },
  };

  if (state.embedMode) {
    document.body.classList.add("embed-mode");
  }

  function postToGame(payload) {
    if (window.parent && window.parent !== window) {
      window.parent.postMessage(payload, "*");
    }
  }

  function applyLiveState(live) {
    if (!live || typeof live !== "object") return;
    const prevArea = state.liveState?.area;
    state.liveState = live;
    if (live.area && live.area !== prevArea && state.graph?.areas?.some((a) => a.id === live.area)) {
      state.selectedAreaId = live.area;
    }
    renderLiveBadge();
    if (state.graph) {
      if (state.activeTab === "overview") renderOverview();
      if (state.activeTab === "maps") renderMaps();
      if (state.activeTab === "events") renderEvents();
    }
  }

  function renderLiveBadge() {
    const actions = document.querySelector(".topbar-actions");
    if (!actions) return;
    let badge = document.getElementById("live-game-pill");
    const live = state.liveState;
    if (!live || !live.area) {
      if (badge) badge.remove();
      return;
    }
    if (!badge) {
      badge = el("span", { id: "live-game-pill", class: "live-pill", title: "Live connected Rusty Tales game state" });
      actions.prepend(badge);
    }
    const pos = live.x !== null && live.x !== undefined ? ` [${live.x},${live.y}]` : "";
    const extra = live.activeActor ? ` · ${live.activeActor}` : live.activeEvent ? ` · ${shortId(live.activeEvent)}` : "";
    badge.innerHTML = "";
    badge.appendChild(el("i", { class: "live-dot" }));
    badge.appendChild(el("span", {}, `LIVE ${shortId(live.area)}${pos}${extra}`));
  }

  window.addEventListener("message", (ev) => {
    if (!ev.data || typeof ev.data !== "object") return;
    if (ev.data.type === "tales.liveState") {
      applyLiveState(ev.data);
    } else if (ev.data.type === "tales.refreshReviews") {
      loadReviews().then(() => {
        if (state.graph) renderAll();
        if (state.lastInspector) state.lastInspector();
      });
    }
  });

  const PIN_META = {
    "needs-fix": { glyph: "✗", color: "#eb5d5d", pill: "pill-err", label: "Needs Fix" },
    question: { glyph: "?", color: "#64a5f6", pill: "pill-info", label: "Question" },
    approved: { glyph: "✓", color: "#3dc4ab", pill: "pill-ok", label: "Approved" },
    stale: { glyph: "⚠", color: "#f0a43b", pill: "pill-warn", label: "Stale" },
    resolved: { glyph: "✔", color: "#9aa4b6", pill: "pill-resolved", label: "Resolved" },
  };

  function pinStyle(pin) {
    if (!pin) return null;
    if (pin.stale) return PIN_META.stale;
    return PIN_META[pin.status] || PIN_META["needs-fix"];
  }

  function findPin(targetKind, target) {
    return state.reviews.find((p) => p.targetKind === targetKind && p.target === target) || null;
  }

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

  function findTileset(qualifiedId) {
    return (state.graph?.tilesets || []).find((t) => t.id === qualifiedId) || null;
  }

  function findEncounter(qualifiedId) {
    return (state.graph?.encounters || []).find((e) => e.id === qualifiedId) || null;
  }

  const BLOB47_MASKS = [
    0, 1, 4, 5, 7, 16, 17, 20, 21, 23, 28, 29, 31, 64, 65, 68,
    69, 71, 80, 81, 84, 85, 87, 92, 93, 95, 112, 113, 116, 117, 119, 124,
    125, 127, 193, 197, 199, 209, 213, 215, 221, 223, 241, 245, 247, 253, 255,
  ];
  const BLOB47_INDEX = new Map(BLOB47_MASKS.map((m, i) => [m, i]));

  function loadSheetImage(assetId, onLoad) {
    if (!assetId) return null;
    const cached = state.sheetImages.get(assetId);
    if (cached) {
      if (cached.complete && cached.naturalWidth > 0) return cached;
      if (onLoad) cached.addEventListener("load", onLoad, { once: true });
      return null;
    }
    const url = assetUrl(assetId);
    if (!url) return null;
    const img = new Image();
    state.sheetImages.set(assetId, img);
    if (onLoad) img.addEventListener("load", onLoad, { once: true });
    img.src = url;
    return null;
  }

  function drawSheetFrame(ctx, img, ts, frameIndex, dx, dy, dw, dh) {
    if (frameIndex === null || frameIndex === undefined || frameIndex < 0) return;
    if (!img || !img.complete || img.naturalWidth <= 0) return;
    const cols = Math.max(1, ts.columns || 1);
    const sx = (frameIndex % cols) * ts.frameWidth;
    const sy = Math.floor(frameIndex / cols) * ts.frameHeight;
    ctx.drawImage(img, sx, sy, ts.frameWidth, ts.frameHeight, dx, dy, dw, dh);
  }

  function drawTilemapToCanvas(canvas, tilemap, ts, cellPx) {
    if (!canvas || !tilemap || !ts) return;
    const w = tilemap.width * cellPx;
    const h = tilemap.height * cellPx;
    canvas.width = w;
    canvas.height = h;
    const sheetAsset = findAsset(ts.sheetAsset);
    const linear = sheetAsset?.sampling === "linear";
    canvas.classList.toggle("sampling-linear", linear);
    const ctx = canvas.getContext("2d");
    ctx.imageSmoothingEnabled = linear;
    ctx.clearRect(0, 0, w, h);

    const glyphByChar = new Map((ts.tiles || []).map((t) => [t.glyph, t]));
    const img = loadSheetImage(ts.sheetAsset, () => drawTilemapToCanvas(canvas, tilemap, ts, cellPx));

    for (let li = 0; li < (tilemap.layers || []).length; li++) {
      const layer = tilemap.layers[li];
      for (let y = 0; y < tilemap.height; y++) {
        for (let x = 0; x < tilemap.width; x++) {
          const frame = layer.frames?.[y]?.[x];
          if (frame === undefined || frame < 0) continue;
          if (img) {
            drawSheetFrame(ctx, img, ts, frame, x * cellPx, y * cellPx, cellPx, cellPx);
          } else if (li === 0) {
            const ch = layer.rows?.[y]?.[x];
            const gDef = glyphByChar.get(ch);
            ctx.fillStyle = gDef?.color || "#222b3a";
            ctx.fillRect(x * cellPx, y * cellPx, cellPx, cellPx);
          }
        }
      }
    }
  }

  function resolveAutotileSample(tileDef, ts, nb) {
    const mode = tileDef.autotileMode;
    const maxF = Math.max(0, (ts.frameCount || 1) - 1);
    const clamp = (f) => Math.max(0, Math.min(maxF, f));
    const frames = tileDef.autotileFrames || {};
    if (mode === "4bit") {
      const mask = (nb.n ? 1 : 0) | (nb.e ? 2 : 0) | (nb.s ? 4 : 0) | (nb.w ? 8 : 0);
      const f = frames[String(mask)] ?? (tileDef.autotileBase !== null && tileDef.autotileBase !== undefined ? tileDef.autotileBase + mask : (tileDef.tile ?? 0));
      return { frame: clamp(f), mask, label: `4bit mask=${mask} (${[nb.n && "N", nb.e && "E", nb.s && "S", nb.w && "W"].filter(Boolean).join("") || "none"})` };
    }
    if (mode === "wall_top") {
      const mask = (nb.n ? 1 : 0) | (nb.s ? 2 : 0);
      let slot = "top";
      let f;
      if (!nb.s) {
        slot = "front";
        f = frames.front ?? (tileDef.autotileBase ?? tileDef.tile ?? 0);
      } else if (!nb.n && frames.cap !== undefined) {
        slot = "cap";
        f = frames.cap;
      } else {
        slot = "top";
        f = frames.top ?? (tileDef.autotileBase !== null && tileDef.autotileBase !== undefined ? tileDef.autotileBase + 1 : (tileDef.tile ?? 0));
      }
      return { frame: clamp(f), mask, label: `wall_top (${slot})` };
    }
    if (mode === "blob47") {
      const mask = (nb.n ? 1 : 0)
        | (nb.n && nb.e && nb.ne ? 2 : 0)
        | (nb.e ? 4 : 0)
        | (nb.s && nb.e && nb.se ? 8 : 0)
        | (nb.s ? 16 : 0)
        | (nb.s && nb.w && nb.sw ? 32 : 0)
        | (nb.w ? 64 : 0)
        | (nb.n && nb.w && nb.nw ? 128 : 0);
      const bIdx = BLOB47_INDEX.get(mask) ?? 0;
      const f = frames[String(mask)] ?? frames[`b${bIdx}`] ?? (tileDef.autotileBase !== null && tileDef.autotileBase !== undefined ? tileDef.autotileBase + bIdx : (tileDef.tile ?? 0));
      return { frame: clamp(f), mask, label: `blob47 mask=${mask} (idx ${bIdx})` };
    }
    const f = tileDef.variants?.length ? tileDef.variants[0] : (tileDef.tile ?? 0);
    return { frame: clamp(f), mask: 0, label: tileDef.variants?.length ? `variants (${tileDef.variants.length})` : "static" };
  }

  async function loadReviews() {
    try {
      const res = await fetch("/api/reviews");
      if (!res.ok) return;
      const data = await res.json();
      state.reviews = data.pins || [];
      state.reviewCounts = data.counts || state.reviewCounts;
      state.reviewStorePath = data.storePath || "";
    } catch {
      // keep existing review state
    }
  }

  async function savePin(targetKind, target, status, note) {
    const res = await fetch("/api/reviews", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ targetKind, target, status, note: note || "", author: "human" }),
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    await loadReviews();
    renderAll();
    if (state.lastInspector) state.lastInspector();
  }

  async function resolvePin(id, resolution = "") {
    const res = await fetch(`/api/reviews/${encodeURIComponent(id)}/resolve`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ resolution }),
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    await loadReviews();
    renderAll();
    if (state.lastInspector) state.lastInspector();
  }

  async function deletePin(id) {
    const res = await fetch(`/api/reviews/${encodeURIComponent(id)}`, { method: "DELETE" });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    await loadReviews();
    renderAll();
    if (state.lastInspector) state.lastInspector();
  }

  async function clearResolvedPins() {
    const res = await fetch("/api/reviews/clear", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ status: "resolved" }),
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    await loadReviews();
    renderAll();
    if (state.lastInspector) state.lastInspector();
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
      await loadReviews();
      if (!state.embedMode) {
        try {
          const liveRes = await fetch("/api/live-state");
          if (liveRes.ok) {
            const live = await liveRes.json();
            if (live && live.connected && live.area) applyLiveState(live);
          }
        } catch {}
      }
      if (!state.selectedAreaId && state.graph.areas.length > 0) {
        state.selectedAreaId = state.graph.areas[0].id;
      } else if (state.selectedAreaId && !state.graph.areas.some((a) => a.id === state.selectedAreaId)) {
        state.selectedAreaId = state.graph.areas[0]?.id || null;
      }
      populateFilters();
      renderAll();
      if (state.lastInspector) state.lastInspector();
      const now = new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
      status.textContent = `loaded ${now}`;
    } catch (err) {
      status.textContent = `error: ${err.message}`;
    }
  }

  function switchTab(tab) {
    state.activeTab = tab;
    document.body.classList.toggle("playtest-wide", tab === "playtest" || tab === "puppets");
    document.querySelectorAll(".tab").forEach((btn) => {
      btn.classList.toggle("active", btn.dataset.tab === tab);
    });
    document.querySelectorAll(".view").forEach((view) => {
      view.classList.toggle("active", view.id === `view-${tab}`);
    });
    if (tab === "overview") renderOverview();
    if (tab === "maps") renderMaps();
    if (tab === "events") renderEvents();
    if (tab === "assets") renderAssetsGallery();
    if (tab === "reviews") renderReviews();
    if (tab === "issues") renderIssues();
    if (tab === "playtest") refreshPlaytestStatus(true);
    if (tab === "puppets") refreshPuppetsStatus();
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
    renderAssetsGallery();
    renderReviews();
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
    document.getElementById("tab-assets-count").textContent = String((g.counts.assets || 0) + (g.counts.skins || 0));

    const revBadge = document.getElementById("tab-reviews-count");
    const rc = state.reviewCounts;
    const attention = (rc.open || 0) + (rc.stale || 0);
    revBadge.textContent = String(attention > 0 ? attention : rc.total || 0);
    revBadge.className = `badge ${rc.needsFix > 0 ? "badge-err" : (rc.question > 0 || rc.stale > 0) ? "badge-warn" : "badge-ok"}`;

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

      const isLiveHere = state.liveState?.area === area.id;
      const badge = isLiveHere ? "● LIVE HERE" : !area.reachable ? "✕ UNREACHABLE" : area.isStartArea ? "★ START" : area.hasEndEvent ? "⚑ ENDING" : isDeadEnd ? "⚠ DEAD END" : "";
      grp.appendChild(svgEl("text", { x: 14, y: 28, fill: "#ece6d9", "font-size": 13.5, "font-weight": 700 }, area.name));
      grp.appendChild(svgEl("text", { x: 14, y: 45, fill: "#9aa4b6", "font-size": 11, "font-family": "var(--font-mono)" },
        `${area.localId} · ${area.width}×${area.height}${area.view === "overhead" ? " · 2D" : " · 3D"}`));

      if (badge) {
        grp.appendChild(svgEl("text", {
          x: CARD_W - 12,
          y: 26,
          fill: isLiveHere ? "#3dc4ab" : borderColor,
          "font-size": 10,
          "font-weight": 700,
          "text-anchor": "end",
        }, badge));
      }

      const evtCount = area.cells.filter((c) => c.eventId).length;
      const combatCount = area.cells.filter((c) => c.eventKind === "combat").length;
      const unreachCells = area.cells.filter((c) => c.reachability === "unreachable").length;
      const areaPins = state.reviews.filter((pin) => pin.areaId === area.id || (pin.targetKind === "area" && pin.target === area.id));
      const openAreaPins = areaPins.filter((pin) => pin.status === "needs-fix" || pin.status === "question" || pin.stale);
      const approvedAreaPins = areaPins.filter((pin) => pin.status === "approved" && !pin.stale);
      grp.appendChild(svgEl("text", { x: 14, y: 72, fill: "#cbd5e1", "font-size": 11 },
        `Entries: ${area.entries.length}   Doors: ${area.doors.length}   Triggers: ${evtCount}`));
      const pinNote = openAreaPins.length > 0
        ? `✗ ${openAreaPins.length} flagged`
        : approvedAreaPins.length > 0
          ? `✓ ${approvedAreaPins.length} approved`
          : unreachCells > 0
            ? `${unreachCells} walled cells`
            : "dbl-click for 2D map";
      grp.appendChild(svgEl("text", {
        x: 14,
        y: 92,
        fill: openAreaPins.length > 0 ? "#eb5d5d" : approvedAreaPins.length > 0 ? "#3dc4ab" : unreachCells > 0 ? "#eb5d5d" : "#9aa4b6",
        "font-size": 11,
      }, `${combatCount > 0 ? `⚔ ${combatCount} combat · ` : ""}${pinNote}`));

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
      if (area.view === "overhead") {
        top.appendChild(el("span", { class: "badge badge-ok" }, "2D"));
      }
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
    const tileCanvas = document.getElementById("map-tile-canvas");
    const lblTilemap = document.getElementById("lbl-toggle-tilemap");
    svg.innerHTML = "";
    if (!area) {
      if (tileCanvas) tileCanvas.hidden = true;
      if (lblTilemap) lblTilemap.hidden = true;
      document.getElementById("active-area-title").textContent = "No area selected";
      document.getElementById("active-area-meta").textContent = "";
      return;
    }

    const areaTilemap = area.tilemap || null;
    const areaTileset = areaTilemap ? findTileset(areaTilemap.tilesetId) : null;
    const renderTileArt = Boolean(areaTilemap && areaTileset && state.showTilemap);
    if (lblTilemap) lblTilemap.hidden = !areaTilemap;

    document.getElementById("active-area-title").textContent = `${area.name} (${area.id})`;
    document.getElementById("active-area-meta").textContent =
      `${area.width} × ${area.height} · ${area.view === "overhead" ? "2D overhead" : "3D first-person"} · ${area.entries.length} entries · ${area.doors.length} declared doors${areaTilemap ? ` · tileset ${shortId(areaTilemap.tilesetId)}` : area.wallSet ? ` · wall_set ${shortId(area.wallSet)}` : ""}`;

    const CELL = Math.round(64 * state.mapZoom);
    const PAD = 38;
    const W = PAD * 2 + area.width * CELL;
    const H = PAD * 2 + area.height * CELL;
    svg.setAttribute("width", String(W));
    svg.setAttribute("height", String(H));
    svg.setAttribute("viewBox", `0 0 ${W} ${H}`);

    if (tileCanvas) {
      if (renderTileArt) {
        tileCanvas.hidden = false;
        tileCanvas.style.left = `${PAD}px`;
        tileCanvas.style.top = `${PAD}px`;
        tileCanvas.style.width = `${area.width * CELL}px`;
        tileCanvas.style.height = `${area.height * CELL}px`;
        drawTilemapToCanvas(tileCanvas, areaTilemap, areaTileset, CELL);
      } else {
        tileCanvas.hidden = true;
      }
    }

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

      let fill = renderTileArt ? "transparent" : "#1b2230";
      if (state.showReachability) {
        if (renderTileArt) {
          if (cell.reachability === "locked") fill = "rgba(240, 164, 59, 0.22)";
          else if (cell.reachability === "secret") fill = "rgba(177, 132, 245, 0.25)";
          else if (cell.reachability === "unreachable") fill = "rgba(235, 93, 93, 0.14)";
        } else {
          if (cell.reachability === "locked") fill = "#332819";
          else if (cell.reachability === "secret") fill = "#271d3a";
          else if (cell.reachability === "unreachable") fill = "url(#hatch-unreach)";
        }
      }

      const cellG = svgEl("g", { class: `svg-cell${isSelected ? " selected" : ""}`, "data-x": cell.x, "data-y": cell.y });
      cellG.appendChild(svgEl("rect", {
        class: "cell-rect",
        x: cx + 1,
        y: cy + 1,
        width: CELL - 2,
        height: CELL - 2,
        rx: 3,
        fill,
        stroke: isSelected ? "#e4a94c" : (renderTileArt ? "rgba(255, 255, 255, 0.10)" : "#242c3b"),
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

      const cellPin = findPin("cell", `${area.id}@[${cell.x},${cell.y}]`);
      if (cellPin) {
        const ps = pinStyle(cellPin);
        cellG.appendChild(svgEl("circle", {
          cx: cx + CELL - 11,
          cy: cy + CELL - 11,
          r: 8,
          fill: "#11151d",
          stroke: ps.color,
          "stroke-width": 1.6,
        }));
        cellG.appendChild(svgEl("text", {
          x: cx + CELL - 11,
          y: cy + CELL - 7.5,
          fill: ps.color,
          "font-size": 9.5,
          "font-weight": 800,
          "text-anchor": "middle",
        }, ps.glyph));
      }

      if (state.liveState?.area === area.id && state.liveState.x === cell.x && state.liveState.y === cell.y) {
        cellG.appendChild(svgEl("rect", {
          class: "live-marker-ring",
          x: cx + 3,
          y: cy + 3,
          width: CELL - 6,
          height: CELL - 6,
          rx: 4,
          fill: "rgba(61, 196, 171, 0.14)",
          stroke: "#3dc4ab",
          "stroke-width": 2.4,
        }));
        const liveArrow = FACING_ARROW[state.liveState.facing] || "●";
        cellG.appendChild(svgEl("circle", {
          cx: cx + 13,
          cy: cy + CELL - 13,
          r: 9,
          fill: "#0f1c1d",
          stroke: "#3dc4ab",
          "stroke-width": 1.8,
        }));
        cellG.appendChild(svgEl("text", {
          x: cx + 13,
          y: cy + CELL - 9.5,
          fill: "#3dc4ab",
          "font-size": 10,
          "font-weight": 800,
          "text-anchor": "middle",
        }, liveArrow));
      }

      const tipLines = [`[${cell.x}, ${cell.y}] (${cell.reachability})`];
      if (cell.zone) tipLines.push(`zone: ${cell.zone}`);
      if (entriesHere.length) tipLines.push(`entry: ${entriesHere.map((e) => `${e.name} (${e.facing})`).join(", ")}`);
      if (cell.eventId) tipLines.push(`event: ${cell.eventId} (${cell.eventKind})`);
      if (cell.propSprite) tipLines.push(`prop: ${cell.propSprite}`);
      if (cellPin) tipLines.push(`review [${cellPin.stale ? "stale" : cellPin.status}]: ${cellPin.note || "(no note)"}`);
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

    // 2. Horizontal edges (lighter when rendering crisp tilemap art)
    for (let y = 0; y <= area.height; y++) {
      for (let x = 0; x < area.width; x++) {
        let edge = area.horizontalEdges[y][x];
        if (edge === "open") continue;
        if (renderTileArt && edge === "wall") continue;
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
        if (renderTileArt && edge === "wall") continue;
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
        const doorPin = findPin("door", `${area.id}#${door.id}`);
        const pinSt = pinStyle(doorPin);
        if (pinSt) {
          g.appendChild(svgEl("circle", { cx: mx, cy: my, r: 13, fill: "none", stroke: pinSt.color, "stroke-width": 2, "stroke-dasharray": doorPin.stale ? "3 2" : null }));
        }
        g.appendChild(svgEl("circle", { cx: mx, cy: my, r: 9, fill: "#131720", stroke: color, "stroke-width": 1.6 }));
        g.appendChild(svgEl("text", { x: mx, y: my + 3.5, fill: color, "font-size": 10, "text-anchor": "middle" }, isLocked ? "🔒" : "D"));
        g.appendChild(svgEl("title", {}, `Door '${door.id}' (${isLocked ? "locked" : "unlocked"})${doorPin ? `\nreview [${doorPin.status}]: ${doorPin.note}` : ""}`));
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

      const evtPin = findPin("event", evt.id);
      if (evtPin) {
        const ps = pinStyle(evtPin);
        grp.appendChild(svgEl("circle", { cx: NODE_W - 14, cy: 14, r: 8, fill: "#11151d", stroke: ps.color, "stroke-width": 1.6 }));
        grp.appendChild(svgEl("text", { x: NODE_W - 14, y: 17.5, fill: ps.color, "font-size": 9.5, "font-weight": 800, "text-anchor": "middle" }, ps.glyph));
      }
      if (state.liveState?.activeEvent === evt.id) {
        grp.appendChild(svgEl("rect", {
          class: "live-marker-ring",
          x: -2,
          y: -2,
          width: NODE_W + 4,
          height: NODE_H + 4,
          rx: 9,
          fill: "none",
          stroke: "#3dc4ab",
          "stroke-width": 2.4,
        }));
      }

      grp.addEventListener("click", () => {
        state.selectedEventId = evt.id;
        renderEvents();
        inspectEvent(evt);
      });
      svg.appendChild(grp);
    }
  }

  // ==================== TAB 4: ART & ASSETS GALLERY ====================
  const imageCache = new Map();
  let animLoopId = null;

  function loadImageCached(url) {
    if (!url) return Promise.reject(new Error("no url"));
    if (imageCache.has(url)) return imageCache.get(url);
    const p = new Promise((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = () => reject(new Error("image load failed"));
      img.src = url;
    });
    imageCache.set(url, p);
    return p;
  }

  function ensureAnimLoop() {
    if (animLoopId !== null) return;
    const tick = (now) => {
      if (state.spriteAnimPlayers.length === 0) {
        animLoopId = null;
        return;
      }
      for (const player of state.spriteAnimPlayers) {
        if (!player.playing || !player.img || !player.frames.length) continue;
        const interval = 1000 / Math.max(0.5, player.fps || 4);
        if (now - player.lastTick >= interval) {
          player.lastTick = now;
          player.frameIdx = (player.frameIdx + 1) % player.frames.length;
          player.draw();
        }
      }
      animLoopId = requestAnimationFrame(tick);
    };
    animLoopId = requestAnimationFrame(tick);
  }

  function createSpriteCanvasPlayer(asset, canvas, animName, onFrameUpdate = null, mirror = false) {
    const fw = asset.frameSize?.[0] || asset.width || 32;
    const fh = asset.frameSize?.[1] || asset.height || 32;
    canvas.width = fw;
    canvas.height = fh;
    const ctx = canvas.getContext("2d");
    const animMap = asset.animations || {};
    const animKeys = Object.keys(animMap);
    const activeAnim = (animName && animMap[animName]) || animMap.idle || animMap[animKeys[0]] || null;

    const player = {
      img: null,
      playing: true,
      mirror,
      fps: activeAnim?.fps || 4,
      frames: activeAnim?.frames?.length ? activeAnim.frames : [0],
      frameIdx: 0,
      lastTick: performance.now(),
      setAnimation(name) {
        const next = animMap[name];
        if (!next) return;
        this.frames = next.frames?.length ? next.frames : [0];
        this.fps = next.fps || 4;
        this.frameIdx = 0;
        this.draw();
      },
      draw() {
        if (!this.img) return;
        const cols = Math.max(1, Math.floor(this.img.naturalWidth / fw));
        const total = asset.frameCount || (cols * Math.max(1, Math.floor(this.img.naturalHeight / fh)));
        const f = this.frames[this.frameIdx % this.frames.length] % Math.max(1, total);
        const sx = (f % cols) * fw;
        const sy = Math.floor(f / cols) * fh;
        ctx.clearRect(0, 0, fw, fh);
        ctx.imageSmoothingEnabled = asset.sampling === "linear";
        ctx.save();
        if (this.mirror) {
          ctx.translate(fw, 0);
          ctx.scale(-1, 1);
        }
        ctx.drawImage(this.img, sx, sy, fw, fh, 0, 0, fw, fh);
        ctx.restore();
        if (onFrameUpdate) onFrameUpdate(this.frameIdx + 1, this.frames.length, f);
      },
    };

    const url = assetUrl(asset.id);
    loadImageCached(url).then((img) => {
      player.img = img;
      if (!activeAnim) {
        const cols = Math.max(1, Math.floor(img.naturalWidth / fw));
        const rows = Math.max(1, Math.floor(img.naturalHeight / fh));
        const count = asset.frameCount || (cols * rows);
        player.frames = Array.from({ length: Math.max(1, count) }, (_, idx) => idx);
      }
      player.draw();
    }).catch(() => {});

    state.spriteAnimPlayers.push(player);
    ensureAnimLoop();
    return player;
  }

  function renderRegionCanvas(asset, regionName, rect, canvas, tileCount = 1) {
    const [rx, ry, rw, rh] = rect;
    canvas.width = rw * tileCount;
    canvas.height = rh * tileCount;
    const ctx = canvas.getContext("2d");
    const url = assetUrl(asset.id);
    loadImageCached(url).then((img) => {
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      ctx.imageSmoothingEnabled = asset.sampling === "linear";
      for (let ty = 0; ty < tileCount; ty++) {
        for (let tx = 0; tx < tileCount; tx++) {
          ctx.drawImage(img, rx, ry, rw, rh, tx * rw, ty * rh, rw, rh);
        }
      }
    }).catch(() => {});
  }

  function buildSkinPreviewElement(skin, compact = true) {
    const c = skin.colors || {};
    const box = el("div", { class: "skin-preview-box" });
    box.style.backgroundColor = c.background || "#181b24";
    box.style.color = c.text || "#ece6d9";
    box.style.borderColor = c.border || "#556070";
    box.style.borderWidth = skin.frameSlice ? `${Math.min(10, skin.frameSlice)}px` : "2px";

    if (skin.panelAsset) {
      const pUrl = assetUrl(skin.panelAsset);
      if (pUrl) {
        box.style.backgroundImage = `linear-gradient(${c.background || "rgba(18,20,28,0.65)"}, ${c.background || "rgba(18,20,28,0.65)"}), url("${pUrl}")`;
        box.style.backgroundRepeat = "repeat";
      }
    }
    if (skin.frameAsset && skin.frameSlice) {
      const fUrl = assetUrl(skin.frameAsset);
      if (fUrl) {
        box.style.borderImageSource = `url("${fUrl}")`;
        box.style.borderImageSlice = String(skin.frameSlice);
        box.style.borderImageRepeat = "repeat";
      }
    }

    const titleRow = el("div", { class: "skin-preview-title" });
    titleRow.style.color = c.accent || "#e4a94c";
    if (skin.titleAsset) {
      const tUrl = assetUrl(skin.titleAsset);
      const tAsset = findAsset(skin.titleAsset);
      const img = el("img", { src: tUrl, alt: skin.name, class: tAsset?.sampling === "linear" ? "sampling-linear" : "sampling-nearest" });
      titleRow.appendChild(img);
    } else {
      titleRow.textContent = skin.name;
    }
    box.appendChild(titleRow);

    const body = el("div", { class: "skin-preview-body" });
    body.textContent = compact
      ? "The party stands before the stone archway."
      : "The torchlight flickers along the damp corridor. Roll initiative!";
    const muted = el("div", { style: `font-size:10px;margin-top:2px;color:${c.muted || "#9aa4b6"}` },
      "HP 18/18 · AC 4 · Gold 120");
    body.appendChild(muted);
    box.appendChild(body);

    const btnRow = el("div", { class: "skin-preview-btn-row" });
    for (const label of ["Forward", "Search"]) {
      const b = el("span", { class: "skin-mock-btn" }, label);
      b.style.color = c.button_text || c.text || "#ffffff";
      b.style.backgroundColor = c.button && !c.button.endsWith("00") ? c.button : (c.inset || "rgba(255,255,255,0.08)");
      b.style.borderColor = c.border || "#556070";
      b.style.borderWidth = skin.buttonSlice ? `${Math.min(6, skin.buttonSlice)}px` : "1px";
      if (skin.buttonAsset && skin.buttonSlice) {
        const bUrl = assetUrl(skin.buttonAsset);
        if (bUrl) {
          b.style.borderImageSource = `url("${bUrl}")`;
          b.style.borderImageSlice = `${skin.buttonSlice} fill`;
          b.style.borderImageRepeat = "stretch";
        }
      }
      btnRow.appendChild(b);
    }
    box.appendChild(btnRow);
    return box;
  }

  function renderAssetsGallery() {
    const g = state.graph;
    if (!g) return;
    // Clear active animation players before rebuilding gallery cards
    state.spriteAnimPlayers = [];

    const assets = g.assets || [];
    const skins = g.skins || [];
    const tilesets = g.tilesets || [];
    const allItems = [
      ...tilesets.map((t) => ({ kind: "tileset", category: "tileset", id: t.id, title: t.name, subtitle: `${t.id} · ${t.frameWidth}×${t.frameHeight} · ${(t.tiles || []).length} glyphs`, targetKind: "definition", target: `tileset:${t.id}`, data: t })),
      ...assets.filter((a) => a.category !== "tileset").map((a) => ({ kind: "asset", category: a.category || "picture", id: a.id, title: a.localId, subtitle: a.file, targetKind: "asset", target: a.id, data: a })),
      ...skins.map((s) => ({ kind: "skin", category: "skin", id: s.id, title: s.name, subtitle: `${s.id} · ${s.file}`, targetKind: "definition", target: `skin:${s.id}`, data: s })),
    ];

    // Update category pill labels with counts
    const catCounts = { all: allItems.length, tileset: tilesets.length, wall_set: 0, sprite: 0, portrait: 0, picture: 0, audio: 0, skin: skins.length };
    for (const a of assets) {
      if (a.category === "tileset") continue;
      const cat = a.category || "picture";
      catCounts[cat] = (catCounts[cat] || 0) + 1;
    }
    const catNames = { all: "All", tileset: "Tilesets", wall_set: "Wall Sets", sprite: "Sprites", portrait: "Portraits", picture: "Pictures & UI", audio: "Audio", skin: "UI Skins" };
    document.querySelectorAll("#asset-category-pills .pill-btn").forEach((btn) => {
      const c = btn.dataset.assetCat;
      btn.textContent = `${catNames[c] || c} (${catCounts[c] || 0})`;
      btn.classList.toggle("active", state.assetCategory === c);
    });

    // Compute approval progress across all art + skins
    let approvedCount = 0;
    let flaggedCount = 0;
    let staleCount = 0;
    for (const item of allItems) {
      const pin = findPin(item.targetKind, item.target);
      if (!pin) continue;
      if (pin.stale) staleCount++;
      else if (pin.status === "approved") approvedCount++;
      else if (pin.status === "needs-fix" || pin.status === "question") flaggedCount++;
    }
    const pct = allItems.length > 0 ? Math.round((approvedCount / allItems.length) * 100) : 0;
    document.getElementById("gallery-summary").textContent =
      `${allItems.length} art & skin records · ${approvedCount} approved (${pct}%) · ${flaggedCount} flagged · ${staleCount} stale · ${allItems.length - approvedCount - flaggedCount - staleCount} unreviewed`;
    document.getElementById("gallery-progress-fill").style.width = `${pct}%`;

    const query = state.assetSearch.trim().toLowerCase();
    const visible = allItems.filter((item) => {
      if (state.assetCategory !== "all" && item.category !== state.assetCategory) return false;
      if (state.assetUnusedOnly && item.kind === "asset" && (item.data.usedBy?.length || 0) > 0) return false;
      const pin = findPin(item.targetKind, item.target);
      if (state.assetReviewFilter === "unreviewed" && pin) return false;
      if (state.assetReviewFilter === "open" && (!pin || (pin.status !== "needs-fix" && pin.status !== "question"))) return false;
      if (state.assetReviewFilter === "approved" && (!pin || pin.status !== "approved" || pin.stale)) return false;
      if (state.assetReviewFilter === "stale" && (!pin || !pin.stale)) return false;
      if (query) {
        const hay = `${item.id} ${item.title} ${item.subtitle} ${(item.data.tags || []).join(" ")} ${(item.data.usedBy || []).join(" ")}`.toLowerCase();
        if (!hay.includes(query)) return false;
      }
      return true;
    });

    const grid = document.getElementById("asset-gallery");
    grid.innerHTML = "";
    if (visible.length === 0) {
      grid.appendChild(el("div", { class: "empty-hint", style: "padding:24px" }, "No assets or skins match the current filters."));
      return;
    }

    for (const item of visible) {
      const pin = findPin(item.targetKind, item.target);
      const pinClass = pin ? (pin.stale ? "pin-stale" : `pin-${pin.status}`) : "";
      const key = `${item.kind}:${item.id}`;
      const card = el("article", { class: `asset-card ${pinClass}${state.selectedGalleryKey === key ? " selected" : ""}` });

      const stage = el("div", { class: "asset-card-stage" });
      if (item.kind === "skin") {
        stage.appendChild(buildSkinPreviewElement(item.data, true));
      } else if (item.kind === "tileset") {
        const ts = item.data;
        const strip = el("div", { class: "tileset-card-strip" });
        const drawSamples = (img) => {
          strip.innerHTML = "";
          for (const t of (ts.tiles || []).slice(0, 8)) {
            const cell = el("div", { class: "tileset-card-cell" });
            const c = el("canvas", { width: "36", height: "36" });
            const ctx = c.getContext("2d");
            ctx.imageSmoothingEnabled = false;
            const sample = resolveAutotileSample(t, ts, { n: true, ne: true, e: true, se: true, s: false, sw: false, w: true, nw: true });
            if (img) {
              drawSheetFrame(ctx, img, ts, sample.frame, 0, 0, 36, 36);
            } else {
              ctx.fillStyle = t.color || "#2c3547";
              ctx.fillRect(0, 0, 36, 36);
            }
            cell.appendChild(c);
            cell.appendChild(el("span", {}, `${t.glyph} ${t.name.slice(0, 6)}`));
            strip.appendChild(cell);
          }
        };
        const img = loadSheetImage(ts.sheetAsset, () => drawSamples(state.sheetImages.get(ts.sheetAsset)));
        drawSamples(img);
        stage.appendChild(strip);
      } else {
        const a = item.data;
        if (a.category === "wall_set" && a.regions) {
          const wgrid = el("div", { class: "wallset-grid" });
          const order = ["wall", "door", "floor", "ceiling", ...Object.keys(a.regions).filter((k) => !["wall", "door", "floor", "ceiling"].includes(k))];
          for (const rName of order.slice(0, 4)) {
            if (!a.regions[rName]) continue;
            const tile = el("div", { class: "wallset-tile" });
            const canvas = el("canvas", { class: a.sampling === "linear" ? "sampling-linear" : "sampling-nearest" });
            renderRegionCanvas(a, rName, a.regions[rName], canvas, 1);
            tile.appendChild(canvas);
            tile.appendChild(el("span", {}, rName));
            wgrid.appendChild(tile);
          }
          stage.appendChild(wgrid);
        } else if (a.category === "sprite") {
          const canvas = el("canvas", { class: a.sampling === "linear" ? "sampling-linear" : "sampling-nearest" });
          const fw = a.frameSize?.[0] || 64;
          const fh = a.frameSize?.[1] || 64;
          const scale = Math.min(180 / fw, 108 / fh);
          canvas.style.width = `${Math.round(fw * scale)}px`;
          canvas.style.height = `${Math.round(fh * scale)}px`;
          const frameLbl = el("span", { class: "asset-chip" }, "f 1/1");
          const animNames = Object.keys(a.animations || {});
          const player = createSpriteCanvasPlayer(a, canvas, animNames[0], (idx, total) => {
            frameLbl.textContent = `f ${idx}/${total}`;
          });
          stage.appendChild(canvas);
          const ctrl = el("div", { class: "sprite-controls" });
          const pills = el("div", { class: "sprite-anim-pills" });
          if (animNames.length > 0) {
            animNames.forEach((name, i) => {
              const pBtn = el("button", { class: `mini-pill${i === 0 ? " active" : ""}`, type: "button" }, name);
              pBtn.addEventListener("click", (ev) => {
                ev.stopPropagation();
                pills.querySelectorAll(".mini-pill").forEach((b) => b.classList.toggle("active", b === pBtn));
                player.setAnimation(name);
              });
              pills.appendChild(pBtn);
            });
          } else {
            pills.appendChild(el("span", { class: "asset-chip" }, `${a.frameSize?.[0] || "?"}×${a.frameSize?.[1] || "?"}`));
          }
          ctrl.appendChild(pills);
          const rightCtrl = el("div", { style: "display:flex;gap:4px;align-items:center" });
          const pauseBtn = el("button", { class: "mini-pill", type: "button", title: "Play/Pause" }, "❚❚");
          pauseBtn.addEventListener("click", (ev) => {
            ev.stopPropagation();
            player.playing = !player.playing;
            pauseBtn.textContent = player.playing ? "❚❚" : "▶";
          });
          rightCtrl.appendChild(frameLbl);
          rightCtrl.appendChild(pauseBtn);
          ctrl.appendChild(rightCtrl);
          stage.appendChild(ctrl);
        } else if (a.category === "audio") {
          const audWrap = el("div", { class: "audio-stage" });
          audWrap.appendChild(el("div", { style: "font-size:28px" }, "🔊"));
          const audio = el("audio", { controls: "controls", src: assetUrl(a.id) });
          audio.addEventListener("click", (ev) => ev.stopPropagation());
          audWrap.appendChild(audio);
          stage.appendChild(audWrap);
        } else {
          const img = el("img", {
            src: assetUrl(a.id),
            alt: a.id,
            class: a.sampling === "linear" ? "sampling-linear" : "sampling-nearest",
          });
          stage.appendChild(img);
        }
      }

      // Top overlay chips
      const topBadges = el("div", { class: "asset-stage-badges" });
      if (item.kind === "skin") {
        topBadges.appendChild(el("span", { class: "asset-chip" }, item.data.isCampaignSkin ? "★ campaign skin" : "skin"));
      } else if (item.kind === "tileset") {
        topBadges.appendChild(el("span", { class: "asset-chip" }, `tileset · ${item.data.frameWidth}×${item.data.frameHeight}`));
        topBadges.appendChild(el("span", { class: "asset-chip" }, `${item.data.frameCount} frames`));
      } else {
        const a = item.data;
        const dims = a.width && a.height ? `${a.width}×${a.height}` : a.media;
        topBadges.appendChild(el("span", { class: "asset-chip" }, `${a.category} · ${dims}`));
        if (a.media !== "audio") {
          topBadges.appendChild(el("span", { class: "asset-chip" }, a.sampling));
        }
      }
      stage.appendChild(topBadges);
      card.appendChild(stage);

      const body = el("div", { class: "asset-card-body" });
      const titleRow = el("div", { class: "asset-card-title" });
      titleRow.appendChild(el("span", {}, item.title));
      if (pin) {
        const ps = pinStyle(pin);
        titleRow.appendChild(el("span", { class: `pill ${ps.pill}` }, `${ps.glyph} ${pin.stale ? "Stale" : ps.label}`));
      }
      body.appendChild(titleRow);
      body.appendChild(el("div", { class: "asset-card-meta" }, item.subtitle));
      if (item.kind === "asset") {
        const used = item.data.usedBy?.length || 0;
        body.appendChild(el("div", { class: "asset-card-meta", style: used === 0 ? "color:var(--amber)" : "" },
          used === 0 ? "⚠ unused in module set" : `used by ${used} definition${used === 1 ? "" : "s"}`));
      }
      card.appendChild(body);

      const foot = el("div", { class: "asset-card-foot" });
      foot.appendChild(el("span", { style: "font-size:10.5px;color:var(--text-muted);font-family:var(--font-mono)" }, shortId(item.data.module || "")));
      const qGroup = el("div", { class: "quick-pin-group" });
      for (const [st, glyph, title] of [["approved", "✓", "Approve"], ["needs-fix", "✗", "Flag Needs Fix"], ["question", "?", "Ask Question"]]) {
        const active = pin && !pin.stale && pin.status === st;
        const b = el("button", { class: `qpin-btn${active ? ` active-${st}` : ""}`, type: "button", title }, glyph);
        b.addEventListener("click", (ev) => {
          ev.stopPropagation();
          savePin(item.targetKind, item.target, st, pin?.note || "");
        });
        qGroup.appendChild(b);
      }
      foot.appendChild(qGroup);
      card.appendChild(foot);

      card.addEventListener("click", () => {
        state.selectedGalleryKey = key;
        document.querySelectorAll(".asset-card").forEach((c) => c.classList.remove("selected"));
        card.classList.add("selected");
        if (item.kind === "skin") inspectSkin(item.data);
        else if (item.kind === "tileset") inspectTileset(item.data);
        else inspectAsset(item.id);
      });

      grid.appendChild(card);
    }
  }

  // ==================== TAB 5: REVIEW QUEUE ====================
  function renderReviews() {
    const g = state.graph;
    if (!g) return;

    const rc = state.reviewCounts;
    document.getElementById("reviews-summary").textContent =
      `${rc.open} open (${rc.needsFix} needs-fix · ${rc.question} question) · ${rc.approved} approved · ${rc.stale} stale · ${rc.resolved} resolved`;

    const cliCmd = `tales review list ${g.module.location} --status open --json`;
    document.getElementById("review-cli-cmd").textContent = cliCmd;
    document.getElementById("review-store-path").textContent = state.reviewStorePath ? `sidecar: ${state.reviewStorePath}` : "";

    const filtered = state.reviews.filter((pin) => {
      if (state.reviewFilter === "all") return true;
      if (state.reviewFilter === "open") return pin.status === "needs-fix" || pin.status === "question";
      if (state.reviewFilter === "stale") return pin.stale === true;
      return pin.status === state.reviewFilter;
    });

    const tbody = document.getElementById("reviews-tbody");
    tbody.innerHTML = "";
    if (filtered.length === 0) {
      const tr = el("tr");
      tr.appendChild(el("td", { colspan: "6", style: "color:var(--text-dim);text-align:center;padding:28px" },
        "No review pins match this filter. Click any area, map cell, door, event, or asset to drop a review pin."));
      tbody.appendChild(tr);
      return;
    }

    for (const pin of filtered) {
      const tr = el("tr");
      tr.appendChild(el("td", { style: "font-family:var(--font-mono);color:var(--gold);white-space:nowrap" }, pin.id));

      const tdStatus = el("td", { style: "white-space:nowrap" });
      const ps = pinStyle(pin);
      tdStatus.appendChild(el("span", { class: `pill ${ps.pill}` }, `${ps.glyph} ${pin.stale ? "Stale Approval" : ps.label}`));
      if (!pin.exists) {
        tdStatus.appendChild(el("div", { style: "margin-top:4px;font-size:10px;color:var(--red)" }, "missing target"));
      }
      tr.appendChild(tdStatus);

      const tdTarget = el("td");
      tdTarget.appendChild(el("div", { style: "font-family:var(--font-mono);font-size:11px;color:var(--text)" }, `${pin.targetKind}: ${pin.target}`));
      tdTarget.appendChild(el("div", { style: "font-size:11.5px;color:var(--text-dim);margin-top:2px" }, pin.summary));
      tr.appendChild(tdTarget);

      const tdNote = el("td");
      tdNote.appendChild(el("div", {}, pin.note || "—"));
      if (pin.resolution) {
        tdNote.appendChild(el("div", { style: "margin-top:4px;font-size:11px;color:var(--teal)" }, `Resolved: ${pin.resolution}`));
      }
      tr.appendChild(tdNote);

      const loc = [pin.file, pin.jsonPath].filter(Boolean).join(" ");
      tr.appendChild(el("td", { style: "font-family:var(--font-mono);font-size:10.5px;color:var(--text-dim);max-width:220px;word-break:break-word" }, loc || "—"));

      const tdAct = el("td", { style: "white-space:nowrap" });
      const actWrap = el("div", { class: "insp-actions" });
      const btnJump = el("button", { class: "btn btn-sm" }, "Jump →");
      btnJump.addEventListener("click", () => jumpToPin(pin));
      actWrap.appendChild(btnJump);
      if (pin.status !== "resolved") {
        const btnRes = el("button", { class: "btn btn-sm" }, "Resolve");
        btnRes.addEventListener("click", () => resolvePin(pin.id));
        actWrap.appendChild(btnRes);
      }
      const btnDel = el("button", { class: "btn btn-sm" }, "✕");
      btnDel.title = "Delete pin";
      btnDel.addEventListener("click", () => deletePin(pin.id));
      actWrap.appendChild(btnDel);
      tdAct.appendChild(actWrap);
      tr.appendChild(tdAct);
      tbody.appendChild(tr);
    }
  }

  function jumpToPin(pin) {
    const g = state.graph;
    if (pin.targetKind === "cell" && pin.areaId && pin.cell) {
      const area = g.areas.find((a) => a.id === pin.areaId);
      if (area) {
        state.selectedAreaId = area.id;
        state.selectedCell = { areaId: area.id, x: pin.cell[0], y: pin.cell[1] };
        switchTab("maps");
        const cell = area.cells.find((c) => c.x === pin.cell[0] && c.y === pin.cell[1]);
        if (cell) inspectCell(area, cell, area.entries.filter((e) => e.x === cell.x && e.y === cell.y));
        return;
      }
    }
    if (pin.targetKind === "door" && pin.areaId && pin.doorId) {
      const area = g.areas.find((a) => a.id === pin.areaId);
      if (area) {
        state.selectedAreaId = area.id;
        state.selectedDoor = { areaId: area.id, doorId: pin.doorId };
        switchTab("maps");
        const door = area.doors.find((d) => d.id === pin.doorId);
        if (door) inspectDoor(area, door);
        return;
      }
    }
    if (pin.targetKind === "area" && pin.areaId) {
      const area = g.areas.find((a) => a.id === pin.areaId);
      if (area) {
        state.selectedAreaId = area.id;
        switchTab("maps");
        inspectArea(area);
        return;
      }
    }
    if (pin.targetKind === "event" && pin.eventId) {
      const evt = g.events.find((e) => e.id === pin.eventId);
      if (evt) {
        state.selectedEventId = evt.id;
        switchTab("events");
        inspectEvent(evt);
        return;
      }
    }
    if (pin.targetKind === "asset") {
      state.selectedGalleryKey = `asset:${pin.target}`;
      switchTab("assets");
      inspectAsset(pin.target);
      return;
    }
    if (pin.targetKind === "definition" && pin.target.startsWith("skin:")) {
      const skinId = pin.target.slice("skin:".length);
      const skin = (g.skins || []).find((s) => s.id === skinId);
      if (skin) {
        state.selectedGalleryKey = `skin:${skin.id}`;
        switchTab("assets");
        inspectSkin(skin);
      }
    }
  }

  // ==================== TAB 5: DIAGNOSTICS ====================
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

  function makeReviewPinCard(targetKind, target) {
    const existing = findPin(targetKind, target);
    const card = el("div", { class: "review-card" });

    const head = el("div", { class: "review-card-head" });
    head.appendChild(el("h4", {}, existing ? `Review Pin · ${existing.id}` : "Review Pin"));
    if (existing) {
      const ps = pinStyle(existing);
      head.appendChild(el("span", { class: `pill ${ps.pill}` }, `${ps.glyph} ${existing.stale ? "Stale" : ps.label}`));
    }
    card.appendChild(head);

    if (existing?.stale) {
      card.appendChild(el("div", { class: "review-stale-banner" },
        "Target changed on disk since this approval. Re-approve or flag needs-fix."));
    }

    const noteInput = el("textarea", {
      class: "review-note-input",
      placeholder: "Optional note for the agent (e.g. 'Door feels misplaced, add hint on wall')…",
    });
    noteInput.value = existing?.note || "";

    const statusRow = el("div", { class: "review-status-row" });
    const statuses = [
      { id: "needs-fix", text: "✗ Needs Fix" },
      { id: "question", text: "? Question" },
      { id: "approved", text: "✓ Approve" },
      { id: "resolved", text: "✔ Resolve" },
    ];
    for (const st of statuses) {
      const isCurrent = existing?.status === st.id && !existing?.stale;
      const btn = el("button", {
        class: `rev-btn${isCurrent ? ` active-${st.id}` : ""}`,
        type: "button",
      }, st.text);
      btn.addEventListener("click", () => savePin(targetKind, target, st.id, noteInput.value));
      statusRow.appendChild(btn);
    }
    card.appendChild(statusRow);
    card.appendChild(noteInput);

    noteInput.addEventListener("keydown", (ev) => {
      if ((ev.ctrlKey || ev.metaKey) && ev.key === "Enter") {
        savePin(targetKind, target, existing?.status || "needs-fix", noteInput.value);
      }
    });

    const foot = el("div", { class: "review-card-foot" });
    const saveBtn = el("button", { class: "btn btn-primary btn-sm", type: "button" }, existing ? "Update Note" : "Save Pin");
    saveBtn.addEventListener("click", () => savePin(targetKind, target, existing?.status || "needs-fix", noteInput.value));
    foot.appendChild(saveBtn);

    if (existing) {
      const delBtn = el("button", { class: "btn btn-sm", type: "button" }, "Remove Pin");
      delBtn.addEventListener("click", () => deletePin(existing.id));
      foot.appendChild(delBtn);
    }
    card.appendChild(foot);
    return card;
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
    const existing = findPin("asset", qualifiedAssetId);
    const mini = el("div", { class: "review-mini-bar" });
    const btnApp = el("button", { class: "btn btn-sm", type: "button" }, existing?.status === "approved" && !existing.stale ? "✓ Asset Approved" : "✓ Approve Art");
    btnApp.addEventListener("click", () => savePin("asset", qualifiedAssetId, "approved", existing?.note || ""));
    const btnFix = el("button", { class: "btn btn-sm", type: "button" }, "✗ Flag Art");
    btnFix.addEventListener("click", () => inspectAsset(qualifiedAssetId));
    mini.appendChild(btnApp);
    mini.appendChild(btnFix);
    wrap.appendChild(mini);
    sec.appendChild(wrap);
    return sec;
  }

  function inspectAsset(qualifiedAssetId) {
    state.lastInspector = () => inspectAsset(qualifiedAssetId);
    const asset = findAsset(qualifiedAssetId);
    const sections = [makeReviewPinCard("asset", qualifiedAssetId)];

    if (asset?.category === "wall_set" && asset.regions) {
      const tileSec = el("div", { class: "insp-section" });
      tileSec.appendChild(el("h4", {}, "Wall Set Regions — 2×2 Tiling Check"));
      const grid = el("div", { class: "tiling-check-grid" });
      for (const [rName, rect] of Object.entries(asset.regions)) {
        const box = el("div", { class: "tiling-tile" });
        const canvas = el("canvas", { class: asset.sampling === "linear" ? "sampling-linear" : "sampling-nearest" });
        renderRegionCanvas(asset, rName, rect, canvas, 2);
        box.appendChild(canvas);
        box.appendChild(el("span", { style: "font-size:10px;color:var(--text-dim);font-family:var(--font-mono)" },
          `${rName} (${rect[2]}×${rect[3]})`));
        grid.appendChild(box);
      }
      tileSec.appendChild(grid);
      sections.push(tileSec);
    } else if (asset?.category === "sprite") {
      const sprSec = el("div", { class: "insp-section" });
      sprSec.appendChild(el("h4", {}, "Animated Sprite Preview"));
      const canvas = el("canvas", { class: asset.sampling === "linear" ? "sampling-linear" : "sampling-nearest" });
      const fw = asset.frameSize?.[0] || 64;
      const fh = asset.frameSize?.[1] || 64;
      const scale = Math.min(260 / fw, 150 / fh);
      canvas.style.width = `${Math.round(fw * scale)}px`;
      canvas.style.height = `${Math.round(fh * scale)}px`;
      canvas.style.display = "block";
      canvas.style.margin = "0 auto 8px auto";
      canvas.style.background = "#0a0c12";
      canvas.style.border = "1px solid var(--border)";
      canvas.style.borderRadius = "6px";
      const animNames = Object.keys(asset.animations || {});
      const player = createSpriteCanvasPlayer(asset, canvas, animNames[0], null, false);
      sprSec.appendChild(canvas);
      const controls = el("div", { class: "insp-actions" });
      for (const name of animNames) {
        const b = el("button", { class: "btn btn-sm", type: "button" }, name);
        b.addEventListener("click", () => player.setAnimation(name));
        controls.appendChild(b);
      }
      const flipBtn = el("button", { class: "btn btn-sm", type: "button" }, "⇄ Flip Facing");
      flipBtn.addEventListener("click", () => {
        player.mirror = !player.mirror;
        player.draw();
      });
      controls.appendChild(flipBtn);
      sprSec.appendChild(controls);
      sections.push(sprSec);
    }

    const preview = makeAssetPreviewSection("Source Sheet / Media", qualifiedAssetId);
    if (preview) sections.push(preview);

    if (asset) {
      sections.push(makeKvSection("Asset Metadata", [
        ["ID", asset.id],
        ["Category", asset.category],
        ["File", asset.file],
        ["Definition", asset.definitionFile],
        ["Dimensions", asset.width && asset.height ? `${asset.width} × ${asset.height} px` : null],
        ["Media / Sampling", `${asset.media} · ${asset.sampling}`],
        ["Frame Size", asset.frameSize ? `${asset.frameSize[0]} × ${asset.frameSize[1]} (${asset.frameCount || "auto"} frames)` : null],
        ["Faces / Height", asset.faces ? `${asset.faces}${asset.heightCells ? ` · ${asset.heightCells} cells` : ""}` : null],
        ["Tags", asset.tags?.join(", ") || "none"],
      ]));

      if (asset.usedBy?.length) {
        const useSec = el("div", { class: "insp-section" });
        useSec.appendChild(el("h4", {}, `Used By (${asset.usedBy.length})`));
        const row = el("div", { class: "insp-actions" });
        for (const ref of asset.usedBy) {
          const btn = el("button", { class: "btn btn-sm", type: "button" }, ref);
          btn.addEventListener("click", () => {
            const [type, ...rest] = ref.split(":");
            const qid = rest.join(":");
            if (type === "area") {
              const area = state.graph.areas.find((a) => a.id === qid);
              if (area) { state.selectedAreaId = area.id; switchTab("maps"); inspectArea(area); }
            } else if (type === "event") {
              const evt = state.graph.events.find((e) => e.id === qid);
              if (evt) { state.selectedEventId = evt.id; switchTab("events"); inspectEvent(evt); }
            } else if (type === "skin") {
              const skin = (state.graph.skins || []).find((s) => s.id === qid);
              if (skin) inspectSkin(skin);
            }
          });
          row.appendChild(btn);
        }
        useSec.appendChild(row);
        sections.push(useSec);
      }
    }
    sections.push(makeJsonSection(qualifiedAssetId, "asset"));
    setInspector("ASSET", qualifiedAssetId, sections);
  }

  function inspectSkin(skin) {
    state.lastInspector = () => {
      const fresh = (state.graph?.skins || []).find((s) => s.id === skin.id);
      if (fresh) inspectSkin(fresh);
    };
    const sections = [makeReviewPinCard("definition", `skin:${skin.id}`)];

    const liveSec = el("div", { class: "insp-section" });
    liveSec.appendChild(el("h4", {}, "Live UI Skin Preview"));
    const wrap = el("div", { style: "height:152px" });
    wrap.appendChild(buildSkinPreviewElement(skin, false));
    liveSec.appendChild(wrap);
    sections.push(liveSec);

    const swatchSec = el("div", { class: "insp-section" });
    swatchSec.appendChild(el("h4", {}, "Theme Palette"));
    const sGrid = el("div", { class: "swatch-grid" });
    for (const [name, hex] of Object.entries(skin.colors || {})) {
      const row = el("div", { class: "color-swatch-row" });
      const dot = el("span", { class: "color-dot" });
      dot.style.background = hex;
      row.appendChild(dot);
      row.appendChild(el("span", {}, `${name}: ${hex}`));
      sGrid.appendChild(row);
    }
    swatchSec.appendChild(sGrid);
    sections.push(swatchSec);

    sections.push(makeKvSection("Skin Metadata", [
      ["ID", skin.id],
      ["Name", skin.name],
      ["Campaign Skin", skin.isCampaignSkin ? "Yes (default)" : "Optional"],
      ["Panel Tile", skin.panelAsset || "none"],
      ["Frame / Slice", skin.frameAsset ? `${skin.frameAsset} (${skin.frameSlice}px)` : "none"],
      ["Button / Slice", skin.buttonAsset ? `${skin.buttonAsset} (${skin.buttonSlice}px)` : "none"],
      ["Title Art", skin.titleAsset || "none"],
      ["File", skin.file],
    ]));

    sections.push(makeJsonSection(skin.id, "skin"));
    setInspector("UI SKIN", skin.name, sections);
  }

  function inspectTileset(ts) {
    state.lastInspector = () => {
      const fresh = findTileset(ts.id);
      if (fresh) inspectTileset(fresh);
    };
    const sections = [makeReviewPinCard("definition", `tileset:${ts.id}`)];

    // 1. Full sheet atlas with numbered frame overlay
    const atlasSec = el("div", { class: "insp-section" });
    atlasSec.appendChild(el("h4", {}, `Tile Atlas (${ts.columns}×${ts.rows} · ${ts.frameCount} frames)`));
    const atlasCanvas = el("canvas", { class: "tileset-atlas-canvas" });
    const drawAtlas = (img) => {
      const cell = Math.max(24, Math.min(44, Math.floor(300 / Math.max(1, ts.columns))));
      atlasCanvas.width = ts.columns * cell;
      atlasCanvas.height = ts.rows * cell;
      const ctx = atlasCanvas.getContext("2d");
      ctx.imageSmoothingEnabled = false;
      ctx.clearRect(0, 0, atlasCanvas.width, atlasCanvas.height);
      for (let i = 0; i < ts.frameCount; i++) {
        const col = i % ts.columns;
        const row = Math.floor(i / ts.columns);
        const dx = col * cell;
        const dy = row * cell;
        if (img) drawSheetFrame(ctx, img, ts, i, dx, dy, cell, cell);
        ctx.strokeStyle = "rgba(255,255,255,0.18)";
        ctx.strokeRect(dx + 0.5, dy + 0.5, cell - 1, cell - 1);
        ctx.fillStyle = "rgba(9,11,15,0.68)";
        ctx.fillRect(dx + 1, dy + 1, 16, 11);
        ctx.fillStyle = "#e4a94c";
        ctx.font = "bold 9px ui-monospace, monospace";
        ctx.fillText(String(i), dx + 3, dy + 9);
      }
    };
    const sheetImg = loadSheetImage(ts.sheetAsset, () => drawAtlas(state.sheetImages.get(ts.sheetAsset)));
    drawAtlas(sheetImg);
    atlasSec.appendChild(atlasCanvas);
    sections.push(atlasSec);

    // 2. Interactive 3x3 neighbor auto-tile seam tester
    const autoGlyphs = (ts.tiles || []).filter((t) => t.autotileMode);
    if (autoGlyphs.length > 0) {
      const testSec = el("div", { class: "insp-section autotile-tester" });
      testSec.appendChild(el("h4", {}, "Interactive Auto-Tile Seam Tester"));
      const sel = el("select", { class: "select-input" });
      for (const g of autoGlyphs) {
        sel.appendChild(el("option", { value: g.glyph }, `'${g.glyph}' ${g.name} (${g.autotileMode})`));
      }
      testSec.appendChild(sel);

      const nb = { nw: true, n: true, ne: true, w: true, e: true, sw: false, s: false, se: false };
      const row = el("div", { class: "autotile-tester-row" });
      const grid = el("div", { class: "autotile-grid" });
      const preview = el("canvas", { class: "autotile-preview-canvas", width: "96", height: "96" });
      const readout = el("div", { class: "autotile-readout" });

      const redrawTest = () => {
        const active = autoGlyphs.find((g) => g.glyph === sel.value) || autoGlyphs[0];
        const sample = resolveAutotileSample(active, ts, nb);
        readout.textContent = `${sample.label} → frame #${sample.frame}`;
        const ctx = preview.getContext("2d");
        ctx.imageSmoothingEnabled = false;
        ctx.clearRect(0, 0, 96, 96);
        const img = loadSheetImage(ts.sheetAsset, redrawTest);
        const dirs = [
          ["nw", 0, 0], ["n", 1, 0], ["ne", 2, 0],
          ["w", 0, 1], ["center", 1, 1], ["e", 2, 1],
          ["sw", 0, 2], ["s", 1, 2], ["se", 2, 2],
        ];
        for (const [key, gx, gy] of dirs) {
          if (key !== "center" && !nb[key]) {
            ctx.fillStyle = "#161b24";
            ctx.fillRect(gx * 32, gy * 32, 32, 32);
            continue;
          }
          const cellFrame = key === "center"
            ? sample.frame
            : resolveAutotileSample(active, ts, {
              n: key !== "nw" && key !== "n" && key !== "ne",
              s: key !== "sw" && key !== "s" && key !== "se",
              w: key !== "nw" && key !== "w" && key !== "sw",
              e: key !== "ne" && key !== "e" && key !== "se",
              nw: false, ne: false, sw: false, se: false,
            }).frame;
          if (img) drawSheetFrame(ctx, img, ts, cellFrame, gx * 32, gy * 32, 32, 32);
          else { ctx.fillStyle = active.color || "#3b4a62"; ctx.fillRect(gx * 32, gy * 32, 32, 32); }
        }
        ctx.strokeStyle = "rgba(228, 169, 76, 0.85)";
        ctx.lineWidth = 2;
        ctx.strokeRect(32, 32, 32, 32);
      };

      for (const key of ["nw", "n", "ne", "w", "center", "e", "sw", "s", "se"]) {
        const b = el("button", {
          type: "button",
          class: `autotile-nb-btn${key === "center" ? " center" : nb[key] ? " on" : ""}`,
        }, key === "center" ? "●" : key.toUpperCase());
        if (key !== "center") {
          b.addEventListener("click", () => {
            nb[key] = !nb[key];
            b.classList.toggle("on", nb[key]);
            redrawTest();
          });
        }
        grid.appendChild(b);
      }
      sel.addEventListener("change", redrawTest);
      row.appendChild(grid);
      row.appendChild(preview);
      testSec.appendChild(row);
      testSec.appendChild(readout);
      redrawTest();
      sections.push(testSec);
    }

    // 3. Glyph Palette Table
    const palSec = el("div", { class: "insp-section" });
    palSec.appendChild(el("h4", {}, `Tile Glyphs (${(ts.tiles || []).length})`));
    const palList = el("div", { class: "tile-glyph-list" });
    for (const t of ts.tiles || []) {
      const row = el("div", { class: "tile-glyph-row" });
      row.appendChild(el("span", { class: "tile-glyph-char" }, t.glyph));
      const thumb = el("canvas", { width: "28", height: "28" });
      const drawThumb = (img) => {
        const ctx = thumb.getContext("2d");
        ctx.imageSmoothingEnabled = false;
        const sample = resolveAutotileSample(t, ts, { n: true, e: true, s: false, w: true, ne: true, nw: true, se: false, sw: false });
        if (img) drawSheetFrame(ctx, img, ts, sample.frame, 0, 0, 28, 28);
        else { ctx.fillStyle = t.color || "#2c3547"; ctx.fillRect(0, 0, 28, 28); }
      };
      drawThumb(loadSheetImage(ts.sheetAsset, () => drawThumb(state.sheetImages.get(ts.sheetAsset))));
      row.appendChild(thumb);
      const meta = el("div", { class: "tile-glyph-meta" });
      meta.appendChild(el("strong", {}, t.name));
      const ruleText = t.autotileMode
        ? `${t.autotileMode}${t.autotileBase !== null && t.autotileBase !== undefined ? ` base=${t.autotileBase}` : ""}${t.connectsTo?.length ? ` connects=${t.connectsTo.join("")}` : ""}`
        : t.variants?.length
          ? `variants [${t.variants.join(",")}]`
          : `frame #${t.tile ?? 0}`;
      const flags = [
        !t.passable ? "solid" : "passable",
        t.door ? "door" : null,
        t.moveCost > 1 ? `cost ${t.moveCost}` : null,
        t.blocksSight ? "blocks sight" : null,
      ].filter(Boolean).join(" · ");
      meta.appendChild(el("span", { class: "tile-glyph-sub" }, `${ruleText} · ${flags}`));
      row.appendChild(meta);
      palList.appendChild(row);
    }
    palSec.appendChild(palList);
    sections.push(palSec);

    if (ts.usedBy?.length) {
      const useSec = el("div", { class: "insp-section" });
      useSec.appendChild(el("h4", {}, `Used By (${ts.usedBy.length})`));
      const uRow = el("div", { class: "insp-actions" });
      for (const ref of ts.usedBy) {
        const btn = el("button", { class: "btn btn-sm", type: "button" }, ref);
        btn.addEventListener("click", () => {
          const [type, ...rest] = ref.split(":");
          const qid = rest.join(":");
          if (type === "area") {
            const area = state.graph.areas.find((a) => a.id === qid);
            if (area) { state.selectedAreaId = area.id; switchTab("maps"); inspectArea(area); }
          }
        });
        uRow.appendChild(btn);
      }
      useSec.appendChild(uRow);
      sections.push(useSec);
    }

    sections.push(makeKvSection("Tileset Metadata", [
      ["ID", ts.id],
      ["Sheet Asset", ts.sheetAsset],
      ["Frame Size", `${ts.frameWidth} × ${ts.frameHeight} px`],
      ["Sheet Size", `${ts.imageWidth} × ${ts.imageHeight} px (${ts.columns}×${ts.rows})`],
      ["File", ts.file],
    ]));
    sections.push(makeJsonSection(ts.id, "tileset"));
    setInspector("TILESET", ts.name, sections);
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
    state.lastInspector = () => {
      const fresh = state.graph?.areas.find((a) => a.id === area.id);
      if (fresh) inspectArea(fresh);
    };
    const sections = [makeReviewPinCard("area", area.id)];
    const actionsSec = el("div", { class: "insp-section" });
    actionsSec.appendChild(el("h4", {}, "Actions"));
    const actRow = el("div", { class: "insp-actions" });
    const openMapBtn = el("button", { class: "btn btn-primary btn-sm" }, "Open 2D Map →");
    openMapBtn.addEventListener("click", () => {
      state.selectedAreaId = area.id;
      switchTab("maps");
    });
    actRow.appendChild(openMapBtn);
    if ((state.embedMode || state.graph?.module?.kind === "campaign") && area.entries.length > 0) {
      const e0 = area.entries[0];
      const jumpBtn = el("button", { class: "btn btn-sm", type: "button" }, state.embedMode ? "⚡ Jump Game Here" : "▶ Playtest Here");
      jumpBtn.addEventListener("click", () => {
        if (state.embedMode) {
          postToGame({ type: "tales.debugGoto", area: area.id, x: e0.x, y: e0.y, facing: e0.facing });
        } else {
          jumpInPlaytest({ area: area.id, x: e0.x, y: e0.y, facing: e0.facing });
        }
      });
      actRow.appendChild(jumpBtn);
    }
    actionsSec.appendChild(actRow);
    sections.push(actionsSec);

    sections.push(makeKvSection("Area Details", [
      ["ID", area.id],
      ["View Mode", area.view === "overhead" ? "2D Overhead Tilemap" : "3D First-Person"],
      ["Dimensions", `${area.width} × ${area.height}`],
      ["Reachable", area.reachable ? "Yes" : "No (unreachable from start)"],
      ["Entries", area.entries.map((e) => `${e.name} [${e.x},${e.y}]`).join(", ")],
      ["Tileset", area.tilemap?.tilesetId || "none"],
      ["Wall Set", area.wallSet || "none"],
      ["File", area.file],
    ]));

    if (area.tilemap) {
      const ts = findTileset(area.tilemap.tilesetId);
      if (ts) {
        const tsSec = el("div", { class: "insp-section" });
        tsSec.appendChild(el("h4", {}, "Tilemap"));
        const row = el("div", { class: "insp-actions" });
        const openTs = el("button", { class: "btn btn-sm", type: "button" }, `Inspect Tileset: ${ts.name} →`);
        openTs.addEventListener("click", () => inspectTileset(ts));
        row.appendChild(openTs);
        tsSec.appendChild(row);
        sections.push(tsSec);
      }
    }

    if (area.wallSet) {
      const preview = makeAssetPreviewSection("Wall Set", area.wallSet);
      if (preview) sections.push(preview);
    }

    sections.push(makeJsonSection(area.id, "area"));
    setInspector("AREA", area.name, sections);
  }

  function inspectCell(area, cell, entriesHere) {
    state.lastInspector = () => {
      const freshArea = state.graph?.areas.find((a) => a.id === area.id);
      const freshCell = freshArea?.cells.find((c) => c.x === cell.x && c.y === cell.y);
      if (freshArea && freshCell) inspectCell(freshArea, freshCell, freshArea.entries.filter((e) => e.x === freshCell.x && e.y === freshCell.y));
    };
    const sections = [makeReviewPinCard("cell", `${area.id}@[${cell.x},${cell.y}]`)];
    if (state.embedMode || state.graph?.module?.kind === "campaign") {
      const jumpSec = el("div", { class: "insp-section" });
      jumpSec.appendChild(el("h4", {}, state.embedMode ? "Live Game Bridge" : "Streamed Playtest"));
      const row = el("div", { class: "insp-actions" });
      const jumpBtn = el("button", { class: "btn btn-primary btn-sm", type: "button" }, state.embedMode ? `⚡ Teleport Party to [${cell.x}, ${cell.y}]` : `▶ Test Cell [${cell.x}, ${cell.y}] in Playtest`);
      jumpBtn.addEventListener("click", () => {
        const targetFacing = cell.facing || state.liveState?.facing || "north";
        if (state.embedMode) {
          postToGame({ type: "tales.debugGoto", area: area.id, x: cell.x, y: cell.y, facing: targetFacing });
        } else {
          jumpInPlaytest({ area: area.id, x: cell.x, y: cell.y, facing: targetFacing });
        }
      });
      row.appendChild(jumpBtn);
      jumpSec.appendChild(row);
      sections.push(jumpSec);
    }
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

    if (area.tilemap) {
      const ts = findTileset(area.tilemap.tilesetId);
      if (ts) {
        const tmSec = el("div", { class: "insp-section" });
        tmSec.appendChild(el("h4", {}, `Tilemap Layers (${area.tilemap.layers.length})`));
        const glyphByChar = new Map((ts.tiles || []).map((t) => [t.glyph, t]));
        const list = el("div", { class: "tile-glyph-list" });
        area.tilemap.layers.forEach((layer, li) => {
          const ch = layer.rows?.[cell.y]?.[cell.x] ?? " ";
          const frame = layer.frames?.[cell.y]?.[cell.x] ?? -1;
          const mask = layer.bitmasks?.[cell.y]?.[cell.x] ?? 0;
          const gDef = glyphByChar.get(ch);
          const row = el("div", { class: "tile-glyph-row" });
          row.appendChild(el("span", { class: "tile-glyph-char" }, ch === " " ? "·" : ch));
          const thumb = el("canvas", { width: "28", height: "28" });
          const drawThumb = (img) => {
            const ctx = thumb.getContext("2d");
            ctx.imageSmoothingEnabled = false;
            ctx.clearRect(0, 0, 28, 28);
            if (frame >= 0 && img) drawSheetFrame(ctx, img, ts, frame, 0, 0, 28, 28);
            else if (gDef?.color) { ctx.fillStyle = gDef.color; ctx.fillRect(0, 0, 28, 28); }
          };
          drawThumb(loadSheetImage(ts.sheetAsset, () => drawThumb(state.sheetImages.get(ts.sheetAsset))));
          row.appendChild(thumb);
          const meta = el("div", { class: "tile-glyph-meta" });
          meta.appendChild(el("strong", {}, `L${li}: ${gDef ? gDef.name : "(empty)"}`));
          meta.appendChild(el("span", { class: "tile-glyph-sub" }, frame >= 0
            ? `frame #${frame}${gDef?.autotileMode ? ` · ${gDef.autotileMode} mask=${mask}` : ""}${gDef && !gDef.passable ? " · solid" : ""}`
            : "transparent"));
          row.appendChild(meta);
          list.appendChild(row);
        });
        tmSec.appendChild(list);
        sections.push(tmSec);
      }
    }

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
    state.lastInspector = () => {
      const freshArea = state.graph?.areas.find((a) => a.id === area.id);
      const freshDoor = freshArea?.doors.find((d) => d.id === door.id);
      if (freshArea && freshDoor) inspectDoor(freshArea, freshDoor);
    };
    const sections = [makeReviewPinCard("door", `${area.id}#${door.id}`)];
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
    state.lastInspector = () => {
      const fresh = state.graph?.events.find((e) => e.id === evt.id);
      if (fresh) inspectEvent(fresh);
    };
    const sections = [makeReviewPinCard("event", evt.id)];
    if (state.embedMode || state.graph?.module?.kind === "campaign") {
      const trigSec = el("div", { class: "insp-section" });
      trigSec.appendChild(el("h4", {}, state.embedMode ? "Live Game Bridge" : "Streamed Playtest"));
      const row = el("div", { class: "insp-actions" });
      const trigBtn = el("button", { class: "btn btn-primary btn-sm", type: "button" }, state.embedMode ? "⚡ Trigger Event in Game" : "▶ Test Event in Playtest");
      trigBtn.addEventListener("click", () => {
        if (state.embedMode) {
          postToGame({ type: "tales.debugEvent", event: evt.id });
        } else {
          jumpInPlaytest({ event: evt.id });
        }
      });
      row.appendChild(trigBtn);
      trigSec.appendChild(row);
      sections.push(trigSec);
    }
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

    if (evt.encounter) {
      const enc = findEncounter(evt.encounter);
      if (enc?.tilemap) {
        const ts = findTileset(enc.tilemap.tilesetId);
        if (ts) {
          const encSec = el("div", { class: "insp-section" });
          encSec.appendChild(el("h4", {}, `Combat Battlefield Tilemap (${enc.tilemap.width}×${enc.tilemap.height})`));
          const canvas = el("canvas", { class: "encounter-tilemap-canvas" });
          const cellPx = Math.max(16, Math.floor(300 / Math.max(1, enc.tilemap.width)));
          drawTilemapToCanvas(canvas, enc.tilemap, ts, cellPx);
          encSec.appendChild(canvas);
          sections.push(encSec);
        }
      }
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

  document.getElementById("chk-tilemap")?.addEventListener("change", (e) => {
    state.showTilemap = e.target.checked;
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

  document.querySelectorAll("#review-filter-pills .pill-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      state.reviewFilter = btn.dataset.reviewFilter;
      document.querySelectorAll("#review-filter-pills .pill-btn").forEach((b) => b.classList.toggle("active", b === btn));
      renderReviews();
    });
  });

  document.getElementById("btn-clear-resolved").addEventListener("click", () => clearResolvedPins());

  document.getElementById("btn-copy-cli").addEventListener("click", () => {
    const text = document.getElementById("review-cli-cmd").textContent;
    if (navigator.clipboard && text) {
      navigator.clipboard.writeText(text).then(() => {
        const btn = document.getElementById("btn-copy-cli");
        btn.textContent = "Copied!";
        setTimeout(() => { btn.textContent = "Copy Agent CLI"; }, 1200);
      }).catch(() => {});
    }
  });

  document.querySelectorAll("#asset-category-pills .pill-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      state.assetCategory = btn.dataset.assetCat;
      renderAssetsGallery();
    });
  });
  document.getElementById("asset-search").addEventListener("input", (e) => {
    state.assetSearch = e.target.value;
    renderAssetsGallery();
  });
  document.getElementById("asset-review-filter").addEventListener("change", (e) => {
    state.assetReviewFilter = e.target.value;
    renderAssetsGallery();
  });
  document.getElementById("chk-unused-assets").addEventListener("change", (e) => {
    state.assetUnusedOnly = e.target.checked;
    renderAssetsGallery();
  });

  // ==================== STREAMED BROWSER PLAYTEST ====================
  function setPlaytestMsg(text, isError = false) {
    const msg = document.getElementById("playtest-launch-msg");
    if (!msg) return;
    msg.textContent = text || "";
    msg.classList.toggle("err", Boolean(isError));
  }

  function applyPlaytestDebugState(debugState) {
    if (!debugState || typeof debugState !== "object") return;
    const pos = debugState.position;
    applyLiveState({
      connected: true,
      screen: debugState.screen,
      module: state.graph?.module?.id,
      campaign: debugState.campaign,
      area: pos?.area || null,
      x: pos?.x ?? null,
      y: pos?.y ?? null,
      facing: pos?.facing || null,
      activeEvent: debugState.pendingMenu || debugState.pendingCombat || debugState.pendingShop || null,
      activeActor: debugState.combat?.activeActor || null,
    });
    populatePlaytestPinTargets();
  }

  function populatePlaytestPinTargets() {
    const sel = document.getElementById("playtest-pin-target");
    if (!sel) return;
    const prev = sel.value;
    const options = [];
    const live = state.liveState;
    if (live?.area && live.x !== null && live.x !== undefined) {
      options.push({ key: `cell|${live.area}@[${live.x},${live.y}]`, label: `Cell: ${shortId(live.area)} [${live.x},${live.y}]` });
    }
    if (live?.activeEvent) {
      options.push({ key: `event|${live.activeEvent}`, label: `Event: ${shortId(live.activeEvent)}` });
    }
    if (live?.area) {
      options.push({ key: `area|${live.area}`, label: `Area: ${shortId(live.area)}` });
    }
    if (state.graph?.module?.id) {
      options.push({ key: `module|${state.graph.module.id}`, label: `Module: ${state.graph.module.id}` });
    }
    sel.innerHTML = "";
    for (const opt of options) {
      sel.appendChild(el("option", { value: opt.key }, opt.label));
    }
    if (prev && !prev.startsWith("module|") && options.some((o) => o.key === prev)) {
      sel.value = prev;
    } else if (options.length > 0) {
      sel.value = options[0].key;
    }
  }

  function renderPlaytest() {
    if (state.embedMode) return;
    const st = state.playtest.status;
    const running = Boolean(st?.running && st?.session?.url);
    const badge = document.getElementById("tab-playtest-badge");
    if (badge) {
      badge.textContent = running ? "live" : "off";
      badge.className = running ? "badge badge-live" : "badge";
    }

    const pill = document.getElementById("playtest-status-pill");
    const urlLabel = document.getElementById("playtest-url-label");
    const liveControls = document.getElementById("playtest-live-controls");
    const pinbar = document.getElementById("playtest-pinbar");
    const launchCard = document.getElementById("playtest-launch-card");
    const frameWrap = document.getElementById("playtest-frame-wrap");
    const iframe = document.getElementById("playtest-iframe");
    const popout = document.getElementById("btn-pt-popout");
    const btnPlay = document.getElementById("btn-pt-launch-play");
    const btnQuickstart = document.getElementById("btn-pt-quickstart");

    const isCampaign = state.graph?.module?.kind === "campaign";
    if (btnPlay) btnPlay.hidden = !isCampaign;
    if (btnQuickstart) btnQuickstart.hidden = !isCampaign;

    if (running) {
      const url = st.session.url;
      if (pill) { pill.textContent = st.managedByWorkbench ? "streaming" : "attached"; pill.className = "pill pill-ok"; }
      if (urlLabel) urlLabel.textContent = `${url}${st.session.instance ? ` (${st.session.instance})` : ""}`;
      if (liveControls) liveControls.hidden = false;
      if (pinbar) pinbar.hidden = false;
      if (launchCard) launchCard.hidden = true;
      if (frameWrap) frameWrap.hidden = false;
      if (popout) popout.href = url;
      if (iframe && state.playtest.iframeUrl !== url) {
        state.playtest.iframeUrl = url;
        iframe.src = url;
      }
      populatePlaytestPinTargets();
      startPlaytestPoll();
    } else {
      if (pill) { pill.textContent = state.playtest.busy ? "starting…" : "stopped"; pill.className = state.playtest.busy ? "pill pill-warn" : "pill pill-muted"; }
      if (urlLabel) urlLabel.textContent = st?.available === false ? "Game project not found" : "";
      if (liveControls) liveControls.hidden = true;
      if (pinbar) pinbar.hidden = true;
      if (launchCard) launchCard.hidden = false;
      if (frameWrap) frameWrap.hidden = true;
      if (iframe && state.playtest.iframeUrl) {
        state.playtest.iframeUrl = null;
        iframe.src = "about:blank";
      }
      stopPlaytestPoll();

      const discWrap = document.getElementById("playtest-discovered");
      const discList = document.getElementById("playtest-discovered-list");
      const sessions = st?.sessions || [];
      if (discWrap && discList) {
        discList.innerHTML = "";
        discWrap.hidden = sessions.length === 0;
        for (const s of sessions) {
          const row = el("li", { class: "playtest-session-row" });
          row.appendChild(el("span", {}, `${s.url}${s.label ? ` · ${s.label}` : ""}${s.instance ? ` [${s.instance}]` : ""}`));
          const attachBtn = el("button", { class: "btn btn-sm btn-primary", type: "button" }, "Attach");
          attachBtn.disabled = state.playtest.busy;
          attachBtn.addEventListener("click", () => startPlaytest({ attachUrl: s.url }));
          row.appendChild(attachBtn);
          discList.appendChild(row);
        }
      }
    }

    for (const id of ["btn-pt-launch-play", "btn-pt-launch-title", "btn-pt-stop"]) {
      const b = document.getElementById(id);
      if (b) b.disabled = state.playtest.busy;
    }
  }

  function startPlaytestPoll() {
    if (state.playtest.pollTimer || state.embedMode) return;
    state.playtest.pollTimer = setInterval(() => {
      if (!state.playtest.busy && state.playtest.status?.running) {
        refreshPlaytestStatus(true);
      }
    }, 3500);
  }

  function stopPlaytestPoll() {
    if (state.playtest.pollTimer) {
      clearInterval(state.playtest.pollTimer);
      state.playtest.pollTimer = null;
    }
  }

  async function refreshPlaytestStatus(probe = false) {
    if (state.embedMode) return null;
    try {
      const res = await fetch(`/api/playtest/status${probe ? "?probe=1" : ""}`);
      if (!res.ok) return null;
      const data = await res.json();
      state.playtest.status = data;
      if (data.state) applyPlaytestDebugState(data.state);
      renderPlaytest();
      return data;
    } catch {
      return null;
    }
  }

  async function startPlaytest({ autoStartCampaign = false, attachUrl = null } = {}) {
    if (state.playtest.busy) return null;
    state.playtest.busy = true;
    setPlaytestMsg(attachUrl ? `Attaching to ${attachUrl}…` : "Building & starting streamed Rusty Engine session…");
    renderPlaytest();
    try {
      const seedEl = document.getElementById("playtest-seed");
      const lanEl = document.getElementById("chk-pt-lan");
      const body = attachUrl
        ? { attachUrl }
        : {
          autoStartCampaign: Boolean(autoStartCampaign),
          seed: Number(seedEl?.value || 1) || 1,
          ...(lanEl?.checked ? { bindHost: "0.0.0.0" } : {}),
        };
      const res = await fetch("/api/playtest/start", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      const data = await res.json();
      if (!res.ok || !data.ok) {
        setPlaytestMsg(data.error || `Start failed (HTTP ${res.status})`, true);
        return null;
      }
      setPlaytestMsg(data.startedMessage || `Connected to ${data.session?.url}`);
      const cmdOut = document.getElementById("playtest-cmd-out");
      if (cmdOut && data.startedMessage) cmdOut.textContent = data.startedMessage;
      await refreshPlaytestStatus(false);
      if (data.state) applyPlaytestDebugState(data.state);
      return data;
    } catch (err) {
      setPlaytestMsg(`Start failed: ${err.message}`, true);
      return null;
    } finally {
      state.playtest.busy = false;
      renderPlaytest();
    }
  }

  async function stopPlaytest() {
    if (state.playtest.busy) return;
    state.playtest.busy = true;
    renderPlaytest();
    try {
      const res = await fetch("/api/playtest/stop", { method: "POST" });
      const data = await res.json();
      setPlaytestMsg(data.message || "Stopped.", !data.ok);
      state.liveState = null;
      renderLiveBadge();
      await refreshPlaytestStatus(false);
    } catch (err) {
      setPlaytestMsg(`Stop failed: ${err.message}`, true);
    } finally {
      state.playtest.busy = false;
      renderPlaytest();
    }
  }

  async function runPlaytestCommand(command) {
    const cmdOut = document.getElementById("playtest-cmd-out");
    if (cmdOut) cmdOut.textContent = `Running: ${command}…`;
    try {
      const res = await fetch("/api/playtest/command", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ command }),
      });
      const data = await res.json();
      if (!res.ok || !data.ok) {
        if (cmdOut) cmdOut.textContent = `Error: ${data.error || `HTTP ${res.status}`}`;
        return null;
      }
      if (cmdOut) cmdOut.textContent = data.output || "OK";
      if (data.state) applyPlaytestDebugState(data.state);
      return data;
    } catch (err) {
      if (cmdOut) cmdOut.textContent = `Error: ${err.message}`;
      return null;
    }
  }

  async function jumpInPlaytest({ area = null, x = null, y = null, facing = "north", event = null } = {}) {
    switchTab("playtest");
    const status = await refreshPlaytestStatus(true);
    if (!status?.running) {
      const started = await startPlaytest({ autoStartCampaign: true });
      if (!started?.ok) return;
    } else if (status.state?.screen !== "play" && state.graph?.module?.id) {
      await runPlaytestCommand(`tales.start ${state.graph.module.id} 1`);
    }

    if (event) {
      await runPlaytestCommand(`tales.event ${event}`);
    } else if (area && x !== null && y !== null) {
      await runPlaytestCommand(`tales.goto ${area} ${x} ${y} ${facing || "north"}`);
    }
  }

  async function submitPlaytestQuickPin(pinStatus) {
    const sel = document.getElementById("playtest-pin-target");
    const noteEl = document.getElementById("playtest-pin-note");
    const fb = document.getElementById("playtest-pin-feedback");
    if (!sel?.value) return;
    const [targetKind, ...rest] = sel.value.split("|");
    const target = rest.join("|");
    try {
      await savePin(targetKind, target, pinStatus, noteEl?.value?.trim() || "");
      if (noteEl) noteEl.value = "";
      if (fb) {
        fb.textContent = `Pinned ${pinStatus} on ${shortId(target)}`;
        setTimeout(() => { if (fb.textContent.startsWith("Pinned")) fb.textContent = ""; }, 2600);
      }
    } catch (err) {
      if (fb) fb.textContent = `Pin failed: ${err.message}`;
    }
  }

  document.getElementById("btn-pt-launch-play")?.addEventListener("click", () => startPlaytest({ autoStartCampaign: true }));
  document.getElementById("btn-pt-launch-title")?.addEventListener("click", () => startPlaytest({ autoStartCampaign: false }));
  document.getElementById("btn-pt-stop")?.addEventListener("click", () => stopPlaytest());
  document.getElementById("btn-pt-quickstart")?.addEventListener("click", () => {
    const modId = state.graph?.module?.id;
    const seed = Number(document.getElementById("playtest-seed")?.value || 1) || 1;
    if (modId) runPlaytestCommand(`tales.start ${modId} ${seed}`);
  });
  document.getElementById("btn-pt-heal")?.addEventListener("click", () => runPlaytestCommand("tales.heal"));
  document.getElementById("playtest-combat-view")?.addEventListener("change", (e) => runPlaytestCommand(`tales.combat-view ${e.target.value}`));
  document.getElementById("btn-pt-reload-frame")?.addEventListener("click", () => {
    const iframe = document.getElementById("playtest-iframe");
    if (iframe && state.playtest.iframeUrl) {
      iframe.src = state.playtest.iframeUrl;
    }
  });
  document.getElementById("playtest-cmd-form")?.addEventListener("submit", (e) => {
    e.preventDefault();
    const input = document.getElementById("playtest-cmd-input");
    const cmd = input?.value?.trim();
    if (!cmd) return;
    runPlaytestCommand(cmd);
    input.value = "";
  });
  document.getElementById("btn-pt-pin-fix")?.addEventListener("click", () => submitPlaytestQuickPin("needs-fix"));
  document.getElementById("btn-pt-pin-q")?.addEventListener("click", () => submitPlaytestQuickPin("question"));
  document.getElementById("btn-pt-pin-ok")?.addEventListener("click", () => submitPlaytestQuickPin("approved"));

  // ---------------------------------------------------------------------------
  // PUPPET STUDIO EMBED (Track 1)
  // ---------------------------------------------------------------------------
  function setPuppetsMsg(text, isError = false) {
    const elMsg = document.getElementById("puppets-launch-msg");
    if (elMsg) {
      elMsg.textContent = text || "";
      elMsg.classList.toggle("is-error", Boolean(isError));
    }
    const bar = document.getElementById("puppets-feedback-bar");
    const fb = document.getElementById("puppets-feedback-text");
    if (bar && fb) {
      if (text && state.puppets.status?.running) {
        bar.hidden = false;
        fb.textContent = text;
        fb.style.color = isError ? "var(--err)" : "var(--ok)";
      } else if (!text) {
        bar.hidden = true;
      }
    }
  }

  function renderPuppets() {
    const st = state.puppets.status;
    const badge = document.getElementById("tab-puppets-badge");
    const pill = document.getElementById("puppets-status-pill");
    const wsLabel = document.getElementById("puppets-workspace-label");
    const liveControls = document.getElementById("puppets-live-controls");
    const launchCard = document.getElementById("puppets-launch-card");
    const frameWrap = document.getElementById("puppets-frame-wrap");
    const iframe = document.getElementById("puppets-iframe");
    const popout = document.getElementById("btn-pup-popout");
    const wsInput = document.getElementById("puppets-ws-input");
    const exportSelect = document.getElementById("puppets-export-char");
    const charsBox = document.getElementById("puppets-chars-box");
    const charsList = document.getElementById("puppets-chars-list");

    const running = Boolean(st?.running && st?.url);
    const available = st?.available !== false;
    const chars = st?.characters || [];

    if (badge) {
      badge.textContent = running ? "live" : chars.length > 0 ? String(chars.length) : "off";
      badge.className = `badge ${running ? "badge-ok" : ""}`;
    }
    if (pill) {
      pill.textContent = running ? "running" : state.puppets.busy ? "starting…" : available ? "stopped" : "not installed";
      pill.className = `pill ${running ? "pill-ok" : available ? "pill-muted" : "pill-err"}`;
    }
    if (wsLabel) {
      wsLabel.textContent = st?.workspace || "";
    }
    if (wsInput && !wsInput.value && st?.workspace) {
      wsInput.value = st.workspace;
    }

    if (exportSelect) {
      const prev = exportSelect.value;
      exportSelect.innerHTML = "";
      for (const c of chars) {
        exportSelect.appendChild(el("option", { value: c.id }, `${c.id} (${c.archetype})`));
      }
      if (chars.some((c) => c.id === prev)) exportSelect.value = prev;
    }

    if (liveControls) liveControls.hidden = !running;
    if (launchCard) launchCard.hidden = running;
    if (frameWrap) frameWrap.hidden = !running;

    if (running) {
      if (popout) popout.href = st.url;
      if (iframe && state.puppets.iframeUrl !== st.url) {
        state.puppets.iframeUrl = st.url;
        iframe.src = st.url;
      }
    } else {
      state.puppets.iframeUrl = null;
      if (iframe && iframe.getAttribute("src")) iframe.removeAttribute("src");
    }

    if (charsBox && charsList) {
      charsBox.hidden = chars.length === 0;
      charsList.innerHTML = "";
      for (const c of chars) {
        const li = el("li", { class: "playtest-session-row" });
        const summary = `${c.id} — ${c.name} · audit:${c.auditStatus} · flags:${c.openFlags}/${c.totalFlags}`;
        li.appendChild(el("span", {}, summary));
        if (st?.moduleDir) {
          const expBtn = el("button", { class: "btn btn-sm", type: "button" }, "Export Sheet");
          expBtn.disabled = state.puppets.busy;
          expBtn.addEventListener("click", () => exportPuppet(c.id));
          li.appendChild(expBtn);
        }
        charsList.appendChild(li);
      }
    }

    if (!available) {
      setPuppetsMsg("puppet-studio executable not found. Install puppet-studio on PATH or set PUPPET_STUDIO_BIN.", true);
    }

    for (const id of ["btn-pup-launch", "btn-pup-stop", "btn-pup-export"]) {
      const b = document.getElementById(id);
      if (b) b.disabled = state.puppets.busy || (!available && id === "btn-pup-launch");
    }
  }

  async function refreshPuppetsStatus() {
    if (state.embedMode) return null;
    try {
      const res = await fetch("/api/puppets/status");
      if (!res.ok) return null;
      state.puppets.status = await res.json();
      renderPuppets();
      return state.puppets.status;
    } catch {
      return null;
    }
  }

  async function startPuppets() {
    if (state.puppets.busy) return;
    state.puppets.busy = true;
    setPuppetsMsg("Starting puppet-studio serve…");
    renderPuppets();
    try {
      const wsInput = document.getElementById("puppets-ws-input");
      const workspace = wsInput?.value?.trim() || undefined;
      const res = await fetch("/api/puppets/start", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(workspace ? { workspace } : {}),
      });
      const data = await res.json();
      if (!res.ok || !data.ok) {
        setPuppetsMsg(data.error || `Start failed (HTTP ${res.status})`, true);
        if (data.status) state.puppets.status = data.status;
        return;
      }
      state.puppets.status = data;
      setPuppetsMsg("");
    } catch (err) {
      setPuppetsMsg(`Start failed: ${err.message}`, true);
    } finally {
      state.puppets.busy = false;
      renderPuppets();
    }
  }

  async function stopPuppets() {
    if (state.puppets.busy) return;
    state.puppets.busy = true;
    renderPuppets();
    try {
      const res = await fetch("/api/puppets/stop", { method: "POST" });
      if (res.ok) state.puppets.status = await res.json();
      setPuppetsMsg("Puppet Studio stopped.");
    } catch (err) {
      setPuppetsMsg(`Stop failed: ${err.message}`, true);
    } finally {
      state.puppets.busy = false;
      renderPuppets();
    }
  }

  async function exportPuppet(characterId) {
    if (!characterId || state.puppets.busy) return;
    state.puppets.busy = true;
    setPuppetsMsg(`Exporting ${characterId} into module…`);
    renderPuppets();
    try {
      const wsInput = document.getElementById("puppets-ws-input");
      const workspace = state.puppets.status?.workspace || wsInput?.value?.trim() || undefined;
      const res = await fetch("/api/puppets/export", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ characterId, workspace }),
      });
      const data = await res.json();
      if (!res.ok || !data.ok) {
        setPuppetsMsg(data.error || `Export failed (HTTP ${res.status})`, true);
        return;
      }
      const ex = data.exported;
      setPuppetsMsg(`Exported '${ex.assetId}' (${ex.frameCount}f @ ${ex.frameWidth}×${ex.frameHeight}px) → ${ex.relativePngPath}`);
      await loadGraph(true);
    } catch (err) {
      setPuppetsMsg(`Export failed: ${err.message}`, true);
    } finally {
      state.puppets.busy = false;
      renderPuppets();
    }
  }

  document.getElementById("btn-pup-launch")?.addEventListener("click", () => startPuppets());
  document.getElementById("btn-pup-refresh")?.addEventListener("click", () => refreshPuppetsStatus());
  document.getElementById("btn-pup-stop")?.addEventListener("click", () => stopPuppets());
  document.getElementById("btn-pup-export")?.addEventListener("click", () => {
    const sel = document.getElementById("puppets-export-char");
    if (sel?.value) exportPuppet(sel.value);
  });
  document.getElementById("btn-pup-reload")?.addEventListener("click", () => {
    const iframe = document.getElementById("puppets-iframe");
    if (iframe && state.puppets.iframeUrl) iframe.src = state.puppets.iframeUrl;
  });

  loadGraph(false).then(() => {
    if (!state.embedMode) {
      refreshPlaytestStatus(false);
      refreshPuppetsStatus();
    }
  });
})();
