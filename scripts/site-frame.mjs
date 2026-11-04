// Writes the toolbar and footer in site/partials/ into every page of site/, so each is
// edited in one place. A page's section, which the toolbar and footer mark as the
// current one, comes from the table below.
//
//   node scripts/site-frame.mjs           rewrite the pages that differ
//   node scripts/site-frame.mjs --check   name the pages that differ, and fail if any does

import { readFileSync, writeFileSync, readdirSync } from "node:fs";
import { join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const site = fileURLToPath(new URL("../site/", import.meta.url));

const sections = {
  "index.html": "overview",
  "tutorials.html": "tutorials",
  "tutorials/": "tutorials",
  "presets.html": "presets",
  "preset.html": "presets",
  "submit.html": "presets",
  "admin.html": "presets",
  "shared-plugins.html": "plugins",
  "plugin.html": "plugins",
  "submit-plugin.html": "plugins",
  "method.html": "method",
  "privacy.html": "privacy",
};

const parts = [
  { file: "toolbar.html", pattern: /<header class="toolbar">[\s\S]*?<\/header>/ },
  { file: "footer.html", pattern: /<footer class="site-foot">[\s\S]*?<\/footer>/ },
].map((part) => ({ ...part, template: readFileSync(join(site, "partials", part.file), "utf8").trimEnd() }));

function pages(folder) {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);
    if (entry.isDirectory()) return entry.name === "partials" ? [] : pages(path);
    return entry.name.endsWith(".html") ? [path] : [];
  });
}

function sectionOf(name) {
  const key = Object.keys(sections).find((k) => (k.endsWith("/") ? name.startsWith(k) : name === k));
  if (!key) throw new Error(`${name}: not in site-frame.mjs's table of sections`);
  return sections[key];
}

/** A part as the page at depth `depth` wears it: links made relative, its own section marked current. */
function fill(template, depth, section) {
  const up = "../".repeat(depth);
  return template
    .replaceAll("{home}", up || "./")
    .replaceAll("{up}", up)
    .replace(/ data-section="([a-z]+)"/g, (_, s) => (s === section ? ' aria-current="page"' : ""));
}

const check = process.argv.includes("--check");
const differ = [];

for (const path of pages(site).sort()) {
  const html = readFileSync(path, "utf8");
  if (!html.includes("<body")) continue;

  const name = relative(site, path).split(sep).join("/");
  const depth = name.split("/").length - 1;
  const section = sectionOf(name);

  let framed = html;
  for (const part of parts) {
    if (!part.pattern.test(framed)) {
      differ.push(`${name}: has no ${part.file.replace(".html", "")}`);
      continue;
    }
    framed = framed.replace(part.pattern, () => fill(part.template, depth, section));
  }

  if (framed === html) continue;
  differ.push(`${name}: its toolbar or footer is not site/partials'`);
  if (!check) writeFileSync(path, framed);
}

if (differ.length) {
  console.log(differ.join("\n"));
  if (check) process.exitCode = 1;
}
