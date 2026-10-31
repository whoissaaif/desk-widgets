// Accounts: anyone can ask for access with their email; the owner accepts on admin.html, which
// emails an invite; the invite opens account.html to pick a username and password. After that,
// people sign in on login.html with username and password (server/functions/login).
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
// Someone who accepted an invite but hasn't picked a username yet goes to account.html first.
export async function requireAccount(needsProfile = true) {
  const who = configured ? await me() : null;
  const here = location.pathname.split("/").pop() || "./";
  if (!who) { location.replace("login.html?next=" + encodeURIComponent(here)); return null; }
  if (needsProfile && !who.profile) { location.replace("account.html"); return null; }
  return who;
}

// "Request access": an email box anyone can use. The owner sees it on the admin page.
export function requestAccessForm() {
  const email = el("input", { class: "input", type: "email", id: "request-email", autocomplete: "email", required: true, maxlength: "254" });
  const result = el("div");
  const button = el("button", { class: "button", type: "submit" }, "Request Access");
  const form = el("form", { class: "form", onsubmit: async e => {
      e.preventDefault();
      if (!configured) { result.replaceChildren(notice("Uploads aren't connected yet.", true)); return; }
      button.disabled = true;
      const db = await supabase();
      const { error } = await db.rpc("request_access", { p_email: email.value.trim() });
      button.disabled = false;
      if (error) { result.replaceChildren(notice(error.message, true)); return; }
      form.reset();
      result.replaceChildren(notice("Thanks. If the owner accepts, you'll get an email with a link to set up your account."));
      toast("Request sent.");
    } },
    el("div", { class: "field" }, el("label", { for: "request-email" }, "Email"), email,
      el("span", { class: "hint" }, "Only the owner sees it. It's used for your invite and for resetting your password.")),
    el("div", {}, button),
    result);
  return form;
}
