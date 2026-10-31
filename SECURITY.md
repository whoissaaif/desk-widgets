# Security

Desk Widgets runs community code on people's PCs, so security reports matter a lot here.

## Reporting a problem

Please report privately through
[GitHub's private vulnerability reporting](https://github.com/whoissaaif/desk-widgets/security/advisories/new),
not in a public issue. Say what's affected, how to reproduce it, and what an attacker could do.
You'll get an answer within a week. Please don't test against other people's accounts or data.

## What's in scope

- Getting code installed or run that the owner didn't approve, or that differs from what was
  reviewed (the upload › review › `deskwidgets://` › hash check › compile chain)
- Reading or changing data that row level security should protect (`server/schema.sql`)
- The Edge Functions (`server/functions/`): invites and sign-in
- Script injection on the website (`docs/`)
- Tokens or passwords stored or sent unsafely by the app

A community widget that does something harmful *after* it was approved is a review problem, not a
vulnerability, but report it the same way so the owner can act on it.

## How it's built to be safe

See "Threat model" in `design/community-plan.md` and the comments at the top of `server/schema.sql`.
