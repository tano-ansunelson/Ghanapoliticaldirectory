/* Public + shared behaviour. Everything here is progressive enhancement:
   forms and links work without JavaScript, the server does the real filtering and validation. */
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

  /* ---------------------------------------------------------------- Type-ahead search */
  document.querySelectorAll('input[data-suggest]').forEach(function (input, n) {
    if (!input.form) return;
    var host = input.parentElement;
    var listId = 'suggest-list-' + n;
    var box = document.createElement('div');
    box.className = 'gpd-suggest';
    box.hidden = true;
    var list = document.createElement('ul');
    list.id = listId;
    list.setAttribute('role', 'listbox');
    list.setAttribute('aria-label', 'Search suggestions');
    box.appendChild(list);
    host.appendChild(box);

    input.setAttribute('role', 'combobox');
    input.setAttribute('aria-autocomplete', 'list');
    input.setAttribute('aria-expanded', 'false');
    input.setAttribute('aria-controls', listId);

    var url = input.form.getAttribute('action').replace(/\/$/, '') + '/suggest?q=';
    var active = -1;
    var seq = 0;

    function close() {
      box.hidden = true;
      input.setAttribute('aria-expanded', 'false');
      input.removeAttribute('aria-activedescendant');
      active = -1;
    }

    function setActive(i) {
      var links = list.querySelectorAll('a');
      links.forEach(function (a) { a.classList.remove('is-active'); a.setAttribute('aria-selected', 'false'); });
      active = i;
      if (i >= 0 && links[i]) {
        links[i].classList.add('is-active');
        links[i].setAttribute('aria-selected', 'true');
        input.setAttribute('aria-activedescendant', links[i].id);
      } else {
        input.removeAttribute('aria-activedescendant');
      }
    }

    function render(items) {
      list.innerHTML = '';
      if (!items.length) {
        var empty = document.createElement('li');
        empty.className = 'gpd-suggest-empty';
        empty.textContent = 'No matches. Press Enter to search everything.';
        list.appendChild(empty);
      }
      items.forEach(function (it, i) {
        var li = document.createElement('li');
        li.setAttribute('role', 'presentation');
        var a = document.createElement('a');
        a.href = it.url;
        a.id = listId + '-' + i;
        a.setAttribute('role', 'option');
        var type = document.createElement('span');
        type.className = 'gpd-suggest-type';
        type.textContent = it.type;
        var label = document.createElement('span');
        label.textContent = it.label;
        if (it.subtitle) {
          var sub = document.createElement('small');
          sub.className = 'gpd-muted';
          sub.textContent = ' · ' + it.subtitle;
          label.appendChild(sub);
        }
        a.appendChild(type);
        a.appendChild(label);
        li.appendChild(a);
        list.appendChild(li);
      });
      box.hidden = false;
      input.setAttribute('aria-expanded', 'true');
      active = -1;
    }

    var fetchNow = debounce(function () {
      var q = input.value.trim();
      if (q.length < 2) { close(); return; }
      var mine = ++seq;
      fetch(url + encodeURIComponent(q), { headers: { Accept: 'application/json' } })
        .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
        .then(function (items) { if (mine === seq) render(items); })
        .catch(function () { close(); }); // search page still works on submit
    }, 200);

    input.addEventListener('input', fetchNow);
    input.addEventListener('keydown', function (e) {
      var links = list.querySelectorAll('a');
      if (e.key === 'Escape') { close(); return; }
      if (box.hidden || !links.length) return;
      if (e.key === 'ArrowDown') { e.preventDefault(); setActive((active + 1) % links.length); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); setActive((active - 1 + links.length) % links.length); }
      else if (e.key === 'Enter' && active >= 0) { e.preventDefault(); links[active].click(); }
    });
    document.addEventListener('click', function (e) { if (!host.contains(e.target)) close(); });
  });

  /* ---------------------------------------------------------------- Region → constituency */
  document.querySelectorAll('[data-dependent-form]').forEach(function (scope) {
    var region = scope.querySelector('[data-role="region"]');
    var cons = scope.querySelector('[data-role="constituency"]');
    var url = scope.getAttribute('data-constituency-url');
    if (!region || !cons || !url) return;

    function reset(text) {
      cons.innerHTML = '';
      var o = document.createElement('option');
      o.value = '';
      o.textContent = text;
      cons.appendChild(o);
    }

    function load(selected) {
      if (!region.value) { reset('Choose a region first'); cons.disabled = true; return; }
      reset('Loading constituencies…');
      cons.disabled = true;
      fetch(url + '?regionId=' + encodeURIComponent(region.value), { headers: { Accept: 'application/json' } })
        .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
        .then(function (items) {
          reset('Choose a constituency');
          items.forEach(function (it) {
            var o = document.createElement('option');
            o.value = it.value;
            o.textContent = it.text;
            if (selected && String(selected) === String(it.value)) o.selected = true;
            cons.appendChild(o);
          });
          cons.disabled = false;
        })
        .catch(function () {
          reset('Could not load constituencies. Try again.');
          cons.disabled = true;
        });
    }

    region.addEventListener('change', function () { load(null); });
    // Admin forms render the constituency list empty; fill it when a region is already chosen.
    if (region.value && cons.options.length <= 1) load(cons.getAttribute('data-selected'));
  });

  /* ---------------------------------------------------------------- Auto-submit selects */
  document.querySelectorAll('select[data-role="autosubmit"]').forEach(function (sel) {
    sel.addEventListener('change', function () { if (sel.form) sel.form.submit(); });
  });
})();
