/* ============================================================================
   Fuaran UI — reference expandable-image enhancement
   (canonical, packaged with Fuaran.UI.Renderer beside fuaran-reference.css)

   Licence: Apache 2.0 — Copyright (c) Diametrical Ltd.
   Forked / extended versions retain this header and remain Apache 2.0, matching
   the reference stylesheet's portability promise: a consumer retiring the typed
   Fuaran.UI.Renderer library can keep using the class vocabulary — and this
   enhancement over it — indefinitely.

   ── What this does ─────────────────────────────────────────────────────────
   An image declaring `expandable` renders, on every conformant host, as a real
   anchor around the picture:

     <a class="fuaran-image-expand" href="/harbour.jpg" data-fuaran-expandable>
       <img class="fuaran-image" src="/harbour.jpg" alt="…">
     </a>

   That anchor already works. Click it with no JavaScript and the browser opens
   the full-size asset in its own viewer. This file upgrades it IN PLACE into an
   in-page overlay: the picture opens over the document, the page keeps its
   scroll position, and Escape closes it.

   ── The no-JS posture, stated first because it is the design ───────────────
   The affordance is NOT created here. It is emitted by the renderer as an
   ordinary link, and everything below is a refinement of an interaction that
   already works. That is the opposite of the usual lightbox, which marks up a
   dead `<div>` or an `href="#"` and depends on script to mean anything — and
   which therefore gives a crawler, a text browser, a locked-down client or a
   failed hydration nothing at all. Nothing here is required for the reader to
   reach the asset; if this file never loads, the page is complete.

   ── The overlay's accessibility contract ───────────────────────────────────
   The overlay is a dialog, and it meets the same contract the declarative
   `Modal` node does — the contract is the node's, not this file's, so an
   enhancement that met a weaker one would be a second, worse dialog on the
   same page:

     - `role="dialog"` + `aria-modal="true"` on the overlay.
     - `aria-label` taken from the image's own `alt`, so the dialog announces
       the picture rather than announcing "dialog".
     - FOCUS TRAP. Tab and Shift+Tab cycle within the overlay. There are
       exactly two focusable elements (the close button and the picture's own
       link back to the asset), so the trap is a wrap, not a search.
     - ESCAPE dismisses, from anywhere inside.
     - A click on the backdrop — outside the picture — dismisses too.
     - FOCUS RESTORATION. On close, focus returns to the anchor that opened
       the overlay. A reader tabbing through a gallery does not lose their
       place because they looked at one picture.
     - The rest of the document is marked `aria-hidden` while the overlay is
       open, so a screen reader in browse mode cannot wander out of it.

   ── Composition with the other Image slots ─────────────────────────────────
   `caption` — when the expandable image sits inside a `<figure>`, the caption
   text is carried into the overlay. The caption is deliberately OUTSIDE the
   anchor in the emitted markup (it is prose a reader selects and quotes, not a
   second button), so it is read from the sibling `<figcaption>` here rather
   than from inside the link.

   `srcSet` — untouched, and that is the point of the pairing. The candidates
   are renditions of the THUMBNAIL, chosen by the browser for the layout box;
   the overlay shows `href`, which is the primary `src` — the full asset. The
   overlay image therefore carries no `srcset` of its own: it is displayed at
   whatever size the viewport allows, and offering the browser a candidate list
   sized for the thumbnail's box would defeat the expansion.

   ── What this file does NOT do ─────────────────────────────────────────────
   No gallery, no next/previous, no zoom, no captions carousel. Those need an
   ordering the document does not state, and inventing one here would make this
   file's behaviour depend on DOM proximity — two images that happen to be
   siblings are not a gallery.

   ── Content-Security-Policy ────────────────────────────────────────────────
   Serve this as a FILE and reference it with `<script src="...">`. A file from
   the host's own origin is covered by `script-src 'self'` — no policy change,
   no hash to maintain, and the browser can cache it:

       <script src="/fuaran-image-expand.js" defer></script>

   If a host must inline the bytes instead, `script-src` needs a `'sha256-...'`
   source expression for the EXACT script body. Derive it from the same string
   the page renders rather than transcribing it beside the script: a hardcoded
   digest is a latent outage the moment this file is edited. `'unsafe-inline'`
   is never required, and naming it would weaken the whole policy for one
   enhancement.

   ── Compatibility ──────────────────────────────────────────────────────────
   ES5, dependency-free, no build step, no globals: one IIFE, no exports. Safe
   to load with `defer`, at the end of `<body>`, or dynamically after load (the
   ready-state guard below covers all three). It binds ONE delegated listener
   on the document rather than one per image, so it is idempotent with respect
   to re-renders: an anchor that appears after hydration, or after a tree-op
   replaced a subtree, needs no re-scan and no marker attribute.
   ============================================================================ */

(function () {
  'use strict';

  var MARKER = 'data-fuaran-expandable';
  var OVERLAY_CLASS = 'fuaran-image-lightbox';

  /* The live overlay, or null. At most one is open at a time: a second
     expansion while one is open would need a stack, and a stack needs a story
     about what Escape means at each level. One is the honest model. */
  var open = null;

  /* Walk up from an event target to the marked anchor, if any. `closest` is
     avoided so the file stays ES5 and needs no polyfill on older engines. */
  function markedAnchor(node) {
    while (node && node !== document) {
      if (node.nodeType === 1 && node.tagName === 'A' && node.hasAttribute(MARKER)) {
        return node;
      }
      node = node.parentNode;
    }
    return null;
  }

  /* The caption text for an expandable anchor, or ''. Read from the sibling
     `<figcaption>` of the enclosing `<figure>` — the caption is outside the
     link by construction, so there is nothing to find inside it. */
  function captionOf(anchor) {
    var parent = anchor.parentNode;
    if (!parent || parent.nodeType !== 1 || parent.tagName !== 'FIGURE') {
      return '';
    }
    var cap = parent.querySelector('.fuaran-image-figure-caption');
    return cap ? String(cap.textContent || '').trim() : '';
  }

  function close() {
    if (!open) {
      return;
    }
    var state = open;
    open = null;

    if (state.overlay.parentNode) {
      state.overlay.parentNode.removeChild(state.overlay);
    }
    for (var i = 0; i < state.hidden.length; i++) {
      /* Restore, rather than remove: an element that was ALREADY aria-hidden
         before the overlay opened must stay that way. */
      if (state.hiddenPrev[i] === null) {
        state.hidden[i].removeAttribute('aria-hidden');
      } else {
        state.hidden[i].setAttribute('aria-hidden', state.hiddenPrev[i]);
      }
    }
    document.removeEventListener('keydown', state.onKey, true);

    /* Focus restoration — the reader gets their place in the document back. */
    if (state.opener && typeof state.opener.focus === 'function') {
      state.opener.focus();
    }
  }

  function focusables(overlay) {
    return Array.prototype.slice.call(overlay.querySelectorAll('button, a[href]'));
  }

  function onKeyFactory(overlay) {
    return function (ev) {
      if (ev.key === 'Escape' || ev.key === 'Esc') {
        ev.preventDefault();
        close();
        return;
      }
      if (ev.key !== 'Tab') {
        return;
      }
      /* The trap. Capture-phase, so it runs before anything the host bound. */
      var items = focusables(overlay);
      if (items.length === 0) {
        return;
      }
      var first = items[0];
      var last = items[items.length - 1];
      var active = document.activeElement;

      if (ev.shiftKey && (active === first || !overlay.contains(active))) {
        ev.preventDefault();
        last.focus();
      } else if (!ev.shiftKey && (active === last || !overlay.contains(active))) {
        ev.preventDefault();
        first.focus();
      }
    };
  }

  function expand(anchor) {
    var img = anchor.querySelector('img');
    var href = anchor.getAttribute('href');
    if (!img || !href) {
      /* Nothing to show. Returning false lets the caller fall through to the
         browser's own navigation rather than swallowing the activation — a
         suppressed click that then does nothing is the dead control this whole
         design exists to avoid. */
      return false;
    }

    close();

    var alt = img.getAttribute('alt') || '';
    var caption = captionOf(anchor);

    var overlay = document.createElement('div');
    overlay.className = OVERLAY_CLASS;
    overlay.setAttribute('role', 'dialog');
    overlay.setAttribute('aria-modal', 'true');
    overlay.setAttribute('aria-label', alt === '' ? 'Expanded image' : alt);

    var closeBtn = document.createElement('button');
    closeBtn.className = 'fuaran-image-lightbox-close';
    closeBtn.setAttribute('type', 'button');
    closeBtn.setAttribute('aria-label', 'Close');
    closeBtn.appendChild(document.createTextNode('×'));

    /* The picture, wrapped in its own link to the asset. That link is the
       second trap stop AND the escape hatch: a reader who wants the file
       itself, or a new tab, still has an ordinary anchor to reach for. */
    var link = document.createElement('a');
    link.setAttribute('href', href);
    var full = document.createElement('img');
    full.className = 'fuaran-image-lightbox-image';
    full.setAttribute('src', href);
    full.setAttribute('alt', alt);
    link.appendChild(full);

    overlay.appendChild(closeBtn);
    overlay.appendChild(link);

    if (caption !== '') {
      var cap = document.createElement('p');
      cap.className = 'fuaran-image-lightbox-caption';
      cap.appendChild(document.createTextNode(caption));
      overlay.appendChild(cap);
    }

    /* Hide the rest of the document from assistive technology. Only the
       overlay's own siblings need marking — everything below them is hidden
       with its ancestor. */
    var hidden = [];
    var hiddenPrev = [];
    var kids = document.body.childNodes;
    for (var i = 0; i < kids.length; i++) {
      if (kids[i].nodeType === 1) {
        hidden.push(kids[i]);
        hiddenPrev.push(kids[i].getAttribute('aria-hidden'));
        kids[i].setAttribute('aria-hidden', 'true');
      }
    }

    document.body.appendChild(overlay);

    var onKey = onKeyFactory(overlay);
    open = {
      overlay: overlay,
      opener: anchor,
      hidden: hidden,
      hiddenPrev: hiddenPrev,
      onKey: onKey
    };

    document.addEventListener('keydown', onKey, true);
    closeBtn.addEventListener('click', function (ev) {
      ev.preventDefault();
      close();
    });
    /* Backdrop dismissal: a click that landed on the overlay itself, not on
       the picture or a control inside it. */
    overlay.addEventListener('click', function (ev) {
      if (ev.target === overlay) {
        close();
      }
    });
    /* Inside the overlay the picture's own link would NAVIGATE, which is
       correct for a plain click but wrong for the trap's Tab stop — so it
       navigates only on a deliberate modified click or Enter, exactly as any
       other anchor does. Nothing is suppressed here. */

    closeBtn.focus();
    return true;
  }

  /* ONE delegated listener, bound once. This is what makes the file safe over
     a re-rendering document: an anchor added later is covered without a rescan,
     and loading is not order-dependent. */
  document.addEventListener('click', function (ev) {
    /* Leave every deliberate "open elsewhere" gesture alone: a modified click
       and a middle click are the reader asking the BROWSER to handle the link,
       and the enhancement has no business overriding that. */
    if (ev.defaultPrevented || ev.button !== 0 || ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey) {
      return;
    }
    var anchor = markedAnchor(ev.target);
    if (!anchor) {
      return;
    }
    /* An anchor inside the overlay is the escape hatch, not an expansion. */
    if (open && open.overlay.contains(anchor)) {
      return;
    }
    if (expand(anchor)) {
      /* Suppress the navigation ONLY once the overlay is really up. */
      ev.preventDefault();
    }
  });
})();
