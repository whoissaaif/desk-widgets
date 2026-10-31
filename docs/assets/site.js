// Shared helpers for every page. Everything people type or upload reaches the page through
// textContent (el()), never innerHTML, and ids/previews are re-validated here even though the
// database already checks them.
import { SUPABASE_URL, SUPABASE_ANON_KEY, RELEASES_URL } from "../config.js";

export { RELEASES_URL };

// The same categories as the app (WidgetRegistry.Categories) and the database check.
export const CATEGORIES = [
  ["clocks", "Clocks"], ["productivity", "Productivity"], ["weather", "Weather"],
  ["system", "System"], ["media", "Media"], ["developer", "Developer"], ["sports", "Sports"],
];
export const categoryName = id => (CATEGORIES.find(c => c[0] === id) || [id, id])[1];

export const ID_PATTERN = /^u\.[a-z0-9-]+\.[a-z0-9-]+$/;
export const isValidId = id => typeof id === "string" && id.length <= 64 && ID_PATTERN.test(id);
const BASE64 = /^[A-Za-z0-9+/=]+$/;

export const configured = Boolean(SUPABASE_URL && SUPABASE_ANON_KEY);

let client;
export async function supabase() {
  if (!configured) throw new Error("The gallery isn't connected to its database yet.");
  if (!client) {
    const { createClient } = await import("https://cdn.jsdelivr.net/npm/@supabase/supabase-js@2.45.4/+esm");
    client = createClient(SUPABASE_URL, SUPABASE_ANON_KEY);
  }
  return client;
}

// el("a", { href: "#", class: "x" }, "text", child, ...): text arguments become text nodes.
export function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v === false || v == null) continue;
    if (k.startsWith("on")) node.addEventListener(k.slice(2), v);
    else node.setAttribute(k, v === true ? "" : String(v));
  }
  for (const c of children.flat()) if (c != null && c !== false) node.append(c instanceof Node ? c : String(c));
  return node;
}

export function nav(current) {
  const link = (href, text, key) => el("a", { class: "link", href, "aria-current": current === key ? "page" : null }, text);
  document.body.prepend(el("nav", { class: "nav", "aria-label": "Site" },
    el("div", { class: "inner" },
      el("a", { class: "brand", href: "./" }, "Desk Widgets"),
      link("./", "Explore", "explore"),
      link("upload.html", "Upload", "upload"),
      el("a", { class: "link", href: RELEASES_URL }, "Download ↗"))));
  document.body.append(el("footer", { class: "footer" },
    el("div", { class: "content" },
      "Desk Widgets is free and open source. Community widgets are reviewed before they appear here. ",
      el("a", { href: "https://github.com/whoissaaif/desk-widgets" }, "Source on GitHub"))));
}

// A widget's preview as an <img>, or a quiet placeholder.
export function art(preview, name) {
  if (typeof preview === "string" && preview.length > 0 && preview.length <= 140000 && BASE64.test(preview))
    return el("div", { class: "art" }, el("img", { src: "data:image/png;base64," + preview, alt: name + " preview" }));
  return el("div", { class: "art" }, el("span", { class: "none" }, "No preview"));
}

// Opens Desk Widgets on this PC; it re-checks everything and always asks before installing.
export const installUrl = (id, version) => `deskwidgets://install/${id}/${version}`;

export function getButton(id, version, label = "Get") {
  return el("a", { class: "get", href: installUrl(id, version), "aria-label": label + " (opens Desk Widgets)" }, label);
}

export async function sha256Hex(text) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
}

export function notice(text, error = false) {
  return el("p", { class: "notice" + (error ? " error" : ""), role: error ? "alert" : "status" }, text);
}

export function formatDate(iso) {
  return iso ? new Date(iso).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" }) : "";
}
