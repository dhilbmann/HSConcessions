/* Public phone menu: fetches /api/public/state (never cached) and renders in place. */
(function () {
  'use strict';

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function badge(item, text) {
    return item.status === 'low' ? ' <span class="pub-badge">' + esc(text.lowBadge) + '</span>' : '';
  }

  function options(item) {
    if (!item.options || !item.options.length) return '';
    return '<div class="pub-options"><b>Options:</b> ' + item.options.map(function (o) {
      return esc(o.name) + (o.deltaText ? ' (' + esc(o.deltaText) + ')' : '');
    }).join(' · ') + '</div>';
  }

  function thumb(url) {
    return url ? '<img class="pub-thumb" src="' + esc(url) + '" alt="" loading="lazy" width="72" height="72">' : '';
  }

  function renderMenu(stand, text) {
    var html = '';
    stand.categories.forEach(function (cat) {
      html += '<section class="pub-cat"><div class="pub-cat-head">' +
        (cat.imageUrl ? '<img class="pub-cat-icon" src="' + esc(cat.imageUrl) + '" alt="">' : '') +
        '<h2>' + esc(cat.name) + '</h2></div>';
      cat.groups.forEach(function (g) {
        if (g.kind === 'item') {
          var it = g.items[0];
          html += '<article class="pub-card">' + thumb(it.thumbUrl) +
            '<div class="pub-body"><div class="pub-name-row"><span class="pub-name">' + esc(it.name) + badge(it, text) + '</span>' +
            '<span class="pub-price">' + esc(it.priceText) + '</span></div>' +
            (it.description ? '<div class="pub-desc">' + esc(it.description) + '</div>' : '') +
            options(it) + '</div></article>';
        } else {
          html += '<article class="pub-card">' + thumb(g.imageUrl ? g.imageUrl.replace(/\.webp$/, '-300.webp') : '') +
            '<div class="pub-body"><div class="pub-name-row"><span class="pub-name">' + esc(g.name) + '</span>' +
            (g.priceText ? '<span class="pub-price">' + esc(g.priceText) + '</span>' : '') + '</div>' +
            (g.description ? '<div class="pub-desc">' + esc(g.description) + '</div>' : '') +
            '<ul class="pub-varieties">' + g.items.map(function (it) {
              return '<li><span>' + esc(it.name) + badge(it, text) + options(it) + '</span>' +
                (it.showPrice && it.priceText ? '<span class="pub-price">' + esc(it.priceText) + '</span>' : '') + '</li>';
            }).join('') + '</ul></div></article>';
        }
      });
      html += '</section>';
    });
    return html;
  }

  function renderGames(games) {
    if (!games || !games.length) return '';
    return '<section class="pub-games"><h3>Upcoming games</h3>' + games.map(function (g) {
      var meta = [g.level, g.sport, g.home ? 'Home' : 'Away', g.location].filter(function (x) { return x; }).join(' · ');
      return '<div class="pub-game"><div class="pub-game-date">' + esc(g.date) + '<small>' + esc(g.time) + '</small></div>' +
        '<div><div class="pub-game-title">' + esc(g.title) + '</div><div class="pub-game-meta">' + esc(meta) + '</div></div></div>';
    }).join('') + '</section>';
  }

  function renderNotices(notices) {
    if (!notices || !notices.length) return '';
    return '<ul class="pub-notices">' + notices.map(function (n) { return '<li>' + esc(n) + '</li>'; }).join('') + '</ul>';
  }

  window.HSCPublic = { renderMenu: renderMenu, esc: esc };

  var app = document.getElementById('app');
  if (!app || !document.body.classList.contains('pub-root')) return;

  var tabsEl = document.querySelector('[data-tabs]');
  var footerEl = document.querySelector('[data-footer]');
  var taglineEl = document.querySelector('[data-tagline]');
  var requestedSlug = decodeURIComponent(location.pathname.replace(/^\/+|\/+$/g, '')).toLowerCase();
  var selectedSlug = requestedSlug || null;
  var lastJson = null;
  var lastState = null;
  var refreshMs = 120000;
  var timer = null;

  function render(state) {
    var open = state.stands.filter(function (s) { return s.isOpen; });
    var requested = requestedSlug ? state.stands.filter(function (s) { return s.slug.toLowerCase() === requestedSlug; })[0] : null;
    var html = '';

    footerEl.textContent = state.site.footer || '';
    taglineEl.textContent = state.site.tagline || '';

    if (requested && !requested.isOpen) {
      html += '<section class="pub-message"><h2>The ' + esc(requested.name) + ' stand is closed</h2></section>';
    }

    if (open.length === 0) {
      tabsEl.hidden = true;
      tabsEl.innerHTML = '';
      if (!requested || requested.isOpen) html += '<section class="pub-message"><h2>' + esc(state.text.noStandsOpen) + '</h2></section>';
      html += renderNotices(state.notices) + renderGames(state.upcomingGames);
      app.innerHTML = html;
      return;
    }

    var current;
    if (open.length === 1) {
      tabsEl.hidden = true;
      tabsEl.innerHTML = '';
      current = open[0];
      html += '<div class="pub-banner">' + esc(current.banner) +
        (current.openUntil ? '<small>' + esc(current.openUntil) + '</small>' : '') + '</div>';
    } else {
      current = open.filter(function (s) { return selectedSlug && s.slug.toLowerCase() === selectedSlug; })[0] || open[0];
      tabsEl.hidden = false;
      tabsEl.setAttribute('role', 'tablist');
      tabsEl.innerHTML = open.map(function (s) {
        return '<button class="pub-tab" role="tab" type="button" data-slug="' + esc(s.slug) + '" aria-selected="' +
          (s === current ? 'true' : 'false') + '">' + esc(s.name) + '</button>';
      }).join('');
      if (current.openUntil) html += '<p class="pub-open-until">' + esc(current.openUntil) + '</p>';
    }

    html += renderNotices(state.notices);
    html += current.categories.length
      ? renderMenu(current, state.text)
      : '<section class="pub-message"><p>Menu coming soon.</p></section>';
    app.innerHTML = html;
  }

  tabsEl.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-slug]');
    if (!btn) return;
    selectedSlug = btn.getAttribute('data-slug').toLowerCase();
    requestedSlug = selectedSlug;
    if (window.history && history.replaceState) history.replaceState(null, '', '/' + btn.getAttribute('data-slug'));
    if (lastState) render(lastState);
    window.scrollTo(0, 0);
  });

  function load() {
    return fetch('/api/public/state?t=' + Date.now(), { cache: 'no-store' })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.text(); })
      .then(function (text) {
        if (text === lastJson) return;
        lastJson = text;
        lastState = JSON.parse(text);
        refreshMs = (lastState.refreshSeconds || 120) * 1000;
        render(lastState);
      })
      .catch(function () {
        if (!lastState) app.innerHTML = '<section class="pub-message"><p>Couldn\'t load the menu. Retrying…</p></section>';
      })
      .then(schedule);
  }

  function schedule() {
    clearTimeout(timer);
    timer = setTimeout(load, refreshMs);
  }

  document.addEventListener('visibilitychange', function () {
    if (document.visibilityState === 'visible') load();
  });

  load();
})();
