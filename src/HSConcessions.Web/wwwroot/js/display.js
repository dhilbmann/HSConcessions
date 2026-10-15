/* TV display (/display/{stand}). ES2017, no modules, no build step — written for smart-TV browsers.
   Layouts: "rotate" (one category per screen), "single" (whole menu on one board), "auto" (single if it fits readably).
   Polls JSON only; status and price changes are applied in place. Never reloads while open (except the 4 AM refresh when closed). */
(function () {
  'use strict';

  var BASE = { name: 72, price: 84, img: 230 };   // rotate: sizes at scale 1.0
  var MIN_SCALE = 0.58;                           // rotate: name ≥ 40px, price ≥ 48px
  var MAX_SCALE = 1.3;
  var FLOOR_SCALE = 0.3;
  var BOARD_MIN_READABLE = 40;                    // single: item names ≥ 40px
  var BOARD_MAX = 96;
  var BOARD_FLOOR = 18;
  var FADE_MS = 650;

  var stage = document.getElementById('stage');
  var layers = [document.getElementById('layer-a'), document.getElementById('layer-b')];
  var offline = document.getElementById('offline');
  var clockEl = document.getElementById('clock');
  var updatedEl = document.getElementById('updated');
  var clockOffset = 0;      // server time minus this TV's clock, so a TV with a wrong clock still shows the right time
  var themeLink = document.getElementById('theme-css');
  var slug = decodeURIComponent(location.pathname.split('/').filter(function (p) { return p; })[1] || '');
  var layoutParam = (location.search.match(/[?&]layout=(rotate|single|auto)\b/) || [])[1] || '';

  var data = null;          // last good display state
  var visible = 0;          // index of the visible layer
  var screenKind = null;    // 'closed' | 'rotate' | 'single' | 'empty'
  var screenKey = null;     // closed: message+image; rotate: category id; single: menu signature
  var catIndex = 0;
  var pending = false;      // rotate: structural change waiting for the next transition
  var autoChoice = null;    // auto: 'single' | 'rotate', decided per menu signature
  var autoSig = null;
  var failures = 0;
  var pollTimer = null;
  var rotateTimer = null;
  var themeEtag = null;
  var loadedAt = Date.now();

  // ---------- helpers ----------
  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function scaleStage() {
    var s = Math.min(window.innerWidth / 1920, window.innerHeight / 1080);
    stage.style.transform = 'scale(' + s + ')';
    stage.style.left = Math.max(0, (window.innerWidth - 1920 * s) / 2) + 'px';
    stage.style.top = Math.max(0, (window.innerHeight - 1080 * s) / 2) + 'px';
  }

  function hiddenLayer() { return layers[1 - visible]; }

  /** Clock and "Menu updated" overlays: shown per settings, positioned for the kind of screen on display. */
  function updateChrome(kind) {
    var showClock = !!(data && data.showClock);
    var showUpdated = !!(data && data.showUpdatedTime);
    stage.setAttribute('data-screen', kind || screenKind || 'closed');
    stage.classList.toggle('has-clock', showClock);
    stage.classList.toggle('has-updated', showUpdated);
    clockEl.hidden = !showClock;
    updatedEl.hidden = !showUpdated;
    if (showUpdated) updatedEl.textContent = data.dataUpdatedText || '';
    tickClock();
  }

  function tickClock() {
    if (!data || !data.showClock) return;
    var now = new Date(Date.now() + clockOffset);
    var text;
    try {
      text = new Intl.DateTimeFormat('en-US', { timeZone: data.site.timeZone || undefined, hour: 'numeric', minute: '2-digit', hour12: true }).format(now);
    } catch (e) {
      var h = now.getHours(), m = now.getMinutes();
      text = ((h % 12) || 12) + ':' + (m < 10 ? '0' : '') + m + (h < 12 ? ' AM' : ' PM');
    }
    var parts = text.split(' ');
    var html = esc(parts[0]) + (parts[1] ? '<span class="tv-clock-ampm">' + esc(parts[1]) + '</span>' : '');
    if (clockEl.innerHTML !== html) clockEl.innerHTML = html;
  }

  function swap() {
    var next = hiddenLayer();
    var cur = layers[visible];
    next.classList.add('visible');
    cur.classList.remove('visible');
    visible = 1 - visible;
    setTimeout(function () { if (!cur.classList.contains('visible')) cur.innerHTML = ''; }, FADE_MS + 50);
  }

  function categories() {
    return (data && data.stand.isOpen && data.stand.categories) || [];
  }

  /** Structure only (ids, grouping, which prices are shown) — statuses and price amounts update in place. */
  function signature(cats) {
    return JSON.stringify(cats.map(function (c) {
      return [c.id, c.name, c.imageUrl, c.groups.map(function (g) {
        return [g.kind, g.id, g.name, !!g.priceText, g.items.map(function (it) { return [it.id, it.name, it.showPrice, !!it.imageUrl]; })];
      })];
    })) + '|' + data.groupVarieties + '|' + data.stand.name + '|' + data.text.soldOutStamp + '|' + data.text.lowBadge;
  }

  function itemClass(it) {
    return (it.status === 'low' ? ' is-low' : '') + (it.status === 'soldOut' ? ' is-sold' : '');
  }

  function miniMarks() {
    return '<span class="tv-mini-stamp">' + esc(data.text.soldOutStamp) + '</span>' +
      '<span class="tv-mini-badge">' + esc(data.text.lowBadge) + '</span>';
  }

  /** A variety inside a group: name, its own price only if it differs from the group's, mini stamp / badge. */
  function varietyHtml(it, extraClass) {
    return '<span class="tv-var ' + extraClass + itemClass(it) + '" data-item="' + esc(it.id) + '">' +
      '<span class="tv-var-name">' + esc(it.name) + miniMarks() + '</span>' +
      (it.showPrice && it.priceText ? '<span class="tv-var-price" data-price>' + esc(it.priceText) + '</span>' : '') +
      '</span>';
  }

  /** Varieties in a row, separated by a wide gap (set in CSS). */
  function varietiesHtml(items, extraClass) {
    return items.map(function (it) {
      return '<span class="tv-varwrap">' + varietyHtml(it, extraClass) + '</span>';
    }).join('');
  }

  // ---------- rotate layout ----------
  function itemCardHtml(it) {
    var opts = (it.options || []).map(function (o) { return esc(o.name) + (o.deltaText ? ' (' + esc(o.deltaText) + ')' : ''); }).join(' · ');
    return '<div class="tv-item' + itemClass(it) + '" data-item="' + esc(it.id) + '">' +
      (it.imageUrl ? '<img class="tv-img" src="' + esc(it.imageUrl) + '" alt="">' : '') +
      '<div class="tv-text">' +
      (it.groupName ? '<div class="tv-group">' + esc(it.groupName) + '</div>' : '') +
      '<div class="tv-name-wrap"><div class="tv-name">' + esc(it.name) + '</div></div>' +
      (it.description ? '<div class="tv-desc">' + esc(it.description) + '</div>' : '') +
      (opts ? '<div class="tv-options">' + opts + '</div>' : '') +
      '<div class="tv-badge">' + esc(data.text.lowBadge) + '</div>' +
      '<div class="tv-stamp">' + esc(data.text.soldOutStamp) + '</div>' +
      '</div>' +
      '<span class="tv-lead"></span>' +
      '<div class="tv-price" data-price>' + esc(it.priceText) + '</div>' +
      '</div>';
  }

  /** A group (e.g. Soda) as its own section: a mid-level header with the shared price, then one button per variety. */
  function groupSectionHtml(g) {
    return '<section class="tv-gsection' + (allSold(g.items) ? ' is-sold' : '') + '" data-group="' + esc(g.id) + '">' +
      '<div class="tv-ghead">' +
      (g.imageUrl ? '<img class="tv-gicon tv-img" src="' + esc(g.imageUrl) + '" alt="">' : '') +
      '<span class="tv-gtitle">' + esc(g.name) + '</span><span class="tv-grule"></span>' +
      (g.priceText ? '<span class="tv-gprice" data-gprice>' + esc(g.priceText) + '</span>' : '') +
      '</div>' +
      '<div class="tv-chips">' + varietiesHtml(g.items, 'tv-chip') + '</div>' +
      '<div class="tv-stamp">' + esc(data.text.soldOutStamp) + '</div>' +
      '</section>';
  }

  function allSold(items) {
    for (var i = 0; i < items.length; i++) if (items[i].status !== 'soldOut') return false;
    return items.length > 0;
  }

  /** Single items become cards; with grouping on, each group becomes a section. Order follows the menu. */
  function bodyFor(cat) {
    var parts = [], run = [], cards = [], sections = 0;
    function flush() {
      if (run.length) parts.push('<div class="tv-grid tv-grid-auto">' + run.join('') + '</div>');
      run = [];
    }
    cat.groups.forEach(function (g) {
      if (g.kind === 'sub' && data.groupVarieties) { flush(); parts.push(groupSectionHtml(g)); sections++; }
      else g.items.forEach(function (it) { var h = itemCardHtml(it); run.push(h); cards.push(h); });
    });
    flush();
    return { mixed: !!data.groupVarieties, html: parts.join(''), cards: cards };
  }

  function renderRotate(layer, cat, index, count) {
    var body = bodyFor(cat);
    var dots = '';
    for (var i = 0; i < count; i++) dots += '<span class="tv-dot' + (i === index ? ' on' : '') + '"></span>';
    layer.innerHTML = '<div class="tv-screen" data-sig="' + esc(signature([cat])) + '">' +
      '<header class="tv-cat-header">' +
      (cat.imageUrl ? '<img class="tv-cat-icon" src="' + esc(cat.imageUrl) + '" alt="">' : '') +
      '<h1 class="tv-cat-name">' + esc(cat.name) + '</h1></header>' +
      (body.mixed ? '<div class="tv-body">' + body.html + '</div>' : '<div class="tv-grid">' + body.cards.join('') + '</div>') +
      '<footer class="tv-footer"><span class="tv-stand">' + esc(data.stand.name) + '</span>' +
      '<span class="tv-dots">' + (count > 1 ? dots : '') + '</span></footer>' +
      '</div>';
    var screen = layer.querySelector('.tv-screen');
    if (body.mixed) fitMixed(screen); else fitRotate(screen, body.cards.length);
  }

  // ---------- mixed screens: card grids plus group sections, stacked in menu order ----------
  function applyMixed(screen, s, cols) {
    screen.style.setProperty('--s', s);
    screen.style.setProperty('--name', Math.round(BASE.name * s) + 'px');
    screen.style.setProperty('--price', Math.round(BASE.price * s) + 'px');
    screen.style.setProperty('--img', Math.round(BASE.img * s) + 'px');
    var grids = screen.querySelectorAll('.tv-grid-auto');
    for (var i = 0; i < grids.length; i++) grids[i].style.setProperty('--cols', Math.min(cols, grids[i].children.length));
  }

  /** Rows size to their content, so what matters is the body's height and that no item name is cut off.
      Only names are measured: rubber stamps and tall display-font glyphs may extend past a row by design. */
  function fitsMixed(screen) {
    if (overflows(screen.querySelector('.tv-body'))) return false;
    var names = screen.querySelectorAll('.tv-name');
    for (var i = 0; i < names.length; i++) {
      if (names[i].scrollWidth > names[i].clientWidth + 1) return false;
    }
    return true;
  }

  function bestMixed(screen) {
    var grids = screen.querySelectorAll('.tv-grid-auto');
    var maxRun = 1;
    for (var i = 0; i < grids.length; i++) maxRun = Math.max(maxRun, grids[i].children.length);
    var best = null;
    for (var cols = 1; cols <= Math.min(maxRun, 4); cols++) {
      applyMixed(screen, FLOOR_SCALE, cols);
      if (!fitsMixed(screen)) continue;
      var lo = FLOOR_SCALE, hi = MAX_SCALE;
      applyMixed(screen, hi, cols);
      if (fitsMixed(screen)) lo = hi;
      else {
        for (var k = 0; k < 7; k++) {
          var mid = (lo + hi) / 2;
          applyMixed(screen, mid, cols);
          if (fitsMixed(screen)) lo = mid; else hi = mid;
        }
      }
      if (!best || lo > best.s + 0.001) best = { s: lo, cols: cols };
    }
    return best;
  }

  /** Same rules as the card grid: never split the category; drop pictures before going below readable sizes. */
  function fitMixed(screen) {
    var best = null;
    if (screen.querySelector('.tv-img')) {
      screen.classList.remove('no-images');
      best = bestMixed(screen);
      if (best && best.s >= MIN_SCALE) { applyMixed(screen, best.s, best.cols); return; }
    }
    screen.classList.add('no-images');
    best = bestMixed(screen) || { s: FLOOR_SCALE, cols: 3 };
    applyMixed(screen, best.s, best.cols);
  }

  function applyScale(screen, s, cols, rows) {
    var grid = screen.querySelector('.tv-grid');
    grid.style.setProperty('--cols', cols);
    grid.style.setProperty('--rows', rows);
    screen.style.setProperty('--s', s);
    screen.style.setProperty('--name', Math.round(BASE.name * s) + 'px');
    screen.style.setProperty('--price', Math.round(BASE.price * s) + 'px');
    screen.style.setProperty('--img', Math.round(BASE.img * s) + 'px');
  }

  function overflows(el) {
    return el.scrollHeight > el.clientHeight + 1 || el.scrollWidth > el.clientWidth + 1;
  }

  function fitsRotate(screen) {
    var items = screen.querySelectorAll('.tv-item');
    for (var i = 0; i < items.length; i++) {
      if (overflows(items[i])) return false;
      var text = items[i].querySelector('.tv-text');
      if (text && text.scrollWidth > text.clientWidth + 1) return false;
    }
    return true;
  }

  function bestRotate(screen, n) {
    var best = null;
    for (var cols = 1; cols <= Math.min(n, 6); cols++) {
      var rows = Math.ceil(n / cols);
      if (rows > 8) continue;
      applyScale(screen, FLOOR_SCALE, cols, rows);
      if (!fitsRotate(screen)) continue;
      var lo = FLOOR_SCALE, hi = MAX_SCALE;
      applyScale(screen, hi, cols, rows);
      if (fitsRotate(screen)) lo = hi;
      else {
        for (var k = 0; k < 7; k++) {
          var mid = (lo + hi) / 2;
          applyScale(screen, mid, cols, rows);
          if (fitsRotate(screen)) lo = mid; else hi = mid;
        }
      }
      if (!best || lo > best.s + 0.001) best = { s: lo, cols: cols, rows: rows };
    }
    return best;
  }

  /** Never split a category; drop images before going below readable sizes. */
  function fitRotate(screen, n) {
    if (n === 0) return;
    var best = null;
    if (screen.querySelector('.tv-img')) {
      screen.classList.remove('no-images');
      best = bestRotate(screen, n);
      if (best && best.s >= MIN_SCALE) { applyScale(screen, best.s, best.cols, best.rows); return; }
    }
    screen.classList.add('no-images');
    best = bestRotate(screen, n);
    if (!best) {
      var cols = Math.min(n, Math.ceil(Math.sqrt(n * 1.8)));
      best = { s: FLOOR_SCALE, cols: cols, rows: Math.ceil(n / cols) };
    }
    applyScale(screen, best.s, best.cols, best.rows);
  }

  // ---------- single-page board ----------
  function boardRowHtml(it) {
    return '<div class="tv-brow' + itemClass(it) + '" data-item="' + esc(it.id) + '">' +
      '<span class="tv-bname"><span class="tv-var-name">' + esc(it.name) + miniMarks() + '</span></span>' +
      '<span class="tv-bdots"></span>' +
      '<span class="tv-bprice" data-price>' + esc(it.priceText) + '</span></div>';
  }

  function boardGroupHtml(g) {
    return '<div class="tv-bgroup' + (allSold(g.items) ? ' is-sold' : '') + '" data-group="' + esc(g.id) + '">' +
      '<div class="tv-brow tv-bgroup-head"><span class="tv-bname">' + esc(g.name) + '</span>' +
      (g.priceText ? '<span class="tv-bdots"></span><span class="tv-bprice" data-gprice>' + esc(g.priceText) + '</span>' : '') + '</div>' +
      '<div class="tv-vars tv-bvars">' + varietiesHtml(g.items, '') + '</div></div>';
  }

  function renderSingle(layer) {
    var cats = categories();
    layer.innerHTML = '<div class="tv-board" data-sig="' + esc(signature(cats)) + '">' +
      '<header class="tv-board-head"><img class="tv-board-logo" src="' + esc(data.site.logoUrl) + '" alt="">' +
      '<div class="tv-board-title">' + esc(data.site.title) + '</div>' +
      '<div class="tv-board-stand">' + esc(data.stand.name) + '</div></header>' +
      '<div class="tv-board-cols">' + cats.map(function (c) {
        return '<section class="tv-bcat"><h2 class="tv-bcat-name">' + esc(c.name) + '</h2>' +
          c.groups.map(function (g) {
            return g.kind === 'sub' ? boardGroupHtml(g) : g.items.map(boardRowHtml).join('');
          }).join('') + '</section>';
      }).join('') + '</div></div>';
    return fitSingle(layer.querySelector('.tv-board'));
  }

  function setBoard(board, cols, size) {
    var c = board.querySelector('.tv-board-cols');
    c.style.columnCount = cols;
    c.style.webkitColumnCount = cols;
    board.style.setProperty('--b', size + 'px');
  }

  function fitsBoard(board) { return !overflows(board.querySelector('.tv-board-cols')); }

  /** Picks the column count and text size that give the largest readable text. Returns true if readable. */
  function fitSingle(board) {
    var best = null;
    for (var cols = 1; cols <= 4; cols++) {
      setBoard(board, cols, BOARD_FLOOR);
      if (!fitsBoard(board)) continue;
      var lo = BOARD_FLOOR, hi = BOARD_MAX;
      setBoard(board, cols, hi);
      if (fitsBoard(board)) lo = hi;
      else {
        for (var k = 0; k < 8; k++) {
          var mid = (lo + hi) / 2;
          setBoard(board, cols, mid);
          if (fitsBoard(board)) lo = mid; else hi = mid;
        }
      }
      // Prefer fewer columns unless more columns give clearly bigger text.
      if (!best || lo > best.size * 1.08) best = { cols: cols, size: lo };
    }
    if (!best) best = { cols: 4, size: BOARD_FLOOR };
    setBoard(board, best.cols, Math.floor(best.size));
    return best.size >= BOARD_MIN_READABLE;
  }

  // ---------- screens ----------
  function closedKey() { return data.stand.message + '|' + data.site.closedImageUrl; }

  function showClosed() {
    var key = closedKey();
    stopRotation();
    updateChrome('closed');
    if (screenKind === 'closed' && screenKey === key) return;
    hiddenLayer().innerHTML = '<div class="tv-closed">' +
      '<img class="tv-closed-img" src="' + esc(data.site.closedImageUrl) + '" alt="">' +
      '<div class="tv-closed-msg">' + esc(data.stand.message) + '</div></div>';
    swap();
    screenKind = 'closed';
    screenKey = key;
  }

  function showEmpty() {
    stopRotation();
    updateChrome('closed');
    if (screenKind === 'empty') return;
    hiddenLayer().innerHTML = '<div class="tv-closed"><img class="tv-closed-img" src="' + esc(data.site.closedImageUrl) + '" alt=""></div>';
    swap();
    screenKind = 'empty';
    screenKey = 'empty';
  }

  function showSingle() {
    updateChrome('single');
    renderSingle(hiddenLayer());
    swap();
    screenKind = 'single';
    screenKey = signature(categories());
    stopRotation();
  }

  function showCategory(index) {
    updateChrome('rotate');
    var cats = categories();
    catIndex = index % cats.length;
    renderRotate(hiddenLayer(), cats[catIndex], catIndex, cats.length);
    swap();
    screenKind = 'rotate';
    screenKey = cats[catIndex].id;
    pending = false;
  }

  /** Which layout to use now. Auto measures the single board once per menu structure. */
  function chooseLayout() {
    if (data.layout !== 'auto') return data.layout === 'single' ? 'single' : 'rotate';
    var sig = signature(categories());
    if (autoSig !== sig) {
      autoSig = sig;
      var probe = hiddenLayer();
      autoChoice = renderSingle(probe) ? 'single' : 'rotate';
      probe.innerHTML = '';
    }
    return autoChoice;
  }

  function showOpen() {
    if (!categories().length) { showEmpty(); return; }
    if (chooseLayout() === 'single') { showSingle(); return; }
    showCategory(screenKind === 'rotate' ? catIndex : 0);
    scheduleRotation();
  }

  function stopRotation() { clearTimeout(rotateTimer); rotateTimer = null; }

  function scheduleRotation() {
    stopRotation();
    rotateTimer = setTimeout(function () {
      rotateTimer = null;
      if (!data.stand.isOpen || screenKind !== 'rotate') return;
      if (chooseLayout() !== 'rotate') { showOpen(); return; }
      var cats = categories();
      if (!cats.length) { showEmpty(); return; }
      if (cats.length > 1) showCategory(catIndex + 1);
      else if (pending || screenKey !== cats[0].id) showCategory(0);
      scheduleRotation();
    }, Math.max(3, data.categorySeconds || 12) * 1000);
  }

  // ---------- in-place updates (no re-render, no flash) ----------
  function itemIndex() {
    var map = {};
    categories().forEach(function (c) {
      c.groups.forEach(function (g) { g.items.forEach(function (it) { map[it.id] = { item: it, group: g }; }); });
    });
    return map;
  }

  /** Updates statuses and prices on the visible screen. Returns false if its structure no longer matches the data. */
  function updateInPlace() {
    var screen = layers[visible].querySelector('[data-sig]');
    if (!screen) return false;
    var expected;
    if (screenKind === 'single') expected = signature(categories());
    else {
      var cats = categories(), cat = null;
      for (var i = 0; i < cats.length; i++) if (cats[i].id === screenKey) { cat = cats[i]; catIndex = i; }
      if (!cat) return false;
      expected = signature([cat]);
    }
    if (screen.getAttribute('data-sig') !== expected) return false;

    var map = itemIndex();
    var nodes = screen.querySelectorAll('[data-item]');
    for (var n = 0; n < nodes.length; n++) {
      var entry = map[nodes[n].getAttribute('data-item')];
      if (!entry) return false;
      var el = nodes[n], it = entry.item;
      el.classList.toggle('is-low', it.status === 'low');
      el.classList.toggle('is-sold', it.status === 'soldOut');
      var price = el.querySelector('[data-price]');
      if (price && price.textContent !== it.priceText) price.textContent = it.priceText;
    }
    var groups = screen.querySelectorAll('[data-group]');
    for (var g = 0; g < groups.length; g++) {
      var gid = groups[g].getAttribute('data-group');
      var group = null;
      for (var key in map) if (map[key].group.id === gid) { group = map[key].group; break; }
      if (!group) return false;
      groups[g].classList.toggle('is-sold', allSold(group.items));
      var gp = groups[g].querySelector('[data-gprice]');
      if (gp && gp.textContent !== (group.priceText || '')) gp.textContent = group.priceText || '';
    }
    return true;
  }

  function apply(next) {
    var prev = data;
    data = next;
    if (!data.stand.isOpen) { showClosed(); return; }
    if (!prev || !prev.stand.isOpen || prev.layout !== data.layout || screenKind === 'closed' || screenKind === 'empty' || screenKind === null) {
      showOpen();
      return;
    }
    updateChrome(screenKind);
    if (updateInPlace()) {
      if (screenKind === 'rotate' && !rotateTimer) scheduleRotation();
      return;
    }
    if (screenKind === 'single') showOpen();      // structure changed: one gentle crossfade
    else { pending = true; if (!rotateTimer) scheduleRotation(); } // rotate: apply at the next transition
  }

  // ---------- polling ----------
  /** Make sure the theme fonts are loaded before measuring text for the auto-fit. */
  function fontsLoaded() {
    try {
      if (!document.fonts || !document.fonts.load) return Promise.resolve();
      var css = getComputedStyle(document.documentElement);
      var display = css.getPropertyValue('--font-display').trim();
      var body = css.getPropertyValue('--font-body').trim();
      var waits = [];
      if (display) waits.push(document.fonts.load('700 72px ' + display), document.fonts.load('400 72px ' + display));
      if (body) waits.push(document.fonts.load('400 30px ' + body), document.fonts.load('700 30px ' + body));
      var timeout = new Promise(function (resolve) { setTimeout(resolve, 3000); });
      return Promise.race([Promise.all(waits), timeout]).catch(function () { });
    } catch (e) {
      return Promise.resolve();
    }
  }

  function poll() {
    clearTimeout(pollTimer);
    var url = '/api/public/display/' + encodeURIComponent(slug) + '?t=' + Date.now() + (layoutParam ? '&layout=' + layoutParam : '');
    fetch(url, { cache: 'no-store' })
      .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
      .then(function (json) { return fontsLoaded().then(function () { return json; }); })
      .then(function (json) {
        failures = 0;
        offline.hidden = true;
        var serverMs = Date.parse(String(json.serverTime).replace(/(\.\d{3})\d+/, '$1')); // older TV browsers want ≤ 3 fraction digits
        if (!isNaN(serverMs)) clockOffset = serverMs - Date.now();
        apply(json);
        checkTheme();
        pollTimer = setTimeout(poll, Math.max(15, data.refreshSeconds || 120) * 1000);
      })
      .catch(function () {
        failures++;
        offline.hidden = false;
        var normal = Math.max(15, (data && data.refreshSeconds) || 120) * 1000;
        pollTimer = setTimeout(poll, failures >= 3 ? Math.min(30000, normal) : normal);
      });
  }

  /** Swap the theme stylesheet in place when its ETag changes (branding edits). */
  function checkTheme() {
    fetch('/theme.css?t=' + Date.now(), { cache: 'no-store' })
      .then(function (r) {
        var etag = r.headers.get('ETag');
        if (!etag) return;
        if (themeEtag && etag !== themeEtag) {
          var link = document.createElement('link');
          link.rel = 'stylesheet';
          link.href = '/theme.css?v=' + encodeURIComponent(etag);
          link.onload = function () { if (themeLink.parentNode) themeLink.parentNode.removeChild(themeLink); themeLink = link; };
          themeLink.parentNode.insertBefore(link, themeLink.nextSibling);
        }
        themeEtag = etag;
      })
      .catch(function () { /* keep current theme */ });
  }

  // ---------- 4 AM refresh (never while open) ----------
  function localHourMinute() {
    try {
      var tz = data && data.site.timeZone;
      var parts = new Intl.DateTimeFormat('en-US', { timeZone: tz || undefined, hour: 'numeric', minute: 'numeric', hour12: false }).formatToParts(new Date());
      var h = 0, m = 0;
      parts.forEach(function (p) { if (p.type === 'hour') h = parseInt(p.value, 10) % 24; if (p.type === 'minute') m = parseInt(p.value, 10); });
      return { h: h, m: m };
    } catch (e) {
      var d = new Date();
      return { h: d.getHours(), m: d.getMinutes() };
    }
  }

  setInterval(function () {
    var t = localHourMinute();
    var open = data && data.stand.isOpen;
    if (t.h === 4 && t.m < 15 && !open && Date.now() - loadedAt > 60 * 60 * 1000) location.reload();
  }, 60 * 1000);

  // ---------- burn-in mitigation for static screens (closed and single-page board) ----------
  setInterval(function () {
    var el = null;
    if (screenKind === 'closed' || screenKind === 'empty') el = layers[visible].querySelector('.tv-closed');
    else if (screenKind === 'single') el = layers[visible].querySelector('.tv-board');
    if (!el) return;
    var range = screenKind === 'single' ? 6 : 24;
    var dx = Math.round(Math.random() * range - range / 2), dy = Math.round(Math.random() * range - range / 2);
    el.style.transform = 'translate(' + dx + 'px,' + dy + 'px)';
  }, 3 * 60 * 1000);

  setInterval(tickClock, 1000);
  window.addEventListener('resize', scaleStage);
  scaleStage();
  poll();
})();
