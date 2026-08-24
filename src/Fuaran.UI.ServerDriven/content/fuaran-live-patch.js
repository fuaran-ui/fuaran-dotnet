/*
 * fuaran-live-patch.js — the generic server-driven shim (Phase 152, Track B).
 *
 * The browser half of the third client tier (HTMX / LiveView / Blazor-Server-
 * shaped). It is **app-agnostic and Fuaran-agnostic**: no generated code, no
 * per-app logic, no framework — just (1) event delegation on
 * `[data-fuaran-node-id]` elements, (2) a transport that ships
 * `(nodeId, event, payload)` to the server and receives frames back, (3) a
 * `DomPatch` applier (the ~8 primitives, addressing by `data-fuaran-node-id`),
 * and (4) a `ClientEffect` performer (clipboard / navigate / focus / download /
 * file-read). Target: a few KB minified.
 *
 * Wire contract (matches Fuaran.UI.ServerDriven F# encoders exactly):
 *   server → client frame: { "patches": DomPatch[], "effects": ClientEffect[] }
 *   client → server event:  { "nodeId": string, "event": string, "payload": any, "lastSeq": number }
 *   DomPatch.kind ∈ SetAttr | RemoveAttr | SetText | ReplaceFragment |
 *                   InsertFragment | RemoveNode | ReorderChildren | MoveNode
 *   ClientEffect.kind ∈ WriteToClipboard | Navigate | PushState | Focus | Download | ReadFileBody
 *
 * Transport is isolated behind a tiny adapter — `connect(onFrame, onState)` +
 * `send(event)`, the client mirror of `IFuaranLiveChannel`. The default adapter
 * is SSE-push + POST-receive; a WebSocket adapter is a drop-in (same patch core).
 *
 * UX + latency-masking quick wins (Phase 158), opt-in via attributes + reserved
 * hooks the reference CSS styles:
 *   QW1 in-flight       — the interacting node gets `data-fuaran-pending` until
 *                         its frame lands.
 *   QW2 connection      — `<html data-fuaran-disconnected>` while the stream is
 *                         down (reconnecting banner).
 *   QW3 debounce        — `data-fuaran-debounce="<ms>"` on an input batches its
 *                         input/change before sending.
 *   QW4 optimistic      — `data-fuaran-optimistic` echoes `data-fuaran-optimistic-
 *                         active` immediately on click; the server patch reconciles.
 *   QW5 focus/caret     — focus + caret + scroll survive a `ReplaceFragment`.
 */
(function (global) {
  "use strict";

  var ATTR = "data-fuaran-node-id";
  var FIELD = "data-fuaran-field"; // per-field buffer marker (form policy)
  var COMMIT = "data-fuaran-commit"; // explicit per-field flush trigger (Apply)

  // ── DOM addressing ──────────────────────────────────────────────────────
  function byId(nodeId) {
    // Real Fuaran nodes address by data-fuaran-node-id; form fields are not
    // nodes but are addressable sub-parts (the Phase 156 field-error patch +
    // the buffer harvest target them by data-fuaran-field) — fall back to it.
    return (
      document.querySelector("[" + ATTR + '="' + cssEscape(nodeId) + '"]') ||
      document.querySelector("[" + FIELD + '="' + cssEscape(nodeId) + '"]')
    );
  }
  function byField(fieldId) {
    return document.querySelector("[" + FIELD + '="' + cssEscape(fieldId) + '"]');
  }
  // Read one buffered field's live DOM value as a wire-shaped value (the DOM IS
  // the client-side buffer — policy (b)). Number/range → num-or-null, checkbox →
  // bool, everything else → its string value.
  function fieldValue(el) {
    if (!el) return null;
    if (el.type === "checkbox") return el.checked;
    if (el.type === "number" || el.type === "range") return el.value === "" ? null : parseFloat(el.value);
    return el.value;
  }
  // Harvest every buffered field within `container`, keyed by field id — the
  // flush payload the server resolves field commits + onSubmit against.
  function harvestFields(container) {
    var out = {};
    var fs = container.querySelectorAll("[" + FIELD + "]");
    for (var i = 0; i < fs.length; i++) out[fs[i].getAttribute(FIELD)] = fieldValue(fs[i]);
    return out;
  }
  function cssEscape(s) {
    return (global.CSS && global.CSS.escape) ? global.CSS.escape(s) : String(s).replace(/["\\]/g, "\\$&");
  }
  // The fuaran-node children of `el` (its descendant elements carrying the
  // marker whose nearest marked ancestor is `el`).
  function fuaranChildren(el) {
    var all = el.querySelectorAll("[" + ATTR + "]");
    var out = [];
    for (var i = 0; i < all.length; i++) {
      if (closestMarked(all[i].parentElement) === el) out.push(all[i]);
    }
    return out;
  }
  function closestMarked(el) {
    while (el && !el.hasAttribute(ATTR)) el = el.parentElement;
    return el;
  }
  function parseFragment(html) {
    var t = document.createElement("template");
    t.innerHTML = html.trim();
    return t.content.firstElementChild;
  }

  // ── UX quick-win hooks (Phase 158) ───────────────────────────────────────
  var PENDING = "data-fuaran-pending";
  var OPTIMISTIC = "data-fuaran-optimistic";
  var OPTIMISTIC_ACTIVE = "data-fuaran-optimistic-active";

  function markPending(el) { if (el) el.setAttribute(PENDING, ""); }   // QW1
  function clearTransient() {                                          // QW1 + QW4
    var p = document.querySelectorAll("[" + PENDING + "],[" + OPTIMISTIC_ACTIVE + "]");
    for (var i = 0; i < p.length; i++) {
      p[i].removeAttribute(PENDING);
      p[i].removeAttribute(OPTIMISTIC_ACTIVE);
    }
  }
  function setConnectionState(connected) {                            // QW2
    var root = document.documentElement;
    if (connected) root.removeAttribute("data-fuaran-disconnected");
    else root.setAttribute("data-fuaran-disconnected", "");
  }

  // QW5 — capture/restore focus + caret + scroll around a fragment swap so a
  // ReplaceFragment that contains the focused field doesn't drop the user's place.
  function captureFocus(container) {
    var a = document.activeElement;
    if (!a || !(container === a || container.contains(a))) return null;
    var marked = closestMarked(a);
    var saved = { id: marked && marked.getAttribute(ATTR), markedIsActive: marked === a };
    try { saved.scrollTop = a.scrollTop; saved.scrollLeft = a.scrollLeft; } catch (_) {}
    try { if (a.selectionStart != null) { saved.selStart = a.selectionStart; saved.selEnd = a.selectionEnd; } } catch (_) {}
    return saved;
  }
  function restoreFocus(saved) {
    if (!saved || !saved.id) return;
    var el = byId(saved.id);
    if (!el) return;
    var target = saved.markedIsActive ? el : (el.querySelector("input,textarea,select,button,[tabindex]") || el);
    try { target.focus(); } catch (_) {}
    if (saved.selStart != null && target.setSelectionRange) {
      try { target.setSelectionRange(saved.selStart, saved.selEnd); } catch (_) {}
    }
    if (saved.scrollTop != null) { try { target.scrollTop = saved.scrollTop; target.scrollLeft = saved.scrollLeft; } catch (_) {} }
  }

  // ── DomPatch applier ────────────────────────────────────────────────────
  function applyPatch(p) {
    switch (p.kind) {
      case "SetAttr": { var e = byId(p.nodeId); if (e) e.setAttribute(p.name, p.value); break; }
      case "RemoveAttr": { var e = byId(p.nodeId); if (e) e.removeAttribute(p.name); break; }
      case "SetText": { var e = byId(p.nodeId); if (e) e.textContent = p.text; break; }
      case "ReplaceFragment": {
        var e = byId(p.nodeId);
        var frag = parseFragment(p.html);
        if (e && frag) {
          var saved = captureFocus(e); // QW5 — preserve focus/caret/scroll
          e.replaceWith(frag);
          restoreFocus(saved);
        }
        break;
      }
      case "InsertFragment": {
        var parent = byId(p.parentId);
        var frag = parseFragment(p.html);
        if (!parent || !frag) break;
        var kids = fuaranChildren(parent);
        if (p.position >= kids.length || kids.length === 0) {
          (kids.length ? kids[kids.length - 1].parentElement : parent).appendChild(frag);
        } else {
          kids[p.position].parentElement.insertBefore(frag, kids[p.position]);
        }
        break;
      }
      case "RemoveNode": { var e = byId(p.nodeId); if (e) e.remove(); break; }
      case "MoveNode": {
        // Relocate the LIVE element (identity-preserving — focus / scroll / state survive).
        var e = byId(p.nodeId);
        var parent = byId(p.newParentId);
        if (!e || !parent) break;
        var kids = fuaranChildren(parent).filter(function (k) { return k !== e; });
        if (p.position >= kids.length || kids.length === 0) parent.appendChild(e);
        else kids[p.position].parentElement.insertBefore(e, kids[p.position]);
        break;
      }
      case "ReorderChildren": {
        var parent = byId(p.parentId);
        if (!parent) break;
        // Append each id in target order — appendChild moves the existing node,
        // so order is fixed without re-render (identity preserved).
        for (var i = 0; i < p.orderedIds.length; i++) {
          var child = byId(p.orderedIds[i]);
          if (child) child.parentElement.appendChild(child);
        }
        break;
      }
    }
  }
  function applyPatches(patches) {
    if (patches) for (var i = 0; i < patches.length; i++) applyPatch(patches[i]);
  }

  // ── ClientEffect performer ──────────────────────────────────────────────
  function performEffect(fx, send) {
    switch (fx.kind) {
      case "WriteToClipboard":
        if (global.navigator && navigator.clipboard) navigator.clipboard.writeText(fx.text);
        break;
      case "Navigate":
        global.location.href = fx.route;
        break;
      case "PushState":
        // In-place navigation (Phase 157): update the URL bar + history WITHOUT
        // a reload; the tree swap rode the accompanying DomPatches.
        if (global.history && history.pushState) history.pushState({ fuaranRoute: fx.route }, "", fx.route);
        break;
      case "Focus": { var e = byId(fx.nodeId); if (e) e.focus(); break; }
      case "Download": {
        var a = document.createElement("a");
        a.href = fx.url; a.download = fx.name || "";
        document.body.appendChild(a); a.click(); a.remove();
        break;
      }
      case "ReadFileBody": {
        var input = byId(fx.nodeId);
        var file = input && input.files && input.files[0];
        if (!file) break;
        var reader = new FileReader();
        reader.onload = function () {
          // Round-trip the body back as a LiveEvent the server's ReadFileBody
          // continuation consumes.
          send({ nodeId: fx.nodeId, event: "file-read", payload: { encoding: fx.encoding, body: reader.result } });
        };
        if (fx.encoding === "Text") reader.readAsText(file);
        else reader.readAsDataURL(file); // Base64 / DataUrl both via data URL
        break;
      }
    }
  }
  function performEffects(effects, send) {
    if (effects) for (var i = 0; i < effects.length; i++) performEffect(effects[i], send);
  }

  // ── Event delegation ────────────────────────────────────────────────────
  // Forward DOM events on marked elements to the server as LiveEvents. The
  // server resolves the node's Action/onChange/onSubmit by (nodeId, event).
  function payloadFor(el, target, type) {
    if (type === "submit") {
      // Form flush (policy b): the buffered field values never round-tripped per
      // keystroke — harvest them all now, keyed by field id, as the submit
      // payload the server resolves field commits + onSubmit against.
      return harvestFields(el);
    }
    if (type === "change" || type === "input") {
      var payload;
      if (target.type === "checkbox") payload = { checked: target.checked };
      else payload = { value: target.value };
      // Filter-control changes: the server renderer marks each filter control
      // (or its segmented fieldset) with data-filter-name; the server-side
      // Filters resolution is name-addressed (one Filters node, many filters)
      // — bridge the changed filter's name across as `payload.name`.
      var named = target.closest && target.closest("[data-filter-name]");
      if (named) payload.name = named.getAttribute("data-filter-name");
      return payload;
    }
    if (type === "click" && target.closest) {
      // Explicit per-field flush ("Apply" — the OnCommitAction analogue): the
      // button carries data-fuaran-commit="<fieldId>"; harvest just that
      // buffered field so the server's Action.CommitLocal can commit it.
      var commit = target.closest("[" + COMMIT + "]");
      if (commit) {
        var fid = commit.getAttribute(COMMIT);
        var cp = {};
        cp[fid] = fieldValue(byField(fid));
        return cp;
      }
      // Tab-header clicks: the server renderer marks each tab button with
      // data-tab-index, and the server-side Tabs action resolution needs
      // `payload.index` — bridge the clicked tab's index across.
      var tab = target.closest("[data-tab-index]");
      if (tab) return { index: parseInt(tab.getAttribute("data-tab-index"), 10) };
      // Step-header clicks: same bridge for the stepper's data-step-index —
      // the server-side Stepper resolution reads `payload.index`.
      var step = target.closest("[data-step-index]");
      if (step) return { index: parseInt(step.getAttribute("data-step-index"), 10) };
      // Segmented-filter option clicks (the horizontal radiogroup's buttons):
      // bridge the option's value + the owning filter's name — the server-side
      // Filters resolution reads `payload.name` + `payload.value`.
      var filterOpt = target.closest("[data-filter-value]");
      if (filterOpt) {
        var group = filterOpt.closest("[data-filter-name]");
        return {
          value: filterOpt.getAttribute("data-filter-value"),
          name: group ? group.getAttribute("data-filter-name") : null
        };
      }
      // Disclosure summary clicks: <details> flips `open` AFTER dispatch (the
      // toggle is the click's default action), so `!open` here is the state
      // the user is switching to. Server-side Disclosure resolution reads
      // `payload.open` and defaults a bare click to "open" — without this a
      // click on an already-open disclosure would re-send "open".
      var summary = target.closest("summary");
      var details = summary && summary.closest("details");
      if (details) return { open: !details.open };
    }
    return {};
  }
  function wireDelegation(send, getSeq) {
    var debounceTimers = {};

    function dispatch(el, type, target) {
      markPending(el); // QW1
      if (type === "click" && el.hasAttribute(OPTIMISTIC)) el.setAttribute(OPTIMISTIC_ACTIVE, ""); // QW4
      var ack = send({
        nodeId: el.getAttribute(ATTR),
        event: type,
        payload: payloadFor(el, target, type),
        lastSeq: getSeq()
      });
      // Clear THIS element's pending mark when the server acknowledges the
      // event (or the POST fails). Without this, an event that produces no
      // patch frame — a rejected noise click, a no-op fold — left the mark
      // (and its `pointer-events: none` styling) in place FOREVER: one click
      // into a form field greyed the form and made its own submit button
      // unclickable, cascading the same dead grey up the ancestor chain as
      // each blocked click landed one node higher. Pending now means
      // "in flight": milliseconds for a no-op, the full turn for a real one
      // (where the anti-double-dispatch lock is exactly what QW1 wanted).
      // A frame that re-renders the node meanwhile already cleared it
      // (`clearTransient`); removing from a detached old node is harmless.
      if (ack && ack.then) {
        var clear = function () {
          el.removeAttribute(PENDING);
          el.removeAttribute(OPTIMISTIC_ACTIVE);
        };
        ack.then(clear, clear);
      }
    }

    ["click", "change", "input", "submit"].forEach(function (type) {
      document.addEventListener(type, function (ev) {
        var el = closestMarked(ev.target);
        if (!el) return;
        if (type === "submit") ev.preventDefault();
        // Policy (b): a buffered form field's change/input is NOT sent — its
        // live DOM value is the client-side buffer; the server sees it only on
        // the flush (submit / Apply). (Filter inputs round-trip; they carry
        // data-filter-name, not data-fuaran-field, so they're unaffected.)
        if ((type === "change" || type === "input") && ev.target.closest && ev.target.closest("[" + FIELD + "]"))
          return;
        // QW3 — declarative debounce on input/change.
        var ms = (type === "input" || type === "change") ? parseInt(el.getAttribute("data-fuaran-debounce"), 10) : NaN;
        if (ms > 0) {
          var key = el.getAttribute(ATTR) + ":" + type;
          var target = ev.target;
          clearTimeout(debounceTimers[key]);
          debounceTimers[key] = setTimeout(function () { dispatch(el, type, target); }, ms);
        } else {
          dispatch(el, type, ev.target);
        }
      });
    });
    // Back/forward (Phase 157): forward a `popstate` LiveEvent carrying the route
    // so the server swaps the tree to match. Not node-addressed — the server
    // routing layer handles it outside the per-node G1 gate.
    global.addEventListener("popstate", function () {
      send({
        nodeId: "",
        event: "popstate",
        payload: { route: global.location.pathname + global.location.search },
        lastSeq: getSeq()
      });
    });
  }

  // ── Transport adapter (SSE-push + POST-receive; the default) ─────────────
  // The client mirror of IFuaranLiveChannel: connect(onFrame) + send(event).
  // A WebSocket adapter is a drop-in — only this object changes; the patch /
  // effect / delegation core above is transport-identical.
  function sseAdapter(config) {
    var lastSeq = 0;
    return {
      connect: function (onFrame, onState) {
        var es = new EventSource(config.streamUrl);
        es.onopen = function () { if (onState) onState(true); }; // QW2
        es.addEventListener("patch", function (e) {
          var frame = JSON.parse(e.data);
          if (frame.seq != null) lastSeq = frame.seq; // EventSource also tracks Last-Event-ID
          onFrame(frame);
        });
        // EventSource auto-reconnects + replays Last-Event-ID; surface the gap.
        es.onerror = function () { if (onState) onState(false); }; // QW2
        return es;
      },
      send: function (event) {
        // Returns the POST's promise so `dispatch` can clear the pending mark
        // on acknowledgement — by the 204 the server has fully processed the
        // event (frames were already pushed on the stream). An adapter that
        // cannot report acknowledgement may return nothing; pending then
        // clears only on the next inbound frame (the pre-ack behaviour).
        return fetch(config.sendUrl, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(event),
          credentials: "same-origin"
        });
      },
      getSeq: function () { return lastSeq; }
    };
  }

  // ── Boot ────────────────────────────────────────────────────────────────
  // start() is restartable: hosts that swap the live tree per page state call
  // it repeatedly, so close the previous stream, point at the new transport,
  // and wire the document-level delegation exactly once — forwarding events
  // to the CURRENT adapter (re-wiring would send each event N times).
  var current = null; // { adapter, stream }
  var delegationWired = false;

  function start(config, adapterFactory) {
    var adapter = (adapterFactory || sseAdapter)(config);
    var send = function (event) { if (current) return current.adapter.send(event); };
    if (current && current.stream && current.stream.close) {
      try { current.stream.close(); } catch (_) {}
    }
    var stream = adapter.connect(function (frame) {
      applyPatches(frame.patches);
      performEffects(frame.effects, send);
      clearTransient(); // QW1/QW4 — the frame resolved the in-flight interaction
    }, setConnectionState);
    current = { adapter: adapter, stream: stream };
    if (!delegationWired) {
      wireDelegation(send, function () { return current ? current.adapter.getSeq() : 0; });
      delegationWired = true;
    }
    return adapter;
  }

  // Public surface — exposed for hosts that wire transport explicitly, and for
  // a WebSocket (or test) adapter to reuse the patch/effect core.
  global.FuaranLive = {
    start: start,
    sseAdapter: sseAdapter,
    applyPatches: applyPatches,
    applyPatch: applyPatch,
    performEffects: performEffects
  };

  // Auto-start when the host supplies config via a <script data-fuaran-live-*>
  // tag: <script src="fuaran-live-patch.js"
  //              data-fuaran-live-stream="/live/stream"
  //              data-fuaran-live-send="/live/event"></script>
  if (document.currentScript) {
    var s = document.currentScript;
    var stream = s.getAttribute("data-fuaran-live-stream");
    var sendUrl = s.getAttribute("data-fuaran-live-send");
    if (stream && sendUrl) {
      var go = function () { start({ streamUrl: stream, sendUrl: sendUrl }); };
      if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", go);
      else go();
    }
  }
})(typeof window !== "undefined" ? window : this);
