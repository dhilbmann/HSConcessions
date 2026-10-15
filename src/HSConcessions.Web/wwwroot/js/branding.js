/* Branding editor: live preview with unsaved values + WCAG contrast warnings.
   Derived shades mirror ThemeService on the server. */
(function () {
  'use strict';
  var form = document.getElementById('brand-form');
  if (!form) return;
  var ctx = window.HSC_BRANDING || {};

  function isHex(v) { return /^#[0-9a-fA-F]{6}$/.test(v); }
  function rgb(h) { return [parseInt(h.substr(1, 2), 16), parseInt(h.substr(3, 2), 16), parseInt(h.substr(5, 2), 16)]; }
  function hex(c) { return '#' + c.map(function (v) { var s = Math.round(Math.max(0, Math.min(255, v))).toString(16); return s.length < 2 ? '0' + s : s; }).join('').toUpperCase(); }
  function mix(a, b, t) { var x = rgb(a), y = rgb(b); return hex([0, 1, 2].map(function (i) { return x[i] + (y[i] - x[i]) * t; })); }
  function lum(h) {
    return rgb(h).map(function (c) { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); })
      .reduce(function (acc, c, i) { return acc + c * [0.2126, 0.7152, 0.0722][i]; }, 0);
  }
  function contrast(a, b) { var la = lum(a), lb = lum(b); return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05); }
  function readable(bg, light, dark) { return contrast(bg, light) >= contrast(bg, dark) ? light : dark; }

  function val(key) {
    var el = form.querySelector('[data-color="' + key + '"]');
    return el && isHex(el.value) ? el.value.toUpperCase() : null;
  }

  function fontStack(sel) {
    var opt = sel.options[sel.selectedIndex];
    return "'" + opt.value + "', " + opt.getAttribute('data-fallback');
  }

  function fill(template, stand) {
    return (template || '').replace(/\{time\}/gi, '6:00 PM').replace(/\{stand\}/gi, stand || 'Football')
      .replace(/\{school\}/gi, ctx.school || '').replace(/\{mascot\}/gi, ctx.mascot || '').replace(/\{site\}/gi, ctx.site || '');
  }

  function update() {
    var p = val('Primary'), a = val('Accent'), d = val('Dark'), l = val('Light'), s = val('Stamp');
    var lowSame = form.querySelector('[data-low-same]').checked;
    var low = lowSame ? a : val('LowBadge');
    form.querySelector('[data-color="LowBadge"]').disabled = lowSame;
    if (!p || !a || !d || !l || !s || !low) return;

    var vars = {
      '--color-primary': p,
      '--color-primary-dark': mix(p, '#000000', 0.35),
      '--color-primary-light': mix(p, '#FFFFFF', 0.85),
      '--color-on-primary': readable(p, l, d),
      '--color-accent': a,
      '--color-on-accent': readable(a, l, d),
      '--color-dark': d,
      '--color-light': l,
      '--color-stamp': s,
      '--color-low': low,
      '--color-on-low': readable(low, l, d),
      '--font-display': fontStack(form.querySelector('[data-font="display"]')),
      '--font-body': fontStack(form.querySelector('[data-font="body"]')),
      '--page-bg': mix(l, p, 0.04),
      '--page-surface': l,
      '--page-text': mix(d, l, 0.1),
      '--page-muted': mix(d, l, 0.45),
      '--page-border': mix(l, d, 0.12),
      '--page-price': contrast(a, l) >= 3 ? a : contrast(p, l) >= 4.5 ? p : mix(a, d, 0.45)
    };
    var previews = document.querySelectorAll('[data-preview]');
    for (var i = 0; i < previews.length; i++) {
      for (var k in vars) previews[i].style.setProperty(k, vars[k]);
    }

    var text = function (name) { var el = form.querySelector('[data-text="' + name + '"]'); return el ? el.value : ''; };
    setAll('[data-pv="banner"]', fill(text('banner')));
    setAll('[data-pv="low"]', fill(text('low')));
    setAll('[data-pv="stamp"]', fill(text('stamp')));
    setAll('[data-pv="closed"]', fill(text('closed')));
    setAll('[data-pv="footer"]', fill(text('footer')));

    var pairs = [
      ['Light text on primary', l, p],
      ['Light text on dark (TV)', l, d],
      ['Accent price on dark (TV)', a, d],
      ['Dark text on light (phone)', d, l],
      ['Badge text on low badge', readable(low, l, d), low],
      ['Stamp on dark (TV)', s, d]
    ];
    form.querySelector('[data-contrast]').innerHTML = pairs.map(function (pr) {
      var r = contrast(pr[1], pr[2]);
      var ok = r >= 4.5;
      return '<div class="contrast-row"><span><span style="display:inline-block;width:16px;height:16px;border-radius:4px;vertical-align:middle;background:' + pr[2] +
        ';border:1px solid #888"></span> ' + pr[0] + '</span><span class="' + (ok ? '' : 'contrast-bad') + '">' + r.toFixed(2) + ':1' + (ok ? ' ✓' : ' ⚠ below 4.5') + '</span></div>';
    }).join('');
  }

  function setAll(sel, value) {
    var els = document.querySelectorAll(sel);
    for (var i = 0; i < els.length; i++) els[i].textContent = value;
  }

  form.addEventListener('input', function (e) {
    var t = e.target;
    if (t.hasAttribute('data-color-picker')) {
      var txt = form.querySelector('[data-color="' + t.getAttribute('data-color-picker') + '"]');
      txt.value = t.value.toUpperCase();
    } else if (t.hasAttribute('data-color') && isHex(t.value)) {
      var picker = form.querySelector('[data-color-picker="' + t.getAttribute('data-color') + '"]');
      if (picker) picker.value = t.value;
    }
    update();
  });
  form.addEventListener('change', update);

  function scaleFrames() {
    var frames = document.querySelectorAll('[data-tv-frame]');
    for (var i = 0; i < frames.length; i++) {
      var inner = frames[i].querySelector('[data-tv-inner]');
      inner.style.transform = 'scale(' + (frames[i].clientWidth / 1920) + ')';
    }
  }
  window.addEventListener('resize', scaleFrames);
  scaleFrames();
  update();
})();
