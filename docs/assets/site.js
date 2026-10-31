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
  account(current).catch(() => {});
}

// Invite and sign-in links come back to whichever page Supabase was told, with the result in the
// URL fragment (#access_token=... or #error=...). Every page finishes that here: on success it goes
// to Review (owner) or Upload (uploader), on failure it says why. Signed in, the nav shows Review.
async function account(current) {
  if (!configured) return;
  const hash = new URLSearchParams(location.hash.slice(1));
  const arriving = hash.has("access_token") || hash.has("error") || hash.has("error_description");
  if (hash.has("error") || hash.has("error_description")) {
    history.replaceState(null, "", location.pathname + location.search); // no error text left in the address bar
    const why = (hash.get("error_description") || hash.get("error") || "").replace(/\+/g, " ");
    const main = document.querySelector("main");
    if (main) main.prepend(notice("That sign-in link didn't work" + (why ? " (" + why + ")" : "") +
      ". Links expire after a while and work once; ask the owner for a new invite, or request a new link on the Upload page.", true));
    return;
  }
  if (!arriving && !hasStoredSession()) return; // nothing to do: don't load the client on every visit
  const db = await supabase(); // reads a session from the URL fragment, if there is one
  const { data: { session } } = await db.auth.getSession();
  if (!session) return;
  const { data: isAdmin } = await db.rpc("is_admin");
  if (arriving) {
    location.replace(isAdmin ? "admin.html" : "upload.html");
    return;
  }
  if (isAdmin && current !== "review") {
    const links = document.querySelector(".nav .inner");
    const upload = links && links.querySelector('a[href="upload.html"]');
    if (upload) upload.after(el("a", { class: "link", href: "admin.html" }, "Review"));
  }
}

// supabase-js keeps the session in localStorage under sb-<project>-auth-token.
function hasStoredSession() {
  try { return Object.keys(localStorage).some(k => /^sb-.*-auth-token$/.test(k)); } catch { return false; }
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

// Characters that make code look different from what the C# compiler sees ("Trojan Source"):
// line breaks only the compiler honors (lone CR, U+0085, U+2028, U+2029), invisible characters and
// bidi controls. The browser wouldn't show them truthfully, so they're refused on upload, block
// approval, and show as markers. Same rule as source_is_plain (server/schema.sql) and
// Plugins.IsHidden (app).
const HIDDEN = /\r(?!\n)|[\x00-\x08\x0B\x0C\x0E-\x1F\x7F-\x9F\u200B-\u200F\u2028\u2029\u202A-\u202E\u2060-\u2069\uFEFF]/g;
const marker = ch => "⟨U+" + ch.charCodeAt(0).toString(16).toUpperCase().padStart(4, "0") + "⟩";

// [{ code: "U+2028", line: 3 }, ...] for every hidden character in the text.
export function hiddenCharacters(text) {
  const found = [];
  for (const m of text.matchAll(HIDDEN)) {
    found.push({ code: marker(m[0]).slice(1, -1), line: text.slice(0, m.index).split("\n").length });
  }
  return found;
}

// The text with every hidden character replaced by a visible marker (for display only).
export const visibleSource = text => text.replace(HIDDEN, marker);

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
