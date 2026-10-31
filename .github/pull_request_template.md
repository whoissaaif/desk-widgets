## What and why

<!-- What this changes and the problem it solves. Link the issue: "Fixes #12". -->

## How it was tested

<!-- What you ran or clicked. Screenshots for anything you can see, in light and dark. -->

## Checklist

- [ ] `build.bat` builds with no errors (C# 5: no `$"..."`, `?.` or `=>` members)
- [ ] UI follows `design/apple-design.md`; new rules are added to its section 17 in this PR
- [ ] Colors, type and shapes come from `src/Theme.cs` (widgets: the palettes in `CardWidget`)
- [ ] `server/schema.sql` changes keep `cd server/test && npm test` passing, with new checks for new rules
- [ ] README or `server/README.md` updated if setup or behavior changed
- [ ] No secrets, tokens, or personal data in the diff
