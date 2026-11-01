// Shared helpers for every page. Everything people type or upload reaches the page through
// textContent (el()), never innerHTML, and ids/previews are re-validated here even though the
// database already checks them.
import { SUPABASE_URL, SUPABASE_ANON_KEY, RELEASES_URL, DOWNLOAD_URL, REPO_URL } from "../config.js";

export { RELEASES_URL, DOWNLOAD_URL, REPO_URL };

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

// One client per page, even when the nav and the page ask at the same time (two would both try to
// read an invite link's session from the address).
let client;
export function supabase() {
  if (!configured) return Promise.reject(new Error("The gallery isn't connected to its database yet."));
  if (!client) client = import("https://cdn.jsdelivr.net/npm/@supabase/supabase-js@2.45.4/+esm")
    .then(({ createClient }) => createClient(SUPABASE_URL, SUPABASE_ANON_KEY));
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

// The global nav: pages on the left, then the account at the right (Sign In, or your photo with a
// menu; Admin for the owner). Shows any message a previous page left for this one (toast()).
// A "Change your password" link that landed on another page (the dashboard's own recovery email
// goes to the site's home page) moves to password.html with its fragment, before this page does
// anything with it. While that happens, me() never answers, so the page can't clear the fragment
// or send the person elsewhere.
let leaving = false;

function routeAuthLink(current) {
  const type = new URLSearchParams(location.hash.slice(1)).get("type");
  if (type === "recovery" && current !== "password") {
    leaving = true;
    location.replace("password.html" + location.hash);
  }
}

export function nav(current) {
  routeAuthLink(current);
  if (leaving) return;
  const link = (href, text, key) => el("a", { class: "link", href, "aria-current": current === key ? "page" : null }, text);
  const account = el("div", { class: "account" });
  document.body.prepend(el("nav", { class: "nav", "aria-label": "Site" },
    el("div", { class: "inner" },
      el("a", { class: "brand", href: "./" }, "Desk Widgets"),
      link("explore.html", "Explore", "explore"),
      link("upload.html", "Upload", "upload"),
      el("a", { class: "link", href: DOWNLOAD_URL }, "Download"),
      account)));
  document.body.append(el("footer", { class: "footer" },
    el("div", { class: "content" },
      "Desk Widgets is free and open source. Community widgets are reviewed before they appear here. ",
      el("a", { href: REPO_URL }, "Source on GitHub"))));
  document.body.append(themeToggle());
  showPendingToast();
  accountNav(account, current).catch(() => account.replaceChildren(signInLink(current)));
}

const signInLink = current => el("a", { class: "link", href: "login.html?next=" + encodeURIComponent(location.pathname.split("/").pop() || "./"),
  "aria-current": current === "login" ? "page" : null }, "Sign In");

// Only loads the database client when there may be a session, so signed-out visitors stay fast.
export function maybeSignedIn() {
  return configured && (/access_token=|error_description=/.test(location.hash) ||
    Object.keys(localStorage).some(k => k.startsWith("sb-") && k.endsWith("-auth-token")));
}

// The signed-in person's profile (null when there's none yet) and whether they're the owner.
export async function me() {
  if (leaving) return new Promise(() => {}); // the page is moving to password.html
  if (!maybeSignedIn()) return null;
  const db = await supabase();
  const { data: { session } } = await db.auth.getSession();
  if (!session) return null;
  const [{ data: profile }, { data: admin }] = await Promise.all([
    db.from("profiles").select("username, bio, avatar").eq("id", session.user.id).maybeSingle(),
    db.rpc("is_admin"),
  ]);
  return { user: session.user, profile, admin: Boolean(admin) };
}

// Invite and sign-in links come back to whichever page Supabase was told (or the site root), with
// the result in the address (#access_token=... or #error=...). Every page finishes that here; the
// account page shows its own message.
async function accountNav(host, current) {
  const hash = new URLSearchParams(location.hash.slice(1));
  if ((hash.has("error") || hash.has("error_description")) && current !== "account") {
    history.replaceState(null, "", location.pathname + location.search); // no error text left in the address bar
    const why = (hash.get("error_description") || hash.get("error") || "").replace(/\+/g, " ");
    const main = document.querySelector("main");
    if (main) main.prepend(notice("That link didn't work" + (why ? " (" + why + ")" : "") +
      ". Links expire after a day and work once. Ask the owner to send your invite again, or use “Forgot your password” on the sign-in page.", true));
    host.replaceChildren(signInLink(current));
    return;
  }
  const arriving = hash.has("access_token");
  const who = await me();
  if (!who) { host.replaceChildren(signInLink(current)); return; }
  if (arriving && current !== "account") {
    history.replaceState(null, "", location.pathname + location.search);
    if (!who.profile) { location.replace("account.html"); return; } // finish setting up first
    toast("You're signed in as " + who.profile.username + ".");
  }
  const name = who.profile ? who.profile.username : who.user.email;
  const close = () => { menu.classList.remove("open"); button.setAttribute("aria-expanded", "false"); };
  const item = (href, text) => el("a", { role: "menuitem", href }, text);
  const menu = el("div", { class: "menu", role: "menu" },
    el("div", { class: "who" }, avatar(who.profile, 40),
      el("div", {}, el("div", { class: "t-headline" }, name), el("div", { class: "t-caption" }, who.admin ? "Owner" : "Signed in"))),
    who.profile ? item("upload.html#uploads", "Your Uploads") : null,
    who.profile ? item("profile.html?user=" + encodeURIComponent(who.profile.username), "Your Profile") : null,
    item("account.html", who.profile ? "Edit Profile" : "Finish Setting Up"),
    who.admin ? item("admin.html", "Admin Console") : null,
    el("hr"),
    el("button", { type: "button", role: "menuitem", onclick: async () => {
      await (await supabase()).auth.signOut();
      toast("You're signed out.", true);
      location.href = "./";
    } }, "Sign Out"));
  const button = el("button", { type: "button", class: "avatar-button", "aria-haspopup": "menu", "aria-expanded": "false",
    "aria-label": "Account: " + name, onclick: e => {
      e.stopPropagation();
      const open = !menu.classList.contains("open");
      if (open) menu.classList.add("open"); else menu.classList.remove("open");
      button.setAttribute("aria-expanded", String(open));
      if (open) menu.querySelector("[role=menuitem]").focus();
    } }, avatar(who.profile, 28));
  document.addEventListener("click", e => { if (!menu.contains(e.target)) close(); });
  document.addEventListener("keydown", e => { if (e.key === "Escape" && menu.classList.contains("open")) { close(); button.focus(); } });
  host.replaceChildren(
    ...[who.admin && el("a", { class: "link", href: "admin.html", "aria-current": current === "admin" ? "page" : null }, "Admin"),
    button, menu].filter(Boolean));
}

// A round profile photo, or the first letter of the name on grey.
export function avatar(profile, size) {
  const style = "width:" + size + "px;height:" + size + "px";
  if (profile && isPhoto(profile.avatar)) {
    const mime = profile.avatar.slice(0, 6) === "R0lGOD" ? "image/gif" : "image/jpeg";
    return el("img", { class: "avatar", style, src: "data:" + mime + ";base64," + profile.avatar, alt: "" });
  }
  const letter = profile && profile.username ? profile.username[0].toUpperCase() : "";
  return el("span", { class: "avatar", style: style + ";font-size:" + Math.round(size * 0.45) + "px", "aria-hidden": "true" }, letter);
}
export const isPhoto = b64 => typeof b64 === "string" && b64.length > 0 && b64.length <= 200000 && BASE64.test(b64);

// Apply saved theme before anything renders (called once at import time).
const _savedTheme = localStorage.getItem("dw-theme");
if (_savedTheme) document.documentElement.setAttribute("data-theme", _savedTheme);

// Light / Dark toggle in the footer. Persists in localStorage so the choice sticks across pages.
function themeToggle() {
  const stored = localStorage.getItem("dw-theme");
  const pick = theme => {
    document.documentElement.setAttribute("data-theme", theme);
    localStorage.setItem("dw-theme", theme);
    for (const b of toggle.querySelectorAll("button")) b.setAttribute("aria-pressed", String(b.dataset.theme === theme));
  };
  const btn = (label, key) => el("button", { type: "button", "data-theme": key,
    "aria-pressed": String((stored || "") === key), onclick: () => pick(key) }, label);
  const toggle = el("span", { class: "theme-toggle", role: "group", "aria-label": "Theme" },
    btn("Light", "light"), btn("Dark", "dark"));
  return el("div", { class: "theme-bar" },
    el("span", { class: "theme-label" }, "Theme:"), toggle);
}

// A short confirmation at the top of the page. With later, it waits for the next page load (for
// messages about a redirect, like "You're signed in").
export function toast(text, later = false) {
  if (later) { sessionStorage.setItem("dw-toast", text); return; }
  const t = el("div", { class: "toast", role: "status" }, text);
  document.body.append(t);
  requestAnimationFrame(() => requestAnimationFrame(() => t.classList.add("in")));
  setTimeout(() => t.classList.add("gone"), 4000);
  setTimeout(() => t.remove(), 4600);
}
function showPendingToast() {
  const text = sessionStorage.getItem("dw-toast");
  if (text) { sessionStorage.removeItem("dw-toast"); toast(text); }
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

export function confirmDialog(title, message, action, onConfirm) {
  const overlay = el("div", { class: "dialog-overlay" },
    el("div", { class: "dialog" },
      el("h3", {}, title),
      el("p", {}, message),
      el("div", { class: "actions" },
        el("button", { class: "button grey small", type: "button", onclick: () => overlay.classList.remove("open") }, "Cancel"),
        el("button", { class: "button destructive small", type: "button", onclick: async () => {
          overlay.classList.remove("open");
          await onConfirm();
        } }, action))));
  document.body.append(overlay);
  requestAnimationFrame(() => overlay.classList.add("open"));
  overlay.addEventListener("click", e => { if (e.target === overlay) overlay.classList.remove("open"); });
  return overlay;
}
