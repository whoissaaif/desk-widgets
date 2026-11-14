// Shared helpers for every page. Everything people type or upload reaches the page through
// textContent (el()), never innerHTML, and ids/previews are re-validated here even though the
// database already checks them.
import { SUPABASE_URL, SUPABASE_ANON_KEY, RELEASES_URL, DOWNLOAD_URL, SETUP_URL, REPO_URL, STORE_URL, ANALYTICS_CODE } from "../config.js";

export { RELEASES_URL, DOWNLOAD_URL, SETUP_URL, REPO_URL, STORE_URL };

// The same categories as the app (WidgetRegistry.Categories) and the database check.
export const CATEGORIES = [
  ["clocks", "Clocks"], ["productivity", "Productivity"], ["weather", "Weather"],
  ["system", "System"], ["media", "Media"], ["developer", "Developer"], ["sports", "Sports"],
  ["finance", "Finance"], ["news", "News"], ["utility", "Utility"], ["social", "Social"],
  ["health", "Health"], ["travel", "Travel"], ["games", "Games"],
];
export const categoryName = id => {
  const known = CATEGORIES.find(c => c[0] === id);
  if (known) return known[1];
  return id ? String(id).charAt(0).toUpperCase() + String(id).slice(1) : "";
};

// One line icon per category, for cards with no preview. 24-unit paths, drawn at any size.
const CATEGORY_PATHS = {
  clocks: ["M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z", "M12 7v5l3.5 2"],
  productivity: ["M5 3.5h14v17H5z", "M9 9h6M9 13h6M9 17h3"],
  weather: ["M7.5 4.5a3 3 0 1 0 0 6 3 3 0 0 0 0-6z", "M17.5 19H10a4.5 4.5 0 0 1 0-9 5 5 0 0 1 9.4 1.6A3.7 3.7 0 0 1 17.5 19z"],
  system: ["M5 5h14v14H5z", "M9.5 9.5h5v5h-5z", "M9 2.5V5M15 2.5V5M9 19v2.5M15 19v2.5M2.5 9H5M2.5 15H5M19 9h2.5M19 15h2.5"],
  media: ["M9.5 17.5V5.5l10-2v12", "M7 15a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5z", "M17 13a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5z"],
  developer: ["M9 8l-4 4 4 4M15 8l4 4-4 4"],
  sports: ["M5 3v18", "M5 5c4.5-2.2 9 2.2 14 0v8.5c-5 2.2-9.5-2.2-14 0z"],
  games: ["M8 10v4M6 12h4", "M3.5 8.5h17v7h-17z", "M15.5 11h.01M18 13.5h.01"],
  finance: ["M4 19h16", "M7 15l4-4 3 3 5-6"],
  widget: ["M4 4.5h7v7H4zM13 4.5h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z"],
};

export function categoryIcon(category, size = 32) {
  const ns = "http://www.w3.org/2000/svg";
  const svg = document.createElementNS(ns, "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("width", String(size));
  svg.setAttribute("height", String(size));
  svg.setAttribute("class", "cat-ico");
  svg.setAttribute("aria-hidden", "true");
  for (const d of CATEGORY_PATHS[category] || CATEGORY_PATHS.widget) {
    const path = document.createElementNS(ns, "path");
    path.setAttribute("d", d);
    svg.appendChild(path);
  }
  return svg;
}

export const ID_PATTERN = /^u\.[a-z0-9-]+\.[a-z0-9-]+$/;
export const isValidId = id => typeof id === "string" && id.length <= 64 && ID_PATTERN.test(id);
const BASE64 = /^[A-Za-z0-9+/=]+$/;

// What people type as a username or an email. The database and the sign-in function hold the
// same rules and have the final word; these keep anything else from leaving the page at all, so
// a pasted value can never become part of a query string or a request body it wasn't meant for.
export const isUsername = name => typeof name === "string" && /^[a-z0-9-]{2,24}$/.test(name);
// For input events: lower case, and only the characters a username can hold.
export const cleanUsername = text => String(text).toLowerCase().replace(/[^a-z0-9-]/g, "").slice(0, 24);
// One address, no spaces, control characters or angle brackets, 254 characters at most.
export const isEmail = text => typeof text === "string" && text.length <= 254 &&
  /^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$/.test(text);

export const configured = Boolean(SUPABASE_URL && SUPABASE_ANON_KEY);

export async function restQuery(pathAndQuery, timeout = 10000) {
  if (!configured) throw new Error("The gallery isn't connected to its database yet.");
  const headers = { apikey: SUPABASE_ANON_KEY };
  if (SUPABASE_ANON_KEY.startsWith("eyJ")) headers.Authorization = "Bearer " + SUPABASE_ANON_KEY;
  const response = await fetch(SUPABASE_URL.replace(/\/$/, "") + "/rest/v1/" + pathAndQuery,
    { headers, signal: AbortSignal.timeout(timeout) });
  if (!response.ok) throw new Error(response.status + " " + response.statusText);
  return response.json();
}

// One client per page, even when the nav and the page ask at the same time (two would both try to
// read a sign-in link's session from the address).
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

// Page views, and the four clicks worth knowing: Download, Microsoft Store, Get, and the
// "not installed" prompt Get shows when nothing opened. Those four answer the questions a launch
// post raises -- did it bring installs, and do visitors arrive without the app -- and nothing
// else is collected. GoatCounter keeps a daily hash instead of a visitor and sets no cookie, so
// there is no consent banner to show. Do Not Track is honoured here because its script does not
// check it. With ANALYTICS_CODE empty, nothing is loaded and nothing is sent.
export const eventKind = href =>
  href === DOWNLOAD_URL || href === SETUP_URL ? "download"
  : href === STORE_URL ? "store"
  : href.indexOf("deskwidgets://install/") === 0 ? "get"
  : null;

// shortcut: a click landing before the async script does is not counted; add a queue if these
// numbers ever read low against the download counts on the GitHub releases.
function count(name) {
  if (window.goatcounter && window.goatcounter.count)
    window.goatcounter.count({ path: name, title: name, event: true });
}

// One delegated listener rather than a handler per link: index.html, explore.html and
// widget.html each build their own download and Get buttons.
function analytics() {
  if (!ANALYTICS_CODE || navigator.doNotTrack === "1") return;
  document.head.append(el("script", { async: true, src: "//gc.zgo.at/count.js",
    "data-goatcounter": "https://" + ANALYTICS_CODE + ".goatcounter.com/count" }));
  addEventListener("click", e => {
    const a = e.target.closest ? e.target.closest("a") : null;
    if (!a) return;
    const kind = eventKind(a.href);
    if (kind) count(kind);
  });
}

export function nav(current, options = {}) {
  routeAuthLink(current);
  if (leaving) return;
  const link = (href, text, key) => el("a", { class: "link", href, "aria-current": current === key ? "page" : null }, text);
  const account = el("div", { class: "account" });
  document.body.prepend(el("nav", { class: "nav", "aria-label": "Site" },
    el("div", { class: "inner" },
      el("a", { class: "brand", href: "./" }, "Desk Widgets"),
      link("explore.html", "Explore", "explore"),
      link("upload.html", "Upload", "upload"),
      link("develop.html", "Develop", "develop"),
      link("download.html", "Download", "download"),
      account, themeToggle())));
  document.body.append(footer());
  showPendingToast();
  accountMenuEvents(account);
  // The sign-in page asks for the signed-out nav: it is showing a sign-in form, so an avatar
  // there (from a sample session) would contradict the page.
  if (options.signedOut) account.replaceChildren(signInLink(current));
  else {
    // What the last page learned about the account is painted now, in the same frame as the nav,
    // so the photo and the owner's Admin link don't appear a moment late on every page. The
    // session is still checked; the nav is repainted only if the answer differs.
    const cached = maybeSignedIn() ? cachedMe() : null;
    if (cached) paintAccount(account, current, cached);
    accountNav(account, current, cached)
      .catch(() => { if (!cached) account.replaceChildren(signInLink(current)); });
  }
  analytics();
  speculate();
}

// Starts fetching a page of this site when a link to it is hovered or pressed, so the click
// finds it already here. Downloads and the app's own links are left alone.
function speculate() {
  if (!HTMLScriptElement.supports || !HTMLScriptElement.supports("speculationrules")) return;
  const rules = el("script", { type: "speculationrules" });
  rules.textContent = JSON.stringify({ prefetch: [{ eagerness: "moderate",
    where: { and: [{ href_matches: "/*" }, { not: { href_matches: "*.exe" } }] } }] });
  document.head.append(rules);
}

const signInLink = current => el("a", { class: "link", href: "login.html?next=" + encodeURIComponent(location.pathname.split("/").pop() || "./"),
  "aria-current": current === "login" ? "page" : null }, "Sign In");

// Every page's footer. Only links that go somewhere real; the licence line is the one in LICENSE.
function footer() {
  const column = (title, links) => el("div", {}, el("h2", { class: "f-head" }, title),
    ...links.map(([href, text]) => el("a", { href }, text)));
  return el("footer", { class: "footer" },
    el("div", { class: "inner" },
      el("p", { class: "f-notice" },
        "Desk Widgets is free and open source. Community widgets are reviewed by a person before they appear here."),
      el("div", { class: "f-cols" },
        column("Widgets", [["explore.html", "Explore"], ["./#catalog", "Built-in widgets"], ["upload.html", "Upload yours"]]),
        column("App", [["download.html", "Download"], [STORE_URL, "Microsoft Store"],
          [RELEASES_URL, "What's new"], ["privacy.html", "Privacy"]]),
        column("Open source", [[REPO_URL, "Source code"], [REPO_URL + "/issues", "Report an issue"],
          ["develop.html", "Create a widget"]])),
      el("p", { class: "f-bottom" },
        "Copyright © 2026 whoissaaif and Desk Widgets contributors. Released under the MIT license.")));
}

// Only loads the database client when there may be a session, so signed-out visitors stay fast.
export function maybeSignedIn() {
  if (!configured) return false;
  if (/access_token=|error_description=/.test(location.hash)) return true;
  try {
    return Object.keys(localStorage).some(k => k.startsWith("sb-") && k.endsWith("-auth-token"));
  } catch (e) {
    return false;     // storage blocked: assume signed out rather than break the page
  }
}

// The signed-in person's profile (null when there's none yet) and whether they're the owner.
// Asked once per page, however many callers there are (the nav and the page both ask). The
// profile carries avatar_thumb, never the photo itself: that can be megabytes, and this runs on
// every page.
let meOnce;
export function me() {
  if (leaving) return new Promise(() => {}); // the page is moving to password.html
  if (!meOnce) meOnce = loadMe();
  return meOnce;
}

async function loadMe() {
  // Imported lazily: data.js imports this module, and only it knows about sample data.
  try {
    const { sampleSession } = await import("./data.js");
    const sample = await sampleSession();
    if (sample) return sample;
  } catch (e) { /* no data layer on this page */ }
  if (!maybeSignedIn()) { rememberMe(null); return null; }
  const db = await supabase();
  const { data: { session } } = await db.auth.getSession();
  if (!session) { rememberMe(null); return null; }
  const row = columns => db.from("profiles").select(columns).eq("id", session.user.id).maybeSingle();
  const profileRow = async () => {
    const first = await row("username, bio, avatar_thumb");
    return first.error ? row("username, bio") : first;   // a database from before avatar_thumb
  };
  const [{ data: profile, error }, { data: admin }] = await Promise.all([profileRow(), db.rpc("is_admin")]);
  // Not the same as "no profile yet": that would send a signed-in person to set up again.
  if (error) throw error;
  const who = { user: session.user, profile, admin: Boolean(admin) };
  rememberMe(who);
  if (profile && "avatar_thumb" in profile && !profile.avatar_thumb) makeThumb(db, who).catch(() => {});
  return who;
}

// The little the nav needs, kept between pages. It only decides what is drawn before the session
// answers: every permission is still the database's (an edited copy shows a link that opens a
// page with nothing in it).
const ME_KEY = "dw-me";
const THUMB = 128;            // px; drawn at 28 to 72, so sharp at 2x for most of them
const MAX_THUMB_B64 = 60000;  // the profiles.avatar_thumb check

export function rememberMe(who) {
  if (!who || !who.profile) {
    try { localStorage.removeItem(ME_KEY); } catch (e) { /* no storage */ }
    return;
  }
  const thumb = who.profile.avatar_thumb;
  store(ME_KEY, JSON.stringify({ id: who.user.id, username: who.profile.username, admin: who.admin === true,
    thumb: isPhoto(thumb) && thumb.length <= MAX_THUMB_B64 ? thumb : null }));
}

function cachedMe() {
  try {
    const c = JSON.parse(stored(ME_KEY) || "null");
    if (!c || typeof c.id !== "string" || !isUsername(c.username)) return null;
    return { user: { id: c.id }, admin: c.admin === true,
      profile: { username: c.username, bio: "", avatar_thumb: isPhoto(c.thumb) ? c.thumb : null } };
  } catch (e) {
    return null;
  }
}

// What the nav shows of an account; two answers with the same key paint the same nav.
const accountKey = who => [who.profile ? who.profile.username : "", who.profile && who.profile.avatar_thumb || "", who.admin].join("|");

// A photo saved before avatar_thumb existed has no small copy. Its owner's next signed-in visit
// makes one, once per browser session, after the page is up.
async function makeThumb(db, who) {
  const key = "dw-thumb-" + who.user.id;
  try {
    if (sessionStorage.getItem(key)) return;
    sessionStorage.setItem(key, "1");
  } catch (e) {
    return;
  }
  const { data } = await db.from("profiles").select("avatar").eq("id", who.user.id).maybeSingle();
  const thumb = data ? await photoThumb(data.avatar) : null;
  if (!thumb) return;
  const { error } = await db.from("profiles").update({ avatar_thumb: thumb }).eq("id", who.user.id);
  if (error) return;
  who.profile.avatar_thumb = thumb;
  rememberMe(who);
}

// The 128 px still copy of a photo that lists and the nav show (base64), or null. An animated
// photo gives its first frame.
export async function photoThumb(b64) {
  if (!isPhoto(b64)) return null;
  const image = new Image();
  image.src = "data:" + photoMime(b64) + ";base64," + b64;
  await image.decode();
  const side = Math.min(image.naturalWidth, image.naturalHeight);
  if (!side) return null;
  const canvas = Object.assign(document.createElement("canvas"), { width: THUMB, height: THUMB });
  const ctx = canvas.getContext("2d");
  ctx.imageSmoothingQuality = "high";
  ctx.drawImage(image, (image.naturalWidth - side) / 2, (image.naturalHeight - side) / 2, side, side, 0, 0, THUMB, THUMB);
  const webp = canvas.toDataURL("image/webp", 0.85);
  const url = webp.startsWith("data:image/webp") ? webp : canvas.toDataURL("image/jpeg", 0.85);
  const out = url.split(",")[1] || "";
  return out && out.length <= MAX_THUMB_B64 ? out : null;
}

// Sign-in and sign-up links come back to whichever page Supabase was told (or the site root), with
// the result in the address (#access_token=... or #error=...). Every page finishes that here; the
// account page shows its own message.
async function accountNav(host, current, cached) {
  const hash = new URLSearchParams(location.hash.slice(1));
  if ((hash.has("error") || hash.has("error_description")) && current !== "account") {
    history.replaceState(null, "", location.pathname + location.search); // no error text left in the address bar
    // The address's own description is never shown: anyone can write a link that carries one.
    const main = document.querySelector("main");
    if (main) main.prepend(notice("That link didn't work. Links expire after an hour and work once. " +
      "Try signing up again, or use “Forgot password” on the sign-in page.", true));
  }
  const arriving = hash.has("access_token");
  const who = await me();
  if (!who) { host.replaceChildren(signInLink(current)); return; }
  if (arriving && current !== "account") {
    history.replaceState(null, "", location.pathname + location.search);
    if (!who.profile) { location.replace("account.html"); return; } // finish setting up first
    toast("You're signed in as " + who.profile.username + ".");
  }
  if (!cached || accountKey(cached) !== accountKey(who)) paintAccount(host, current, who);
}

function paintAccount(host, current, who) {
  const name = who.profile ? who.profile.username : who.user.email;
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
      rememberMe(null);
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
  if (current === "account") button.setAttribute("aria-current", "page");
  host.replaceChildren(
    ...[who.admin && el("a", { class: "link", href: "admin.html", "aria-current": current === "admin" ? "page" : null }, "Admin"),
    button, menu].filter(Boolean));
}

// Closing the account menu: a click outside it, or Escape. Registered once per page; the nav can
// be painted twice (from the cached account, then from the session).
function accountMenuEvents(host) {
  const close = () => {
    const menu = host.querySelector(".menu.open");
    if (!menu) return null;
    menu.classList.remove("open");
    const button = host.querySelector(".avatar-button");
    if (button) button.setAttribute("aria-expanded", "false");
    return button;
  };
  document.addEventListener("click", e => {
    const menu = host.querySelector(".menu");
    if (menu && !menu.contains(e.target)) close();
  });
  document.addEventListener("keydown", e => {
    if (e.key !== "Escape") return;
    const button = close();
    if (button) button.focus();
  });
}

// A round profile photo, or the first letter of the name on grey. Up to 96 px the small copy
// (avatar_thumb) is the one drawn; the photo itself is for the profile page and the editor.
export function avatar(profile, size) {
  const style = "width:" + size + "px;height:" + size + "px";
  const small = profile && isPhoto(profile.avatar_thumb) ? profile.avatar_thumb : null;
  const full = profile && isPhoto(profile.avatar) ? profile.avatar : null;
  const photo = size <= 96 ? small || full : full || small;
  if (photo) {
    return el("img", { class: "avatar", style, src: "data:" + photoMime(photo) + ";base64," + photo, alt: "" });
  }
  const letter = profile && profile.username ? profile.username[0].toUpperCase() : "";
  return el("span", { class: "avatar", style: style + ";font-size:" + Math.round(size * 0.45) + "px", "aria-hidden": "true" }, letter);
}
// base64 magic: GIF87a/89a, PNG, RIFF (WebP), else JPEG.
export const photoMime = (b64) => b64.startsWith("R0lGOD") ? "image/gif"
  : b64.startsWith("iVBORw0") ? "image/png"
  : b64.startsWith("UklGR") ? "image/webp" : "image/jpeg";

export const isPhoto = b64 => typeof b64 === "string" && b64.length > 0 && b64.length <= 6700000 && BASE64.test(b64);

// Brave with Shields blocking a site's storage -- and any browser with cookies blocked -- throws a
// SecurityError on localStorage instead of returning null. Every page imports this module, so one
// unguarded read blanks the whole site. All access goes through these two.
function stored(key) {
  try { return localStorage.getItem(key); } catch (e) { return null; }
}
function store(key, value) {
  try { localStorage.setItem(key, value); } catch (e) { /* no storage: the choice won't stick */ }
}

// Apply saved theme before anything renders (called once at import time).
const _savedTheme = stored("dw-theme");
if (_savedTheme) document.documentElement.setAttribute("data-theme", _savedTheme);

// Light / Dark button at the right of the nav. Persists in localStorage so the choice sticks
// across pages; with nothing stored it flips away from whatever the system asks for.
function themeToggle() {
  const current = () => stored("dw-theme") ||
    (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
  const button = el("button", { type: "button", class: "theme-button", "aria-label": "Switch light or dark",
    onclick: () => {
      const theme = current() === "dark" ? "light" : "dark";
      document.documentElement.setAttribute("data-theme", theme);
      store("dw-theme", theme);
    } });
  button.innerHTML = '<svg width="17" height="17" viewBox="0 0 16 16" aria-hidden="true" focusable="false">' +
    '<circle cx="8" cy="8" r="7.25" fill="none" stroke="currentColor" stroke-width="1.5"/>' +
    '<path d="M8 .75a7.25 7.25 0 0 0 0 14.5z" fill="currentColor"/></svg>';
  return button;
}

// A short confirmation at the top of the page. With later, it waits for the next page load (for
// messages about a redirect, like "You're signed in").
export function toast(text, later = false) {
  if (later) {
    try { sessionStorage.setItem("dw-toast", text); } catch (e) { /* no storage: the message is skipped */ }
    return;
  }
  const t = el("div", { class: "toast", role: "status" }, text);
  document.body.append(t);
  requestAnimationFrame(() => requestAnimationFrame(() => t.classList.add("in")));
  setTimeout(() => t.classList.add("gone"), 4000);
  setTimeout(() => t.remove(), 4600);
}
function showPendingToast() {
  let text = null;
  try {
    text = sessionStorage.getItem("dw-toast");
    if (text) sessionStorage.removeItem("dw-toast");
  } catch (e) {
    return;     // storage blocked: nav() must not throw, or the page never renders
  }
  if (text) toast(text);
}

// The Explore card. Shared so the upload page can show the real thing rather than a lookalike;
// pass artNode to supply your own art element (explore.html lazy-loads previews into a skeleton).
// options: author false drops the "by" line (a profile page is already one person's), and status
// replaces Get with that version's pill, for the owner's unapproved widgets.
export function widgetCard(w, artNode, options = {}) {
  // The generated page (tools/build-widget-pages.mjs): same content, but it unfurls as itself
  // when shared. 404.html sends a widget without a page yet back to widget.html?id=.
  const href = "w/" + encodeURIComponent(w.id) + "/";
  const by = options.author === false ? [categoryName(w.category)]
    : [el("a", { href: "profile.html?user=" + encodeURIComponent(w.author) }, w.author),
       " · " + categoryName(w.category)];
  const trailing = options.status
    ? el("span", { class: "up-pill " + options.status },
        options.status === "pending" ? "In review" : options.status === "approved" ? "Approved" : "Rejected")
    : getButton(w.id, w.version);
  return el("article", { class: "widget-card" },
    el("a", { href, "aria-label": w.name }, artNode || art(w.preview, w.name, w.category)),
    el("div", { class: "widget-card-body" },
      el("div", { class: "widget-card-text" },
        el("a", { class: "name", href }, w.name),
        el("div", { class: "by" }, by)),
      trailing));
}

// The person card from Explore's People tab. The account page shows the same one, live, as a
// preview of how others see you: pass href: null for a card that is not a link, and emptyBio to
// show a placeholder line instead of leaving the bio out.
export function personCard(profile, count, options = {}) {
  const href = options.href === undefined
    ? "profile.html?user=" + encodeURIComponent(profile.username) : options.href;
  const bio = profile.bio || (options.emptyBio || null);
  const body = el("div", { class: "p-info" },
    el("div", { class: "p-name" }, profile.username || options.emptyName || ""),
    bio ? el("div", { class: "p-bio" + (profile.bio ? "" : " dim") }, bio) : null,
    el("div", { class: "p-stat" }, count + " widget" + (count !== 1 ? "s" : "")));
  const parts = [avatar(profile, 72), body, href ? el("span", { class: "p-arrow" }, "›") : null];
  return href ? el("a", { class: "profile-card", href }, parts)
    : el("div", { class: "profile-card still" }, parts);
}

// A widget's preview as an <img>, or a quiet placeholder.
export function art(preview, name, category) {
  if (typeof preview === "string" && preview.length > 0 && preview.length <= 140000 && BASE64.test(preview))
    return el("div", { class: "art" }, el("img", { src: "data:image/png;base64," + preview, alt: name + " preview" }));
  return el("div", { class: "art" }, categoryIcon(category));
}

// Opens Desk Widgets on this PC; it re-checks everything and always asks before installing.
export const installUrl = (id, version) => `deskwidgets://install/${id}/${version}`;

// A protocol link does nothing visible when the app isn't installed, so visitors arriving from a
// shared link click Get and see nothing. If the page still has focus 1.5s later, nothing opened.
export function getButton(id, version, label = "Get", cls = "get") {
  const check = () => {
    let left = document.hidden;
    const away = () => { left = true; };
    addEventListener("blur", away);
    document.addEventListener("visibilitychange", away);
    setTimeout(() => {
      removeEventListener("blur", away);
      document.removeEventListener("visibilitychange", away);
      if (left || document.hidden || !document.hasFocus()) return;
      count("no-app");
      confirmDialog("Desk Widgets isn’t installed",
        "Get opens the app on your PC. Download it, then click Get again to install this widget.",
        "Download", () => { location.href = "download.html"; }, false);
    }, 1500);
  };
  return el("a", { class: cls, href: installUrl(id, version), "aria-label": label + " (opens Desk Widgets)", onclick: check }, label);
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

export function confirmDialog(title, message, action, onConfirm, destructive = true) {
  const overlay = el("div", { class: "dialog-overlay" },
    el("div", { class: "dialog" },
      el("h3", {}, title),
      el("p", {}, message),
      el("div", { class: "actions" },
        el("button", { class: "button grey small", type: "button", onclick: () => overlay.classList.remove("open") }, "Cancel"),
        el("button", { class: destructive ? "button destructive small" : "button small", type: "button", onclick: async () => {
          overlay.classList.remove("open");
          await onConfirm();
        } }, action))));
  document.body.append(overlay);
  requestAnimationFrame(() => overlay.classList.add("open"));
  overlay.addEventListener("click", e => { if (e.target === overlay) overlay.classList.remove("open"); });
  return overlay;
}
