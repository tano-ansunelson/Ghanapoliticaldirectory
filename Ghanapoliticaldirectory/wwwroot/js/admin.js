/* Administrative dashboard behaviour. The server re-validates everything:
   nothing here is a security control, only a convenience for the person typing. */
(function () {
  'use strict';

  function debounce(fn, ms) {
    var t;
    return function () {
      var args = arguments, self = this;
      clearTimeout(t);
      t = setTimeout(function () { fn.apply(self, args); }, ms);
    };
  }
  function text(tag, cls, str) {
    var el = document.createElement(tag);
    if (cls) el.className = cls;
    if (str != null) el.textContent = str;
    return el;
  }

  /* ---------------------------------------------------------------- Sidebar (mobile) */
  var sidebar = document.getElementById('adminSidebar');
  var toggle = document.querySelector('[data-sidebar-toggle]');
  if (sidebar && toggle) {
    toggle.addEventListener('click', function () {
      var open = sidebar.classList.toggle('is-open');
      toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && sidebar.classList.contains('is-open')) {
        sidebar.classList.remove('is-open');
        toggle.setAttribute('aria-expanded', 'false');
        toggle.focus();
      }
    });
  }

  /* ---------------------------------------------------------------- Confirmation dialog */
  var modalEl = document.getElementById('confirmModal');
  if (modalEl && window.bootstrap) {
    var modal = window.bootstrap.Modal.getOrCreateInstance(modalEl);
    var body = document.getElementById('confirmBody');
    var ok = document.getElementById('confirmOk');
    var pending = null;

    document.addEventListener('click', function (e) {
      var btn = e.target.closest('[data-confirm]');
      if (!btn || btn.dataset.confirmed === '1') return;
      e.preventDefault();
      pending = btn;
      body.textContent = btn.getAttribute('data-confirm');
      ok.textContent = btn.getAttribute('data-confirm-label') || 'Confirm';
      modal.show();
    });

    ok.addEventListener('click', function () {
      var btn = pending;
      pending = null;
      modal.hide();
      if (!btn) return;
      if (btn.form && btn.type === 'submit') {
        btn.dataset.confirmed = '1';
        btn.form.requestSubmit(btn); // keeps the button's name/value (e.g. Command=publish)
      } else {
        var content = document.querySelector('.gpd-admin-content');
        var note = text('div', 'alert alert-success d-flex gap-2', null);
        note.setAttribute('role', 'status');
        note.textContent = 'Confirmed (prototype). In the live system the server performs this action and writes an audit log entry.';
        if (content) content.insertBefore(note, content.firstChild);
      }
    });
    modalEl.addEventListener('hidden.bs.modal', function () {
      if (pending) pending = null;
      if (ok) ok.blur();
    });
  }

  /* ---------------------------------------------------------------- Add person */
  var personForm = document.querySelector('[data-person-form]');
  if (personForm) {
    var preview = document.getElementById('save-preview');
    var existing = preview && preview.getAttribute('data-existing') === 'true';

    function updatePreview() {
      if (!preview) return;
      preview.innerHTML = '';
      preview.appendChild(text('li', null, existing ? 'Existing profile (no new person record)' : '1 new person record'));
      var party = personForm.querySelector('select[name="PartyId"]');
      if (party && party.value) preview.appendChild(text('li', null, '1 party affiliation record (with start date)'));
      var checked = personForm.querySelectorAll('[data-position-toggle]:checked');
      checked.forEach(function (c) {
        var label = c.parentElement.querySelector('label').getAttribute('data-label') || c.value;
        preview.appendChild(text('li', null, label + ': 1 separate record with its own dates'));
      });
      if (!checked.length) preview.appendChild(text('li', null, 'No position records. Profile only.'));
    }

    personForm.querySelectorAll('[data-position-toggle]').forEach(function (cb) {
      var panel = document.getElementById('panel-' + cb.getAttribute('data-position-toggle'));
      function sync() {
        if (panel) panel.hidden = !cb.checked;
        updatePreview();
      }
      cb.addEventListener('change', sync);
    });
    personForm.addEventListener('change', updatePreview);
    updatePreview();

    // End date must not be before the start date.
    personForm.querySelectorAll('[data-end]').forEach(function (end) {
      var key = end.getAttribute('data-end');
      var start = personForm.querySelector('[data-start="' + key + '"]');
      var msg = text('div', 'invalid-feedback', 'The end date cannot be before the start date.');
      end.parentElement.appendChild(msg);
      function check() {
        var bad = start && start.value && end.value && end.value < start.value;
        end.classList.toggle('is-invalid', !!bad);
        msg.classList.toggle('d-block', !!bad);
      }
      end.addEventListener('change', check);
      if (start) start.addEventListener('change', check);
    });

    // Duplicate detection: the same search runs on the "check" box and on the new-name field.
    var results = document.getElementById('dup-results');
    var inputs = personForm.querySelectorAll('[data-duplicate-check], [data-fullname]');
    var urlHolder = personForm.querySelector('[data-similar-url]');
    if (results && urlHolder && inputs.length) {
      var url = urlHolder.getAttribute('data-similar-url');
      var addUrl = window.location.pathname;
      var run = debounce(function (q) {
        q = q.trim();
        if (q.length < 3) { results.innerHTML = ''; return; }
        fetch(url + '?name=' + encodeURIComponent(q), { headers: { Accept: 'application/json' } })
          .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
          .then(function (items) {
            results.innerHTML = '';
            if (!items.length) {
              results.appendChild(text('p', 'gpd-muted small mb-0', 'No similar profiles found. Safe to create a new one.'));
              return;
            }
            results.appendChild(text('p', 'small mb-1', 'Possible matches. Check before creating a new profile:'));
            items.forEach(function (it) {
              var row = text('div', 'gpd-dup-item');
              var info = text('div');
              info.appendChild(text('strong', null, it.fullName));
              info.appendChild(text('div', 'gpd-muted small', it.summary));
              var a = text('a', 'btn btn-sm btn-gpd', 'Add a position to this profile');
              a.href = addUrl + '?existingPersonId=' + encodeURIComponent(it.id);
              row.appendChild(info);
              row.appendChild(a);
              results.appendChild(row);
            });
          })
          .catch(function () { results.innerHTML = ''; });
      }, 250);
      inputs.forEach(function (inp) {
        inp.addEventListener('input', function () { run(inp.value); });
      });
    }
  }

  /* ---------------------------------------------------------------- Enter results */
  var resultsForm = document.querySelector('[data-results-form]');
  if (resultsForm) {
    // Parliamentary-only fields
    var parl = resultsForm.querySelectorAll('[data-parl-only]');
    resultsForm.querySelectorAll('[data-contest-type]').forEach(function (r) {
      r.addEventListener('change', function () {
        var isParl = resultsForm.querySelector('#ct-parl').checked;
        parl.forEach(function (el) { el.hidden = !isParl; });
      });
    });

    var tbody = resultsForm.querySelector('[data-lines]');

    function reindex() {
      tbody.querySelectorAll('[data-line]').forEach(function (row, i) {
        row.querySelectorAll('input, select').forEach(function (el) {
          if (el.name) el.name = el.name.replace(/Lines\[\d+\]/, 'Lines[' + i + ']');
          if (el.id) el.id = el.id.replace(/-\d+$/, '-' + i);
        });
        row.querySelectorAll('label').forEach(function (l) {
          if (l.htmlFor) l.htmlFor = l.htmlFor.replace(/-\d+$/, '-' + i);
        });
      });
    }

    resultsForm.querySelector('[data-add-line]').addEventListener('click', function () {
      var rows = tbody.querySelectorAll('[data-line]');
      var clone = rows[rows.length - 1].cloneNode(true);
      clone.querySelectorAll('input').forEach(function (i) { i.value = ''; });
      clone.querySelectorAll('select').forEach(function (s) { s.selectedIndex = 0; });
      tbody.appendChild(clone);
      reindex();
      var first = clone.querySelector('input');
      if (first) first.focus();
      check();
    });

    tbody.addEventListener('click', function (e) {
      var btn = e.target.closest('[data-remove-line]');
      if (!btn) return;
      var rows = tbody.querySelectorAll('[data-line]');
      var row = btn.closest('[data-line]');
      if (rows.length > 2) { row.remove(); reindex(); }
      else { row.querySelectorAll('input').forEach(function (i) { i.value = ''; }); }
      check();
    });

    // Live arithmetic check (the server repeats it)
    var box = document.getElementById('sum-check');
    var valid = resultsForm.querySelector('[data-valid]');
    var rejected = resultsForm.querySelector('[data-rejected]');
    var cast = resultsForm.querySelector('[data-cast]');
    function num(el) { return el && el.value !== '' ? Number(el.value) : null; }
    function fmt(n) { return n.toLocaleString('en'); }

    function check() {
      if (!box) return;
      var votes = Array.prototype.map.call(tbody.querySelectorAll('[data-votes]'), function (v) { return v.value === '' ? null : Number(v.value); });
      var entered = votes.filter(function (v) { return v !== null; });
      var sum = entered.reduce(function (a, b) { return a + b; }, 0);
      var v = num(valid), r = num(rejected), c = num(cast);
      var msgs = [], bad = false, good = false;

      if (!entered.length) { box.className = 'gpd-sumcheck'; box.textContent = 'Totals are checked as you type.'; return; }
      msgs.push('Candidates total ' + fmt(sum) + ' votes.');
      if (v !== null) {
        if (entered.length === votes.length && sum !== v) { bad = true; msgs.push('This does not match the valid votes entered (' + fmt(v) + '). Difference: ' + fmt(Math.abs(sum - v)) + '.'); }
        else if (entered.length === votes.length) { good = true; msgs.push('Matches the valid votes entered.'); }
      } else {
        msgs.push('Enter the officially reported valid votes to compare.');
      }
      if (v !== null && r !== null && c !== null && v + r !== c) {
        bad = true; good = false;
        msgs.push('Valid votes + rejected ballots (' + fmt(v + r) + ') do not equal total votes cast (' + fmt(c) + ').');
      }
      box.className = 'gpd-sumcheck' + (bad ? ' is-bad' : good ? ' is-ok' : '');
      box.textContent = msgs.join(' ');
    }
    resultsForm.addEventListener('input', check);
    check();
  }
})();
