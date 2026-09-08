const API = "http://127.0.0.1:8420";

// ---- tiny helpers ----------------------------------------------------

function hexToRgb(hex) {
  const n = parseInt(hex.slice(1), 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

function rgbToHex([r, g, b]) {
  return "#" + [r, g, b].map((c) => Math.round(c).toString(16).padStart(2, "0")).join("");
}

function debounce(fn, ms) {
  let t = null;
  return (...args) => {
    clearTimeout(t);
    t = setTimeout(() => fn(...args), ms);
  };
}

async function api(path, options) {
  const res = await fetch(API + path, options);
  return res.json();
}

function get(path) { return api(path); }
function post(path, body) {
  return api(path, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body || {}) });
}
function del(path) { return api(path, { method: "DELETE" }); }

let toastTimer = null;
function toast(msg, isError) {
  const el = document.getElementById("toast");
  el.textContent = msg;
  el.classList.toggle("error", !!isError);
  el.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove("show"), 2200);
}

// ---- state -------------------------------------------------------------

const state = {
  cells: [],           // [{index, name, row, col}]
  cellEls: new Map(),  // index -> DOM element
  effects: [],
  presets: {},
  defaultPreset: null,
  activePreset: null,  // name of the preset card currently considered "active", or null
  gradientExtra: null, // legacy 2-zone overrides (left_overrides/right_overrides/custom_colors) to preserve while live-tuning
  ckCellEls: new Map(),      // index -> DOM element, for the Custom Key Colors editor grid
  ckSelected: new Set(),     // indices currently selected in that editor
  customKeyColors: {},       // {index (str): [r,g,b]} overrides being edited
  customKeyDefault: [0, 0, 0],
  recentColors: [],          // hex strings, most-recent first
};

const RECENT_COLORS_STORAGE_KEY = "jma_studio_recent_colors";
const RECENT_COLORS_MAX = 16;

function loadRecentColors() {
  try {
    const raw = localStorage.getItem(RECENT_COLORS_STORAGE_KEY);
    state.recentColors = raw ? JSON.parse(raw) : [];
  } catch (e) {
    state.recentColors = [];
  }
}

function saveRecentColors() {
  try {
    localStorage.setItem(RECENT_COLORS_STORAGE_KEY, JSON.stringify(state.recentColors));
  } catch (e) {
    // private browsing / storage blocked -- recent colors just won't persist
  }
}

function addRecentColor(hex) {
  state.recentColors = [hex, ...state.recentColors.filter((c) => c !== hex)].slice(0, RECENT_COLORS_MAX);
  saveRecentColors();
  renderRecentColors();
}

function renderRecentColors() {
  const row = document.getElementById("ck-recent-row");
  const empty = document.getElementById("ck-recent-empty");
  row.querySelectorAll(".ck-swatch").forEach((el) => el.remove());
  empty.hidden = state.recentColors.length > 0;
  for (const hex of state.recentColors) {
    const swatch = document.createElement("button");
    swatch.className = "ck-swatch";
    swatch.style.background = hex;
    swatch.title = hex;
    swatch.addEventListener("click", () => applyColorToSelection(hex));
    row.appendChild(swatch);
  }
}

// Known wide keys, in grid units (matches effects/layout.py's stagger).
const KEY_WIDTH = {
  tab: 1.5, caps_lock: 1.75, left_shift: 2.25, right_shift: 1.3,
  backspace: 1.5, enter: 1.75, left_ctrl: 1.25, fn: 1, windows: 1, left_alt: 1,
  space: 5, alt_gr: 1, context_menu: 1, right_ctrl: 1.3, num_0: 1, num_plus: 1, num_enter: 1,
  // Row 0 (function row) keys are all slightly narrower than the
  // standard 1u -- solved so Del's right edge lines up with
  // Backspace's right edge one row down.
  esc: 0.85, f1: 0.85, f2: 0.85, f3: 0.85, f4: 0.85, f5: 0.85, f6: 0.85,
  f7: 0.85, f8: 0.85, f9: 0.85, f10: 0.85, f11: 0.85, f12: 0.85,
  prtsc: 0.85, ins: 0.85, del: 0.85,
  // Media + power share the numpad's standard 1u width instead --
  // no entry needed here, 1 is the default.
};
// Numpad Enter is a real tall key spanning two grid rows, not a wide one.
const KEY_HEIGHT = {
  num_enter: 2,
};
// Friendly keycap text -- letters/digits/F-keys pass through as-is;
// everything else gets a short legend instead of the raw snake_case
// name (which doesn't fit on a key and isn't how real keycaps read).
const FRIENDLY_LABEL = {
  left_ctrl: "Ctrl", right_ctrl: "Ctrl",
  left_shift: "Shift", right_shift: "Shift",
  left_alt: "Alt", alt_gr: "AltGr",
  caps_lock: "Caps", context_menu: "Menu",
  backspace: "⌫", enter: "⏎", num_enter: "⏎", tab: "Tab",
  space: "", windows: "⊞", fn: "Fn",
  up_arrow: "↑", down_arrow: "↓", left_arrow: "←", right_arrow: "→",
  prtsc: "PrSc", numlk: "Num", ins: "Ins", del: "Del",
  backtick: "`", backslash: "\\", left_bracket: "[", right_bracket: "]",
  semicolon: ";", quote: "'", comma: ",", period: ".", slash: "/",
  minus: "-", equals: "=", esc: "Esc",
  predator_key: "\u{1F43E}", power_button: "⏻",
  play_pause: "⏯", fast_forward: "⏭", rewind: "⏮",
  num_decimal: ".", num_divide: "/", num_multiply: "*", num_plus: "+", num_minus: "-",
};

function friendlyLabel(name) {
  if (name in FRIENDLY_LABEL) return FRIENDLY_LABEL[name];
  if (name.startsWith("num_")) return name.slice(4);
  return name.length <= 3 ? name.toUpperCase() : name;
}

// ---- keyboard preview ----------------------------------------------------

// Shared by the live-preview board and the Custom Key Colors editor
// grid -- both lay out the same physical key positions, just into
// different containers with different per-cell behavior (live color
// polling vs. click-to-select).
function buildKeyboardGrid(cells, boardId, cellElsMap, decorateCell) {
  const board = document.getElementById(boardId);
  const panel = board.closest(".panel");
  board.innerHTML = "";
  cellElsMap.clear();

  let maxCol = 0, maxRow = 0;
  for (const c of cells) {
    maxCol = Math.max(maxCol, c.col);
    maxRow = Math.max(maxRow, c.row);
  }
  const totalCols = maxCol + 3;
  const totalRows = maxRow + 1.4;

  // Scale the whole board to fit the panel's width so it's never
  // clipped/scrolled -- rather than a fixed px-per-unit that only
  // happens to fit some window sizes.
  const availableWidth = panel.clientWidth - 40;
  const unit = Math.max(16, Math.min(34, availableWidth / totalCols));

  board.style.width = `${totalCols * unit}px`;
  board.style.height = `${totalRows * unit}px`;

  for (const c of cells) {
    const width = (KEY_WIDTH[c.name] || 1) * unit - 4;
    const height = (KEY_HEIGHT[c.name] || 1) * unit - 4;
    const el = document.createElement("div");
    el.className = "key";
    el.title = c.name;
    el.textContent = friendlyLabel(c.name);
    el.style.left = `${c.col * unit}px`;
    el.style.top = `${c.row * unit}px`;
    el.style.width = `${width}px`;
    el.style.height = `${height}px`;
    el.style.fontSize = `${Math.max(7, Math.min(11, unit * 0.28))}px`;
    board.appendChild(el);
    cellElsMap.set(c.index, el);
    if (decorateCell) decorateCell(el, c);
  }
}

function buildKeyboard(cells) {
  buildKeyboardGrid(cells, "keyboard", state.cellEls);
}

async function pollFrame() {
  try {
    const data = await get("/frame");
    for (const [idxStr, el] of state.cellEls) {
      const c = data.colors[idxStr];
      if (c) el.style.backgroundColor = `rgb(${c[0]},${c[1]},${c[2]})`;
    }
    document.getElementById("current-effect-label").textContent = data.current_effect || "--";
  } catch (e) {
    // daemon not reachable this tick -- try again next poll
  }
}

// ---- custom key colors editor --------------------------------------------

function buildCustomKeyboard(cells) {
  buildKeyboardGrid(cells, "ck-keyboard", state.ckCellEls, (el, c) => {
    el.addEventListener("click", (e) => {
      toggleKeySelection(c.index, e.shiftKey || e.ctrlKey || e.metaKey);
    });
  });
  renderCustomKeyboardColors();
  updateSelectionVisual();
}

function renderCustomKeyboardColors() {
  const [dr, dg, db] = state.customKeyDefault;
  for (const [idxStr, el] of state.ckCellEls) {
    const override = state.customKeyColors[idxStr];
    const [r, g, b] = override || [dr, dg, db];
    el.style.backgroundColor = `rgb(${r},${g},${b})`;
  }
}

function currentColorForIndex(idxStr) {
  return state.customKeyColors[idxStr] || state.customKeyDefault;
}

function updateSelectionVisual() {
  for (const [idxStr, el] of state.ckCellEls) {
    el.classList.toggle("selected", state.ckSelected.has(idxStr));
  }
  const count = state.ckSelected.size;
  const label = document.getElementById("ck-selection-label");
  const picker = document.getElementById("ck-picker");
  if (count === 0) {
    label.textContent = "No keys selected -- click a key to select it (shift-click to add more)";
    picker.disabled = true;
  } else {
    const names = [...state.ckSelected]
      .map((idxStr) => state.cells.find((c) => String(c.index) === idxStr))
      .filter(Boolean)
      .map((c) => friendlyLabel(c.name) || c.name);
    label.textContent = count === 1
      ? `Selected: ${names[0]}`
      : `Selected ${count} keys: ${names.slice(0, 6).join(", ")}${count > 6 ? ", ..." : ""}`;
    picker.disabled = false;
    // Reflect the (first) selected key's actual current color -- an
    // already-painted key shows its own color when selected, rather
    // than the picker holding onto whatever was last applied elsewhere.
    picker.value = rgbToHex(currentColorForIndex([...state.ckSelected][0]));
  }
}

function toggleKeySelection(idx, additive) {
  const idxStr = String(idx);
  if (!additive) {
    const wasOnlySelected = state.ckSelected.size === 1 && state.ckSelected.has(idxStr);
    state.ckSelected.clear();
    if (!wasOnlySelected) state.ckSelected.add(idxStr);
  } else if (state.ckSelected.has(idxStr)) {
    state.ckSelected.delete(idxStr);
  } else {
    state.ckSelected.add(idxStr);
  }
  updateSelectionVisual();
}

function applyColorToSelection(hex) {
  if (state.ckSelected.size === 0) return;
  const rgb = hexToRgb(hex);
  for (const idxStr of state.ckSelected) {
    state.customKeyColors[idxStr] = rgb;
  }
  document.getElementById("ck-picker").value = hex;
  addRecentColor(hex);
  renderCustomKeyboardColors();
  applyCustomKeysLive();
}

function readCustomKeysParams() {
  return {
    colors: state.customKeyColors,
    default_color: state.customKeyDefault,
  };
}

const applyCustomKeysLive = debounce(async () => {
  if (!document.getElementById("ck-live").checked) return;
  await post("/effect", { name: "custom_keys", params: readCustomKeysParams() });
  state.activePreset = null;
  await renderPresets();
}, 120);

function wireCustomKeysPanel() {
  document.getElementById("ck-picker").addEventListener("input", (e) => {
    applyColorToSelection(e.target.value);
  });
  document.getElementById("ck-default-color").addEventListener("input", (e) => {
    state.customKeyDefault = hexToRgb(e.target.value);
    renderCustomKeyboardColors();
    applyCustomKeysLive();
  });
  document.getElementById("ck-select-all").addEventListener("click", () => {
    state.ckSelected = new Set(state.cells.map((c) => String(c.index)));
    updateSelectionVisual();
  });
  document.getElementById("ck-select-none").addEventListener("click", () => {
    state.ckSelected.clear();
    updateSelectionVisual();
  });
  document.getElementById("ck-reset-selected").addEventListener("click", () => {
    for (const idxStr of state.ckSelected) delete state.customKeyColors[idxStr];
    renderCustomKeyboardColors();
    applyCustomKeysLive();
  });
  document.getElementById("ck-clear-all").addEventListener("click", () => {
    if (!confirm("Clear all custom key colors?")) return;
    state.customKeyColors = {};
    renderCustomKeyboardColors();
    applyCustomKeysLive();
  });
  document.getElementById("ck-pull-current").addEventListener("click", async () => {
    // Snapshots whatever's actually lit right now (any effect -- a
    // gradient, a preset, even mid-chase) into per-key overrides, so
    // "set up a gradient, then pull it into custom and edit it" works
    // as a starting point rather than starting from a blank board.
    const frame = await get("/frame");
    const colors = frame.colors || [];
    const overrides = {};
    for (const c of state.cells) {
      const rgb = colors[c.index];
      if (rgb) overrides[String(c.index)] = rgb;
    }
    state.customKeyColors = overrides;
    renderCustomKeyboardColors();
    applyCustomKeysLive();
    toast("Pulled current keyboard colors into the custom editor");
  });
  loadRecentColors();
  renderRecentColors();
}

// ---- hardware status -----------------------------------------------------

async function refreshStatus() {
  try {
    const s = await get("/status");
    const el = document.getElementById("hw-status");
    el.classList.toggle("connected", !!s.hardware_connected);
  } catch (e) {
    document.getElementById("hw-status").classList.remove("connected");
  }
}

// ---- presets -------------------------------------------------------------

function presetSwatch(preset) {
  const p = preset.params || {};
  if (preset.effect === "gradient") {
    const hexColors = p.colors
      ? p.colors.map(rgbToHex)
      : [rgbToHex(p.left_color || [20, 90, 230]), rgbToHex(p.right_color || [200, 20, 160])];
    return `linear-gradient(90deg, ${hexColors.join(", ")})`;
  }
  if (preset.effect === "typing_reactive") {
    if (p.base_effect === "gradient") {
      return presetSwatch({ effect: "gradient", params: p.base_params || {} });
    }
    if (p.base_effect === "custom_keys") {
      return presetSwatch({ effect: "custom_keys", params: p.base_params || {} });
    }
    return rgbToHex(p.base_color || [38, 38, 38]);
  }
  if (preset.effect === "custom_keys") {
    const overrideHexes = Object.values(p.colors || {}).slice(0, 5).map(rgbToHex);
    const stops = [rgbToHex(p.default_color || [0, 0, 0]), ...overrideHexes];
    return stops.length > 1 ? `linear-gradient(90deg, ${stops.join(", ")})` : stops[0];
  }
  return "linear-gradient(90deg, #333, #333)";
}

async function renderPresets() {
  const grid = document.getElementById("preset-grid");
  grid.innerHTML = "";
  const names = Object.keys(state.presets).sort();
  if (names.length === 0) {
    grid.innerHTML = `<div class="panel-sub">No presets saved yet.</div>`;
    return;
  }
  for (const name of names) {
    const preset = state.presets[name];
    const card = document.createElement("div");
    card.className = "preset-card" + (state.activePreset === name ? " active" : "");
    card.style.setProperty("--swatch", presetSwatch(preset));
    card.innerHTML = `
      <div style="position:absolute;inset:0;background:${presetSwatch(preset)};opacity:0.18;"></div>
      <button class="preset-delete" data-name="${name}">&times;</button>
      <div class="preset-card-name">${name}</div>
      <div class="preset-card-effect">${preset.effect}</div>
      <div class="preset-card-badges">
        ${state.defaultPreset === name ? '<span class="badge default">default</span>' : ""}
      </div>`;
    card.addEventListener("click", (e) => {
      if (e.target.closest(".preset-delete")) return;
      applyPreset(name);
    });
    card.querySelector(".preset-delete").addEventListener("click", async (e) => {
      e.stopPropagation();
      if (!confirm(`Delete preset "${name}"?`)) return;
      await del(`/presets/${encodeURIComponent(name)}`);
      await loadPresets();
    });
    grid.appendChild(card);
  }
}

async function loadPresets() {
  state.presets = await get("/presets");
  const d = await get("/default");
  state.defaultPreset = d.default_preset;
  await renderPresets();
}

async function applyPreset(name) {
  const res = await post(`/presets/${encodeURIComponent(name)}/apply`);
  if (!res.ok) { toast(res.error || "failed to apply preset", true); return; }
  state.activePreset = name;
  syncTuningPanelsFromPreset(state.presets[name]);
  await renderPresets();
  renderEffectChips();
  toast(`Applied "${name}"`);
}

// ---- quick effect chips ----------------------------------------------------

const HIDDEN_FROM_CHIPS = new Set(["probe", "mask", "gradient", "typing_reactive", "static", "custom_keys"]);

function renderEffectChips() {
  const grid = document.getElementById("effect-grid");
  grid.innerHTML = "";
  for (const name of state.effects) {
    if (HIDDEN_FROM_CHIPS.has(name)) continue;
    const chip = document.createElement("button");
    chip.className = "chip" + (state.activePreset === null && currentEffectName === name ? " active" : "");
    chip.textContent = name;
    chip.addEventListener("click", async () => {
      await post("/effect", { name, params: {} });
      state.activePreset = null;
      currentEffectName = name;
      await renderPresets();
      renderEffectChips();
      toast(`Activated "${name}"`);
    });
    grid.appendChild(chip);
  }
}
let currentEffectName = null;

// ---- gradient tuning ----------------------------------------------------

// state.gradientExtra holds left_overrides/right_overrides/custom_colors
// carried over from a loaded legacy 2-zone preset (the PredatorSense
// hardware-quirk patches) -- kept alive across live-tuning as long as
// the zone count stays at 2, so nudging a slider doesn't silently wipe
// out e.g. "backslash forced blue". Switching to 3+ zones drops it,
// since those overrides only make sense for the original 2-zone split.
const DEFAULT_ZONE_ENDPOINTS = [[20, 90, 230], [200, 20, 160]]; // blue -> pink

function defaultZoneColors(count) {
  if (count === 2) return DEFAULT_ZONE_ENDPOINTS.map((c) => [...c]);
  const [a, b] = DEFAULT_ZONE_ENDPOINTS;
  const colors = [];
  for (let i = 0; i < count; i++) {
    const f = i / (count - 1);
    colors.push(a.map((c, ch) => Math.round(c + (b[ch] - c) * f)));
  }
  return colors;
}

function defaultBoundaries(count) {
  if (count === 2) return [13.5];
  const min = 0, max = 20;
  const boundaries = [];
  for (let i = 1; i < count; i++) {
    boundaries.push(Math.round((min + (max - min) * (i / count)) * 4) / 4);
  }
  return boundaries;
}

function updateBoundaryLabel(rangeEl) {
  const label = document.querySelector(`.g-boundary-val[data-idx="${rangeEl.dataset.idx}"]`);
  if (label) label.textContent = rangeEl.value;
}
function updateAllBoundaryLabels() {
  for (const el of document.querySelectorAll(".g-zone-boundary")) updateBoundaryLabel(el);
}

function renderGradientZoneFields(count, colors, boundaries) {
  const colorsRow = document.getElementById("g-colors-row");
  const boundariesRow = document.getElementById("g-boundaries-row");
  colorsRow.innerHTML = "";
  boundariesRow.innerHTML = "";

  for (let i = 0; i < count; i++) {
    const wrap = document.createElement("div");
    wrap.className = "subfield";
    wrap.innerHTML = `<span>Zone ${i + 1}</span><input type="color" class="g-zone-color" data-zone="${i}" value="${rgbToHex(colors[i])}">`;
    colorsRow.appendChild(wrap);
  }
  for (let i = 0; i < count - 1; i++) {
    const wrap = document.createElement("div");
    wrap.className = "subfield";
    wrap.innerHTML = `<span>Boundary ${i + 1} <em class="g-boundary-val" data-idx="${i}"></em></span>
      <input type="range" class="g-zone-boundary" data-idx="${i}" min="0" max="25" step="0.25" value="${boundaries[i]}">`;
    boundariesRow.appendChild(wrap);
  }

  for (const el of colorsRow.querySelectorAll(".g-zone-color")) {
    el.addEventListener("input", applyGradientLive);
  }
  for (const el of boundariesRow.querySelectorAll(".g-zone-boundary")) {
    el.addEventListener("input", (e) => {
      updateBoundaryLabel(e.target);
      applyGradientLive();
    });
  }
  updateAllBoundaryLabels();
}

function readGradientZoneColors() {
  return [...document.querySelectorAll(".g-zone-color")]
    .sort((a, b) => a.dataset.zone - b.dataset.zone)
    .map((el) => hexToRgb(el.value));
}
function readGradientBoundaries() {
  return [...document.querySelectorAll(".g-zone-boundary")]
    .sort((a, b) => a.dataset.idx - b.dataset.idx)
    .map((el) => parseFloat(el.value));
}

function readGradientParams() {
  const colors = readGradientZoneColors();
  const params = {
    colors,
    boundaries: readGradientBoundaries(),
    brightness: parseFloat(document.getElementById("g-brightness").value),
    hard: true,
  };
  if (colors.length === 2 && state.gradientExtra) {
    Object.assign(params, state.gradientExtra);
  }
  return params;
}

function updateGradientLabels() {
  updateAllBoundaryLabels();
  document.getElementById("g-brightness-val").textContent =
    Math.round(document.getElementById("g-brightness").value * 100) + "%";
}

const applyGradientLive = debounce(async () => {
  if (!document.getElementById("gradient-live").checked) return;
  await applyCurrentLive();
}, 120);

function setGradientZoneCount(count, colors, boundaries) {
  document.getElementById("g-zone-count").value = count;
  renderGradientZoneFields(count, colors || defaultZoneColors(count), boundaries || defaultBoundaries(count));
}

function wireGradientPanel() {
  document.getElementById("g-zone-count").addEventListener("change", (e) => {
    state.gradientExtra = null; // changing zone count abandons legacy overrides
    setGradientZoneCount(parseInt(e.target.value, 10));
    applyGradientLive();
  });
  document.getElementById("g-brightness").addEventListener("input", () => {
    updateGradientLabels();
    applyGradientLive();
  });
  setGradientZoneCount(2, defaultZoneColors(2), defaultBoundaries(2));
  updateGradientLabels();
}

// ---- typing reactive tuning ----------------------------------------------

function readTypingReactiveParams() {
  const params = {
    bright_color: hexToRgb(document.getElementById("tr-bright").value),
    decay_seconds: parseFloat(document.getElementById("tr-decay").value),
    bolt_shape: document.getElementById("tr-shape").value,
    bolt_style: document.getElementById("tr-style").value,
    bolt_speed: parseFloat(document.getElementById("tr-speed").value),
    bolt_tail: parseFloat(document.getElementById("tr-tail").value),
    bolt_max_distance: parseFloat(document.getElementById("tr-maxdist").value),
    bolt_reset: document.getElementById("tr-bolt-reset").checked,
    bolt_flicker_speed: parseFloat(document.getElementById("tr-flicker").value),
  };
  if (document.getElementById("tr-use-gradient").checked) {
    params.base_effect = "gradient";
    params.base_params = readGradientParams();
  } else if (document.getElementById("tr-use-custom-keys").checked) {
    params.base_effect = "custom_keys";
    params.base_params = readCustomKeysParams();
  } else {
    params.base_color = hexToRgb(document.getElementById("tr-base").value);
  }
  return params;
}

function updateTypingReactiveLabels() {
  document.getElementById("tr-decay-val").textContent = document.getElementById("tr-decay").value + "s";
  document.getElementById("tr-speed-val").textContent = document.getElementById("tr-speed").value;
  document.getElementById("tr-tail-val").textContent = document.getElementById("tr-tail").value;
  document.getElementById("tr-maxdist-val").textContent = document.getElementById("tr-maxdist").value;
  document.getElementById("tr-flicker-val").textContent = document.getElementById("tr-flicker").value;
  document.getElementById("tr-base-color-field").hidden =
    document.getElementById("tr-use-gradient").checked || document.getElementById("tr-use-custom-keys").checked;
  document.getElementById("tr-flicker-field").hidden = document.getElementById("tr-style").value !== "rainbow";
  document.getElementById("tr-reactive-fields").hidden = !document.getElementById("tr-enabled").checked;
}

// Both tuning panels' live-apply funnel through here, gated by
// tr-enabled -- this is the single place that decides whether the
// currently-tuned gradient should go out wrapped in typing_reactive or
// on its own. Without this, tuning the gradient panel (e.g. changing
// the zone count) always posted a bare "gradient" effect regardless of
// whether typing_reactive was the active mode, silently dropping the
// chase wrapper every time -- the exact bug reported after adding the
// multi-zone dropdown.
async function applyCurrentLive() {
  if (document.getElementById("tr-enabled").checked) {
    await post("/effect", { name: "typing_reactive", params: readTypingReactiveParams() });
  } else if (document.getElementById("tr-use-gradient").checked) {
    await post("/effect", { name: "gradient", params: readGradientParams() });
  } else if (document.getElementById("tr-use-custom-keys").checked) {
    await post("/effect", { name: "custom_keys", params: readCustomKeysParams() });
  } else {
    await post("/effect", { name: "static", params: { color: hexToRgb(document.getElementById("tr-base").value) } });
  }
  state.activePreset = null;
  await renderPresets();
}

const applyTypingReactiveLive = debounce(async () => {
  if (!document.getElementById("tr-live").checked) return;
  await applyCurrentLive();
}, 120);

function wireTypingReactivePanel() {
  const ids = ["tr-enabled", "tr-bolt-reset", "tr-base", "tr-bright", "tr-decay", "tr-shape", "tr-style", "tr-speed", "tr-tail", "tr-maxdist", "tr-flicker"];
  for (const id of ids) {
    document.getElementById(id).addEventListener("input", () => {
      updateTypingReactiveLabels();
      applyTypingReactiveLive();
    });
  }

  // "Use Gradient panel as background" and "Use Custom Key Colors as
  // background" are mutually exclusive -- checking one always unchecks
  // the other, so base_effect (a single string on the daemon side) is
  // never ambiguous. Both unchecked falls back to the flat base color.
  document.getElementById("tr-use-gradient").addEventListener("input", (e) => {
    if (e.target.checked) document.getElementById("tr-use-custom-keys").checked = false;
    updateTypingReactiveLabels();
    applyTypingReactiveLive();
  });
  document.getElementById("tr-use-custom-keys").addEventListener("input", (e) => {
    if (e.target.checked) document.getElementById("tr-use-gradient").checked = false;
    updateTypingReactiveLabels();
    applyTypingReactiveLive();
  });

  updateTypingReactiveLabels();
}

// ---- sync tuning panels when a preset is applied --------------------------

function syncTuningPanelsFromPreset(preset) {
  if (!preset) return;
  const p = preset.params || {};
  let gradientParams = null;

  document.getElementById("tr-enabled").checked = preset.effect === "typing_reactive";

  if (preset.effect === "custom_keys") {
    state.customKeyColors = { ...(p.colors || {}) };
    state.customKeyDefault = p.default_color || [0, 0, 0];
    document.getElementById("ck-default-color").value = rgbToHex(state.customKeyDefault);
    state.ckSelected.clear();
    renderCustomKeyboardColors();
    updateSelectionVisual();
  }

  if (preset.effect === "gradient") {
    gradientParams = p;
  } else if (preset.effect === "typing_reactive") {
    document.getElementById("tr-bright").value = rgbToHex(p.bright_color || [255, 255, 255]);
    document.getElementById("tr-decay").value = p.decay_seconds ?? 0.6;
    document.getElementById("tr-shape").value = p.bolt_shape || "rays";
    document.getElementById("tr-style").value = p.bolt_style || "solid";
    document.getElementById("tr-speed").value = p.bolt_speed ?? 12;
    document.getElementById("tr-tail").value = p.bolt_tail ?? 3;
    document.getElementById("tr-maxdist").value = p.bolt_max_distance ?? 18.5;
    document.getElementById("tr-bolt-reset").checked = p.bolt_reset ?? false;
    document.getElementById("tr-flicker").value = p.bolt_flicker_speed ?? 6;
    document.getElementById("tr-use-gradient").checked = p.base_effect === "gradient";
    document.getElementById("tr-use-custom-keys").checked = p.base_effect === "custom_keys";
    if (p.base_effect === "gradient") {
      gradientParams = p.base_params || {};
    } else if (p.base_effect === "custom_keys") {
      const bp = p.base_params || {};
      state.customKeyColors = { ...(bp.colors || {}) };
      state.customKeyDefault = bp.default_color || [0, 0, 0];
      document.getElementById("ck-default-color").value = rgbToHex(state.customKeyDefault);
      state.ckSelected.clear();
      renderCustomKeyboardColors();
      updateSelectionVisual();
    } else {
      document.getElementById("tr-base").value = rgbToHex(p.base_color || [38, 38, 38]);
    }
  }
  updateTypingReactiveLabels();

  if (gradientParams) {
    document.getElementById("g-brightness").value = gradientParams.brightness ?? 0.65;
    if (gradientParams.colors) {
      state.gradientExtra = null;
      setGradientZoneCount(
        gradientParams.colors.length,
        gradientParams.colors,
        gradientParams.boundaries || defaultBoundaries(gradientParams.colors.length)
      );
    } else {
      // Legacy 2-zone preset -- keep its hardware-quirk overrides
      // alive so live-tuning afterward doesn't silently drop them.
      state.gradientExtra = {
        left_overrides: gradientParams.left_overrides,
        right_overrides: gradientParams.right_overrides,
        custom_colors: gradientParams.custom_colors,
      };
      setGradientZoneCount(
        2,
        [gradientParams.left_color || [20, 90, 230], gradientParams.right_color || [200, 20, 160]],
        [gradientParams.boundary ?? 13.5]
      );
    }
    updateGradientLabels();
  }
}

// ---- footer actions --------------------------------------------------------

function wireFooter() {
  document.getElementById("lightbar-btn").addEventListener("click", () => {
    if (window.pywebview && window.pywebview.api && window.pywebview.api.open_lightbar) {
      window.pywebview.api.open_lightbar();
    } else {
      toast("Lightbar window requires the desktop app");
    }
  });

  document.getElementById("off-btn").addEventListener("click", async () => {
    await post("/off");
    state.activePreset = null;
    currentEffectName = null;
    await renderPresets();
    renderEffectChips();
    toast("Off");
  });

  document.getElementById("save-preset-btn").addEventListener("click", () => {
    document.getElementById("modal-name").value = "";
    document.getElementById("modal-backdrop").hidden = false;
    document.getElementById("modal-name").focus();
  });
  document.getElementById("modal-cancel").addEventListener("click", () => {
    document.getElementById("modal-backdrop").hidden = true;
  });
  document.getElementById("modal-backdrop").addEventListener("click", (e) => {
    if (e.target.id === "modal-backdrop") document.getElementById("modal-backdrop").hidden = true;
  });
  document.getElementById("modal-confirm").addEventListener("click", async () => {
    const name = document.getElementById("modal-name").value.trim();
    if (!name) return;
    await post("/presets/save", { name });
    document.getElementById("modal-backdrop").hidden = true;
    state.activePreset = name;
    await loadPresets();
    toast(`Saved "${name}"`);
  });
  document.getElementById("modal-name").addEventListener("keydown", (e) => {
    if (e.key === "Enter") document.getElementById("modal-confirm").click();
  });

  document.getElementById("set-default-btn").addEventListener("click", async () => {
    if (!state.activePreset) {
      toast("Apply or save a preset first", true);
      return;
    }
    const res = await post("/default", { name: state.activePreset });
    if (!res.ok) { toast(res.error || "failed", true); return; }
    state.defaultPreset = state.activePreset;
    await renderPresets();
    toast(`"${state.activePreset}" is now the startup default`);
  });
}

// ---- keydown forwarding (focus-independent input path) --------------------
//
// The daemon's global OS-level keyboard hook (daemon/input_listener.py)
// stops seeing real keystrokes whenever this window's page content
// actually has keyboard focus -- confirmed empirically: works fine
// while the app is open but unfocused (e.g. alt-tabbed away), breaks
// the instant the page is focused again, regardless of which top-level
// window the OS considers foreground. WebView2's embedded Chromium
// control appears to consume the keystrokes before Windows' global
// hook chain reaches the daemon process. Since a focused page is
// guaranteed to receive normal browser keydown events for anything
// physically typed, this forwards those to the daemon as a second,
// focus-independent path -- it's additive with the OS hook (both can
// fire for the same press with no ill effect; the effects already
// support overlapping presses by design).
const CODE_TO_KEYMAP_NAME = {
  Backquote: "backtick", Minus: "minus", Equal: "equals",
  BracketLeft: "left_bracket", BracketRight: "right_bracket",
  Backslash: "backslash", Semicolon: "semicolon", Quote: "quote",
  Comma: "comma", Period: "period", Slash: "slash",
  Space: "space", Tab: "tab", CapsLock: "caps_lock", Enter: "enter",
  Backspace: "backspace", Escape: "esc", ContextMenu: "context_menu",
  ControlLeft: "left_ctrl", ControlRight: "right_ctrl",
  ShiftLeft: "left_shift", ShiftRight: "right_shift",
  AltLeft: "left_alt", AltRight: "alt_gr",
  MetaLeft: "windows", MetaRight: "windows",
  ArrowUp: "up_arrow", ArrowDown: "down_arrow",
  ArrowLeft: "left_arrow", ArrowRight: "right_arrow",
  Insert: "ins", Delete: "del", PrintScreen: "prtsc",
  NumLock: "numlk", NumpadEnter: "num_enter", NumpadDecimal: "num_decimal",
  NumpadAdd: "num_plus", NumpadSubtract: "num_minus",
  NumpadMultiply: "num_multiply", NumpadDivide: "num_divide",
  Numpad0: "num_0", Numpad1: "num_1", Numpad2: "num_2", Numpad3: "num_3",
  Numpad4: "num_4", Numpad5: "num_5", Numpad6: "num_6", Numpad7: "num_7",
  Numpad8: "num_8", Numpad9: "num_9",
  MediaTrackNext: "fast_forward", MediaTrackPrevious: "rewind",
  MediaPlayPause: "play_pause",
};

function codeToKeymapName(code) {
  if (code in CODE_TO_KEYMAP_NAME) return CODE_TO_KEYMAP_NAME[code];
  let m = code.match(/^Key([A-Z])$/);
  if (m) return m[1].toLowerCase();
  m = code.match(/^Digit([0-9])$/);
  if (m) return m[1];
  m = code.match(/^F([0-9]{1,2})$/);
  if (m) return "f" + m[1];
  return null;
}

// Only actual text-entry fields should suppress forwarding (e.g. typing
// a name into the save-preset modal). Range sliders, color pickers, and
// checkboxes are all <input> too, but they keep keyboard focus after a
// mouse interaction until something else is clicked -- excluding every
// <input> tag meant physical typing silently stopped forwarding for as
// long as, say, a tuning slider still had focus. Confirmed by the user:
// changing anything in the Typing Reactive panel broke the chase until
// focus moved off the app entirely.
const TEXT_ENTRY_INPUT_TYPES = new Set(["text", "search", "email", "url", "tel", "password", "number"]);
function isTypingIntoTextField() {
  const el = document.activeElement;
  if (!el) return false;
  if (el.tagName === "TEXTAREA" || el.isContentEditable) return true;
  return el.tagName === "INPUT" && TEXT_ENTRY_INPUT_TYPES.has(el.type);
}

function wireKeypressForwarding() {
  window.addEventListener("keydown", (e) => {
    if (isTypingIntoTextField()) return; // typing into the app's own UI, not "using the keyboard"
    const name = codeToKeymapName(e.code);
    if (!name) return;
    post("/keypress", { name }).catch(() => {});
  });
}

// ---- boot -----------------------------------------------------------------

async function init() {
  const layout = await get("/layout");
  state.cells = layout.cells;
  buildKeyboard(state.cells);
  buildCustomKeyboard(state.cells);

  const effectsData = await get("/effects");
  state.effects = effectsData.available;
  currentEffectName = effectsData.current;

  await loadPresets();
  renderEffectChips();
  wireGradientPanel();
  wireTypingReactivePanel();
  wireCustomKeysPanel();
  wireFooter();
  wireKeypressForwarding();
  await refreshStatus();

  setInterval(pollFrame, 50);
  setInterval(refreshStatus, 3000);

  window.addEventListener("resize", debounce(() => {
    buildKeyboard(state.cells);
    buildCustomKeyboard(state.cells);
  }, 150));
}

init().catch((e) => {
  console.error(e);
  toast("Could not reach the daemon on :8420", true);
});
