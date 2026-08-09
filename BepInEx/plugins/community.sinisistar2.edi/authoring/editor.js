'use strict';

// funscript authoring GUI. Every position is drawn by the user; the MOD never generates,
// interpolates, or mirrors a waveform (SPEC001 FR-039).

const state = {
  catalog: null,
  triggers: [],
  selected: null,
  variant: null,
  // variant name -> [{ at, pos }]
  scripts: {},
  duration: 1000,
  // Key of the trigger the game is playing right now, or null.
  playingKey: null,
  followPlaying: true,
  // 'trigger' edits catalogued stages; 'filler' edits the idle waveforms.
  mode: 'trigger',
  fillers: [],
  // Selected filler descriptor when mode === 'filler'.
  filler: null,
};

const isFillerMode = () => state.mode === 'filler';

// The canvas is editable whenever something is loaded, whether that is a trigger or a filler.
const isEditing = () => (isFillerMode() ? state.filler : state.selected) !== null;

const el = (id) => document.getElementById(id);

const keyOf = (t) => `${t.context}|${t.actorId}|${t.animationId}|${t.phase}|${t.stageId}`;

const keyPayload = (t) => ({
  context: t.context,
  actorId: t.actorId,
  animationId: t.animationId,
  phase: t.phase,
  stageId: t.stageId,
});

// Matches EventKey.ToString() on the MOD side, which is how link results name their targets.
const keyText = (t) => `${t.context}/${t.actorId}/${t.animationId}/${t.phase}/${t.stageId}`;

// A stage that has not been played, or whose binder could not be named, can never be mapped, so it
// is not offered as a target either (FR-060).
const isAuthorable = (t) => t.animationId !== '*' && t.actorId !== 'unidentified-binder';

const clamp = (value, min, max) => Math.min(max, Math.max(min, value));

function setStatus(kind, text) {
  const node = el('status');
  node.className = `status ${kind}`;
  node.textContent = text;
}

async function api(path, options) {
  const response = await fetch(path, options);
  const body = await response.json().catch(() => null);
  return { ok: response.ok, status: response.status, body };
}

// ---------------------------------------------------------------- catalog

async function loadCatalog() {
  const { ok, body } = await api('/api/catalog');
  if (!ok || !body) {
    setStatus('err', 'カタログを取得できませんでした。ゲームが起動しているか確認してください。');
    return;
  }

  state.catalog = body;
  state.triggers = body.triggers;
  el('buildInfo').textContent =
    `mapping ${body.mappingVersion} / build ${body.gameBuild.gameAssemblySha256.slice(0, 12)}…`;
  renderCatalog();
}

function matchesNeedle(t, needle) {
  if (!needle) {
    return true;
  }
  return [
    t.actorDisplayName, t.actorId, t.displayName, t.stageId, t.animationId, t.context,
    // So "#2" and "2" both find the stage shown as 2 in the game.
    typeof t.displayNumber === 'number' ? `#${t.displayNumber}` : null,
  ]
    .filter(Boolean)
    .some((value) => String(value).toLowerCase().includes(needle));
}

function visibleTriggers() {
  const needle = el('filter').value.trim().toLowerCase();
  return needle ? state.triggers.filter((t) => matchesNeedle(t, needle)) : state.triggers;
}

// Prefer the game's own localized names; fall back to the internal identifiers.
const actorLabel = (t) => t.actorDisplayName || t.actorId;
const stageLabel = (t) => t.displayName || t.stageId;

// The in-game viewer selects stages by the number shown along the top of the screen, so that
// number is what ties a row here to what is on screen. It is inferred, not reported by the game,
// so the underlying values are shown too and the label says so.
const numberLabel = (t) => (typeof t.displayNumber === 'number' ? `#${t.displayNumber}` : '#?');

function numberTooltip(t) {
  const parts = [];
  parts.push(typeof t.stageNumber === 'number' ? `SelectID=${t.stageNumber}` : 'SelectID=なし');
  parts.push(typeof t.stageIndex === 'number' ? `配列位置=${t.stageIndex}` : '配列位置=なし');
  return `ギャラリー画面上部の番号タブに対応する推定値\n${parts.join(' / ')}`;
}

function renderCatalog() {
  if (isFillerMode()) {
    renderFillers();
    return;
  }

  const list = el('catalog');
  const items = visibleTriggers();
  el('catalogCount').textContent = `${items.length} / ${state.triggers.length} 段階`;
  list.replaceChildren();

  for (const trigger of items) {
    const li = document.createElement('li');
    if (state.selected && keyOf(state.selected) === keyOf(trigger)) {
      li.classList.add('selected');
    }
    const isPlaying = state.playingKey === keyOf(trigger);
    if (isPlaying) {
      li.classList.add('playing');
    }

    const number = document.createElement('span');
    number.className = 'stageNumber';
    number.textContent = numberLabel(trigger);
    number.title = numberTooltip(trigger);

    const name = document.createElement('div');
    name.className = 'name';
    name.append(number, document.createTextNode(`${actorLabel(trigger)} — ${stageLabel(trigger)}`));

    if (isPlaying) {
      const live = document.createElement('span');
      live.className = 'badge live';
      live.textContent = '▶ ゲームで再生中';
      name.append(live);
    }

    const disposition = document.createElement('span');
    disposition.className = `badge ${trigger.disposition}`;
    disposition.textContent = trigger.disposition;
    name.append(disposition);

    if (trigger.animationId === '*') {
      const badge = document.createElement('span');
      badge.className = 'badge static';
      badge.textContent = '未再生';
      name.append(badge);
    }

    if ((trigger.sharedWith || []).length > 0) {
      const badge = document.createElement('span');
      badge.className = 'badge shared';
      badge.textContent = trigger.isLinked
        ? `共有 ← ${trigger.sharedWith.length + 1}段階`
        : `共有 → ${trigger.sharedWith.length + 1}段階`;
      badge.title = `同じ波形を再生する段階:\n${trigger.sharedWith.join('\n')}`;
      name.append(badge);
    }

    const sub = document.createElement('div');
    sub.className = 'sub';
    const length = trigger.clipLengthSeconds
      ? `${Math.round(trigger.clipLengthSeconds * 1000)}ms`
      : '長さ未取得';
    const loop = trigger.isLooping === true ? 'loop' : trigger.isLooping === false ? 'once' : '?';
    const authored = trigger.authoredVariants.length
      ? ` / 作成済: ${trigger.authoredVariants.join(', ')}`
      : '';
    const clip = trigger.animationId === '*' ? '未再生' : trigger.animationId;
    sub.textContent =
      `${trigger.actorId} · ${trigger.stageId} · ${clip} · ${trigger.phase} · ${length} · ${loop}${authored}`;

    li.append(name, sub);
    li.addEventListener('click', () => selectTrigger(trigger));
    list.append(li);
  }
}

// ----------------------------------------------------------------- fillers

async function loadFillers() {
  const { ok, body } = await api('/api/fillers');
  if (!ok || !body) {
    setStatus('err', 'fillerを取得できませんでした。');
    return;
  }

  state.fillers = body.fillers;
  renderFillers();
}

function fillerLabel(filler) {
  return filler.role === 'status'
    ? `${filler.statusDisplayName || filler.statusId} 用`
    : `既定（${filler.outputs.join(' + ')}）`;
}

function renderFillers() {
  const list = el('catalog');
  list.replaceChildren();
  el('catalogCount').textContent = `${state.fillers.length} filler`;

  for (const filler of state.fillers) {
    const li = document.createElement('li');
    if (state.filler && state.filler.gallery === filler.gallery) {
      li.classList.add('selected');
    }

    const name = document.createElement('div');
    name.className = 'name';
    name.textContent = `${filler.gallery} — ${fillerLabel(filler)}`;

    const missing = filler.requiredVariants.filter((v) => !filler.authoredVariants.includes(v));
    if (missing.length) {
      const badge = document.createElement('span');
      badge.className = 'badge static';
      badge.textContent = `未作成: ${missing.join(', ')}`;
      name.append(badge);
    }

    const sub = document.createElement('div');
    sub.className = 'sub';
    // EDI plays a filler for the length in Definitions.csv, so a mismatch is worth showing.
    const drift = filler.definitionEndTime !== null
      && filler.definitionEndTime !== filler.durationMilliseconds
      ? ` · ⚠ CSV=${filler.definitionEndTime}ms と不一致`
      : '';
    sub.textContent =
      `${filler.outputs.join(' + ')} · ${filler.durationMilliseconds}ms`
      + ` · ${filler.requiredVariants.join(', ')}${drift}`;

    li.append(name, sub);
    li.addEventListener('click', () => selectFiller(filler));
    list.append(li);
  }
}

function selectFiller(filler) {
  state.filler = filler;
  state.selected = null;
  state.scripts = {};
  for (const [variant, document_] of Object.entries(filler.variants || {})) {
    state.scripts[variant] = document_.actions.map((a) => ({ at: a.at, pos: a.pos }));
  }

  const authored = Object.values(state.scripts)[0];
  state.duration = authored && authored.length ? authored[authored.length - 1].at : 2000;
  state.variant = filler.requiredVariants[0];

  el('empty').hidden = true;
  el('editor').hidden = false;
  el('triggerTitle').textContent = `${filler.gallery} — ${fillerLabel(filler)}`;
  el('triggerMeta').textContent =
    `filler / ${filler.outputs.join(' + ')} / ${filler.requiredVariants.join(' + ')}`
    + `\nDefinitions.csv: EndTime=${filler.definitionEndTime ?? 'なし'}`
    + ` / Loop=${filler.definitionLoop ?? '?'}`;
  el('duration').value = state.duration;
  el('linkInfo').hidden = true;
  setStatus('', '');

  renderFillers();
  renderVariantBar();
  draw();
}

async function saveFiller() {
  const variants = collectVariants();
  if (Object.keys(variants).length === 0) {
    setStatus('err', '保存できる波形がありません。点を2つ以上置いてください。');
    return;
  }

  const { ok, body } = await api('/api/save-filler', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      gallery: state.filler.gallery,
      variants,
    }),
  });

  if (!body) {
    setStatus('err', '保存要求に失敗しました。');
    return;
  }

  if (ok && body.success) {
    setStatus('ok',
      `保存しました。${body.gallery} (${body.durationMilliseconds}ms)\n`
      + `${body.writtenPaths.join('\n')}\n`
      + `Definitions.csv: ${body.definitionUpdated ? '更新済み' : '該当行なし'}`);
    await loadFillers();
    const refreshed = state.fillers.find((f) => f.gallery === state.filler.gallery);
    if (refreshed) {
      state.filler = refreshed;
    }
    return;
  }

  setStatus('err', (body.errors || ['保存に失敗しました。']).join('\n'));
}

function setMode(mode) {
  state.mode = mode;
  state.selected = null;
  state.filler = null;
  state.scripts = {};
  el('modeTrigger').classList.toggle('active', mode === 'trigger');
  el('modeFiller').classList.toggle('active', mode === 'filler');
  el('triggerOnly').hidden = mode !== 'trigger';
  el('fillerOnly').hidden = mode !== 'filler';
  el('filter').hidden = mode !== 'trigger';
  el('empty').hidden = false;
  el('editor').hidden = true;
  setStatus('', '');

  if (mode === 'filler') {
    loadFillers();
  } else {
    renderCatalog();
  }
}

// ------------------------------------------------------- live playing state

// Matching a catalog row to the gallery screen by number turned out to be unreliable, so the
// GUI asks the game what it is playing and highlights that row instead.
async function pollPlaying() {
  if (isFillerMode()) {
    return;
  }

  const { ok, body } = await api('/api/current');
  const next = ok && body && body.playing ? keyOf(body) : null;
  if (next === state.playingKey) {
    return;
  }

  state.playingKey = next;
  renderCatalog();
  updatePlayingBanner();

  if (next && state.followPlaying) {
    const index = state.triggers.findIndex((t) => keyOf(t) === next);
    const node = el('catalog').children[visibleTriggers().findIndex((t) => keyOf(t) === next)];
    if (index >= 0 && node) {
      node.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
  }
}

function updatePlayingBanner() {
  const banner = el('playing');
  if (!state.playingKey) {
    banner.textContent = 'ゲームで再生中のイベントはありません。';
    banner.classList.remove('active');
    return;
  }

  const trigger = state.triggers.find((t) => keyOf(t) === state.playingKey);
  banner.classList.add('active');
  banner.replaceChildren();

  const label = document.createElement('span');
  label.textContent = trigger
    ? `▶ ゲームで再生中: ${actorLabel(trigger)} — ${stageLabel(trigger)}`
    : '▶ ゲームで再生中: カタログ未登録のトリガー';
  banner.append(label);

  if (trigger) {
    const jump = document.createElement('button');
    jump.type = 'button';
    jump.textContent = 'この段階を編集';
    jump.addEventListener('click', () => selectTrigger(trigger));
    banner.append(jump);
  }
}

// ---------------------------------------------------------------- selection

async function selectTrigger(trigger) {
  state.selected = trigger;
  state.scripts = {};

  const query = new URLSearchParams({
    context: trigger.context,
    actorId: trigger.actorId,
    animationId: trigger.animationId,
    phase: trigger.phase,
    stageId: trigger.stageId,
  });
  const { ok, body } = await api(`/api/script?${query}`);
  if (ok && body && body.variants) {
    for (const [variant, document_] of Object.entries(body.variants)) {
      state.scripts[variant] = document_.actions.map((a) => ({ at: a.at, pos: a.pos }));
    }
  }

  const clipMs = trigger.clipLengthSeconds ? Math.round(trigger.clipLengthSeconds * 1000) : 1000;
  const authored = Object.values(state.scripts)[0];
  state.duration = authored && authored.length ? authored[authored.length - 1].at : clipMs;

  const variants = rosterVariants();
  state.variant = variants[0];

  el('empty').hidden = true;
  el('editor').hidden = false;
  el('triggerTitle').textContent =
    `${numberLabel(trigger)} ${actorLabel(trigger)} — ${stageLabel(trigger)}`;
  const numberSource = [
    typeof trigger.stageNumber === 'number' ? `SelectID=${trigger.stageNumber}` : null,
    typeof trigger.stageIndex === 'number' ? `配列位置=${trigger.stageIndex}` : null,
  ].filter(Boolean).join(' / ') || '番号情報なし';
  el('triggerMeta').textContent =
    `番号の根拠: ${numberSource}\n`
    + `${trigger.context}/${trigger.actorId}/${trigger.animationId}/${trigger.phase}/${trigger.stageId}`
    + ` · gallery ${trigger.gallery} · clip ${clipMs}ms · ${trigger.isLooping ? 'ループ' : '非ループ'}`;
  el('duration').value = state.duration;
  el('approveLoop').checked = false;
  // Takes with no animator report no loop even while the stage visibly repeats on screen, so the
  // game's answer is only the starting point; the author confirms it.
  el('repeat').checked = trigger.isLooping === true;
  setStatus('', '');

  renderCatalog();
  renderVariantBar();
  renderLinkInfo();
  draw();
}

// The waveform on screen may belong to several stages at once. Editing it then changes all of
// them, so that has to be visible before the first point is dragged.
function renderLinkInfo() {
  const node = el('linkInfo');
  const trigger = state.selected;
  const shared = trigger ? (trigger.sharedWith || []) : [];
  if (!trigger || shared.length === 0) {
    node.hidden = true;
    node.replaceChildren();
    return;
  }

  node.hidden = false;
  node.replaceChildren();

  const label = document.createElement('span');
  label.textContent = trigger.isLinked
    ? `この段階は別の段階の波形を共有しています（全 ${shared.length + 1} 段階）。ここでの編集は共有している段階すべてに反映されます。`
    : `この波形は他の ${shared.length} 段階でも再生されます。編集はそれら全てに反映されます。`;
  node.append(label);

  if (trigger.isLinked) {
    const unlink = document.createElement('button');
    unlink.type = 'button';
    unlink.textContent = 'リンク解除（この段階専用の波形にする）';
    unlink.addEventListener('click', unlinkSelected);
    node.append(unlink);
  }

  const list = document.createElement('div');
  list.className = 'sharedList';
  list.textContent = `同じ波形の段階:\n${shared.join('\n')}`;
  node.append(list);
}

function renderVariantBar() {
  const bar = el('variantBar');
  bar.replaceChildren();
  // A filler only serves the outputs the mapping selects it for.
  const variants = isFillerMode()
    ? state.filler.requiredVariants
    : rosterVariants();

  for (const variant of variants) {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = variant;
    if (variant === state.variant) {
      button.classList.add('active');
    }
    if ((state.scripts[variant] || []).length > 0) {
      const dot = document.createElement('span');
      dot.className = 'dot';
      dot.textContent = '●';
      button.append(dot);
    }
    button.addEventListener('click', () => {
      state.variant = variant;
      renderVariantBar();
      draw();
    });
    bar.append(button);
  }

  updateChannelPreview();
}

// Every variant belongs to exactly one output, so what is drawn decides which devices move.
// There is no pairing rule any more: drawing one side alone is a legitimate choice, and the
// other device simply keeps its filler.
function rosterVariants() {
  return (state.catalog.outputs || []).map((output) => output.variant);
}

function outputForVariant(variant) {
  return (state.catalog.outputs || []).find((output) => output.variant === variant);
}

function computedOutputs() {
  const drawn = Object.entries(state.scripts)
    .filter(([, points]) => (points || []).length >= 2)
    .map(([variant]) => variant);
  const outputs = drawn.map(outputForVariant).filter(Boolean);
  return { drawn, outputs };
}

function updateChannelPreview() {
  const node = el('channelPreview');
  if (!node) {
    return;
  }

  if (isFillerMode()) {
    node.textContent = `出力: ${state.filler.outputs.join(' + ')}`;
    node.className = 'channelPreview muted';
    return;
  }

  const { outputs } = computedOutputs();
  if (outputs.length === 0) {
    node.textContent = '保存不可: 波形が1つも描かれていません';
    node.className = 'channelPreview warn';
    return;
  }

  const suppressed = outputs.filter((output) => output.available === false);
  node.textContent = `保存すると ${outputs.map((o) => o.displayName).join(' + ')} が動きます`
    + (suppressed.length
      ? ` / ${suppressed.map((o) => `${o.displayName}は抑止中`).join('、')}`
      : '');
  node.className = suppressed.length ? 'channelPreview warn' : 'channelPreview ok';
}

// ---------------------------------------------------------------- canvas

const PAD = { left: 44, right: 16, top: 14, bottom: 26 };

function plotArea() {
  const canvas = el('canvas');
  return {
    x: PAD.left,
    y: PAD.top,
    w: canvas.width - PAD.left - PAD.right,
    h: canvas.height - PAD.top - PAD.bottom,
  };
}

const toX = (at, area) => area.x + (at / state.duration) * area.w;
const toY = (pos, area) => area.y + (1 - pos / 100) * area.h;
const fromX = (px, area) => clamp(Math.round(((px - area.x) / area.w) * state.duration), 0, state.duration);
const fromY = (py, area) => clamp(Math.round((1 - (py - area.y) / area.h) * 100), 0, 100);

// Mirrors Funscript.MinPistonSegmentMilliseconds / MaxPistonUnitsPerSecond. Keep in step.
const MIN_PISTON_SEGMENT_MS = 100;
const MAX_PISTON_UNITS_PER_SECOND = 500;

/**
 * What the piston will actually trace for a drawn waveform.
 *
 * A point is not a sample: it is one "travel to P by time T" command. The device is still running
 * the previous one when the next arrives, so a point that lands inside the command interval
 * preempts the move already in flight and the carriage keeps whatever ground it covered. Drawing a
 * curve at 30ms spacing therefore yields a flat buzz, not the curve. This reproduces that so the
 * drawn line and the reachable line can be compared before saving.
 */
function simulateDevice(points) {
  if (points.length < 2) {
    return [];
  }

  const trace = [{ at: points[0].at, pos: points[0].pos }];
  let pos = points[0].pos;
  let issuedAt = points[0].at;

  for (let index = 1; index < points.length; index += 1) {
    const target = points[index];
    if (target.at - issuedAt < MIN_PISTON_SEGMENT_MS && index < points.length - 1) {
      continue; // preempted before the device could act on it
    }

    const span = target.at - issuedAt;
    const reach = (MAX_PISTON_UNITS_PER_SECOND * span) / 1000;
    const wanted = target.pos - pos;
    pos += Math.sign(wanted) * Math.min(Math.abs(wanted), reach);
    issuedAt = target.at;
    trace.push({ at: target.at, pos });
  }

  return trace;
}

/** Keeps only the turning points, which is what a piston can express. */
function turningPoints(points) {
  if (points.length < 3) {
    return points.slice();
  }

  const kept = [points[0]];
  for (let index = 1; index < points.length - 1; index += 1) {
    const rising = points[index].pos > points[index - 1].pos;
    const stillRising = points[index + 1].pos > points[index].pos;
    if (rising !== stillRising) {
      kept.push(points[index]);
    }
  }
  kept.push(points[points.length - 1]);

  // A turn the device cannot reach in time is not a turn it can play, so neighbours that fall
  // inside one command interval collapse into the more extreme of the two. The first point is
  // where the carriage starts, so it keeps its own position and its crowded neighbours are simply
  // dropped — those are turns the device could not have performed either way.
  const spaced = [kept[0]];
  for (let index = 1; index < kept.length; index += 1) {
    const previous = spaced[spaced.length - 1];
    if (kept[index].at - previous.at >= MIN_PISTON_SEGMENT_MS) {
      spaced.push(kept[index]);
      continue;
    }

    if (spaced.length === 1) {
      continue;
    }

    const base = spaced[spaced.length - 2].pos;
    if (Math.abs(kept[index].pos - base) > Math.abs(previous.pos - base)) {
      spaced[spaced.length - 1] = { at: previous.at, pos: kept[index].pos };
    }
  }

  return spaced;
}

function currentPoints() {
  if (!state.scripts[state.variant]) {
    state.scripts[state.variant] = [];
  }
  return state.scripts[state.variant];
}

function draw() {
  const canvas = el('canvas');
  const ctx = canvas.getContext('2d');
  const area = plotArea();
  ctx.clearRect(0, 0, canvas.width, canvas.height);

  ctx.strokeStyle = '#333a47';
  ctx.fillStyle = '#97a0b0';
  ctx.lineWidth = 1;
  ctx.font = '11px sans-serif';

  for (let pos = 0; pos <= 100; pos += 25) {
    const y = toY(pos, area);
    ctx.beginPath();
    ctx.moveTo(area.x, y);
    ctx.lineTo(area.x + area.w, y);
    ctx.stroke();
    ctx.fillText(String(pos), 12, y + 4);
  }

  for (let i = 0; i <= 4; i += 1) {
    const at = (state.duration / 4) * i;
    const x = toX(at, area);
    ctx.beginPath();
    ctx.moveTo(x, area.y);
    ctx.lineTo(x, area.y + area.h);
    ctx.stroke();
    ctx.fillText(`${Math.round(at)}ms`, x - 16, area.y + area.h + 16);
  }

  // Clip-length marker: the loop end must line up with it within tolerance (FR-040).
  const trigger = state.selected;
  if (trigger && trigger.isLooping && trigger.clipLengthSeconds) {
    const clipMs = Math.round(trigger.clipLengthSeconds * 1000);
    if (clipMs <= state.duration) {
      const x = toX(clipMs, area);
      ctx.strokeStyle = '#e0a340';
      ctx.setLineDash([4, 4]);
      ctx.beginPath();
      ctx.moveTo(x, area.y);
      ctx.lineTo(x, area.y + area.h);
      ctx.stroke();
      ctx.setLineDash([]);
    }
  }

  const points = currentPoints();

  // The reachable trace goes underneath the drawn one: when they diverge, what the user drew is
  // not what they will feel, and that is the whole point of showing it.
  if (points.length > 1 && state.variant === 'a10-main') {
    const trace = simulateDevice(points);
    if (trace.length > 1) {
      ctx.strokeStyle = '#e0a340';
      ctx.lineWidth = 3;
      ctx.beginPath();
      trace.forEach((point, index) => {
        const x = toX(point.at, area);
        const y = toY(point.pos, area);
        if (index === 0) {
          ctx.moveTo(x, y);
        } else {
          ctx.lineTo(x, y);
        }
      });
      ctx.stroke();

      ctx.fillStyle = '#e0a340';
      ctx.fillText(`実際の動き（${trace.length}点まで間引かれます）`, area.x + 6, area.y + 14);
      ctx.fillStyle = '#5aa9e6';
      ctx.fillText(`描いた線（${points.length}点）`, area.x + 6, area.y + 28);
    }
  }

  if (points.length > 0) {
    ctx.strokeStyle = '#5aa9e6';
    ctx.lineWidth = 2;
    ctx.beginPath();
    points.forEach((point, index) => {
      const x = toX(point.at, area);
      const y = toY(point.pos, area);
      if (index === 0) {
        ctx.moveTo(x, y);
      } else {
        ctx.lineTo(x, y);
      }
    });
    ctx.stroke();

    ctx.fillStyle = '#e6e9ef';
    for (const point of points) {
      ctx.beginPath();
      ctx.arc(toX(point.at, area), toY(point.pos, area), 4, 0, Math.PI * 2);
      ctx.fill();
    }
  }
}

function hitTest(px, py) {
  const area = plotArea();
  const points = currentPoints();
  for (let index = 0; index < points.length; index += 1) {
    const dx = toX(points[index].at, area) - px;
    const dy = toY(points[index].pos, area) - py;
    if ((dx * dx) + (dy * dy) <= 64) {
      return index;
    }
  }
  return -1;
}

function canvasPoint(event) {
  const canvas = el('canvas');
  const rect = canvas.getBoundingClientRect();
  return {
    x: (event.clientX - rect.left) * (canvas.width / rect.width),
    y: (event.clientY - rect.top) * (canvas.height / rect.height),
  };
}

function sortPoints() {
  currentPoints().sort((a, b) => a.at - b.at);
}

let dragIndex = -1;

function attachCanvasHandlers() {
  const canvas = el('canvas');

  canvas.addEventListener('mousedown', (event) => {
    if (event.button !== 0 || !isEditing()) {
      return;
    }
    const { x, y } = canvasPoint(event);
    const index = hitTest(x, y);
    if (index >= 0) {
      dragIndex = index;
      return;
    }

    const area = plotArea();
    const at = fromX(x, area);
    const points = currentPoints();
    if (points.some((point) => point.at === at)) {
      return;
    }
    points.push({ at, pos: fromY(y, area) });
    sortPoints();
    renderVariantBar();
    draw();
  });

  canvas.addEventListener('mousemove', (event) => {
    if (dragIndex < 0) {
      return;
    }
    const { x, y } = canvasPoint(event);
    const area = plotArea();
    const points = currentPoints();
    const at = fromX(x, area);
    // Times must remain strictly increasing, so a drag cannot pass a neighbour.
    const lower = dragIndex > 0 ? points[dragIndex - 1].at + 1 : 0;
    const upper = dragIndex < points.length - 1 ? points[dragIndex + 1].at - 1 : state.duration;
    points[dragIndex] = { at: clamp(at, lower, Math.max(lower, upper)), pos: fromY(y, area) };
    draw();
  });

  const endDrag = () => { dragIndex = -1; };
  canvas.addEventListener('mouseup', endDrag);
  canvas.addEventListener('mouseleave', endDrag);

  canvas.addEventListener('contextmenu', (event) => {
    event.preventDefault();
    if (!isEditing()) {
      return;
    }
    const { x, y } = canvasPoint(event);
    const index = hitTest(x, y);
    if (index >= 0) {
      currentPoints().splice(index, 1);
      renderVariantBar();
      draw();
    }
  });
}

// ---------------------------------------------------------------- actions

function collectVariants() {
  const variants = {};
  for (const [variant, points] of Object.entries(state.scripts)) {
    if (points.length >= 2) {
      variants[variant] = points
        .slice()
        .sort((a, b) => a.at - b.at)
        .map((point) => ({ pos: point.pos, at: point.at }));
    }
  }
  return variants;
}

async function save() {
  const trigger = state.selected;
  const variants = collectVariants();
  if (Object.keys(variants).length === 0) {
    setStatus('err', '保存できる波形がありません。点を2つ以上置いてください。');
    return;
  }

  const { ok, body } = await api('/api/save', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      context: trigger.context,
      actorId: trigger.actorId,
      animationId: trigger.animationId,
      phase: trigger.phase,
      stageId: trigger.stageId,
      variants,
      approveLoopMismatch: el('approveLoop').checked,
      repeat: el('repeat').checked,
    }),
  });

  if (!body) {
    setStatus('err', '保存要求に失敗しました。');
    return;
  }

  if (ok && body.success) {
    const lines = [`保存しました。gallery=${body.gallery}`];
    if ((body.outputs || []).length) {
      const devices = body.outputs
        .map((id) => {
          const output = (state.catalog.outputs || []).find((o) => o.id === id);
          return output ? output.displayName : id;
        })
        .join(' + ');
      lines.push(`動作する出力: ${devices}`);
    }
    lines.push(...body.writtenPaths);
    if ((body.removedPaths || []).length) {
      lines.push(`未使用バリアントを削除: ${body.removedPaths.length}件`);
      lines.push(...body.removedPaths);
    }
    lines.push(`マッピング更新: ${body.mappingUpdated ? '完了' : '未実施'}`);
    lines.push(`繰り返し再生: ${el('repeat').checked ? 'する' : 'しない（1回で停止）'}`);
    if ((trigger.sharedWith || []).length > 0) {
      lines.push(`この波形を共有する他の ${trigger.sharedWith.length} 段階にも反映されました。`);
    }
    if ((body.motionWarnings || []).length > 0) {
      lines.push('', '動きの注意（保存は完了しています）:');
      lines.push(...body.motionWarnings);
    }
    setStatus(body.motionWarnings && body.motionWarnings.length ? 'warn' : 'ok', lines.join('\n'));
    await refreshSelectedFromCatalog();
    return;
  }

  const lines = [...(body.errors || [])];
  if ((body.loopWarnings || []).length > 0) {
    lines.push(...body.loopWarnings);
    lines.push('差異を意図している場合は「ループ長の差異を承認」にチェックして再保存してください。');
  }
  setStatus(body.errors && body.errors.length ? 'err' : 'warn', lines.join('\n'));
}

async function preview() {
  if (isFillerMode()) {
    const { ok, body } = await api('/api/preview', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ gallery: state.filler.gallery, outputs: state.filler.outputs }),
    });
    setStatus(ok ? 'ok' : 'err',
      ok ? `試聴中: ${state.filler.gallery} (${state.filler.outputs.join(' + ')})\n保存前の編集内容は反映されません。`
         : (body && body.error) || '試聴を開始できませんでした。');
    return;
  }

  const trigger = state.selected;
  // A gallery may only be auditioned on the outputs it has a saved variant for, so this asks for
  // the ones already on disk rather than the ones currently drawn.
  const outputs = (trigger.authoredVariants || [])
    .map(outputForVariant)
    .filter(Boolean)
    .map((output) => output.id);

  if (outputs.length === 0) {
    setStatus('warn', '試聴できる保存済みバリアントがありません。先に保存してください。');
    return;
  }

  const { ok, body } = await api('/api/preview', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ gallery: trigger.gallery, outputs }),
  });
  setStatus(ok ? 'ok' : 'err',
    ok ? `試聴中: ${trigger.gallery} (${outputs.join(', ')})\n保存前の波形は反映されません。先に保存してください。`
       : (body && body.error) || '試聴を開始できませんでした。');
}

function openCopyDialog() {
  const select = el('copySource');
  select.replaceChildren();

  // Copying the normal filler onto the swollen one is the usual way to start a stronger variant,
  // so fillers that serve the same outputs are offered as sources.
  if (isFillerMode()) {
    const sameOutputs = (filler) =>
      filler.outputs.length === state.filler.outputs.length
      && filler.outputs.every((id) => state.filler.outputs.includes(id));
    for (const filler of state.fillers) {
      if (filler.gallery === state.filler.gallery
        || !sameOutputs(filler)
        || filler.authoredVariants.length === 0) {
        continue;
      }
      const option = document.createElement('option');
      option.value = filler.gallery;
      option.textContent = `${filler.gallery} — ${fillerLabel(filler)}`;
      select.append(option);
    }

    if (select.children.length === 0) {
      setStatus('warn', '同じチャンネルに複製元となるfillerがありません。');
      return;
    }
    el('copyDialog').showModal();
    return;
  }

  for (const trigger of state.triggers) {
    if (trigger.authoredVariants.length === 0 || keyOf(trigger) === keyOf(state.selected)) {
      continue;
    }
    const option = document.createElement('option');
    option.value = keyOf(trigger);
    option.textContent =
      `${numberLabel(trigger)} ${actorLabel(trigger)} / ${stageLabel(trigger)} `
      + `(${trigger.authoredVariants.join(', ')})`;
    select.append(option);
  }

  if (select.children.length === 0) {
    setStatus('warn', '複製できる作成済みの段階がまだありません。');
    return;
  }
  el('copyDialog').showModal();
}

async function copyFromSelected() {
  const chosen = el('copySource').value;

  if (isFillerMode()) {
    const source = state.fillers.find((filler) => filler.gallery === chosen);
    if (!source) {
      return;
    }

    for (const [variant, document_] of Object.entries(source.variants || {})) {
      state.scripts[variant] = document_.actions.map((a) => ({ at: a.at, pos: a.pos }));
    }
    const points = Object.values(state.scripts)[0] || [];
    if (points.length) {
      state.duration = points[points.length - 1].at;
      el('duration').value = state.duration;
    }

    setStatus('ok', `${source.gallery} から複製しました。保存前に強さを調整してください。`);
    renderVariantBar();
    draw();
    return;
  }

  const source = state.triggers.find((trigger) => keyOf(trigger) === chosen);
  if (!source) {
    return;
  }

  const query = new URLSearchParams({
    context: source.context,
    actorId: source.actorId,
    animationId: source.animationId,
    phase: source.phase,
    stageId: source.stageId,
  });
  const { ok, body } = await api(`/api/script?${query}`);
  if (!ok || !body) {
    setStatus('err', '複製元を読み込めませんでした。');
    return;
  }

  for (const [variant, document_] of Object.entries(body.variants)) {
    state.scripts[variant] = document_.actions.map((a) => ({ at: a.at, pos: a.pos }));
  }

  const points = Object.values(state.scripts)[0] || [];
  if (points.length) {
    state.duration = points[points.length - 1].at;
    el('duration').value = state.duration;
  }

  setStatus('ok', `${actorLabel(source)} / ${stageLabel(source)} から複製しました。`);
  renderVariantBar();
  draw();
}

// ------------------------------------------------- applying to other stages

// Which stage the event picks depends on what the player was doing when it started — idle, walking,
// falling — while the motion on screen is all but the same. 共有 states that once: the stages play
// one waveform, and a later correction reaches all of them. 複製 is for the cases where they should
// drift apart afterwards.
const applyTargets = new Set();

function applyMode() {
  const chosen = document.querySelector('input[name="applyMode"]:checked');
  return chosen ? chosen.value : 'link';
}

function setApplyStatus(kind, text) {
  const node = el('applyStatus');
  node.className = `status ${kind}`;
  node.textContent = text;
}

function openApplyDialog() {
  if (isFillerMode()) {
    setStatus('warn', 'fillerはトリガーではないため、他の段階へ適用できません。');
    return;
  }

  const trigger = state.selected;
  if (!trigger) {
    return;
  }

  // The MOD applies what is on disk, not what is on the canvas, because EDI can only play a
  // gallery it has already read (6.7-6).
  if ((trigger.authoredVariants || []).length === 0) {
    setStatus('warn', 'この段階にはまだ保存済みの波形がありません。先に保存してください。');
    return;
  }

  applyTargets.clear();
  el('applyFilter').value = '';
  el('applyApprove').checked = false;
  setApplyStatus('', '');
  renderApplyList();
  el('applyDialog').showModal();
}

function renderApplyList() {
  const list = el('applyList');
  const needle = el('applyFilter').value.trim().toLowerCase();
  const source = state.selected;
  list.replaceChildren();

  const candidates = state.triggers.filter((t) =>
    keyOf(t) !== keyOf(source) && isAuthorable(t) && matchesNeedle(t, needle));

  if (candidates.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'empty';
    empty.textContent = '該当する段階がありません。';
    list.append(empty);
    return;
  }

  for (const trigger of candidates) {
    const key = keyOf(trigger);
    const label = document.createElement('label');

    const box = document.createElement('input');
    box.type = 'checkbox';
    box.checked = applyTargets.has(key);
    box.addEventListener('change', () => {
      if (box.checked) {
        applyTargets.add(key);
      } else {
        applyTargets.delete(key);
      }
    });

    const text = document.createElement('div');
    const name = document.createElement('div');
    name.textContent = `${numberLabel(trigger)} ${actorLabel(trigger)} — ${stageLabel(trigger)}`;
    const sub = document.createElement('div');
    sub.className = 'sub';
    const clip = trigger.clipLengthSeconds
      ? `${Math.round(trigger.clipLengthSeconds * 1000)}ms`
      : '長さ未取得';
    const held = trigger.isLinked
      ? '既に他段階と共有中'
      : (trigger.authoredVariants || []).length
        ? `作成済: ${trigger.authoredVariants.join(', ')}`
        : '未作成';
    sub.textContent = `${trigger.animationId} · ${trigger.phase} · ${clip} · ${held}`;
    text.append(name, sub);

    label.append(box, text);
    list.append(label);
  }
}

async function applyToTargets() {
  const source = state.selected;
  const targets = state.triggers.filter((t) => applyTargets.has(keyOf(t)));
  if (targets.length === 0) {
    setApplyStatus('warn', '適用先の段階を1つ以上選んでください。');
    return;
  }

  const mode = applyMode();
  const { ok, body } = await api('/api/link', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      source: keyPayload(source),
      targets: targets.map(keyPayload),
      mode,
      approveMismatch: el('applyApprove').checked,
    }),
  });

  if (!body) {
    setApplyStatus('err', '適用要求に失敗しました。');
    return;
  }

  const label = (targetText) => {
    const trigger = state.triggers.find((t) => keyText(t) === targetText);
    return trigger ? `${numberLabel(trigger)} ${actorLabel(trigger)} / ${stageLabel(trigger)}` : targetText;
  };

  const done = (body.targets || []).filter((outcome) => outcome.success);
  const failed = (body.targets || []).filter((outcome) => !outcome.success);
  const lines = [];
  if (body.errors && body.errors.length) {
    lines.push(...body.errors);
  }
  if (done.length) {
    lines.push(
      mode === 'link'
        ? `${done.length}段階を ${body.gallery} へ共有しました:`
        : `${done.length}段階へ複製しました:`);
    lines.push(...done.map((outcome) => `  ${label(outcome.target)}`));
  }
  for (const outcome of failed) {
    lines.push(`${label(outcome.target)}: ${[...outcome.errors, ...outcome.warnings].join(' / ')}`);
  }
  if (failed.some((outcome) => outcome.errors.length === 0 && outcome.warnings.length > 0)) {
    lines.push('差異を意図している場合は下のチェックを入れて再実行してください。');
  }

  await refreshSelectedFromCatalog();

  if (ok && body.success) {
    el('applyDialog').close();
    setStatus('ok', lines.join('\n'));
    return;
  }

  setApplyStatus(failed.length && done.length ? 'warn' : 'err', lines.join('\n'));
  renderApplyList();
}

async function unlinkSelected() {
  const trigger = state.selected;
  const { ok, body } = await api('/api/unlink', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      key: keyPayload(trigger),
      repeat: el('repeat').checked,
      approveLoopMismatch: el('approveLoop').checked,
    }),
  });

  if (!body) {
    setStatus('err', 'リンク解除の要求に失敗しました。');
    return;
  }

  if (ok && body.success) {
    await refreshSelectedFromCatalog();
    setStatus('ok',
      `リンクを解除しました。この段階は専用の波形 ${body.gallery} を持ちます。\n`
      + `${body.writtenPaths.join('\n')}`);
    return;
  }

  const lines = [...(body.errors || []), ...(body.loopWarnings || [])];
  setStatus('err', lines.join('\n') || 'リンクを解除できませんでした。');
}

// The mapping changed but the canvas did not, so the row is refreshed in place instead of being
// re-selected, which would throw away unsaved edits.
async function refreshSelectedFromCatalog() {
  const key = state.selected ? keyOf(state.selected) : null;
  await loadCatalog();
  if (key) {
    const refreshed = state.triggers.find((t) => keyOf(t) === key);
    if (refreshed) {
      state.selected = refreshed;
    }
  }
  renderCatalog();
  renderLinkInfo();
}

function attachControls() {
  el('reload').addEventListener('click', loadCatalog);
  el('filter').addEventListener('input', renderCatalog);
  el('followPlaying').addEventListener('change', (event) => {
    state.followPlaying = event.target.checked;
  });

  el('duration').addEventListener('change', (event) => {
    const value = Number(event.target.value);
    if (Number.isFinite(value) && value >= 100) {
      state.duration = Math.round(value);
      for (const points of Object.values(state.scripts)) {
        for (const point of points) {
          point.at = Math.min(point.at, state.duration);
        }
      }
      draw();
    }
  });

  el('fitLoop').addEventListener('click', () => {
    if (isFillerMode()) {
      setStatus('warn', 'fillerはゲームのクリップに紐づかないため、長さは任意に決めてください。');
      return;
    }
    const trigger = state.selected;
    if (!trigger || !trigger.clipLengthSeconds) {
      setStatus('warn', 'この段階はクリップ長が未取得です。ゲーム内で一度再生してください。');
      return;
    }
    state.duration = Math.round(trigger.clipLengthSeconds * 1000);
    el('duration').value = state.duration;
    draw();
  });

  // Reducing a sampled curve is an edit the user asks for, like 反対側にコピー; nothing is
  // invented, points are only removed (FR-039).
  el('simplify').addEventListener('click', () => {
    const points = currentPoints();
    if (points.length < 3) {
      setStatus('warn', '減らせる点がありません。');
      return;
    }

    const reduced = turningPoints(points);
    state.scripts[state.variant] = reduced;
    renderVariantBar();
    draw();
    setStatus(
      'ok',
      `${points.length}点 → ${reduced.length}点に減らしました。`
      + 'ピストンは点と点の間を自分で移動するので、折り返し点だけのほうが描いた線に近く動きます。');
  });

  el('clear').addEventListener('click', () => {
    state.scripts[state.variant] = [];
    renderVariantBar();
    draw();
  });

  el('modeTrigger').addEventListener('click', () => setMode('trigger'));
  el('modeFiller').addEventListener('click', () => setMode('filler'));
  // Both sides are separate outputs now, so neither requires the other. The MOD is still not
  // allowed to invent a waveform, so copying across is an explicit action the user takes.
  el('mirrorSide').addEventListener('click', () => {
    const sides = ['ufo-left', 'ufo-right'];
    if (!isEditing() || !sides.includes(state.variant)) {
      setStatus('warn', 'ufo-left か ufo-right を選んでから押してください。');
      return;
    }

    const source = state.scripts[state.variant] || [];
    if (source.length < 2) {
      setStatus('warn', 'コピー元の波形がありません。先に点を2つ以上置いてください。');
      return;
    }

    const other = sides.find((v) => v !== state.variant);
    state.scripts[other] = source.map((point) => ({ at: point.at, pos: point.pos }));
    setStatus('ok',
      `${state.variant} を ${other} へコピーしました。\n`
      + '左右が同一波形のままだと同じ動きになります。必要に応じて調整してください。');
    renderVariantBar();
    draw();
  });

  el('copyFrom').addEventListener('click', openCopyDialog);
  el('applyTo').addEventListener('click', openApplyDialog);
  el('applyFilter').addEventListener('input', renderApplyList);
  el('applyCancel').addEventListener('click', () => el('applyDialog').close());
  el('applyConfirm').addEventListener('click', applyToTargets);
  el('copyConfirm').addEventListener('click', () => { window.setTimeout(copyFromSelected, 0); });
  el('save').addEventListener('click', () => (isFillerMode() ? saveFiller() : save()));
  el('preview').addEventListener('click', preview);
  el('previewStop').addEventListener('click', async () => {
    await api('/api/preview/stop', { method: 'POST' });
    setStatus('ok', '試聴を停止し、ゲーム側の状態へ復帰しました。');
  });
}

attachCanvasHandlers();
attachControls();
loadCatalog();
updatePlayingBanner();

// Poll rather than push: the plugin serves plain HTTP and the page must survive the game exiting.
window.setInterval(() => { pollPlaying().catch(() => {}); }, 1000);
