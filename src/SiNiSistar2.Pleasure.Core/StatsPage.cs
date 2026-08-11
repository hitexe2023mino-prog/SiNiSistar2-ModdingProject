namespace SiNiSistar2.Pleasure.Core;

/// <summary>
/// The statistics page, as one self-contained document (SPEC006 4.6, FR-612).
///
/// Everything is inline. There is no stylesheet, no script and no font fetched from anywhere: the
/// MOD runs beside an offline game and a page that went looking for a CDN would be blank exactly
/// when it was wanted. The parchment and the ornaments are drawn with gradients and characters for
/// the same reason.
///
/// Headings are in Japanese, matching the spec and its readers. The names inside the tables are
/// whatever the game's own localisation returned, so the diary reads in the same words the game
/// does (DEC-607).
/// </summary>
internal static class StatsPage
{
    internal const string Html = """
<!DOCTYPE html>
<html lang="ja">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>巡礼の記録 — プレイ統計</title>
<style>
  :root {
    --ink: #3b2415;
    --ink-soft: #6b4b31;
    --rule: #c8a882;
    --parchment: #efe0c2;
    --parchment-deep: #e2cfa8;
    --accent: #7d2b3a;
  }
  * { box-sizing: border-box; }
  body {
    margin: 0;
    padding: 2rem 1rem 3rem;
    min-height: 100vh;
    color: var(--ink);
    font-family: "Yu Mincho", "游明朝", YuMincho, "Hiragino Mincho ProN",
                 "MS PMincho", Georgia, "Times New Roman", serif;
    background-color: #2c2118;
    background-image:
      radial-gradient(ellipse at 20% 10%, rgba(255, 240, 205, 0.10), transparent 60%),
      radial-gradient(ellipse at 80% 90%, rgba(255, 240, 205, 0.08), transparent 60%);
  }
  main {
    max-width: 46rem;
    margin: 0 auto;
    padding: 2.5rem 2.5rem 2rem;
    background-color: var(--parchment);
    background-image:
      radial-gradient(circle at 12% 18%, rgba(160, 120, 70, 0.13), transparent 28%),
      radial-gradient(circle at 88% 72%, rgba(160, 120, 70, 0.11), transparent 26%),
      radial-gradient(circle at 45% 95%, rgba(120, 85, 45, 0.10), transparent 30%),
      linear-gradient(160deg, var(--parchment) 0%, var(--parchment-deep) 100%);
    border: 1px solid #b8946a;
    border-radius: 3px;
    box-shadow: 0 1.5rem 3rem rgba(0, 0, 0, 0.55), inset 0 0 5rem rgba(150, 110, 60, 0.22);
  }
  header { text-align: center; padding-bottom: 1.25rem; }
  h1 { margin: 0; font-size: 1.9rem; font-weight: normal; letter-spacing: 0.35em; }
  .subtitle { margin: 0.5rem 0 0; font-size: 0.85rem; color: var(--ink-soft); letter-spacing: 0.2em; }
  .flourish { color: var(--rule); letter-spacing: 0.6em; font-size: 0.9rem; }
  section { margin-top: 2rem; }
  h2 {
    margin: 0 0 0.9rem;
    font-size: 1.05rem;
    font-weight: normal;
    letter-spacing: 0.18em;
    border-bottom: 1px solid var(--rule);
    padding-bottom: 0.4rem;
  }
  h2::before { content: "❧ "; color: var(--accent); }
  .measure { display: flex; align-items: baseline; justify-content: space-between; gap: 1rem; }
  .measure + .measure { margin-top: 1rem; }
  .measure .label { letter-spacing: 0.14em; }
  .measure .value { font-size: 1.35rem; }
  .measure .value .of { font-size: 0.85rem; color: var(--ink-soft); }
  .gauge {
    margin-top: 0.5rem;
    height: 0.55rem;
    border: 1px solid var(--rule);
    background: rgba(255, 255, 255, 0.28);
    overflow: hidden;
  }
  .gauge > i {
    display: block;
    height: 100%;
    width: 0;
    background: linear-gradient(90deg, #8d3446, var(--accent));
    transition: width 0.6s ease;
  }
  .laurel { text-align: center; padding: 0.5rem 0 0.25rem; }
  .laurel .name { font-size: 1.6rem; display: block; }
  .laurel .count { color: var(--ink-soft); font-size: 0.95rem; }
  .laurel .none { color: var(--ink-soft); font-size: 0.95rem; }
  table { width: 100%; border-collapse: collapse; }
  td { padding: 0.4rem 0; vertical-align: baseline; }
  tbody tr + tr td { border-top: 1px dotted var(--rule); }
  td.n { text-align: right; white-space: nowrap; font-size: 1.05rem; padding-left: 1rem; }
  td.n small { color: var(--ink-soft); font-size: 0.75rem; margin-left: 0.15rem; }
  .raw { color: var(--ink-soft); font-style: italic; }
  .empty { color: var(--ink-soft); }
  footer {
    margin-top: 2.25rem;
    padding-top: 0.9rem;
    border-top: 1px solid var(--rule);
    text-align: center;
    font-size: 0.75rem;
    color: var(--ink-soft);
    letter-spacing: 0.1em;
  }
  footer .stale { color: var(--accent); }
</style>
</head>
<body>
<main>
  <header>
    <p class="flourish">✦ ❧ ✦</p>
    <h1>巡礼の記録</h1>
    <p class="subtitle">S I S T E R ' S   D I A R Y</p>
  </header>

  <section>
    <h2>身の証</h2>
    <div class="measure">
      <span class="label">堕落</span>
      <span class="value"><span id="corruption-value">—</span><span class="of" id="corruption-cap"></span></span>
    </div>
    <div class="gauge"><i id="corruption-bar"></i></div>
    <div class="measure">
      <span class="label">絶頂回数</span>
      <span class="value"><span id="climax-count">—</span><span class="of" id="climax-limit"></span></span>
    </div>
  </section>

  <section>
    <h2>最も辱めた者</h2>
    <div class="laurel" id="top-actor">
      <span class="none">まだ絶頂させた敵がいません</span>
    </div>
  </section>

  <section>
    <h2>受けた呪いの記録</h2>
    <table><tbody id="debuffs">
      <tr><td class="empty">まだ何も受けていません</td></tr>
    </tbody></table>
  </section>

  <section>
    <h2>相手ごとの記録</h2>
    <table><tbody id="actors">
      <tr><td class="empty">まだ記録がありません</td></tr>
    </tbody></table>
  </section>

  <footer>
    <span id="updated">記録を読み込んでいます…</span>
  </footer>
</main>
<script>
(function () {
  var POLL_MS = 3000;
  var UNKNOWN = 'unknown';
  var lastGeneratedAt = null;

  function byId(id) { return document.getElementById(id); }

  // Written only when it differs, so a poll that changed nothing does not touch the document.
  function setText(node, text) {
    if (node && node.textContent !== text) { node.textContent = text; }
  }

  function trimNumber(value) {
    if (typeof value !== 'number' || !isFinite(value)) { return '—'; }
    return Number.isInteger(value) ? String(value) : value.toFixed(1);
  }

  function nameOf(row, fallbackLabel) {
    if (row.displayName) { return { text: row.displayName, raw: false }; }
    if (row.actorId === UNKNOWN) { return { text: fallbackLabel, raw: false }; }
    return { text: row.actorId || row.abnormalType || '—', raw: true };
  }

  function renderRows(tbody, rows, keyName, emptyText) {
    if (!rows || rows.length === 0) {
      if (tbody.dataset.state !== 'empty') {
        tbody.dataset.state = 'empty';
        tbody.textContent = '';
        var tr = document.createElement('tr');
        var td = document.createElement('td');
        td.className = 'empty';
        td.textContent = emptyText;
        tr.appendChild(td);
        tbody.appendChild(tr);
      }
      return;
    }

    tbody.dataset.state = 'rows';
    var signature = rows.map(function (r) { return r[keyName] + ':' + r.count; }).join('|');
    if (tbody.dataset.signature === signature) { return; }
    tbody.dataset.signature = signature;
    tbody.textContent = '';

    rows.forEach(function (row) {
      var tr = document.createElement('tr');
      var label = document.createElement('td');
      var resolved = nameOf(row, '不明な相手');
      // textContent throughout: the names come from the game's localisation and are shown as
      // text, never parsed as markup.
      label.textContent = resolved.text;
      if (resolved.raw) { label.className = 'raw'; }
      var count = document.createElement('td');
      count.className = 'n';
      count.textContent = String(row.count);
      var unit = document.createElement('small');
      unit.textContent = '回';
      count.appendChild(unit);
      tr.appendChild(label);
      tr.appendChild(count);
      tbody.appendChild(tr);
    });
  }

  function renderTopActor(top) {
    var host = byId('top-actor');
    var signature = top ? top.actorId + ':' + top.count : 'none';
    if (host.dataset.signature === signature) { return; }
    host.dataset.signature = signature;
    host.textContent = '';

    if (!top) {
      var none = document.createElement('span');
      none.className = 'none';
      none.textContent = 'まだ絶頂させた敵がいません';
      host.appendChild(none);
      return;
    }

    var resolved = nameOf(top, '不明な相手');
    var name = document.createElement('span');
    name.className = resolved.raw ? 'name raw' : 'name';
    name.textContent = resolved.text;
    var count = document.createElement('span');
    count.className = 'count';
    count.textContent = top.count + ' 回';
    host.appendChild(name);
    host.appendChild(count);
  }

  function render(data) {
    setText(byId('corruption-value'), trimNumber(data.corruption.value));
    setText(byId('corruption-cap'), data.corruption.cap > 0 ? ' / ' + trimNumber(data.corruption.cap) : '');

    var ratio = data.corruption.cap > 0
      ? Math.max(0, Math.min(1, data.corruption.value / data.corruption.cap))
      : 0;
    byId('corruption-bar').style.width = (ratio * 100).toFixed(1) + '%';

    setText(byId('climax-count'), String(data.climax.count));
    setText(byId('climax-limit'), data.climax.limit > 0 ? ' / ' + data.climax.limit : '');

    renderTopActor(data.topActor);
    renderRows(byId('debuffs'), data.debuffCounts, 'abnormalType', 'まだ何も受けていません');
    renderRows(byId('actors'), data.actorClimaxCounts, 'actorId', 'まだ記録がありません');

    lastGeneratedAt = data.generatedAt;
    setText(byId('updated'), '最終更新 ' + new Date(data.generatedAt).toLocaleTimeString('ja-JP'));
    byId('updated').className = '';
  }

  // A failed poll leaves the last good reading on the page. The game may simply be shutting down,
  // and blanking the diary would lose the very thing the reader was looking at (SPEC006 7章).
  function markStale() {
    var updated = byId('updated');
    updated.className = 'stale';
    updated.textContent = lastGeneratedAt
      ? '接続できません（最終更新 ' + new Date(lastGeneratedAt).toLocaleTimeString('ja-JP') + '）'
      : 'ゲームに接続できません';
  }

  function poll() {
    fetch('/api/stats', { cache: 'no-store' })
      .then(function (response) { return response.ok ? response.json() : Promise.reject(response.status); })
      .then(render)
      .catch(markStale);
  }

  poll();
  setInterval(poll, POLL_MS);
})();
</script>
</body>
</html>
""";
}
