/* Admin helpers: API calls with anti-forgery token, toasts, nav toggle, confirmations. */
(function () {
  'use strict';
  var meta = document.querySelector('meta[name="csrf-token"]');
  var csrf = meta ? meta.getAttribute('content') : '';

  function api(method, url, body) {
    var sep = url.indexOf('?') >= 0 ? '&' : '?';
    var opts = {
      method: method,
      cache: 'no-store',
      credentials: 'same-origin',
      headers: { 'RequestVerificationToken': csrf, 'Accept': 'application/json' }
    };
    if (body !== undefined) {
      opts.headers['Content-Type'] = 'application/json';
      opts.body = JSON.stringify(body);
    }
    return fetch(url + sep + 't=' + Date.now(), opts).then(function (res) {
      return res.text().then(function (text) {
        var data = null;
        try { data = text ? JSON.parse(text) : null; } catch (e) { data = null; }
        if (!res.ok) {
          var msg = (data && data.error) || (res.status === 401 ? 'Signed out — reload to sign in.' :
                    res.status === 403 ? 'You do not have permission to do that.' :
                    res.status === 429 ? 'Too many changes — wait a moment.' : 'Save failed (' + res.status + ').');
          throw new Error(msg);
        }
        return data;
      });
    });
  }

  var toastEl = document.querySelector('[data-toast]');
  var toastTimer = null;
  function toast(msg, isError) {
    if (!toastEl) return;
    toastEl.textContent = msg;
    toastEl.className = 'toast' + (isError ? ' error' : '');
    toastEl.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { toastEl.hidden = true; }, isError ? 4500 : 1800);
  }

  /** Re-fetch the current page and replace the element matching selector (no full reload). */
  function refreshFragment(selector) {
    return fetch(location.href, { cache: 'no-store', credentials: 'same-origin' })
      .then(function (r) { return r.text(); })
      .then(function (html) {
        var doc = new DOMParser().parseFromString(html, 'text/html');
        var fresh = doc.querySelector(selector);
        var current = document.querySelector(selector);
        if (fresh && current) current.innerHTML = fresh.innerHTML;
      });
  }

  // Mobile nav toggle
  var toggle = document.querySelector('[data-nav-toggle]');
  var nav = document.querySelector('[data-nav]');
  if (toggle && nav) {
    toggle.addEventListener('click', function () {
      var open = nav.classList.toggle('open');
      toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
  }

  // <form data-confirm="..."> and <button data-confirm="...">
  document.addEventListener('submit', function (e) {
    var form = e.target;
    var submitter = e.submitter;
    var msg = (submitter && submitter.getAttribute('data-confirm')) || form.getAttribute('data-confirm');
    if (msg && !window.confirm(msg)) e.preventDefault();
  });

  window.HSC = { api: api, toast: toast, refreshFragment: refreshFragment, csrf: csrf };
})();
