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
  await emailInput(page).type("a@b.com");

  // Immediately after typing, the model has not committed yet (debounce
  // timer is still running).
  await expect(modelEmail(page)).toHaveText("");

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
