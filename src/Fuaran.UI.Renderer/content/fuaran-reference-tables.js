/* ============================================================================
   Fuaran UI — reference table-sort enhancement
   (canonical, packaged with Fuaran.UI.Renderer beside fuaran-reference.css)

   Licence: Apache 2.0 — Copyright (c) Diametrical Ltd.
   Forked / extended versions retain this header and remain Apache 2.0, matching
   the reference stylesheet's portability promise: a consumer retiring the typed
   Fuaran.UI.Renderer library can keep using the class vocabulary — and this
   enhancement over it — indefinitely.

   ── What this does ─────────────────────────────────────────────────────────
   Server-rendered Fuaran tables are semantic, static HTML: a `staticRows`
   DataGrid and a markdown-node table both emit `<table class="fuaran-table">`
   with `.fuaran-table-header` / `.fuaran-table-row` / `.fuaran-table-cell`.
   This file makes those tables sortable with no data binding and no client
   grid library. Drop it into any host that serves the reference stylesheet.

   Every table on the page with a `thead` and at least two body rows gains
   sortable column headers:

     - Click a header — or focus it and press Enter or Space — to cycle
       ascending → descending → the authored order. The authored order is the
       restore state, not a fourth sort: a table's emitted row order is a
       deliberate default (a grouping, a ranking, a chronology), so the third
       activation puts it back rather than leaving a stuck sort.
     - `aria-sort` on the active header mirrors the live state
       (`ascending` / `descending`, removed on restore), so assistive
       technology reads the same thing the arrow shows.
     - Cell text parses numerically THROUGH its display annotations: currency,
       thousands separators, percent signs and `±` markers are stripped
       (`$0.341 (n=6)`, `98.6%`, `±5.4 pp`); an `a / b` fraction compares by
       ratio (`20/21` sorts above `19/21`); anything left unparseable compares
       as lower-cased text.
     - The en-dash placeholder `–` (and `—` / `-` / empty) means UNMEASURED,
       not zero, and sorts LAST in both directions. Sorting a column to find
       the worst value must never surface the rows that were never measured.
     - Ties keep their authored relative order (the sort is made stable by
       carrying the original index), so a partial ordering never scrambles the
       rest of the table.

   The enhancement is client-side only: it re-orders existing DOM rows and sets
   attributes. It writes nothing to the server, mutates no cell content, and
   leaves the server-rendered bytes byte-identical for every visitor — which is
   what keeps a host's deterministic-render gate green.

   ── No-JS posture ──────────────────────────────────────────────────────────
   This is progressive enhancement, and the page is complete before it runs.
   Without JavaScript — disabled, blocked, or still loading — the tables are
   simply static: fully rendered, fully readable, in their authored order. No
   placeholder, no empty state, no layout shift. The affordance markers
   (`data-sortable`, `tabindex`, `aria-sort`) are all set BY this file, so the
   indicator CSS that hangs off them cannot match until sorting really works —
   a table never advertises an interaction it cannot perform.

   ── Content-Security-Policy ────────────────────────────────────────────────
   Serve this as a FILE and reference it with `<script src="...">`. A file from
   the host's own origin is covered by `script-src 'self'` — no policy change,
   no hash to maintain, and the browser can cache it:

       <script src="/fuaran-reference-tables.js" defer></script>

   If a host must inline the bytes instead, `script-src` needs a
   `'sha256-...'` source expression for the EXACT script body — the bytes
   between `<script>` and `</script>`, hashed as UTF-8 and base64-encoded. Any
   later edit to this file changes that digest, so derive the hash from the
   same string the page renders rather than transcribing it beside the script:
   a hardcoded digest is a latent outage, silently stopping matching the moment
   the file is updated. `'unsafe-inline'` is never required for this file, and
   naming it would weaken the whole policy for one enhancement.

   ── Compatibility ──────────────────────────────────────────────────────────
   ES5, dependency-free, no build step, no globals: one IIFE, no exports, no
   polyfills required. Safe to load with `defer`, at the end of `<body>`, or
   dynamically after load (the ready-state guard below covers all three).
   Loading it twice is not supported and would double the click handlers.
   ============================================================================ */

(function () {
  'use strict';

  /* Parse one cell's text into a sort key.

     Returns `null` for the unmeasured placeholder (handled specially by
     `compare` and by the direction flip), a Number where the text carries a
     figure, and a lower-cased String otherwise. */
  function keyOf(text) {
    var t = String(text == null ? '' : text).trim();

    /* Unmeasured — an en-dash (or em-dash, hyphen, or blank) placeholder. NOT
       zero: it has no position on the scale, so it is pushed to the end. */
    if (t === '' || t === '–' || t === '—' || t === '-') {
      return null;
    }

    /* `a / b` compares by ratio, so 20/21 outranks 19/21 rather than sorting
       by the numerator's leading digits. A zero denominator falls through to
       the numeric path below. */
    var frac = t.match(/^(\d+(?:\.\d+)?)\s*\/\s*(\d+(?:\.\d+)?)$/);

    if (frac && parseFloat(frac[2]) > 0) {
      return parseFloat(frac[1]) / parseFloat(frac[2]);
    }

    /* Strip the display annotations a rendered figure carries, then let
       parseFloat take the leading number — which is what discards a trailing
       unit or count (`±5.4 pp`, `$0.341 (n=6)`). */
    var n = parseFloat(t.replace(/[$,%±]/g, '').trim());

    return isNaN(n) ? t.toLowerCase() : n;
  }

  /* Ascending comparison of two keys. `null` (unmeasured) is greater than
     everything, so it lands last before the direction flip is applied — and
     the flip deliberately skips it (see `sortBy`). */
  function compare(a, b) {
    if (a === null) {
      return b === null ? 0 : 1;
    }

    if (b === null) {
      return -1;
    }

    if (typeof a === 'number' && typeof b === 'number') {
      return a - b;
    }

    return String(a) < String(b) ? -1 : String(a) > String(b) ? 1 : 0;
  }

  function enhance(table) {
    var thead = table.tHead;
    var tbody = table.tBodies[0];

    /* A header row and something to order: one body row has no sort, and a
       table with no `thead` has no header to hang the affordance on. */
    if (!thead || !thead.rows.length || !tbody || tbody.rows.length < 2) {
      return;
    }

    var ths = thead.rows[0].cells;

    /* The authored order, captured once before any sort — this is the restore
       state the third activation returns to. */
    var original = Array.prototype.slice.call(tbody.rows);
    var state = { col: -1, dir: '' };

    function clearSort() {
      Array.prototype.forEach.call(ths, function (h) {
        h.removeAttribute('aria-sort');
      });
    }

    function sortBy(col, dir) {
      var keyed = original.map(function (r, i) {
        var c = r.cells[col];

        return { row: r, i: i, key: c ? keyOf(c.textContent) : null };
      });

      keyed.sort(function (x, y) {
        var d = compare(x.key, y.key);

        /* The unmeasured placeholder sorts last in BOTH directions — the flip
           below would otherwise drag it to the top on a descending pass. The
           `|| x.i - y.i` tiebreak keeps the sort stable on equal keys. */
        if (x.key === null || y.key === null) {
          return d || x.i - y.i;
        }

        return (dir === 'descending' ? -d : d) || x.i - y.i;
      });

      keyed.forEach(function (k) {
        tbody.appendChild(k.row);
      });
    }

    function restore() {
      original.forEach(function (r) {
        tbody.appendChild(r);
      });
    }

    Array.prototype.forEach.call(ths, function (th, col) {
      /* Set by the script, never by the server: the indicator CSS keys off
         these, so an unenhanced table advertises nothing. */
      th.setAttribute('data-sortable', '');
      th.setAttribute('tabindex', '0');

      function activate() {
        var dir =
          state.col === col && state.dir === 'ascending'
            ? 'descending'
            : state.col === col && state.dir === 'descending'
              ? ''
              : 'ascending';

        clearSort();

        if (dir === '') {
          state.col = -1;
          state.dir = '';
          restore();

          return;
        }

        state.col = col;
        state.dir = dir;
        th.setAttribute('aria-sort', dir);
        sortBy(col, dir);
      }

      th.addEventListener('click', activate);
      th.addEventListener('keydown', function (e) {
        /* Enter and Space are the button-like activation keys; preventDefault
           stops Space from scrolling the page. */
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          activate();
        }
      });
    });
  }

  function enhanceAll() {
    Array.prototype.forEach.call(document.querySelectorAll('.fuaran-table'), enhance);
  }

  /* Run once the document is parsed. The ready-state guard covers the case a
     plain `DOMContentLoaded` listener misses: a host that injects this file
     dynamically after load would otherwise wait for an event that has already
     fired, and every table would silently stay static. */
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', enhanceAll);
  } else {
    enhanceAll();
  }
})();
