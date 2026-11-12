// Is a second reading the same reading? Every median of every operation and
// stage must agree within the tolerance docs/CLIENT-RENDER-COST.md states.
//   node compare.mjs <a.json> <b.json>
import { readFileSync } from "node:fs";

const [aPath, bPath] = process.argv.slice(2);
const a = JSON.parse(readFileSync(aPath, "utf8"));
const b = JSON.parse(readFileSync(bPath, "utf8"));
const relative = 0.25;
const absolute = 0.5;
const groups = ["isolated", "fullRender", "writeReactive", "writePlain", "structural"];
let compared = 0;
const outside = [];
for (const sa of a.surfaces) {
  const sb = b.surfaces.find((s) => s.surface === sa.surface);
  for (const group of groups) {
    for (const [stage, va] of Object.entries(sa[group])) {
      const vb = sb[group][stage];
      if (!va || !vb) continue;
      compared += 1;
      const delta = Math.abs(va.median - vb.median);
      const allowed = Math.max(absolute, relative * Math.max(va.median, vb.median));
      if (delta > allowed) outside.push(`${sa.surface} ${group}.${stage}: ${va.median} vs ${vb.median}`);
    }
  }
}
if (compared === 0) throw new Error("nothing compared");
console.log(`${compared} medians compared, ${outside.length} outside tolerance`);
for (const line of outside) console.log(`  ${line}`);
process.exitCode = outside.length ? 1 : 0;
