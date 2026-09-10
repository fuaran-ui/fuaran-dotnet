// ============================================================================
//  A minimal DOM stub for the shipped browser scripts (Phase 1648).
//
//  WHAT THIS IS FOR. Three JavaScript files ship in this repo's packages and
//  run in a reader's browser — `src/Fuaran.UI.Renderer/content/`'s two
//  enhancement scripts and `src/Fuaran.UI.ServerDriven/content/`'s live shim —
//  and until this harness none of them had ANY behavioural coverage. Phase 1532
//  could only add a source-SHAPE guard, which is a check on how the file is
//  written rather than on what it does; the defect that motivated this harness
//  (a server-driven `ReadFileBody` that could not reach a file input, and said
//  nothing about it) is invisible to any shape guard, because the shape was
//  fine.
//
//  WHAT THIS IS NOT. It is NOT a browser and NOT a DOM implementation. It
//  models exactly the surface these three files touch, and it THROWS on
//  anything else rather than returning `undefined` — because a stub that
//  silently answers a question it does not understand turns a real defect into
//  a passing test, which is the failure mode a hand-rolled stub exists to
//  avoid. Extending it for a new assertion is the intended cost.
//
//  Dependency-free by construction: Node's own `vm` and nothing else. The repo
//  has no npm install on the .NET side and must not grow one for a test.
// ============================================================================

/** A stub element. `attrs` is the attribute bag; `children` the element tree. */
class StubElement {
    constructor(tagName, attrs = {}) {
        this.tagName = tagName.toUpperCase();
        this._attrs = { ...attrs };
        this.children = [];
        this.parentNode = null;
        this.listeners = new Map();
        // Only a file input carries this, and only when a test gives it one.
        this.files = undefined;
        this.style = {};
    }

    get type() {
        return this._attrs["type"];
    }

    getAttribute(name) {
        return Object.prototype.hasOwnProperty.call(this._attrs, name) ? this._attrs[name] : null;
    }

    setAttribute(name, value) {
        this._attrs[name] = String(value);
    }

    removeAttribute(name) {
        delete this._attrs[name];
    }

    hasAttribute(name) {
        return Object.prototype.hasOwnProperty.call(this._attrs, name);
    }

    appendChild(child) {
        child.parentNode = this;
        this.children.push(child);
        return child;
    }

    addEventListener(type, handler) {
        if (!this.listeners.has(type)) {
            this.listeners.set(type, []);
        }
        this.listeners.get(type).push(handler);
    }

    /** Every element at or below this one, document order. */
    descendants() {
        const out = [];
        const walk = (el) => {
            out.push(el);
            for (const c of el.children) {
                walk(c);
            }
        };
        walk(this);
        return out;
    }

    /** The `class` attribute's tokens. */
    classes() {
        const raw = this.getAttribute("class");
        return raw ? raw.split(/\s+/).filter(Boolean) : [];
    }

    matches(selector) {
        return compileSelector(selector)(this);
    }

    /** Nearest self-or-ancestor matching `selector`, or null — the DOM's rule. */
    closest(selector) {
        const test = compileSelector(selector);
        let node = this;
        while (node) {
            if (test(node)) {
                return node;
            }
            node = node.parentNode;
        }
        return null;
    }

    /**
     * A DELIBERATELY TINY selector engine: a comma-separated list of simple
     * selectors, each a tag name and/or any number of `.class` and
     * `[attr]` / `[attr="value"]` qualifiers. No combinators, no
     * pseudo-classes. Anything else throws by name — see the header.
     */
    querySelector(selector) {
        const matches = this.querySelectorAll(selector);
        return matches.length > 0 ? matches[0] : null;
    }

    querySelectorAll(selector) {
        const test = compileSelector(selector);
        // `this` is excluded, matching the DOM: a query is over descendants.
        return this.descendants()
            .slice(1)
            .filter(test);
    }
}

/**
 * Compile a selector in the modelled subset, or throw naming what was outside
 * it. The subset: a comma-separated list, each member an optional tag name
 * followed by any number of `.class` and `[attr]` / `[attr="value"]`
 * qualifiers. No combinators (descendant, child, sibling) and no
 * pseudo-classes — the three subject files use none, and a stub that quietly
 * mis-answered one would turn a defect into a pass.
 */
function compileSelector(selector) {
    const members = String(selector)
        .split(",")
        .map((m) => m.trim())
        .filter(Boolean);

    if (members.length === 0) {
        throw new Error(`dom-stub: empty selector '${selector}'.`);
    }

    const tests = members.map((member) => compileSimple(member, selector));
    return (el) => tests.some((t) => t(el));
}

const SIMPLE_TOKEN = /^(?:([a-zA-Z][\w-]*)|\.([\w-]+)|\[([\w-]+)(?:=(?:"([^"]*)"|'([^']*)'|([^\]]*)))?\])/;

function compileSimple(member, whole) {
    let rest = member;
    let tag = null;
    const classes = [];
    const attrs = [];
    let first = true;

    while (rest.length > 0) {
        const m = SIMPLE_TOKEN.exec(rest);
        if (!m) {
            throw new Error(
                `dom-stub: selector '${whole}' is outside the modelled subset — '${rest}' is not a tag name, a .class or an [attr] qualifier. ` +
                    `Extend compileSelector deliberately rather than letting the stub answer a question it does not understand.`
            );
        }
        if (m[1]) {
            if (!first) {
                throw new Error(`dom-stub: selector '${whole}' has a tag name after a qualifier, which the subset does not model.`);
            }
            tag = m[1].toUpperCase();
        } else if (m[2]) {
            classes.push(m[2]);
        } else {
            const value = m[4] !== undefined ? m[4] : m[5] !== undefined ? m[5] : m[6];
            attrs.push([m[3], value]);
        }
        rest = rest.slice(m[0].length);
        first = false;
    }

    return (el) => {
        if (tag && el.tagName !== tag) {
            return false;
        }
        for (const c of classes) {
            if (!el.classes().includes(c)) {
                return false;
            }
        }
        for (const [name, value] of attrs) {
            if (!el.hasAttribute(name)) {
                return false;
            }
            if (value !== undefined && el.getAttribute(name) !== value) {
                return false;
            }
        }
        return true;
    };
}

/** A stub document rooted at `<html>`, with a `<body>` under it. */
class StubDocument {
    constructor() {
        this.documentElement = new StubElement("html");
        this.body = new StubElement("body");
        this.documentElement.appendChild(this.body);
        this.listeners = new Map();
        this.readyState = "complete";
        // The shim's auto-start reads this; a harness never wants auto-start.
        this.currentScript = null;
    }

    createElement(tagName) {
        return new StubElement(tagName);
    }

    addEventListener(type, handler) {
        if (!this.listeners.has(type)) {
            this.listeners.set(type, []);
        }
        this.listeners.get(type).push(handler);
    }

    /** How many document-level listeners are bound for `type`. */
    listenerCount(type) {
        return (this.listeners.get(type) || []).length;
    }

    querySelector(selector) {
        return this.documentElement.querySelector(selector);
    }

    querySelectorAll(selector) {
        return this.documentElement.querySelectorAll(selector);
    }
}

/**
 * A stub `FileReader`. Synchronous on purpose: the real one is async and a test
 * that awaited it would be testing the harness's scheduling. What is under test
 * is WHICH element the shim reads from and what it sends, neither of which the
 * timing changes.
 */
class StubFileReader {
    constructor() {
        this.result = null;
        this.onload = null;
        this.onerror = null;
    }

    readAsText(file) {
        this.result = file.text;
        if (this.onload) {
            this.onload();
        }
    }

    readAsDataURL(file) {
        this.result = `data:${file.type || "application/octet-stream"};base64,${Buffer.from(
            file.text ?? "",
            "utf8"
        ).toString("base64")}`;
        if (this.onload) {
            this.onload();
        }
    }
}

/** A stub `File` — the members the shim reads, and nothing more. */
function stubFile(name, text, type = "text/plain") {
    return { name, text, type, size: Buffer.byteLength(text ?? "", "utf8") };
}

export { StubElement, StubDocument, StubFileReader, stubFile, compileSelector };
