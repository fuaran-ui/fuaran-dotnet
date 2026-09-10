import { test, expect, Page } from "@playwright/test";

// Phase 62 — `Binding<'T>.Local` headless invariants.
//
// The catalog page at `?local-bindings=1` mounts four canonical shapes
// (Salary OnBlur, Email OnDebounce 250, Note OnCommitAction, preset re-sync).
// Each test drives keyboard input + observes the visible "Model panel" mirror
// to assert the model-side dispatch happened on the configured flush boundary
// — and importantly, did NOT happen mid-keystroke.

const URL = "/?local-bindings=1";

const waitForPage = async (page: Page) => {
  await page.locator("#local-bindings-page").waitFor();
};

const modelSalary = (page: Page) => page.locator("[data-testid='model-salary']");
const modelEmail = (page: Page) => page.locator("[data-testid='model-email']");
const modelNote = (page: Page) => page.locator("[data-testid='model-note']");
// Phase 1666 — the commit COUNT, which is what distinguishes a spurious
// commit from none. See the block at the foot of this file.
const modelEmailCommits = (page: Page) => page.locator("[data-testid='model-email-commits']");

// The seed the page's `init()` puts in the model, and therefore the value the
// email buffer holds at mount. Duplicated from `LocalBindings.fs` on purpose:
// the spec asserting a literal is what would catch the seed being quietly
// emptied, which is the state in which the mount test below passes vacuously.
const EMAIL_SEED = "seed@example.com";

const salaryInput = (page: Page) => page.locator("#salary-input");
const emailInput = (page: Page) => page.locator("#email-input");
const noteInput = (page: Page) => page.locator("#note-input");

// ─── 1. Salary OnBlur ─────────────────────────────────────────────────────

test("OnBlur: keystrokes update buffer only; blur commits to model", async ({ page }) => {
  await page.goto(URL);
  await waitForPage(page);

  // Initial state: model salary = 50,000 (formatted via formatThousands).
  await expect(modelSalary(page)).toHaveText("50,000");

  // Focus, clear, type the new value. Each keystroke updates the visible
  // buffer; the model panel should NOT change yet.
  await salaryInput(page).click();
  await salaryInput(page).fill("");
  await salaryInput(page).type("7");
  await expect(salaryInput(page)).toHaveValue("7");
  await expect(modelSalary(page)).toHaveText("50,000"); // model still at initial

  await salaryInput(page).type("5000");
  await expect(salaryInput(page)).toHaveValue("75000");
  await expect(modelSalary(page)).toHaveText("50,000"); // still no commit

  // Blur — the input's onBlur handler runs Parse → SetSalary.
  await salaryInput(page).blur();
  await expect(modelSalary(page)).toHaveText("75,000");
});

test("OnBlur cursor-preservation: partial decimal 'trailing dot' survives", async ({ page }) => {
  await page.goto(URL);
  await waitForPage(page);

  await salaryInput(page).click();
  await salaryInput(page).fill("");
  await salaryInput(page).type("5.");

  // Mid-edit — the buffer shows the partial value, the model has not been
  // dispatched (the OnBlur trigger hasn't fired). Crucially, the buffer
  // must STILL contain "5." — per-keystroke dispatch would have erased it.
  await expect(salaryInput(page)).toHaveValue("5.");
  await expect(modelSalary(page)).toHaveText("50,000");
});

// ─── Re-sync invariant ─────────────────────────────────────────────────────

test("Re-sync: preset-apply updates buffer when not mid-edit", async ({ page }) => {
  await page.goto(URL);
  await waitForPage(page);

  // No focus on the salary input — clicking Preset £100,000 should re-sync
  // the visible buffer to the new external value via the useEffect re-sync.
  await page.locator("[data-testid='preset-100k']").click();
  await expect(modelSalary(page)).toHaveText("100,000");
  // The re-sync invariant: the input's visible value updates to match the
  // model-side preset value.
  await expect(salaryInput(page)).toHaveValue("100,000");
});

test("Re-sync invariant: mid-edit typing position survives unrelated re-render", async ({
  page,
}) => {
  await page.goto(URL);
  await waitForPage(page);

  // Start typing into the salary input but DO NOT blur.
  await salaryInput(page).click();
  await salaryInput(page).fill("");
  await salaryInput(page).type("9999");
  await expect(salaryInput(page)).toHaveValue("9999");

  // Trigger an unrelated re-render by typing into the email input (which is
  // an entirely separate field; its state change re-renders the whole tree).
  await emailInput(page).click();
  await emailInput(page).type("a");

  // The salary buffer must still hold "9999" — the re-sync useEffect saw
  // the external salary value unchanged (the preset was not clicked) so
  // it left the in-progress buffer alone.
  await expect(salaryInput(page)).toHaveValue("9999");
});

// ─── 2. Email OnDebounce 250 ───────────────────────────────────────────────

test("OnDebounce: model commits after the configured idle delay", async ({ page }) => {
  await page.goto(URL);
  await waitForPage(page);

  await emailInput(page).click();
  // Phase 1666 — the field is seeded now, so clear before typing. Without the
  // fill the typed text appends to the seed and the assertion below would be
  // about a string neither the test nor the page intended.
  await emailInput(page).fill("");
  await emailInput(page).type("a@b.com");

  // Immediately after typing, the model has not committed yet (debounce
  // timer is still running), so it still holds the seed.
  await expect(modelEmail(page)).toHaveText(EMAIL_SEED);

  // Wait past the 250ms debounce window plus jitter.
  await page.waitForTimeout(500);

  // After the debounce, parse succeeded → SetEmail dispatched.
  await expect(modelEmail(page)).toHaveText("a@b.com");
});

// ─── 3. Note OnCommitAction (explicit Apply) ───────────────────────────────

test("OnCommitAction: explicit Apply button drains the buffer", async ({ page }) => {
  await page.goto(URL);
  await waitForPage(page);

  await noteInput(page).click();
  await noteInput(page).type("hello world");

  // Nothing committed yet — neither blur nor debounce nor commit-action has
  // fired. The buffer holds the typed value; the model is unchanged.
  await expect(noteInput(page)).toHaveValue("hello world");
  await expect(modelNote(page)).toHaveText("");

  // Click Apply — dispatches the fuaran-commit-local-note-input custom
  // event that the note input's useEffect listens for.
  await page.locator("[data-testid='apply-note']").click();
  await expect(modelNote(page)).toHaveText("hello world");
});

// ─── 4. ResetSalary — re-sync from a different preset ──────────────────────

test("Reset button re-syncs buffer to the reset external value", async ({ page }) => {
  await page.goto(URL);
  await waitForPage(page);

  // Apply preset 100k first.
  await page.locator("[data-testid='preset-100k']").click();
  await expect(salaryInput(page)).toHaveValue("100,000");

  // Now reset.
  await page.locator("[data-testid='reset-salary']").click();
  await expect(salaryInput(page)).toHaveValue("50,000");
  await expect(modelSalary(page)).toHaveText("50,000");
});

// ─── 5. Mount emits no commit (Phase 1666) ─────────────────────────────────
//
// The rule: a `Local` binding initialises its buffer from `InitialFrom` at
// mount and MUST NOT dispatch a commit for doing so. Every case above observes
// a commit that SHOULD have happened; this one observes one that should not,
// and until this phase the page could not express it.
//
// WHY THE VALUE MIRROR CANNOT SAY IT. A commit at mount dispatches the buffer,
// and the buffer at mount IS the model's own value — so the dispatch writes
// back exactly what was already there. `model-email` reads the same either way.
// Seeding it differently does not help: whatever the seed is, the buffer is
// initialised FROM it, so the spurious write is still a write of that seed.
// The two histories are distinguishable only by whether a dispatch occurred,
// which is why `EmailCommits` counts rather than mirrors.
//
// WHY THE SEED IS STILL LOAD-BEARING. With `Email = ""` the buffer at mount
// holds nothing, and an implementation may reasonably decline to dispatch an
// empty value — so a zero count would be evidence about emptiness rather than
// about the flush boundary. The seed puts a real string in the buffer, so zero
// means the boundary held.
//
// GO-RED (recorded, Phase 1666): with a mount-time `SetEmail model.Email`
// dispatched from a `React.useEffect [||]` in `LocalBindings.fs`, this test
// fails on the count assertion — `Expected "0", received "1"` — while the
// `model-email` assertion beside it still passes, which is precisely the gap
// the count closes.

test("Mount emits no commit: the seeded buffer is not dispatched on load", async ({ page }) => {
  await page.goto(URL);
  await waitForPage(page);

  // The buffer is initialised from the model, so it shows the seed...
  await expect(emailInput(page)).toHaveValue(EMAIL_SEED);
  // ...and the model still shows the seed, which on its own proves nothing.
  await expect(modelEmail(page)).toHaveText(EMAIL_SEED);
  // This is the assertion: no commit has been dispatched.
  await expect(modelEmailCommits(page)).toHaveText("0");

  // Nor does an unrelated re-render flush it. Typing into a DIFFERENT field
  // re-renders the whole tree and re-runs every `Local`'s re-sync effect —
  // the one place a mount-shaped commit would recur after load.
  await noteInput(page).click();
  await noteInput(page).type("x");
  await expect(modelEmailCommits(page)).toHaveText("0");
});

test("Mount emits no commit: and the counter is not simply stuck at zero", async ({ page }) => {
  // The go-green half. A count that never moves would pass the test above for
  // the worst possible reason, and a wired-but-dead observable is the failure
  // mode this page exists to avoid — it is the same class as the empty seed.
  await page.goto(URL);
  await waitForPage(page);

  await expect(modelEmailCommits(page)).toHaveText("0");

  await emailInput(page).click();
  await emailInput(page).fill("");
  await emailInput(page).type("changed@example.com");
  await page.waitForTimeout(500);

  await expect(modelEmail(page)).toHaveText("changed@example.com");
  await expect(modelEmailCommits(page)).toHaveText("1");
});
