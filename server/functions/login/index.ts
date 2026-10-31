// Supabase Edge Function: sign in with username and password (the website's login page and the
// app). Supabase Auth signs in by email, and emails are never public, so the lookup happens here
// with the service key and only the session goes back. Every failure gives the same answer, so it
// can't be used to find out which usernames exist.
// Deploy with "Verify JWT" off: people call it before they have a session.
//   supabase functions deploy login --no-verify-jwt   (secrets: SITE_URL, as for invite)
import { createClient } from "jsr:@supabase/supabase-js@2";

const SITE_URL = Deno.env.get("SITE_URL") ?? "";
const cors = {
  "Access-Control-Allow-Origin": SITE_URL ? new URL(SITE_URL).origin : "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

// First key of a JSON dictionary env var (SUPABASE_SECRET_KEYS, SUPABASE_PUBLISHABLE_KEYS), or the
// legacy single key where the new ones don't exist yet.
function key(dictionary: string, legacy: string): string {
  try {
    const keys = JSON.parse(Deno.env.get(dictionary) ?? "{}") as Record<string, string>;
    const k = keys.default ?? Object.values(keys)[0];
    if (k) return k;
  } catch { /* fall through to the legacy key */ }
  return Deno.env.get(legacy)!;
}

function reply(status: number, body: Record<string, unknown>) {
  return new Response(JSON.stringify(body), { status, headers: { ...cors, "Content-Type": "application/json" } });
}

const WRONG = { error: "Wrong username or password." };

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (req.method !== "POST") return reply(405, { error: "Use POST." });

  let username = "", password = "";
  try {
    const body = await req.json();
    username = String(body.username ?? "").trim().toLowerCase();
    password = String(body.password ?? "");
  } catch {
    return reply(400, { error: "Send JSON with username and password." });
  }
  if (!/^[a-z0-9-]{2,24}$/.test(username) || password.length < 1 || password.length > 200) return reply(400, WRONG);

  const url = Deno.env.get("SUPABASE_URL")!;
  const opts = { auth: { persistSession: false, autoRefreshToken: false } };
  const admin = createClient(url, key("SUPABASE_SECRET_KEYS", "SUPABASE_SERVICE_ROLE_KEY"), opts);
  const { data: profile } = await admin.from("profiles").select("id").eq("username", username).maybeSingle();
  if (!profile) return reply(400, WRONG);
  const { data: found } = await admin.auth.admin.getUserById(profile.id);
  const email = found?.user?.email;
  if (!email) return reply(400, WRONG);

  // A separate client with the public key: signing in is what Supabase Auth checks and rate-limits.
  const auth = createClient(url, key("SUPABASE_PUBLISHABLE_KEYS", "SUPABASE_ANON_KEY"), opts);
  const { data, error } = await auth.auth.signInWithPassword({ email, password });
  if (error || !data.session) return reply(400, WRONG);
  const s = data.session;
  return reply(200, {
    access_token: s.access_token, refresh_token: s.refresh_token, expires_in: s.expires_in, expires_at: s.expires_at,
    token_type: s.token_type, user: { id: s.user.id }, username,
  });
});
