// Generates one shareable page per community widget: docs/w/<id>/index.html plus its preview.png.
//
// Why these exist: docs/widget.html is a single file that reads ?id= in the browser, so every
// widget link unfurls with the same title and the same picture. A link to the F1 widget posted in
// an F1 Discord shows nothing about F1. Crawlers do not run the page's JavaScript, so the only
// fix is a real file per widget with its own <title>, description and og:image.
//
// The pages are not a second copy of the UI. Each one is docs/widget.html with four mechanical
// edits, so a change to widget.html reaches every generated page the next time this runs:
//   1. <base href="../../"> so the shared nav, footer and profile links still resolve
//   2. the real title and the social meta tags
//   3. the widget id baked in, instead of reading the query string
//   4. the one fragment link fixed, because <base> would send it to the site root
//
// Run: node tools/build-widget-pages.mjs        (add --check to fail instead of writing)
//
// Reads the gallery with the publishable key, exactly as a visitor's browser does.

import { readFile, writeFile, mkdir, rm, readdir } from "fs/promises";
import { existsSync } from "fs";
import { fileURLToPath } from "url";
import { dirname, join } from "path";

const here = dirname(fileURLToPath(import.meta.url));
const docs = join(here, "..", "docs");
const outRoot = join(docs, "w");

const { SUPABASE_URL, SUPABASE_ANON_KEY, REPO_URL } = await import(
  "file://" + join(docs, "config.js").replace(/\\/g, "/"));

// github.com/<owner>/<repo> -> <owner>.github.io/<repo>, the Pages URL og: tags must be absolute for.
const repo = REPO_URL.replace(/\/+$/, "").split("/");
const SITE = `https://${repo[repo.length - 2]}.github.io/${repo[repo.length - 1]}`;

// Same rule as isValidId in docs/assets/site.js. Kept here rather than imported because site.js
// touches document at module load. It also means no id can escape docs/w/ as a path.
const ID_PATTERN = /^u\.[a-z0-9-]+\.[a-z0-9-]+$/;

const esc = s => String(s ?? "")
  .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
  .replace(/"/g, "&quot;").replace(/'/g, "&#39;");

async function gallery() {
  const url = `${SUPABASE_URL}/rest/v1/explore?select=id,name,category,description,version,preview,author`;
  const res = await fetch(url, { headers: { apikey: SUPABASE_ANON_KEY } });
  if (!res.ok) throw new Error(`gallery read failed: ${res.status} ${res.statusText}`);
  return res.json();
}

function pageFor(template, w) {
  const title = `${w.name} · Desk Widgets`;
  const desc = w.description && w.description !== w.name
    ? w.description
    : `A community widget for the Desk Widgets desktop app.`;
  const url = `${SITE}/w/${w.id}/`;
  const image = w.preview ? `${url}preview.png` : `${SITE}/assets/img/calendar-dark.png`;

  const meta = [
    `  <title>${esc(title)}</title>`,
    `  <meta name="description" content="${esc(desc)}">`,
    `  <link rel="canonical" href="${esc(url)}">`,
    `  <meta property="og:type" content="website">`,
    `  <meta property="og:title" content="${esc(w.name)}">`,
    `  <meta property="og:description" content="${esc(desc)}">`,
    `  <meta property="og:image" content="${esc(image)}">`,
    `  <meta property="og:url" content="${esc(url)}">`,
    `  <meta name="twitter:card" content="summary_large_image">`,
  ].join("\n");

  let html = template;
  const edits = [
    // 1. Every link in the shared nav, footer and page body is relative to docs/.
    [`  <meta name="viewport" content="width=device-width, initial-scale=1">`,
     `  <meta name="viewport" content="width=device-width, initial-scale=1">\n  <base href="../../">`],
    // 2. The whole point: this page's own identity, for crawlers that never run the script.
    [`  <title>Widget · Desk Widgets</title>`, meta],
    // 3. No query string to read: this file is only ever this widget.
    [`    const id = new URLSearchParams(location.search).get("id");`,
     `    const id = ${JSON.stringify(w.id)};`],
    // 4. <base> would resolve a bare "#source" against the site root and navigate away.
    [`el("a", { class: "more", href: "#source" }, "View source")`,
     `el("a", { class: "more", href: location.pathname + "#source" }, "View source")`],
  ];
  for (const [from, to] of edits) {
    if (!html.includes(from)) throw new Error(`docs/widget.html no longer contains:\n  ${from}`);
    html = html.replace(from, to);
  }
  return html;
}

const check = process.argv.includes("--check");
const widgets = (await gallery()).filter(w => ID_PATTERN.test(w.id) && w.id.length <= 64);
if (!widgets.length) throw new Error("the gallery returned no usable widgets; refusing to wipe docs/w");

const template = await readFile(join(docs, "widget.html"), "utf8");
let written = 0, stale = [];

for (const w of widgets) {
  const dir = join(outRoot, w.id);
  const html = pageFor(template, w);
  const current = existsSync(join(dir, "index.html")) ? await readFile(join(dir, "index.html"), "utf8") : null;
  if (current === html && (!w.preview || existsSync(join(dir, "preview.png")))) continue;
  if (check) { stale.push(w.id); continue; }
  await mkdir(dir, { recursive: true });
  await writeFile(join(dir, "index.html"), html);
  if (w.preview) await writeFile(join(dir, "preview.png"), Buffer.from(w.preview, "base64"));
  written++;
}

// A widget pulled from the gallery must not keep a live page.
const live = new Set(widgets.map(w => w.id));
const existing = existsSync(outRoot)
  ? (await readdir(outRoot, { withFileTypes: true })).filter(e => e.isDirectory()).map(e => e.name) : [];
for (const name of existing) {
  if (live.has(name)) continue;
  if (check) { stale.push(`${name} (removed from the gallery)`); continue; }
  await rm(join(outRoot, name), { recursive: true, force: true });
  console.log(`removed docs/w/${name}`);
}

if (check) {
  if (stale.length) {
    console.error(`docs/w is out of date with the gallery:\n  ${stale.join("\n  ")}`);
    console.error(`Run: node tools/build-widget-pages.mjs`);
    process.exit(1);
  }
  console.log(`docs/w matches the gallery (${widgets.length} widgets).`);
} else {
  console.log(`${widgets.length} widgets, ${written} page(s) written to docs/w/.`);
}
