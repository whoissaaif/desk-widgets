// Sign-in for invited uploaders and the owner: an email link (sign-ups are off in Supabase, so
// only invited addresses get one).
import { el, notice, supabase, configured } from "./site.js";

// Resolves with the signed-in user, after showing a sign-in form in `host` if needed.
export async function signedIn(host, purpose) {
  if (!configured) { host.replaceChildren(notice("Uploads aren't connected yet.")); return null; }
  const db = await supabase();
  const { data: { session } } = await db.auth.getSession();
  if (session) return session.user;

  const email = el("input", { class: "input", type: "email", id: "email", autocomplete: "email", required: true });
  const result = el("div");
  const form = el("form", { class: "form", onsubmit: async e => {
      e.preventDefault();
      const { error } = await db.auth.signInWithOtp({
        email: email.value.trim(),
        options: { shouldCreateUser: false, emailRedirectTo: location.href.split("#")[0] },
      });
      result.replaceChildren(error
        ? notice("That address can't sign in. Uploading is by invitation; ask the owner for one.", true)
        : notice("Check your email for a sign-in link."));
    } },
    el("div", { class: "field" }, el("label", { for: "email" }, "Email"), email,
      el("span", { class: "hint" }, purpose)),
    el("div", {}, el("button", { class: "button", type: "submit" }, "Send Sign-In Link")),
    result);
  host.replaceChildren(form);
  return null;
}

export async function signOutLink() {
  const db = await supabase();
  return el("a", { href: "#", onclick: async e => { e.preventDefault(); await db.auth.signOut(); location.reload(); } }, "Sign out");
}
