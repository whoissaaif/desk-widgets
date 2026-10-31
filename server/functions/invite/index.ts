// Supabase Edge Function: the owner accepts an access request (or invites an address directly), and
// Supabase emails an invite link. The link opens docs/account.html, where the invitee picks their
// username, password, bio and photo. Inviting needs the service key, which lives only in this
// function's secrets, never in the repo.
// Deploy: supabase functions deploy invite   (secrets: SITE_URL, e.g. https://whoissaaif.github.io/desk-widgets)
import { createClient } from "jsr:@supabase/supabase-js@2";

const SITE_URL = Deno.env.get("SITE_URL") ?? "";
const cors = {
  "Access-Control-Allow-Origin": SITE_URL ? new URL(SITE_URL).origin : "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

// The project's secret key: SUPABASE_SECRET_KEYS (a JSON dictionary, "default" first) on current
// projects; the deprecated SUPABASE_SERVICE_ROLE_KEY only where the new keys don't exist yet.
function serviceKey(): string {
  try {
    const keys = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS") ?? "{}") as Record<string, string>;
    const key = keys.default ?? Object.values(keys)[0];
    if (key) return key;
  } catch { /* fall through to the legacy key */ }
  return Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
}

function reply(status: number, body: Record<string, unknown>) {
  return new Response(JSON.stringify(body), { status, headers: { ...cors, "Content-Type": "application/json" } });
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (req.method !== "POST") return reply(405, { error: "Use POST." });

  const url = Deno.env.get("SUPABASE_URL")!;
  const admin = createClient(url, serviceKey(), { auth: { persistSession: false } });

  // Who is calling: their own access token, verified by Supabase Auth.
  const token = (req.headers.get("Authorization") ?? "").replace(/^Bearer\s+/i, "");
  const { data: { user } } = token ? await admin.auth.getUser(token) : { data: { user: null } };
  if (!user) return reply(401, { error: "Sign in first." });
  const { data: isAdmin } = await admin.from("admins").select("user_id").eq("user_id", user.id).maybeSingle();
  if (!isAdmin) return reply(403, { error: "Only the owner can invite people." });

  // { request: <id> } accepts a waiting request; { email } invites an address directly.
  let email = "", request: number | null = null;
  try {
    const body = await req.json();
    if (body.request != null) request = Number(body.request);
    else email = String(body.email ?? "").trim().toLowerCase();
  } catch {
    return reply(400, { error: "Send JSON with a request id or an email." });
  }
  if (request != null) {
    if (!Number.isSafeInteger(request)) return reply(400, { error: "That request doesn't exist." });
    const { data: row } = await admin.from("access_requests").select("email, status").eq("id", request).maybeSingle();
    if (!row) return reply(404, { error: "That request doesn't exist." });
    if (row.status !== "pending") return reply(409, { error: "That request was already " + row.status + "." });
    email = row.email;
  }
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) return reply(400, { error: "That email address doesn't look right." });

  const { error } = await admin.auth.admin.inviteUserByEmail(email, {
    redirectTo: SITE_URL ? SITE_URL + "/account.html" : undefined,
  });
  if (error) {
    const known = /already|registered|exists/i.test(error.message);
    return reply(known ? 409 : 400, { error: known ? email + " already has an account." : error.message });
  }

  // Remembered either way, so the same address can't queue another request.
  if (request != null) {
    await admin.from("access_requests").update({ status: "accepted", decided_at: new Date().toISOString() }).eq("id", request);
  } else {
    await admin.from("access_requests").upsert({ email, status: "accepted", decided_at: new Date().toISOString() }, { onConflict: "email" });
  }
  return reply(200, { invited: email });
});
