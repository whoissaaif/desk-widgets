// The gallery's data layer. Callers ask for widgets, profiles and previews and never learn where
// the rows came from.
//
// On a dev host (localhost, 127.0.0.1, [::1] or a file:// page) these are the sample rows, because
// config.js ships with keys for a project that is not connected yet -- so "are there keys?" cannot
// tell us anything. Add ?live=1 to a dev URL to use the real queries instead.
//
// Anywhere else, the rows are always real: the published site has no dev host, so it can never
// serve sample data, and with no keys it reports "not connected", as before.
import { supabase, configured, me, restQuery, isUsername } from "./site.js";

const DEV = /^(localhost|127\.0\.0\.1|\[::1\])$/.test(location.hostname) || location.protocol === "file:";
const LIVE = new URLSearchParams(location.search).has("live");
export const usingMocks = DEV && !LIVE;
export const available = configured || usingMocks;
// The same switch under another name, for pages that stub auth rather than rows.
export const usingStub = usingMocks;

const stubAnnounced = new Set();
export function stubbed(label = "auth", ms = 600) {
  if (!stubAnnounced.has(label)) { stubAnnounced.add(label); console.log("[" + label + "] using stub"); }
  return new Promise((done) => setTimeout(done, ms));
}

const TIMEOUT = 10000;      // every call fails rather than hanging the loading state
const MOCK_DELAY = 400;     // so the loading state is still visible while working on it

function withTimeout(work) {
  return Promise.race([
    work,
    new Promise((_, reject) => setTimeout(() => reject(new Error("timed out")), TIMEOUT)),
  ]);
}

let announced = false;
let mockModule = null;
function mocks() {
  if (!announced) {
    announced = true;
    console.log("[explore] using mock data");
  }
  // Imported here, so the file is never requested by the published site.
  if (!mockModule) mockModule = import("./mock-data.js");
  return Promise.all([mockModule, new Promise((done) => setTimeout(done, MOCK_DELAY))])
    .then(([module]) => module);
}

const WIDGET_COLUMNS = "id, version, name, category, description, api_version, sha256, reviewed_at, author";

// The newest approved version of every widget, newest first.
export async function getWidgets() {
  if (usingMocks) return withTimeout(mocks().then((m) => m.widgets.slice()));
  return restQuery("explore?" + new URLSearchParams({ select: WIDGET_COLUMNS, order: "reviewed_at.desc" }));
}

// The authors view with the small photo (avatar_thumb), or without any photo on a database
// from before that column. Never the photo itself: those run to megabytes each.
async function authors(filter = {}) {
  const ask = (select) => restQuery("authors?" + new URLSearchParams(Object.assign({ select }, filter)));
  try { return await ask("username,bio,avatar_thumb"); }
  catch (e) { return ask("username,bio"); }
}

// Everyone with a profile: username, bio, avatar_thumb.
export async function getProfiles() {
  if (usingMocks) return withTimeout(mocks().then((m) => m.profiles.slice()));
  return authors();
}

// A single author's profile by username, or null.
export async function getProfile(username) {
  if (!isUsername(username)) return null;
  if (usingMocks) return withTimeout(mocks().then((m) => (m.profiles || []).find((p) => p.username === username) || null));
  const rows = await authors({ username: "eq." + username, limit: "1" });
  return rows.length ? rows[0] : null;
}

// One person's photo itself (base64, possibly a multi-megabyte animation), or null. Asked for
// on its own and given a minute, after the page that wants it is already showing. onlyIfNoThumb
// is for lists: it answers null for anyone whose small copy is enough.
export async function getPhoto(username, onlyIfNoThumb = false) {
  if (usingMocks || !isUsername(username)) return null;
  const query = { select: "avatar", username: "eq." + username, avatar: "not.is.null", limit: "1" };
  if (onlyIfNoThumb) query.avatar_thumb = "is.null";
  const rows = await restQuery("authors?" + new URLSearchParams(query), 60000);
  return rows.length ? rows[0].avatar : null;
}

// id -> base64 PNG (or null), for the previews the grid asks for as cards scroll into view.
export async function getPreviews(ids, basePath) {
  if (!ids.length) return new Map();
  if (usingMocks) return withTimeout(mocks().then((m) => m.previewsFor(ids, basePath)));
  const data = await restQuery("explore?" + new URLSearchParams({ select: "id,preview", id: "in.(" + ids.join(",") + ")" }));
  return new Map(data.map((row) => [row.id, row.preview]));
}

export async function usernameTaken(username) {
  if (!isUsername(username)) throw new Error("not a username");
  if (usingMocks) return withTimeout(mocks().then((module) =>
    username === "taken" || module.profiles.some((profile) => profile.username === username)));
  const rows = await restQuery("authors?" + new URLSearchParams({ select: "username", username: "eq." + username, limit: "1" }));
  return rows.length > 0;
}

// ---- the upload page: who is signed in, and what they have sent ----

// { user, profile } or null. In mock mode, a sample account, so the signed-in page can be worked
// on without a database.
// The sample account, or null when not using sample data. site.js calls this so the nav shows
// the avatar on every page while the database is not connected.
export async function sampleSession() {
  if (!usingMocks) return null;
  return (await mocks()).session;
}

export async function getSession() {
  if (usingMocks) return withTimeout(mocks().then((m) => m.session));
  return withTimeout(me());
}

export async function getMyWidgets(userId) {
  if (usingMocks) return withTimeout(mocks().then((m) => m.myWidgets.slice()));
  return withTimeout((async () => {
    const db = await supabase();
    const { data, error } = await db.from("widgets").select("id, slug").eq("owner", userId).order("id");
    if (error) throw error;
    return data || [];
  })());
}

export async function getMyVersions() {
  if (usingMocks) return withTimeout(mocks().then((m) => m.myVersions.slice()));
  return withTimeout((async () => {
    const db = await supabase();
    const { data, error } = await db.from("versions")
      .select("widget_id, version, name, category, description, status, created_at")
      .order("created_at", { ascending: false });
    if (error) throw error;
    return data || [];
  })());
}

// True when that widget id already exists. Row-level security hides other people's unapproved
// widgets, so this catches the common cases and the insert is still the final word.
export async function idTaken(id) {
  if (usingMocks) return withTimeout(mocks().then((m) => m.myWidgets.some((w) => w.id === id)));
  const db = await supabase();
  const { data } = await db.from("widgets").select("id").eq("id", id).maybeSingle();
  return Boolean(data);
}

// Both return { error } exactly as the Supabase client does, so callers read one shape.
export async function createWidget(row) {
  if (usingMocks) return mocks().then((m) => { m.myWidgets.push({ id: row.id, slug: row.slug }); return {}; });
  const db = await supabase();
  return db.from("widgets").insert(row);
}

export async function createVersion(row) {
  if (usingMocks) return mocks().then((m) => {
    m.myVersions.unshift(Object.assign({ version: 1, status: "pending", created_at: new Date().toISOString() }, row));
    return {};
  });
  const db = await supabase();
  return db.from("versions").insert(row);
}

export async function deleteWidget(id) {
  if (usingMocks) return mocks().then((m) => {
    const at = m.myWidgets.findIndex((w) => w.id === id);
    if (at >= 0) m.myWidgets.splice(at, 1);
    return {};
  });
  const db = await supabase();
  return db.rpc("delete_widget", { p_widget: id });
}
