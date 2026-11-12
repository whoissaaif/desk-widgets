// Accounts: anyone can sign up on login.html with their email (magic link creates the account),
// then set up username and password on account.html. After that, people sign in on login.html
// with username and password (server/functions/login).
import { el, notice, supabase, configured, me, toast } from "./site.js";

// Calls an Edge Function and returns { data } or { error: "a sentence" }.
export async function callFunction(name, body) {
  const db = await supabase();
  const { data, error } = await db.functions.invoke(name, { body });
  if (!error) return { data };
  let message = error.message;
  try { const j = await error.context.json(); if (j && j.error) message = j.error; } catch { /* not JSON */ }
  return { error: message };
}

export async function signInWithPassword(username, password) {
  const { data, error } = await callFunction("login", { username, password });
  if (error) return error;
  const db = await supabase();
  const { error: sessionError } = await db.auth.setSession({ access_token: data.access_token, refresh_token: data.refresh_token });
  return sessionError ? sessionError.message : null;
}

// The signed-in person, or a trip to the login page (which comes back here afterwards).
// Someone who signed up but hasn't picked a username yet goes to account.html first.
export async function requireAccount(needsProfile = true) {
  const who = configured ? await me() : null;
  const here = location.pathname.split("/").pop() || "./";
  if (!who) { location.replace("login.html?next=" + encodeURIComponent(here)); return null; }
  if (needsProfile && !who.profile) { location.replace("account.html"); return null; }
  return who;
}
