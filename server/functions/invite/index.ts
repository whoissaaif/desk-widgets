// Supabase Edge Function: the owner invites an uploader by email and sets their username.
// Inviting needs the service key, which lives only in this function's secrets, never in the repo.
// Deploy: supabase functions deploy invite   (secrets: SITE_URL, e.g. https://whoissaaif.github.io/desk-widgets)
import { createClient } from "jsr:@supabase/supabase-js@2";

const SITE_URL = Deno.env.get("SITE_URL") ?? "";
const cors = {
  "Access-Control-Allow-Origin": SITE_URL ? new URL(SITE_URL).origin : "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

function reply(status: number, body: Record<string, unknown>) {
  return new Response(JSON.stringify(body), { status, headers: { ...cors, "Content-Type": "application/json" } });
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (req.method !== "POST") return reply(405, { error: "Use POST." });

  const url = Deno.env.get("SUPABASE_URL")!;
  // Who is calling: their own token, checked by Supabase.
  const caller = createClient(url, Deno.env.get("SUPABASE_ANON_KEY")!, {
    global: { headers: { Authorization: req.headers.get("Authorization") ?? "" } },
    auth: { persistSession: false },
  });
  const { data: { user } } = await caller.auth.getUser();
  if (!user) return reply(401, { error: "Sign in first." });

  const admin = createClient(url, Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!, { auth: { persistSession: false } });
  const { data: isAdmin } = await admin.from("admins").select("user_id").eq("user_id", user.id).maybeSingle();
  if (!isAdmin) return reply(403, { error: "Only the owner can invite people." });

  let email = "", username = "";
  try {
    const body = await req.json();
    email = String(body.email ?? "").trim().toLowerCase();
    username = String(body.username ?? "").trim().toLowerCase();
  } catch {
    return reply(400, { error: "Send JSON with email and username." });
  }
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) return reply(400, { error: "That email address doesn't look right." });
  if (!/^[a-z0-9-]{2,24}$/.test(username)) {
    return reply(400, { error: "Usernames are 2 to 24 lowercase letters, digits or hyphens." });
  }
  const { data: taken } = await admin.from("profiles").select("id").eq("username", username).maybeSingle();
  if (taken) return reply(409, { error: `The username ${username} is taken.` });

  const { data: invited, error } = await admin.auth.admin.inviteUserByEmail(email, {
    redirectTo: SITE_URL ? SITE_URL + "/upload.html" : undefined,
  });
  if (error || !invited.user) return reply(400, { error: error?.message ?? "The invite couldn't be sent." });

  // The profile is what allows uploading (see the insert policies in schema.sql).
  const { error: profileError } = await admin.from("profiles").insert({ id: invited.user.id, username });
  if (profileError) {
    await admin.auth.admin.deleteUser(invited.user.id);
    return reply(409, { error: "The invite was cancelled: " + profileError.message });
  }
  return reply(200, { invited: email, username });
});
