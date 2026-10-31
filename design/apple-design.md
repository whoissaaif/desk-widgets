# Apple design, as Apple actually ships it

A field guide to Apple's design language as it appears on Apple's own websites and web apps,
written so anyone can build something that looks and behaves like Apple made it.

Sections 1 to 16 describe Apple, measured. Section 17 maps them onto Desk Widgets. Together they
are the only design language for the app, its menus, dialogs, widgets and website.

## How this was made

- **Measured, not remembered.** On 31 October 2026 I opened these pages in Chrome at a
  1920 px wide viewport and read the computed styles of real elements (font size, line height,
  weight, tracking, color, radius, shadow, blur, padding):
  - apple.com home
  - iPhone 18 Pro (product page)
  - Mac (family page)
  - Store
  - Buy iPhone 18 Pro (configurator)
  - Environment (editorial)
  - Newsroom and a Newsroom article
  - support.apple.com
  - the Human Interface Guidelines (index and Typography)
  - App Store web (an app page)
  - Apple Music web (Browse / New)
- **Apple's own rules.** Text styles, tracking and minimum sizes come from the HIG pages in
  the local apple-design skill (`typography.md`, `branding.md`, `liquid-glass.md`).
- **Known values, labeled.** The iOS system color hex values and apple.com's breakpoints were
  not re-measured. They're Apple's published UIKit values and long-standing CSS conventions, and
  they're marked "(published)" or "(convention)" below.
- **Snapshot.** Apple ships new pages constantly. When something here disagrees with
  apple.com today, apple.com wins. Re-measure and update this file.

Numbers are CSS px unless marked pt. "Light" and "dark" mean the page's own theme. Marketing
pages pick a theme per section; docs and web apps follow the system appearance.

---

## 1. What makes it look like Apple

These are the ideas behind every measurement below. Read them first.

1. **The product is the hero; the interface is quiet.** Pages are mostly huge photography or
   video on pure black, pure white or `#f5f5f7`. The interface around it is grey, small and
   monochrome. Color comes from the product, not the chrome.
2. **Type does the work.** One family (SF Pro), two optical sizes (Text and Display), weights
   400 and 600 almost everywhere (700 in docs and apps). Hierarchy comes from huge size jumps
   (12 → 17 → 28 → 48 → 80 → 96), not from color or decoration.
3. **Tracking changes with size.** Small text is tightened, mid sizes are opened slightly, and
   huge display text is tightened hard. That's a large part of why it reads as Apple (§3.3).
4. **Enormous whitespace.** Sections get 120 to 144 px above and below. Content columns are
   narrow (653 to 980 px) even on a 1920 px screen.
5. **One blue for action.** Interactive text and primary buttons use one blue. Everything else
   is neutral. Orange appears only as a tiny "New" eyebrow.
6. **Pills and continuous corners.** Every button is a capsule (`border-radius: 980px`). Cards
   use big radii (18 or 28). Nothing has a sharp corner except full-bleed tiles.
7. **Glass only on things that float.** The global nav, local nav, sidebars and media controls
   are translucent and blurred. Content (cards, tiles, text) is opaque.
8. **Few words, said twice.** A short, confident headline ("Pro further.") followed by a
   plain-language line ("PROLED XDR Display"). CTAs come in pairs: Learn more + Buy.
9. **Consistency at scale.** The same 12 px nav, 44 px buttons and 980 px footer appear on
   apple.com, Support, Newsroom and the Developer site. Users should never relearn the frame.

---

## 2. Page types and their anatomy

### 2.1 Marketing homepage (apple.com)
```
┌ global nav 44 (glass, dark) ───────────────────────────────┐
├ full-bleed tile 692 tall, bg #000 / #f5f5f7 / #fff ────────┤
│        Headline 56/60 600        (centered)                 │
│        Subhead 28/32 400                                    │
│        Availability 19/23 #86868b                           │
│        [Learn more] [Buy]   ← 44 px pills, 12 px gap        │
│        product image, bottom-anchored                       │
├ 12 px gap ─────────────────────────────────────────────────┤
├ half tile | 12 | half tile  (40/44 headline, 21/25 sub) ───┤
└ footer #f5f5f7, 12 px text, 980 column ───────────────────┘
```
- Tiles are full-bleed rectangles with **no radius**, separated by **12 px** gutters that show
  the white page between them.
- Each tile is one product: centered headline, one line of positioning, CTA pair, then the
  image. Light and dark tiles alternate.
- Tile padding top is 56. Text blocks are only 280 to 340 px wide and centered.

### 2.2 Product page (iPhone 18 Pro)
- **Hero:** a black page, then an eyebrow (`32/36 600`), then a giant headline (`80/84 600, -1.2px`).
  A floating buy bar sits bottom-right: a translucent capsule with the price in 14 px and a
  small blue Buy pill.
- **Floating local nav (2026 style):** a 1024 × 52 glass bar with an **18 px radius**,
  `rgba(18,18,18,.8)` plus `blur(20px)` and a 1 px `rgb(40,40,40)` ring. It holds the product name
  (19/600), "Explore" as an outlined pill and "Buy" as a filled blue pill.
- **Sections:** each opens with a `48/52 600` section header and a link on the right ("Watch the
  film ⊕"), with 144 px padding top and bottom.
- **Highlights gallery:** media cards at **28 px radius**, 1260 px wide, with headlines over the
  media (`28/32 600, rgba(255,255,255,.92)`). Below sits a pagination capsule with dots and a
  separate round play/pause button, both glass.
- **Stat callouts:** a value in `48/48 600` over a caption in `14/18 400`.
- **Big lockups:** an eyebrow (21/21), a headline (96/100, -1.44px) and copy (21/29).
- The section background on dark pages is `#1d1d1f`, and the body is `#000`.

### 2.3 Family page (Mac)
- Large page title "Mac" (about 80 px) on white, then the **chapter nav**: a row of product
  icons 64 px tall, 14 px labels at `rgba(0,0,0,.8)`, and a "New" under the label in 12/600
  orange. The row is 168 px tall and scrolls sideways with a chevron.
- A **ribbon** above it: a full-width 52 px strip, 14 px text, `#f5f5f7` or blue, with an
  inline "Learn more ›" link.
- **"Explore the lineup."** uses a `48/52 600` header and a **pill segmented control**: the
  track is `#e8e8ed`, `radius 999`, 6 px padding and 56 px tall. The selected pill is `#1d1d1f`
  and 44 px tall with white text; unselected text is black, all at 17 px.
- Product cards: color dots, then the name `28/32 600`, a tagline `17/21 600`, the price `14/18`,
  a **Learn more** (filled) and a **Buy ›** (text link) pair, then paddle arrows. The section
  background is `#f5f5f7` with 120 px padding.

### 2.4 Store
- Page title "Store" `80/84 600 -1.2px`, with a right-aligned two-line promise in `24/28 600`
  and blue links ("Connect with a Specialist ↗").
- **Shelf headers in two tones:** "**The latest.** Take a look at what's new." The first
  sentence is `#1d1d1f` and the second `#6e6e73`, both `28/32 600`.
- **Shelf cards:** 400 × 500, **18 px radius**, white on `#f5f5f7`, shadow
  `2px 4px 12px rgba(0,0,0,.08)`, with 20 px between cards. Inside is an eyebrow (`12/16 600`,
  `#b64400` on light and `#ff791b` on dark, e.g. "PRE-ORDER NOW"), a title `28/32 600`, copy
  `14/18 600` and the price `14/18 400`.
- **Value-prop cards:** a sentence in `24/28 600` where the key phrase is colored: blue
  `#007aff`, green `#008009`, red `#ff3333`, purple `#ac39ff` or slate blue `#3f6eb8`. A small
  colored line icon sits above it. This is the one place Apple uses several colors at once.
- Paddles: round grey buttons with a chevron at the shelf edge.

### 2.5 Buy flow (configurator)
- Two columns: a sticky gallery on the left (about 1045 wide, `#f5f5f7`, slight radius) and
  choices on the right (328 wide).
- **Option tiles:** 328 wide, `1px solid #86868b`, **12 px radius**, 15 px padding. The selected
  tile gets a 2 px blue `#0071e3` outline (convention).
- **Step headers in two tones:** "**Finish.** Pick your favorite."
- Swatches are 36 px circles with a 2 px ring when selected.
- **Help box:** `#f5f5f7`, 12 px radius, a bold question, plain answer text and a ⊕ icon.
- A sticky summary bar shows the price on the right in `14/600`.

### 2.6 Support
- A centered blue round Apple logo, the title `64/68 600`, then a subtitle `24/32 400`.
- A product icon row like the chapter nav.
- **Quick-link cards:** `#f5f5f7`, **18 px radius**, shadow `8px 8px 16px rgba(0,0,0,.08)`, a
  colored line icon on top and a blue `17/25` link label.
- **Search field:** 56 tall, 980 wide, 12 px radius, `1px solid #d2d2d7`, `rgba(255,255,255,.8)`
  background, 17 px text, with a magnifier inset 49 px.

### 2.7 Newsroom
- A local nav with "Newsroom" in `21/24 600`, small links and a "Search Newsroom" pill.
- "Latest News" `32/36 700`. The lead story card is image plus text side by side; others sit in a
  2-up grid with **32 px radius** cards. Eyebrows read "PRESS RELEASE" in `12–14 / 700` grey
  `#6e6e73`, headlines `21–24 / 700`, and dates `14–17 / 600` grey with a clock glyph.
- **Article:** a 653 px text column, eyebrow then date, a headline `48/52 700`, a dek `24/28 500`,
  then share icons. The body is `19/27 400 +0.228px` and links are blue. Media goes wider than the
  column.

### 2.8 Documentation (HIG / developer.apple.com)
- Three columns: a left sidebar (a filter field, then a disclosure tree in 14 px grey with small
  icons), a center column **740 px** wide, and a right "on this page" index in 12 px.
- H1 `48/52 700`, abstract about `24/30 400`, H2 `32/36 700`, body `17/25 400 -0.374px`, links
  blue. Tables use `14/20`, with 600 headers and grey cells.
- Topic tiles: 233 × 132, **18 px radius**, a solid colored gradient (green, yellow and so on)
  with a big white glyph, and the title below in 17/600.
- Docs follow the system appearance, so dark mode is real and complete.

### 2.9 Web apps (App Store, Apple Music)
These are the closest thing to app UI on the web. They use **iOS text styles exactly**, not
marketing type.
- **Sidebar** 260 wide. Music uses `rgba(38,38,40,.6)` with `saturate(2.2) blur(16px)`; the App
  Store uses a nearly flat `rgba(235,235,245,.03)` on `#1f1f1f`. Items are 32–36 tall, 14–15 px,
  with a 6–8 px radius. The selected item is `rgba(255,255,255,.05)` with semibold text. Section
  labels ("Library", "Playlists") are `12/15 600` at 64 % opacity.
- **Large title** `34/40 700`, section titles `17/22 700` with a "›", and rows `15/20` and
  `12/15`. Eyebrows are `11/14 600 UPPERCASE` at 64 %.
- **Text color uses opacity, not grey hex:** primary `rgba(255,255,255,.92)`, secondary `.64` (on
  dark).
- **App page:** a 230 px icon, the name `34/40 700`, the subtitle `17/22 700` at 60 %, and meta
  `13/16`. Then an **info strip** of columns, each with a label `11/14 600 caps .64`, a value
  `22/26 700` and a unit `11/16`.
- **Tinted pill button:** "View" or "Get" at 64 × 32, background `rgba(0,122,255,.08)`, text
  `#007aff` `13/700`, `radius 1000`, 7/16 padding.
- Artwork radius is small: **7–8 px** on album art, 10 on larger art. Search fields are 32 tall
  with a 4 px radius.
- A floating glass mini-player sits at the bottom center.

---

## 3. Typography

### 3.1 Families
| Family | Used for | Web stack |
| --- | --- | --- |
| **SF Pro Text** | Everything under 19 px on marketing pages, everything under 20 pt in apps | `"SF Pro Text", "SF Pro Icons", "Helvetica Neue", Helvetica, Arial, sans-serif` (apple.com) |
| **SF Pro Display** | 19 px and up on apple.com, 20 pt and up in apps | `"SF Pro Display", "SF Pro Icons", "Helvetica Neue", Helvetica, Arial, sans-serif` |
| SF Mono | Code | `"SF Mono", ui-monospace, Menlo, monospace` |
| New York | Editorial serif (Books, News); never seen on apple.com marketing | — |

Outside Apple devices, `-apple-system, BlinkMacSystemFont` resolves to SF only on Apple
platforms. On Windows, the closest system face is **Segoe UI Variable**: Text optical size under
20, Display at 20 and up. Don't substitute Inter or Helvetica unless the platform can't do better.

### 3.2 Weights
- **apple.com marketing:** 400 (body) and **600** (every headline). 500 appears rarely (a
  Newsroom dek, local nav links). Nothing lighter than 400. 700 isn't used for marketing
  headlines.
- **Docs, Newsroom, web apps:** headlines are **700**, body 400, labels 600.
- **Never use Thin or Light** for anything a person has to read.

### 3.3 The apple.com type scale (measured)

These are Apple's own `typography-*` classes, with computed values.

| Size / line | Weight | Tracking | Face | Apple's class names (examples) | Use |
| --- | --- | --- | --- | --- | --- |
| **96 / 100** | 600 | −1.44 px (−0.015 em) | Display | `ric-type-lockup-xxl-headline` | Once per page, giant lockups |
| **80 / 84** | 600 | −1.2 px (−0.015 em) | Display | `media-hero-headline-elevated`, Store page title | Hero headline |
| **64 / 68** | 600 | −0.576 px (−0.009 em) | Display | `ric-type-lockup-xl-headline`, Support title | Big lockups |
| **56 / 60** | 600 | −0.28 px (−0.005 em) | Display | homepage hero, `ps-headline`, `modal-header-headline` | Hero / tile headline |
| **48 / 52** | 600 (700 in docs) | −0.144 px (−0.003 em) | Display | `section-header-headline` | Section headers |
| **40 / 44** | 600 | normal | Display | homepage half tiles, `ps-stat-value` | Secondary tiles |
| **32 / 36** | 600 / 700 | +0.128 px (+0.004 em) | Display | `eyebrow-super`, docs H2 | Product eyebrow over hero |
| **28 / 32** | 600 (400 for subheads) | +0.196 px (+0.007 em) | Display | `media-card-gallery-headline`, `product-tile-headline`, shelf headers | Card titles, shelf titles |
| **24 / 28** | 600 | +0.216 px (+0.009 em) | Display | `label`, nav flyout links, value-prop cards | Large labels |
| **21 / 25** (29 for copy) | 400 / 600 | +0.231 px (+0.011 em) | Display | `callout-elevated`, `ps-intro`, lockup copy | Intro copy, tile subheads |
| **19 / 23** (27 for body) | 400 / 600 | +0.228 px (+0.012 em) | Display | `localnav-product-name`, Newsroom body | Lead copy, availability |
| **17 / 21** or **17 / 25** | 400 / 600 | −0.374 px (−0.022 em) | Text | `body`, `callout-base`, `feature-card-body`, buttons | Body text, buttons |
| **14 / 18** or **14 / 20** | 400 / 600 | −0.224 px (−0.016 em) | Text | `body-reduced`, `product-tile-price` | Secondary copy, prices |
| **12 / 16** | 400 / 600 | −0.12 px (−0.01 em) | Text | `caption`, `tout-copy`, global nav, footer | Captions, nav, legal |

Line-height rule: at 28 px and up, line height is size + 4 px. Body text gets about 1.47 (17/25) for
reading, or 1.24 (17/21) for UI.

### 3.4 Tracking rules
- **apple.com (web):**
  - 12–17 px Text is tightened: −0.01 to −0.022 em.
  - 19–32 px Display is opened slightly: +0.004 to +0.012 em.
  - 40 px is neutral.
  - 48 px and up is tightened, increasingly: −0.003 em at 48, up to −0.015 em at 80–96.
- **Apps (HIG, SF Pro):** tracking in pt is +0.06 at 11, 0 at 12, −0.08 at 13, −0.23 at 15,
  **−0.43 at 17**, −0.45 at 20, −0.26 at 22, +0.07 at 24, +0.38 at 28, +0.40 at 34, +0.35 at 48 and
  +0.22 at 64 (`typography.md › Tracking values`). In a native app the system applies this for
  you. On the web or with another font, apply it by hand.

### 3.5 App text styles (iOS default "Large" size, from the HIG)
| Style | Size / line (pt) | Weight |
| --- | --- | --- |
| Large Title | 34 / 41 | Regular (Bold when emphasized; web apps use Bold) |
| Title 1 | 28 / 34 | Regular / Bold |
| Title 2 | 22 / 28 | Regular / Bold |
| Title 3 | 20 / 25 | Regular / Semibold |
| Headline | 17 / 22 | Semibold |
| Body | 17 / 22 | Regular |
| Callout | 16 / 21 | Regular |
| Subheadline | 15 / 20 | Regular |
| Footnote | 13 / 18 | Regular |
| Caption 1 | 12 / 16 | Regular |
| Caption 2 | 11 / 13 | Regular |

On macOS, Body is 13/16, Large Title 26/32, Title 1 22/26, Title 2 17/22, Title 3 15/20,
Headline 13/16 Bold, Callout 12/15, Subheadline 11/14, and Footnote and Captions 10/13. The
minimum is 11 pt on iOS and 10 pt on macOS; the defaults are 17 pt and 13 pt.

### 3.6 Type patterns Apple repeats
- **Eyebrow + headline + subhead:** a small product name (32 or 21) over a huge claim (80 or
  96), over one plain sentence (21–28).
- **Two-tone headline:** "**The latest.** Take a look at what's new." Same size and weight; the
  second sentence is in secondary grey. Used for shelves and buy-flow steps.
- **Headlines end with a period** when they're a sentence ("Pro further.", "Explore the
  lineup."). Product names don't ("iPhone 18 Pro").
- **Headlines are centered** on tiles and heroes, and left-aligned in sections, docs, the Store
  and articles.
- **Multicolor emphasis** only in value-prop sentences (§2.4).
- **Footnote markers** are superscript numbers or symbols (¹ ² ‡ ◊ ※), with the legal text in 12 px
  at the bottom.
- `text-wrap: balance`-style breaks: headlines are hand-broken into even lines.

---

## 4. Color

### 4.1 apple.com neutrals (measured)
| Value | Role |
| --- | --- |
| `#ffffff` | Light tiles, cards, page |
| `#f5f5f7` | The Apple grey: alternating tiles, footer, Store page, support cards, ribbons |
| `#e8e8ed` | Segmented control track, quiet fills |
| `#d2d2d7` | Field borders, hairlines, captions on dark |
| `#86868b` | Tertiary text: "Available starting…", nav flyout headers, option borders. **3.6:1 on white, so only use it at 19 px and up or for non-essential text** |
| `#6e6e73` | Secondary text on light: footnotes, two-tone headline tails, prices (5.1:1) |
| `#333336` | Nav flyout links on light |
| `#1d1d1f` | **Primary text on light.** Also the dark section background |
| `#000000` | Dark hero tiles, product-page body |
| `#f5f5f7` / `#e8e8ed` | Primary text and nav link text on dark |
| `rgba(0,0,0,.56 / .72 / .88)` | Footer text tiers: body, links, headers |
| `rgba(255,255,255,.92 / .64)` | Primary and secondary text on dark in web apps and gallery cards |

### 4.2 Action and accent colors (measured)
| Value | Role |
| --- | --- |
| `#0071e3` | **Primary button fill** (white text, 4.7:1) |
| `#0066cc` | **Links and outlined buttons on light** (5.6:1 on white) |
| `#2997ff` | **Links and outlined buttons on dark** (7.0:1 on black) |
| `#007aff` | Tinted pills in web apps (`rgba(0,122,255,.08)` background) and system blue |
| `#b64400` | "New" / "Pre-order now" eyebrows on light |
| `#ff791b` | The same eyebrows on dark |
| `#00d959` | Environment's own CTA (section branding is allowed in the local nav only) |

**Rule:** blue means "you can act on this". Orange means "new". Nothing else in the chrome has
color.

### 4.3 System colors for app UI (published UIKit values)
| Color | Light | Dark |
| --- | --- | --- |
| Red | `#FF3B30` | `#FF453A` |
| Orange | `#FF9500` | `#FF9F0A` |
| Yellow | `#FFCC00` | `#FFD60A` |
| Green | `#34C759` | `#30D158` |
| Mint | `#00C7BE` | `#63E6E2` |
| Teal | `#30B0C7` | `#40C8E0` |
| Cyan | `#32ADE6` | `#64D2FF` |
| Blue | `#007AFF` | `#0A84FF` |
| Indigo | `#5856D6` | `#5E5CE6` |
| Purple | `#AF52DE` | `#BF5AF2` |
| Pink | `#FF2D55` | `#FF375F` |
| Brown | `#A2845E` | `#AC8E68` |
| Gray | `#8E8E93` | `#8E8E93` |
| Gray 2–6 | `#AEAEB2 #C7C7CC #D1D1D6 #E5E5EA #F2F2F7` | `#636366 #48484A #3A3A3C #2C2C2E #1C1C1E` |

Semantic roles (published):

| Role | Light | Dark |
| --- | --- | --- |
| label | `#000000` | `#FFFFFF` |
| secondaryLabel | `rgba(60,60,67,.6)` | `rgba(235,235,245,.6)` |
| tertiaryLabel | `rgba(60,60,67,.3)` | `rgba(235,235,245,.3)` |
| quaternaryLabel | `rgba(60,60,67,.18)` | `rgba(235,235,245,.16)` |
| systemBackground / secondary / tertiary | `#FFFFFF / #F2F2F7 / #FFFFFF` | `#000000 / #1C1C1E / #2C2C2E` |
| groupedBackground / secondary / tertiary | `#F2F2F7 / #FFFFFF / #F2F2F7` | `#000000 / #1C1C1E / #2C2C2E` |
| separator / opaqueSeparator | `rgba(60,60,67,.29)` / `#C6C6C8` | `rgba(84,84,88,.6)` / `#38383A` |
| fill / secondary / tertiary / quaternary | `rgba(120,120,128,.2/.16/.12/.08)` | `rgba(120,120,128,.36/.32/.24/.18)` |

Use the **semantic role**, not the hex value. Dark mode is a separate palette, not an inversion:
dark colors are brighter and slightly desaturated so they hold contrast on black.

### 4.4 Color rules
- One accent per product. Apple's own is blue. Apply it to text and the primary button fill
  only (`branding.md`: "Apply your app's accent color judiciously").
- Status colors (red, green, yellow) mean status. Never use them for decoration.
- Gradients are for **artwork** (topic tiles, product photography, wallpapers), never for
  buttons, bars or text backgrounds.
- Dark sections: `#000` for heroes and media, `#1d1d1f` for content sections, `#2a2a2d` at 72 %
  for floating controls.

---

## 5. Layout and spacing

### 5.1 Widths (measured at 1920)
| Width | What |
| --- | --- |
| 980 | Classic content width: footer, support search, nav content (1024 including 22 px side padding) |
| 1024 | Floating local nav |
| 1260 | Product-page media galleries |
| 740 | Docs text column |
| 653 | Newsroom article text column |
| 280–340 | Centered text blocks inside homepage tiles |
| Full bleed | Homepage tiles, heroes, ribbons, media |

### 5.2 Breakpoints (apple.com convention)
`xlarge ≥ 1441` · `large 1069–1440` · `medium 735–1068` · `small ≤ 734`. Type steps down
one size per breakpoint (e.g. hero 80 → 64 → 48 → 40). Content width goes 980 → 692 → 87.5 %
of the viewport. Side padding is never under 16 px. Half-tiles stack to one column at small.

### 5.3 Vertical rhythm (measured)
| Gap | Where |
| --- | --- |
| 144 | Section padding top/bottom, product page |
| 120 | Section padding, family page |
| 56 | Homepage tile top padding |
| 32–40 | Header to content |
| 20 | Between Store cards |
| 12 | Between homepage tiles, between CTA buttons |
| 8 | Headline to subhead |

The spacing scale in use is 4, 8, 12, 16, 20, 24, 32, 40, 56, 80, 120 and 144. Anything else is
a smell.

### 5.4 Alignment
- Marketing tiles and heroes are centered. Everything that is read (docs, articles, Store
  shelves, configurators) is left-aligned.
- Shelves bleed off the right edge to show they scroll, with the first card aligned to the
  content column.
- Two-column layouts put the gallery left and the decisions right (buy flow), or the content
  center and the index right (docs).

---

## 6. Shape

| Radius | Where (measured) |
| --- | --- |
| **980px** (capsule) | Every CTA button, local nav CTA, segmented control (999), tinted "View/Get" pills (1000) |
| **32** | Newsroom cards |
| **28** | Product-page media and feature cards |
| **18** | Store shelf cards, Support quick-link cards, HIG topic tiles, floating local nav |
| **12** | Configurator options, help boxes, search fields |
| **8** | Small rectangular buttons ("Continue"), sidebar selection in Music |
| **6–7** | Sidebar items, album artwork |
| **50 %** | Color swatches, round paddles, play/pause, avatar-like icons |
| **0** | Full-bleed homepage tiles and heroes |

Inner radius equals outer radius minus padding. App icons use Apple's own continuous corners
(a superellipse), never a CSS circle-arc rectangle, when shown large.

---

## 7. Elevation and materials

### 7.1 Shadows (measured; rare and soft)
| Shadow | Where |
| --- | --- |
| `2px 4px 12px rgba(0,0,0,.08)` | Store cards |
| `8px 8px 16px rgba(0,0,0,.08)` | Support quick-link cards |
| `inset 0 0 0.5px rgba(0,0,0,.11)` | Hairline edge on product-page cards |
| `0 0 0 1px rgb(40,40,40)` | Ring around the dark floating local nav |

Heroes, tiles and dark cards have **no shadow**. Depth comes from color, not from shadows.

### 7.2 Glass (measured)
| Surface | Background | Filter |
| --- | --- | --- |
| Global nav (dark) | `rgba(29,29,31,.8)`, or lighter over light pages | `saturate(1.8) blur(20px)` |
| Local nav (light) | `rgba(250,250,252,.8)` | `saturate(1.8) blur(20px)` |
| Floating local nav (dark) | `rgba(18,18,18,.8)` + 1 px ring | `blur(20px)` |
| Media controls (dots, play, paddles) | `rgba(42,42,45,.72)` | blur |
| Apple Music sidebar | `rgba(38,38,40,.6)` | `saturate(2.2) blur(16px)` |

Rules (`liquid-glass.md`):
- Glass belongs to the **functional layer** (bars, sidebars, controls), never to content.
- Saturate the blur (1.4–2.2×) so color shows through.
- Add a hairline edge.
- Fall back to solid when Reduce Transparency is on.

---

## 8. Components

### 8.1 Global nav
- 44 tall, full width, glass. Content is 1024 wide with 22 px padding. The Apple logo sits at
  the left, then about 11 evenly spaced links, then search and bag icons at the right.
- Links are `12/12`, 400, −0.12 px, `rgba(255,255,255,.8)`, going to 100 % on hover, with 8 px
  side padding.
- **Flyout:** opens on hover as a full-width panel in the nav color. Columns have 12 px grey
  headers ("Explore Mac") over big links in `24/28 600 #333336` (or `#e8e8ed` on dark) in the
  first column, and 12/600 links in the others. The page behind it blurs.

### 8.2 Local nav
- 52 tall and sticky under the global nav, in glass. The page title is on the left (`21/24 600`,
  or 19 on product pages). On the right are 12 px links and one CTA pill (12 px, 3/10 padding,
  capsule, blue fill or brand color).
- The 2026 product style floats as a rounded 18 px capsule instead of a full-width bar.

### 8.3 Buttons
| Variant | Spec (measured) |
| --- | --- |
| **Primary** | `#0071e3` fill, white text, `17px/400/-0.374px`, padding `11px 21px`, height 44, capsule |
| **Secondary** | Transparent, `1px solid` + text `#0066cc` (light) or `#2997ff` (dark), same size |
| **Small** | Same, `14px`, padding `8px 15px`, height 36 |
| **Tinted** (apps) | `rgba(0,122,255,.08)` fill, `#007aff` text, `13px/700`, 32 tall, capsule |
| **Neutral rect** | `#f5f5f7` fill, black `14px`, `8px 15px`, **8 px radius** (banners, "Continue") |
| **Text link** | Blue, no underline, with a trailing chevron "›" or ↗ for external links. Underline on hover |

Buttons usually come as a primary + secondary pair with a 12–16 px gap ("Learn more" + "Buy").
At most one filled button per group. Labels are sentence case and verbs or verb phrases.

### 8.4 Segmented pill control
The track is `#e8e8ed`, `radius 999`, 6 px padding and 56 tall. Segments are 44 tall at
`17/20 400`. The selected segment is a `#1d1d1f` pill with white text. Use it to filter one
collection. In apps, use the system segmented control instead (white thumb on a grey track).

### 8.5 Chapter nav
A horizontal row of product icons 64 px tall, each with a 14 px label at 80 % black and an
optional orange "New" in 12/600. It scrolls sideways with a paddle and ends with "Compare",
"Help Me Choose" and "Accessories". The whole row is 168 tall.

### 8.6 Cards
| Card | Spec |
| --- | --- |
| Store shelf card | 400 × 500, r18, white, `2px 4px 12px rgba(0,0,0,.08)`. Eyebrow, title 28/32, copy 14 |
| Feature / gallery card | Width of the column, r28, image or video fill, headline 28/32 600 in white at 92 % on top |
| Support link card | 311 × 181, r18, `#f5f5f7`, soft shadow, colored line icon, blue 17 px label |
| Lineup card | Image, color dots, name 28/32, tagline 17/21 600, price 14, Learn more + Buy › |
| Newsroom card | r32, image on top or left, caps eyebrow, 21–24/700 headline, date |
| Doc topic tile | 233 × 132, r18, gradient with white glyph, title below |

Cards are clickable as a whole. They never have visible borders on light backgrounds; the
grey page against a white card is the border.

### 8.7 Forms
- **Search:** 56 tall, r12, `1px solid #d2d2d7`, white at 80 %, 17 px, magnifier icon inside.
- **Option tile (radio as a card):** `1px solid #86868b`, r12, 15 px padding, with name and
  price on one line. The selected tile gets a 2 px blue outline.
- **Swatch:** 36 px circle, with a 2 px ring and 2 px gap when selected, and the color name shown
  as text.
- **Help box:** `#f5f5f7`, r12, a ⊕ to expand, opening as a modal sheet.
- In apps, use grouped inset lists (`groupedBackground`, 10–12 px radius, inset separators,
  44 pt rows).

### 8.8 Gallery controls
A dot pagination capsule (glass) with the active dot stretched into a pill, plus a separate
round play/pause button. Autoplaying media **always** has a visible pause control.

### 8.9 Ribbon
A full-width 52 px strip under the nav with `14/20` text, one sentence and an inline link with
›. It's `#f5f5f7` on light pages, or blue or black for campaigns.

### 8.10 Footer
`#f5f5f7` with a 980 px column. All text is 12/16: body at 56 % black, links at 72 %, column
headers at 88 %. There's a breadcrumb on top, about 5 link columns, then legal, the country
selector and the copyright. A thin `#d2d2d7` line separates the parts.

### 8.11 App sidebar (web apps, macOS)
260 wide and glass. Items are 32–36 tall with a 16 px icon in the accent color, 14–15 px text and
a 6–8 radius. Selection is a subtle fill, not a color block. Section headers are 11–12 px
semibold at 64 %, and the account sits at the bottom.

### 8.12 Info strip (App Store)
A horizontally scrolling row of columns separated by hairlines. Each column has a caps label
(11/600, 64 %), a big value (22/700) and a small unit line (11).

---

## 9. Iconography
- **SF Symbols**: line icons that match the weight of adjacent text and sit on its baseline. On
  the web, use inline SVG with about a 1.5 px stroke and round caps.
- Multicolor icons (green truck, blue bag, red card) appear only in value-prop and support cards.
  Chrome icons (search, bag, share, chevrons) are monochrome.
- Chevrons are text: "›" after links, "↗" for leaving the site, "⊕" for "open more".
- No emoji in UI.
- The Apple logo appears once, top-left in the nav (and in "Watch" style wordmarks).
- Product images do the job of icons in the chapter nav and support rows.

## 10. Imagery and media
- Products are shot on pure black, white or `#f5f5f7`, matching the tile color exactly so the
  photo has no visible edge.
- Hands holding devices, and real people in lifestyle shots. The device is always the subject.
- Images are often the headline itself (custom lettering as a `<picture>` with real text for
  screen readers: `aria-hidden` on the image, visually hidden text beside it).
- Video autoplays muted, loops quietly and always has a pause button.
- Media is 2× resolution, with art direction per breakpoint (different crops, not just scaling).

## 11. Motion
- **Scroll is the timeline.** Sections fade or slide in as they enter (the Environment page is
  blank until scrolled), media pins while copy scrolls past, and numbers count up.
- **Quiet and short** for interface: hover and press take about 200–300 ms with an ease-out;
  nav flyouts take about 300 ms.
- **Long and cinematic** for storytelling only: hero reveals and product rotations.
- `prefers-reduced-motion` turns reveals into static content, stops autoplay and removes
  parallax.
- Content must never be hidden waiting for an animation when motion is off.

## 12. Writing
- **Headlines:** two to four words, confident, often wordplay ("Pro further.", "Hello.",
  "Allllll that."). End with a period.
- **Subheads:** one plain, factual line under the clever one ("14-inch model now with M6.").
- **CTAs:** sentence case verbs: "Learn more", "Buy", "Pre-order", "Shop", "Watch the film".
  Pair "Learn more" (filled) with "Buy" (outline or link ›).
- **Shelf and step headers:** two sentences in two tones ("Finish. Pick your favorite.").
- **Docs and apps:** title-style capitalization for buttons and menu items (HIG), plain sentences
  in body text, direct second person ("you").
- Numbers and units: "6.3-inch", "From $1199 or $49.95/mo. for 24 mo.", "Available starting
  11.6". Footnotes carry the legal detail so headlines stay clean.

## 13. Accessibility (measured contrast)
| Pair | Ratio | Verdict |
| --- | --- | --- |
| `#1d1d1f` on `#ffffff` | 16.8:1 | ✓ |
| `#6e6e73` on `#ffffff` | 5.1:1 | ✓ body text |
| `#86868b` on `#ffffff` | 3.6:1 | Large text (19 px+) or non-essential only |
| White on `#0071e3` | 4.7:1 | ✓ |
| `#0066cc` on `#ffffff` | 5.6:1 | ✓ |
| `#2997ff` on `#000000` | 7.0:1 | ✓ |
| `#b64400` on `#ffffff` | 5.5:1 | ✓ small caps eyebrow |

- Every button is at least 36 px tall on the web (44 preferred); 44 pt on iOS, 28 pt on macOS.
- Headlines drawn as images carry real text for assistive tech.
- Autoplay has a pause, and reduced motion and reduced transparency are honored.
- Color never carries meaning alone: "New" is a word, the selection is a ring plus a state change,
  and prices are text.

## 14. Implementation kit (CSS)

```css
:root {
  /* type */
  --font-text: "SF Pro Text", -apple-system, BlinkMacSystemFont, "Segoe UI Variable Text", "Helvetica Neue", Helvetica, Arial, sans-serif;
  --font-display: "SF Pro Display", -apple-system, BlinkMacSystemFont, "Segoe UI Variable Display", "Helvetica Neue", Helvetica, Arial, sans-serif;
  --font-mono: "SF Mono", ui-monospace, Menlo, Consolas, monospace;

  /* neutrals (light) */
  --white: #ffffff;  --grey-bg: #f5f5f7;  --grey-fill: #e8e8ed;  --grey-line: #d2d2d7;
  --text: #1d1d1f;   --text-2: #6e6e73;   --text-3: #86868b;
  /* action */
  --blue-fill: #0071e3;  --link: #0066cc;  --new: #b64400;
  /* shape */
  --r-pill: 980px; --r-card-lg: 28px; --r-card: 18px; --r-field: 12px; --r-small: 8px;
  /* glass */
  --glass-light: rgba(250, 250, 252, .8);
  --glass-dark: rgba(29, 29, 31, .8);
  --glass-filter: saturate(180%) blur(20px);
  --shadow-card: 2px 4px 12px rgba(0, 0, 0, .08);
}
.dark { --text: #f5f5f7; --text-2: #86868b; --link: #2997ff; --new: #ff791b; background: #000; color: var(--text); }

/* type scale: size / line-height / weight / tracking */
.t-hero      { font: 600 80px/84px var(--font-display); letter-spacing: -0.015em; }
.t-headline  { font: 600 56px/60px var(--font-display); letter-spacing: -0.005em; }
.t-section   { font: 600 48px/52px var(--font-display); letter-spacing: -0.003em; }
.t-tile      { font: 600 40px/44px var(--font-display); letter-spacing: 0; }
.t-eyebrow   { font: 600 32px/36px var(--font-display); letter-spacing: 0.004em; }
.t-card      { font: 600 28px/32px var(--font-display); letter-spacing: 0.007em; }
.t-sub       { font: 400 28px/32px var(--font-display); letter-spacing: 0.007em; }
.t-intro     { font: 400 21px/25px var(--font-display); letter-spacing: 0.011em; }
.t-lead      { font: 400 19px/23px var(--font-display); letter-spacing: 0.012em; }
.t-body      { font: 400 17px/25px var(--font-text);    letter-spacing: -0.022em; }
.t-body-ui   { font: 400 17px/21px var(--font-text);    letter-spacing: -0.022em; }
.t-reduced   { font: 400 14px/20px var(--font-text);    letter-spacing: -0.016em; }
.t-caption   { font: 400 12px/16px var(--font-text);    letter-spacing: -0.01em; }
.t-new       { font: 600 12px/16px var(--font-text);    letter-spacing: -0.01em; color: var(--new); }

/* buttons */
.button { display: inline-flex; align-items: center; justify-content: center; min-height: 44px;
  padding: 11px 21px; border-radius: var(--r-pill); font: 400 17px/1.18 var(--font-text);
  letter-spacing: -0.022em; border: 1px solid transparent; cursor: pointer; }
.button-primary   { background: var(--blue-fill); color: #fff; }
.button-secondary { background: transparent; color: var(--link); border-color: var(--link); }
.button-small     { min-height: 36px; padding: 8px 15px; font-size: 14px; letter-spacing: -0.016em; }
.button:hover     { filter: brightness(1.08); }
.link-more::after { content: " ›"; }

/* nav */
.globalnav { position: sticky; top: 0; height: 44px; background: var(--glass-dark);
  backdrop-filter: var(--glass-filter); -webkit-backdrop-filter: var(--glass-filter); }
.globalnav a { font: 400 12px/44px var(--font-text); letter-spacing: -0.01em; color: rgba(255,255,255,.8); padding: 0 8px; }
.globalnav a:hover { color: #fff; }
@media (prefers-reduced-transparency: reduce) { .globalnav { background: #1d1d1f; backdrop-filter: none; } }

/* cards and sections */
.section { padding: 144px 0; }
.content { max-width: 980px; margin: 0 auto; padding: 0 22px; }
.card { background: #fff; border-radius: var(--r-card); box-shadow: var(--shadow-card); overflow: hidden; }
.media-card { border-radius: var(--r-card-lg); overflow: hidden; }

@media (max-width: 1068px) { .t-hero { font-size: 64px; line-height: 68px; } .section { padding: 120px 0; } .content { max-width: 692px; } }
@media (max-width: 734px)  { .t-hero { font-size: 48px; line-height: 52px; } .section { padding: 80px 0; } .content { max-width: 87.5%; } }
```

## 15. Checklist

Before you call something "Apple":

1. Is the product or content the biggest thing on screen, with the chrome grey and small?
2. Is the type SF (or the platform's system face), weights only 400 and 600 (700 in docs and
   apps), with sizes from the scale in §3.3 and tracking from §3.4?
3. Is blue the only action color, and does orange appear only as "New"?
4. Are buttons capsules, 44 tall (36 small), with one filled button per group?
5. Do cards use 18 or 28 radius with no border, and almost no shadow?
6. Is glass only on bars, sidebars and floating controls, with a solid fallback?
7. Is there 120–144 px around sections and are text columns 980 px or narrower?
8. Are headlines short sentences with a period, with a plain subhead under them?
9. Does it work in light and dark (docs and apps), at 734 px, with reduced motion and
   transparency, and at 4.5:1 contrast for body text?
10. Remove one thing. Apple pages always have less than you think.

## 16. Anti-patterns (things Apple never does)
- Gradient buttons, colored nav bars, or drop shadows on text.
- More than one accent color in the chrome, or blue used for decoration.
- Bold (700+) marketing headlines, condensed fonts, all-caps headlines (caps only on tiny
  eyebrows in apps and News).
- Borders around cards on light backgrounds, or heavy shadows.
- Glass on cards, list rows or text blocks.
- Icons with mixed stroke weights, or emoji in UI.
- Dense pages: many CTAs per tile, paragraphs in heroes, text columns wider than 980 px.
- Animations that hide content, autoplay without a pause, or scroll-jacking that ignores
  reduced motion.

---

## 17. Desk Widgets

How sections 1 to 16 apply to this app. Code: colors, type and shape for the app's own UI live in
`src/Theme.cs`; widget colors live in the palettes in `src/Widgets/CardWidget.cs`. Don't put hex
values anywhere else.

### 17.1 Surfaces
| Surface | Model | Theme |
| --- | --- | --- |
| Library window | Apple web apps (§2.9): sidebar, Large Title, cards | Follows Windows light/dark |
| Right-click menus | macOS context menu (17.4) | Follows Windows light/dark |
| Alerts and input sheets | macOS alert / sheet (17.5) | Follows Windows light/dark |
| Widgets | Apple's desktop widgets | Each widget's own Light / Dark / Vibrant setting |
| Website | apple.com and App Store web (§2, §8) | `prefers-color-scheme` |

Windows draws only the title bar and the scrollbars, in its light or dark style to match.

### 17.2 Color
- **Blue is the only action color**: links, the default button (`#0071e3`, white text), the
  selected menu item, sidebar icons and switches that are on. Link text is `#0066cc` light,
  `#2997ff` dark.
- Neutrals: window `#ffffff` / `#1f1f1f`; sidebar and cards `#f5f5f7` / `#262628` and `#2c2c2e`;
  card hover `#e8e8ed` / `#3a3a3c`. Text `#1d1d1f` / white at 92 %; secondary `#6e6e73` / white at
  64 %. Grey button fill and separators use the system `fill` and `separator` roles (§4.3).
- Destructive text: `#d70015` light, `#ff453a` dark.
- Inside widgets, color is content: system colors (§4.3) for data and status, the way Apple's own
  widgets use orange for the Clock second hand and the Timer ring. Never in chrome.
- No gradients in the app's chrome (§4.4). Widget previews and wallpapers are artwork.

### 17.3 Type and shape
- Segoe UI Variable Text under 20 px and Display from 20 px (Segoe UI on Windows 10). Weights
  400, 600 and 700 (titles) only.
- Library: Large Title 34/700, section titles 17/700 with "›", card names 15/600, everything
  secondary 13 or 12. Sidebar rows 14, 600 when selected; group labels 12/600 secondary.
- Radii: cards 18, grouped lists 12, fields and sidebar rows 8, menu highlight 6, menus 10, every
  button a capsule.
- Sizes: sidebar 260 wide with 34 px rows; content padding 32; grid gap 20; cards 176 × 240
  (double width for medium widgets).

### 17.4 Right-click menus
Drawn by the app (`PopupMenu`), never the Windows menu. A 10 px-radius panel, near-opaque
(`#f6f6f8` / `#2a2a2d` at 98 %) with a hairline edge and a soft shadow, 5 px padding, 24 px rows,
14 px text with a 26 px left column for checkmarks and a chevron for submenus. The hovered or
keyboard-selected row is a blue 6 px-radius fill with white text. Separators are inset hairlines.
Behavior follows the HIG (`context-menus.md`, `menus.md`):
- Title-style labels, an ellipsis when more input follows ("Set Location…").
- At most about three groups: the widget's own actions, then window options (Lock Position,
  Opacity), then Open Desk Widgets, then the destructive item last ("Remove Widget", in red).
- Hide items that don't apply; never dim them. Submenus one level deep, about five items.
- Keyboard: arrows, Return, Escape. Submenus open after 200 ms of hover.

### 17.5 Alerts and sheets
- **Alert**: 340 wide, centered: the 48 px app icon, a 17/600 title, the message in 13, optional
  facts as a right-aligned label column and a left-aligned value column, an optional "Show
  Source" link, then capsule buttons side by side with Cancel on the left.
- **Default button**: the blue one, and it's the safe choice. For risky actions (install,
  uninstall) Cancel is blue and default; the action is grey, or grey with red text when
  destructive.
- **Input sheet**: 420 wide, left-aligned: 17/600 title, a secondary hint, 13 px labels over
  rounded fields (8 px radius, hairline border, blue 2 px when focused), then Cancel and the blue
  default button on the right.

### 17.6 Buttons
Capsules, 32 px tall in the app (36–44 on the web, §8.3), 13/600 text, at least 76 wide:
**Primary** blue fill; **Tinted** blue at 8 % with blue text (secondary actions like "Install
from File…" and "Try Again"); **Grey** `fill` with label text; **Destructive** `fill` with red
text. One Primary per group.

### 17.7 Library pages for community widgets
- **Explore**: Large Title "Explore", a Tinted "Upload a Widget ↗" at the top right, then the
  same 176 × 240 cards with the preview, name, "author · category" and a Tinted button at the
  bottom right: **Get**, **Update** when a newer version than yours is out, **Add** when you have it.
- **Installed**: "Install from File…" at the top right; first a grouped list of updates
  ("Moon Phase 2 is available", **Update**), then the cards, then a grouped list of turned-off
  widgets (**Try Again**). Show Source and Uninstall… (red, last) are on right-click.
- Empty and loading states: a centered 22/700 title and one 13 px line, both secondary ("No
  Installed Widgets", "Loading…", "Explore Couldn’t Load").

### 17.8 Website
`docs/assets/site.css` implements §14 with the light and dark tokens of §4: the 44 px glass nav,
a 980 px column (1024 with 22 px gutters), a 56/60 hero with a two-tone intro, the pill segmented
filter, tiles at r18 with a Tinted **Get**, an App Store-style widget page (info strip of Version,
Category, Reviewed, Size, Needs) and grouped lists for uploads. Everything people upload reaches
the page as text, never as HTML.

### 17.9 Writing
Buttons and menu items use title style ("Add to Desktop", "Show Source", "Remove Widget");
descriptions and messages are plain sentences. Errors say what happened and what to do.

### 17.10 Widget sizes and data colors
- Sizes are Apple's: small 170 × 170 pt, medium 364 × 170, large 364 × 382 (`CardWidget.CardSize`),
  22 pt corners and 16 pt margins. In the library a large widget's preview shrinks into a normal card.
- Colors a data source sends are content, not styling: F1 team colors (OpenF1 and F1's feed send
  them; `F1.TeamColor` is the fallback table). Show them only as a small bar or dot beside the name
  they belong to, and as white in Vibrant like every other accent.
- Tyre compounds are a ring with the letter inside (S, M, H, I, W) in the palette's red, yellow,
  label color, green and blue, so the color never stands alone.

