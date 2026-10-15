/* Stand control: each tap saves immediately with an optimistic update; failures revert. */
(function () {
  'use strict';
  var root = document.getElementById('stand');
  if (!root) return;
  var standId = root.getAttribute('data-stand');
  var standName = root.getAttribute('data-stand-name');
  var base = '/api/admin/stands/' + encodeURIComponent(standId);

  function indicator(row, state, msg) {
    var ind = row.querySelector('[data-ind]');
    if (!ind) return;
    ind.className = 'save-ind ' + (state === 'ok' ? 'ok' : state === 'err' ? 'err' : '');
    ind.textContent = state === 'ok' ? 'Saved' : state === 'err' ? 'Failed' : 'Saving…';
    if (state === 'ok') setTimeout(function () { if (ind.textContent === 'Saved') ind.textContent = ''; }, 1500);
    if (msg) HSC.toast(msg, state === 'err');
  }

  function applyRowState(row, selling, status) {
    row.setAttribute('data-selling', selling ? '1' : '0');
    row.setAttribute('data-status', status);
    row.querySelector('[data-selling-toggle]').checked = !!selling;
    var btns = row.querySelectorAll('.seg button');
    for (var i = 0; i < btns.length; i++) btns[i].classList.toggle('on', btns[i].getAttribute('data-status') === status);
  }

  function patch(row, body) {
    var prevSelling = row.getAttribute('data-selling') === '1';
    var prevStatus = row.getAttribute('data-status');
    var nextSelling = body.selling !== undefined ? body.selling : prevSelling;
    var nextStatus = body.status !== undefined ? body.status : prevStatus;
    applyRowState(row, nextSelling, nextStatus);
    indicator(row, 'saving');
    HSC.api('PATCH', base + '/items/' + encodeURIComponent(row.getAttribute('data-item')), body)
      .then(function (state) {
        applyRowState(row, state.selling, state.status);
        indicator(row, 'ok');
        applyFilter();
      })
      .catch(function (err) {
        applyRowState(row, prevSelling, prevStatus);
        indicator(row, 'err', err.message);
      });
  }

  root.addEventListener('change', function (e) {
    var t = e.target;
    if (t.matches('[data-selling-toggle]')) {
      var row = t.closest('[data-item]');
      patch(row, { selling: t.checked });
    }
  });

  root.addEventListener('click', function (e) {
    var t = e.target.closest('button');
    if (!t || t.disabled) return;

    if (t.parentElement && t.parentElement.classList.contains('seg')) {
      var row = t.closest('[data-item]');
      var status = t.getAttribute('data-status');
      if (row.getAttribute('data-status') === status) return;
      var body = { status: status };
      // Marking Low / Sold Out on an item that isn't selling also turns it on, when allowed.
      if (status !== 'ok' && row.getAttribute('data-selling') !== '1' && root.getAttribute('data-can-selling') === '1') body.selling = true;
      patch(row, body);
      return;
    }

    if (t.hasAttribute('data-bulk')) {
      var bulk = { scope: t.getAttribute('data-bulk'), targetId: t.getAttribute('data-target') };
      if (t.hasAttribute('data-selling')) bulk.selling = t.getAttribute('data-selling') === 'true';
      if (t.hasAttribute('data-status')) bulk.status = t.getAttribute('data-status');
      t.disabled = true;
      HSC.api('POST', base + '/bulk', bulk)
        .then(function (res) { syncAll(res.items); HSC.toast('Saved'); })
        .catch(function (err) { HSC.toast(err.message, true); })
        .then(function () { t.disabled = false; });
      return;
    }

    var action = t.getAttribute('data-action');
    if (!action) return;
    if (action === 'open') {
      run(t, HSC.api('POST', base + '/open'), true);
    } else if (action === 'close') {
      if (!window.confirm('Close ' + standName + ' stand? This resets all items.')) return;
      run(t, HSC.api('POST', base + '/close'), true);
    } else if (action === 'times' || action === 'clear-times') {
      var clear = action === 'clear-times';
      run(t, HSC.api('POST', base + '/times', {
        date: document.getElementById('t-date').value || null,
        opening: clear ? null : (document.getElementById('t-open').value || null),
        closing: clear ? null : (document.getElementById('t-close').value || null)
      }), true);
    } else if (action === 'apply') {
      var sel = document.getElementById('menu-select');
      var label = sel.options[sel.selectedIndex].text;
      if (!window.confirm('Apply "' + label + '"? Items not on this menu will stop selling, and Low/Sold Out flags reset.')) return;
      run(t, HSC.api('POST', base + '/apply-menu', { menuId: sel.value }), true);
    } else if (action === 'reapply') {
      run(t, HSC.api('POST', base + '/apply-menu', { menuId: null }), true);
    }
  });

  function run(btn, promise, reload) {
    btn.disabled = true;
    promise.then(function () {
      HSC.toast('Saved');
      if (reload) setTimeout(function () { location.reload(); }, 300);
    }).catch(function (err) {
      HSC.toast(err.message, true);
      btn.disabled = false;
    });
  }

  function syncAll(items) {
    var rows = root.querySelectorAll('[data-item]');
    for (var i = 0; i < rows.length; i++) {
      var st = items[rows[i].getAttribute('data-item')] || { selling: false, status: 'ok' };
      applyRowState(rows[i], st.selling, st.status);
    }
    applyFilter();
  }

  // Filters + search
  var search = root.querySelector('[data-search]');
  var filter = root.querySelector('[data-filter]');
  function applyFilter() {
    var q = (search.value || '').trim().toLowerCase();
    var f = filter.value;
    var rows = root.querySelectorAll('[data-item]');
    for (var i = 0; i < rows.length; i++) {
      var r = rows[i];
      var show = (!q || r.getAttribute('data-name').indexOf(q) >= 0) &&
        (f === 'all' || (f === 'selling' && r.getAttribute('data-selling') === '1') ||
         (f === 'flagged' && r.getAttribute('data-status') !== 'ok'));
      r.classList.toggle('hidden', !show);
    }
    var groups = root.querySelectorAll('[data-group]');
    for (var g = 0; g < groups.length; g++)
      groups[g].classList.toggle('hidden', !groups[g].querySelector('[data-item]:not(.hidden)'));
    var cats = root.querySelectorAll('[data-cat]');
    for (var c = 0; c < cats.length; c++)
      cats[c].classList.toggle('hidden', !cats[c].querySelector('[data-item]:not(.hidden)'));
  }
  search.addEventListener('input', applyFilter);
  filter.addEventListener('change', applyFilter);
})();
