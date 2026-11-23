# Accessibility

How Gadfly Exams stands against WCAG 2.2 level AA, what was checked, what was fixed and how to check it again.
The public statement for visitors is `/accessibility` (`Gadfly.Server/Components/Pages/Site/Accessibility.razor`); this
file is the engineering record behind it.

## Target

**WCAG 2.2 level AA** for every page Gadfly renders: the public site (landing, how it works, science, pricing,
security, contact, legal, 404), the help center, the sign-in and account pages Gadfly styles (the forms themselves come
from MindAttic.Authentication), the student pages (My exams, get ready / start exam, practice, results, notifications)
and the staff pages (admin console, admin desk, assessments, exams, sources, review, grading, analytics, members).

We do **not** claim conformance. Automated checks and code review find only part of the barriers; nobody has yet tested
the app with a screen reader, switch access or voice control. See [Known gaps](#known-gaps).

## Shared tokens

All colours and spacing come from custom properties in `Gadfly.Server/wwwroot/app.css` (the public site adds its own
palette on `.site` in `site.css`). Fix contrast or spacing at the token, not in a single rule.

**Spacing scale (GAD-T507).** `--space-1` to `--space-8`: .25, .5, .75, 1, 1.5, 2, 3, 4 rem.
`--gutter` is the page's side padding, `clamp(1rem, 4vw, 2.5rem)`: 16px on a phone, growing to 40px; the public site's
`.site --gutter` is the same value. `--content-max` (1100px) is the app shell's widest content. `--target-min` (24px)
is the smallest pointer target for an icon-sized control (WCAG 2.5.8). New CSS uses these instead of ad-hoc values.

Applied: the top bar, `main`, the bare sign-in layout and the mock banner use `--gutter`; panels, cards, stat tiles,
dialogs, help results and practice boxes use the scale for padding and gaps; `.cards`, `.grid`, `.table-scroll` and
`.stat-tiles` leave `--space-5` below them unless they end their container, so stacked sections never touch; the
sign-in library's forms get one field per row with a `--space-4` gap (the password label used to sit flush against the
username box).

### Design system (GAD-T689)

One system in `app.css` for every app page (the public site keeps `site.css`). Page passes compose these classes and
tokens; a page stylesheet adds only what is specific to that page, using the tokens, never new sizes or colours.
Bases that pages may need to override are written with `:where()` (zero specificity), so any page class wins.

**Tokens**

| Group | Tokens | Use |
| --- | --- | --- |
| Type | `--text-xs` .8, `--text-sm` .9, `--text-base` 1, `--text-lg` 1.1, `--text-xl` 1.25 rem, `--text-2xl` fluid 1.4-1.6rem; `--leading` 1.5, `--leading-tight` 1.25; `--measure` 68ch | h1 2xl, h2 xl, h3 lg, h4 base; hints and table cells sm; badges xs; prose width |
| Headings | h1 `0 0 --space-4`; h2 `--space-5 0 --space-3`; h3 `--space-5 0 --space-2`; h4 `--space-4 0 --space-2` | a heading that is its container's first child has no top margin |
| Space | `--space-1`..`--space-8`, `--gutter`, `--content-max` | as above |
| Radius | `--radius-sm` 6px (controls), `--radius-md` = `--radius` 10px (panels, cards), `--radius-lg` 14px (dialogs), `--radius-pill` | |
| Border | `--border` (decorative, `--line`), `--border-strong` (a control's only boundary, 3:1) | |
| Elevation | `--shadow-1` (resting card), `--shadow-2` (toast, dropped menu), `--shadow-3` (dialog) | |
| Focus | `--focus-color` (`--accent`), `--focus-width` 3px, `--focus-offset` 2px | the global `:focus-visible` ring on every link, button, input, select, textarea, summary and `[tabindex]`; never remove it, only change its offset |
| Targets | `--target-min` 24px (WCAG 2.5.8 floor), `--target-touch` 44px | buttons, inputs, selects and top-bar links are at least `--target-min`, and `--target-touch` under `(pointer: coarse)` |
| Status | `--info`, `--ok`, `--warn`, `--error` and their `--*-tint` backgrounds; `--accent-tint`; `--danger-ink` (text on `--error`) | notices, badges, danger buttons |

**Breakpoints.** Custom properties can't be used in media queries, so these are the fixed values: phone below
`40rem` (640px; write `max-width: 39.99rem` / `min-width: 40rem`), tablet `40rem`-`64rem`, desktop from `64rem`
(1024px). `.grid` tables start scrolling sideways below 900px. Every page must work at 320px with no horizontal page
scroll (WCAG 1.4.10): only a `.table-scroll` box (or a `.grid` table) may scroll sideways.

**Layout primitives**

| Class | What it is |
| --- | --- |
| `.page-header` + `.page-header-actions` | the title row: h1 left, actions right, actions wrap under the title on a phone; `--space-5` below |
| `.page-section` | one titled block of a page, `--space-6` apart (`h2.section` on admin pages is a heading style, not this) |
| `.stack` (`.stack-sm`, `.stack-lg`, or set `--stack-gap`) | vertical rhythm: children `--space-4` apart, their own block margins dropped |
| `.cluster` (`--cluster-gap`) | a wrapping row of buttons, badges or filters |
| `.auto-grid` (`--grid-min`, default 15rem; `--grid-gap`) | a card grid: as many columns as fit, one on a phone. `.grid` is the data table and `.cards` the older card list; both keep working |
| `.prose` | text at `--measure` |
| `.table-scroll` | wraps a wide table so it, not the page, scrolls; `js/scroll-regions.js` adds `tabindex="0"`, `role="region"` and a name while it overflows, so pages don't hand-write those |
| `.visually-hidden` | text for screen readers only |
| `.skip` / `.skip-link` | skip-to-content link, the first focusable thing in a layout |

**Components**

- *Buttons.* `<button>` is primary (accent fill). `.secondary` (outlined), `.danger` (destructive, `--error` fill),
  `.ghost` (quiet, `--border-strong`), `.link` (inline text). `.button` gives an `<a>` the same look (combine with the
  variants). Disabled: the `disabled` attribute on a button, `aria-disabled="true"` on a `.button` link.
- *Forms.* `.form` stacks fields `--space-4` apart (max 40rem); `.field` wraps a label, its control, an optional
  `.field-hint` and `.field-error`, each tied to the control with `aria-describedby`; an invalid control carries
  `aria-invalid="true"` (red border, doubled). A bare `<label>` wrapping its control still works. `.check` lays out a
  checkbox or radio with its text; `fieldset` + `legend` group related controls; `.form-actions` holds the buttons.
- *Tables.* `.grid` on the `<table>`: cell padding, row dividers, muted header, top-aligned cells, `caption`, `.num`
  cells right-aligned with tabular figures, `.zebra` for striped rows. Wrap anything wider than a phone in
  `.table-scroll`.
- *Notices.* `.notice` plus `.info`, `.ok`, `.warn` or `.error`: a tint background with a status-coloured left edge.
  Give it `role="status"` (or `role="alert"` for an error that needs attention now), and start the text with a word
  that says the status so colour is never the only cue.
- *Badges.* `.badge` plus `.badge-accent`, `.badge-ok`, `.badge-warn`, `.badge-error`, `.badge-info`.
- *Panels and cards.* `.panel` (a form-width box, `.panel-wide` for full width), `.card` / `.cards`, `.dialog-box`,
  `.toast`, `.empty-state`: padding from the space scale, `--radius-md`, `--border`.

**App shell.** `MainLayout`: skip link, `<header class="topbar">`, `<nav>`, `<main id="content">`. Below 40rem the nav,
account controls, language switch and Help fold into a "Menu" `<details class="nav-menu">` whose panel drops under the
bar (closes on navigation); from 40rem it is one wrapping row. The top bar is not sticky; the mock banner is, and
`html { scroll-padding-top }` keeps a focused control clear of it (WCAG 2.4.11). A new sticky bar must fit inside that
padding.

**Contrast of the colour tokens** (WCAG 2.x relative luminance; text needs 4.5:1, borders and focus rings 3:1).
Every token pair already passed, including the dark theme's salmon accent, so no colour token changed:

| Pair | Light | Dark |
| --- | --- | --- |
| `--ink` on `--bg` | 15.43 | 15.04 |
| `--muted` on `--bg` / `--panel` (hints, placeholders) | 5.55 / 6.05 | 7.46 / 6.77 |
| `--accent` (links, secondary buttons) on `--bg` / `--panel` | 7.53 / 8.22 | 6.66 / 6.05 |
| `--accent-ink` on `--accent` (primary buttons, active nav) | 8.22 | 6.66 |
| `--ok` on `--panel` | 5.28 | 8.56 |
| `--error` on `--bg` / `--panel` | 5.99 / - | - / 6.81 |
| `--line-strong` (control borders) on `--bg` / `--panel` | 3.82 / 4.17 | 4.01 / 3.64 |
| Focus ring (`--accent`) on `--bg` | 7.53 | 6.66 |
| Site `--oxblood` on `--paper` / `--sheet` | 7.53 / 8.22 | 7.76 / 7.04 |
| Site `--oxblood-ink` on `--oxblood` (buttons) | 8.22 | 7.76 |
| Site `--muted` on `--paper` | 6.27 | 7.63 |
| Site plate: `--slate-muted` / `--slate-mark` on `--slate` | 7.15 / 7.51 | 8.38 / 9.93 |
| Mock banner text / tag | 8.31 / 5.93 | 9.78 / 9.91 |
| `--info` on `--bg` / `--panel` / `--info-tint` (GAD-T689) | 6.30 / 6.88 / 6.16 | 8.27 / 7.51 / 7.05 |
| `--ok` on `--bg` / `--ok-tint` | 4.84 / 4.79 | 9.43 / 8.03 |
| `--warn` on `--bg` / `--panel` / `--warn-tint` | 5.43 / 5.93 / 5.37 | 10.58 / 9.60 / 8.83 |
| `--error` on `--panel` / `--error-tint` | 6.54 / 5.70 | 6.81 / 6.65 |
| `--ink` / `--muted` on the lightest-contrast tint | 14.67 / 5.27 | 12.55 / 6.23 |
| `--danger-ink` on `--error` (danger buttons) | 6.54 | 7.50 |

`--line` (1.38 light, 1.33 dark) is for decorative dividers only; any control whose border is its only boundary uses
`--line-strong`.

Component colours that failed and were fixed:

| Where | Before | After |
| --- | --- | --- |
| Help search excerpt, highlighted word (`.help-excerpt mark`): muted text on the accent tint | 4.23 light, 4.67 dark | ink: 11.76 light, 9.41 dark |
| Input placeholders: browser default (about `#757575` on `--bg`) | 4.22 light | `--muted`: 5.55 light, 7.46 dark |

## What was checked and fixed (GAD-T507)

Earlier passes (GAD-T307, T365-T368) added the skip link, `--line-strong`, the blanket reduced-motion rule and
sideways-scrolling tables. This pass:

- **1.4.3 / 1.4.11 contrast.** Tokens computed above. Fixed the help-search highlight and set `::placeholder` to
  `--muted` explicitly.
- **1.4.10 reflow at 320px.** Found by measuring `scrollWidth` at 320px: Organization settings (a dropdown sized to its
  longest option pushed the whole form to 723px), Analytics (the scope dropdown) and the exams manager (screen-reader
  text inside a sideways-scrolling table was positioned against the page, not the table, so the page scrolled too).
  Fixed with `max-width: 100%` on form controls, `position: relative` on `.grid`, and `min(Npx, 100%)` in every
  `repeat(auto-fill/auto-fit, minmax(...))` with a minimum of 200px or more. Data tables still scroll inside their own
  box, which 1.4.10 allows for two-dimensional content.
- **2.1.1 keyboard.** A sideways-scrolling table with no link or button inside can't be scrolled from the keyboard in
  every browser: `js/scroll-regions.js` (loaded from `App.razor`) gives such a box `tabindex="0"`, a region role and a
  name from its caption or nearest heading, only while it actually overflows.
- **2.4.7 / 2.4.11 focus visible, not obscured.** Every control keeps the 2px `--accent` ring. The public site's skip
  link sat under the sticky mock banner (z-index 50 vs 1000); raised to 1200. `html { scroll-padding-top: 4rem }` keeps a
  focused control clear of a sticky bar (mock banner, assignments console bulk bar). On phones and short screens the mock
  banner is no longer sticky (it wraps to five lines at 375px and covered a fifth of the screen). Headings and `<main>`
  that receive focus only from script (Blazor's focus-on-navigate, the skip link) no longer draw a ring.
- **2.5.8 target size.** The help "?" mark is 24px; small row buttons, the tour's Skip, the integrity notice's small
  button, `a.button`, the toast close and help heading anchors have a 24px minimum. axe's `target-size` rule passes.
- **1.3.1 info and relationships.** 22 empty `<th>` cells (action columns in admin, grading, members, LTI, API keys,
  webhooks, platform organizations, and the dashboard's row-header column) now carry visually hidden text.
- **Mock banner styling.** `demo.css` is linked from `App.razor`: Blazor keeps only one `HeadContent` per page, so on
  every page with its own stylesheet the banner lost its styles (no padding, text against the screen edge).
- **3.2.6 consistent help.** The app shell's Help link is in the same place on every page; the public site's footer now
  lists the Help center next to Request a demo on every page.
- **Reviewed, already in place:** `lang="en"`; a `<PageTitle>` per page plus the version suffix; the skip link in both
  layouts; landmarks (`header`, `nav` with labels, `main#content`, `footer`); labels on every form field the scan
  reached; `role="alert"` on sign-in errors and `role="status"` on save messages and loading states; a blanket
  `prefers-reduced-motion` rule plus per-feature overrides; no fixed-height text boxes that clip under 1.4.12 text
  spacing (the exam card that has a fixed height scrolls).
- **3.3.8 accessible authentication.** Username and password fields have `autocomplete` and nothing blocks paste or
  password managers; two-step verification uses an authenticator code (`autocomplete="one-time-code"`, pasteable); no
  CAPTCHA or cognitive test anywhere (the contact form uses a honeypot and a signed render time).
- **3.3.7 redundant entry.** No flow asks for the same information twice in one process.

## Automated results

axe-core 4.10 (`@axe-core/playwright`, tags wcag2a/aa, wcag21a/aa, wcag22aa, best-practice) over 37 pages
(14 public, the student's dashboard, My exams, practice, notifications and three get-ready pages, 18 staff pages), each in
light and dark at 1280px and 320px, plus a horizontal-scroll check at 320px, against a local mock-mode run with the demo
seed.

| Rule | Before | After |
| --- | --- | --- |
| color-contrast (help search highlight) | 46 nodes (help search, light, both widths) | 0 |
| empty-table-header | 16 nodes, 12 page/width runs | 0 |
| scrollable-region-focusable | 10 nodes, 6 page/width runs | 0 |
| Horizontal scroll at 320px | 3 pages (settings, analytics, exams manager) x 2 themes | 0 |

The landing page's 4 contrast hits in the baseline were the exchange's fade-in animation caught mid-fade (a false
positive; the scan now waits for it).

After GAD-T507 the same scan reports **no violations** on any page, theme or width. axe passing is a floor, not
conformance: it can't judge reading order, meaningful names, keyboard traps in custom widgets or how a page sounds in a
screen reader.

## Languages (GAD-T526)

- The page's `<html lang>` follows the interface language the student chose (English or Korean), so a screen reader uses the
  right voice (WCAG 3.1.1). Text in another language than the page carries its own `lang`: a language's own name in a picker,
  a student's Korean answer on an English staff page, a machine translation under it (WCAG 3.1.2).
- A machine translation is always labelled in text, not by styling alone ("Machine translation from Korean into English").
- Korean interface text follows the glossary and style in [LOCALIZATION.md](LOCALIZATION.md); the examiner's captions are
  always on, in the exam language.

## Known gaps

- **The voice and camera exam** is spoken and on camera by design. A student who cannot speak, hear or use a camera
  can't take it as designed; the instructor-granted accommodations (extra time, the eye-contact accommodation) and the
  institution's alternative arrangements cover that. The exam room (`VoiceRoom.razor`, `voice-room.js`, `voice.css`,
  `webcam.css`, `examiner-gaze.css`) was out of scope for edits in GAD-T507 because another change was in progress there;
  its open findings are noted on GAD-T507:
  - Hold to talk works only with a pointer (`pointerdown`/`pointerup`): no Space or Enter hold, so a keyboard user must
    stay in the default hands-free mode (2.1.1).
  - The end of an answer is detected after about two seconds of silence, with no setting to lengthen it short of the
    Done button and hold-to-talk mode (2.2.1 timing adjustable; consider a per-student longer pause as an accommodation).
  - The spoken caption is `aria-hidden` and the transcript log is `aria-live="off"`, so a screen-reader user relies on
    the exam card's announcement of the question; verify with a screen reader.
  - The self-view note is .72rem and the transcript speaker labels .74rem uppercase; both pass contrast but are small.
- **No assistive-technology testing yet** (NVDA, JAWS, VoiceOver, TalkBack, Dragon, switch access).
- Status messages added with their live region in the same render (`role="status"` on a newly inserted element) can be
  missed by some screen readers; keeping an always-present empty region and filling it is more reliable.
- Dense staff tables scroll sideways on phones rather than reflowing into cards.
- Authentication forms are MindAttic.Authentication's markup; changes to them belong in that repo.

## How to re-check

1. Run the app in mock mode with the demo seed: `.\run.ps1 -SeedDemo -Password '<a password>'` (https://localhost:7311).
   If the working tree doesn't build because of someone else's in-progress change, export `HEAD` to a scratch folder
   (`git archive HEAD | tar -x -C <dir>`), copy your changed files in, and run it there with
   `ConnectionStrings__Gadfly` pointing at a separate LocalDB database and `ASPNETCORE_URLS` at a free port.
2. Scan with Playwright and axe: for each page, light and dark, at 1280px and 320px,
   `new AxeBuilder({ page }).withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa']).analyze()`, and check
   `document.documentElement.scrollWidth > clientWidth` for reflow. Sign in as `demo.student` and `demo.owner`.
3. By hand: Tab through each page from the address bar (skip link first, every control reachable, ring always visible
   and not under the mock banner); zoom to 400% at 1280px; apply the 1.4.12 text-spacing bookmarklet; turn on reduced
   motion; switch the OS to dark mode.
4. Contrast for a new colour: compute it against both `--bg` and `--panel` in both themes before adding it, and prefer an
   existing token.

## Public site pass (GAD-T692)

Pages: landing, how it works, science, pricing, security, contact, privacy, terms, accessibility, 404, and the
`PublicLayout` header and footer (`site.css`, `site.js`).

- **One palette.** `.site`'s `--paper`, `--sheet`, `--rule`, `--rule-strong`, `--oxblood` and `--oxblood-ink` are now
  aliases of app.css's `--bg`, `--panel`, `--line`, `--line-strong`, `--accent` and `--accent-ink`; the site's own copies
  of `--ink`, `--muted`, `--error`, `--ok`, `--measure` and `--gutter` are gone, so the app's values (and its contrast
  table) apply. Only the hero plate's slate colours stay site-specific (`--slate-*`, now including the two divider
  colours that were hard-coded). The serif headings and the 72rem page width keep the site editorial.
- **Tokens.** Spacing, radii, borders and small type use `--space-*`, `--radius-sm`, `--border`/`--border-strong` and
  `--text-xs`..`--text-xl`; the site's own `:focus-visible` rule is gone, so the global 3px `--focus-color` ring applies.
  The display sizes of the serif headings and the bands' fluid padding stay as they were.
- **Targets.** Header links, language links, footer links and the brand link are at least `--target-min`; buttons and the
  contact form's controls `--target-touch`.
- **Status.** The contact form's success and error notices use the `--ok`/`--error` border on their tints, as app.css's
  `.notice.ok`/`.error`; the wording says which it is, so colour isn't the only signal.
- **The live examiner.** The landing hero's plate shows `<LiveExaminer Framing="head-and-shoulders">` instead of the
  portrait image; its accessible name is `site.landing.plate.alt` ("One of Gadfly's two examiners, shown live and
  silent"). These pages are static (no circuit), so `LiveExaminer` writes its mount config into `data-live-examiner` when
  rendered statically and `site.js` mounts her; she loads only once scrolled into view. The hero stacks below 820px with
  the plate after the headline, lede and actions, and below 40rem the examiner sits above the exchange at most 14rem wide,
  so the h1 is the first thing on a phone screen.
- **Statement.** `/accessibility` now states WCAG 2.2 AA as the target for every page, explicitly not a conformance claim,
  and describes the live examiner's accessible name and her reduced-motion stand-in.
- **Not changed.** The statement's lines about the exam room's still-portrait fallback (`support.motion`,
  `limits.avatar`) describe the exam room, which this pass didn't touch; reconcile them when the exam room's fallback
  changes.

## Student pages (GAD-T693)

- **Get ready (`StartExam`).** The examiner choice is a visible-legend `fieldset` of radio cards (name plus a short
  description, tied to its radio with `aria-describedby`), with no portrait; the selected card is marked by border,
  inset ring and tint, not colour alone. The preview is one `<LiveExaminer>` in the student's examiner,
  customization and framing: another examiner loads the other model (once per browser, model-cache.js), a new
  customization or framing restyles the loaded model in place (`live-examiner.js` handle `update`). The still
  portrait, its clothing-tint band and its skin and hair dots are gone, with their CSS; the voice preload no longer
  fetches the portrait. `get-ready.css` spacing, type sizes, radii and borders now come from the tokens; the selected
  look segment uses `--accent-ink` instead of a hard-coded white.
- **Shared student CSS.** `student.css` (outside the exam room's rules, which GAD-T694 owns), `practice.css` and
  `exams.css` spacing and type sizes moved to `--space-*` / `--text-*`; the practice tag uses `--radius-pill`.
- **Markup pass (GAD-T704).** My exams, My notifications, My research and My result open with one `h1` in a
  `.page-header`; their blocks are `.page-section`s. On My exams each exam card and the practice offer is a section
  named by its own `h2` (`aria-labelledby`); the empty list is a `.notice.info` with `role="status"`. The exam catalog's
  filters are a named search landmark, each category is a section named by its `h3`, and the redundant `title` tooltip
  on the languages line is gone. My notifications is a `.form`: the opt-out checkbox is described by its explanation
  (`aria-describedby`), the save button sits in `.form-actions`, the loading line and the saved/failed message are
  status messages (the message as a `.notice.ok` / `.notice.error`). My research's text is a panel held to
  `--measure`; "no study" is a `.notice.info`; its message is a status `.notice`. My result's appeal is a labelled
  section with `.field`s: each textarea has a `for`-linked label and is described by its hint; the "not published" and
  appeal messages are status notices. These pages have no data tables, so no `.table-scroll` was needed.
- **Eye-contact indicator (`PracticeGaze`, `ExaminerGaze`).** The live region moved from the whole indicator (which held a
  button and a disclosure, so every change re-read them) to its label alone, so only the state sentence is announced.
  The state is always said in words, the dot's colour only repeats it.
- **CSS.** `student.css` no longer has fixed 760px widths outside the exam room: `.panel.wide` is full width, exam cards
  and the catalog are held to `--measure`, the camera preview is 12.5rem; its own `a.button` / `.secondary` copies are
  gone (app.css has them); the result, steps, check-mark and catalog rows use the space and border tokens.
  `languages.css` (exam languages panel, machine-translation bar) is on `--space-*`, `--text-*`, `--radius-*` and
  `--border`.

## Exam room (GAD-T694)

- **The examiner is only ever live.** The loading stand-in and every 2D fallback (no WebGL, model failed to load, slow device,
  lost graphics, no avatar at all) show her presence (`live-examiner.js` `mountPresence`, the `<ExaminerPresence>` sphere) over
  the stage, `aria-hidden` like the 3D head; its glow follows her voice syllable by syllable, so she still visibly speaks. The
  room no longer reads `PortraitUrl`/`PortraitMouth` and never shows `portrait-f.png`/`portrait-m.png`. The fallback notes say
  "live presence" instead of "picture" (`examRoom.avatar.presence.*`, en/ko/es).
- **Reduced motion keeps her live** (WCAG 2.3.3 is about non-essential motion; her lip sync is essential content). Under
  `prefers-reduced-motion` the 3D examiner still loads, with TalkingHead's head movement cut to a third, blinking and lip sync
  kept, no camera moves; the presence turns no more and its fades and the gaze nudge are off. `<LiveExaminer>` follows the same rule.
- **Gaze target.** The eye-contact check (examiner-gaze.js `faceShare`) uses her projected eye bones in 3D and, while the
  presence is shown, the centre of the sphere (the centre of `.examiner-presence`), the natural point a student looks at; with
  neither it keeps the fixed fraction of the stage as before.
- **Layout.** A phone on its side (landscape, at most 32rem tall) keeps her beside the controls and lets her take most of the
  height; portrait phones stack her, controls and transcript as before.
- **Not done in this pass.** A token sweep of voice.css (it still has its own spacing and the dark stage colour), and a review of
  how often the polite status line (`data-voice-status`) changes, are follow-ups.

### Admin, platform and members pages (GAD-T695)

Pages: the admin dashboard, desk, assignments console, audit log, email preview, exam preview, exams manager, live
sessions, LTI, API keys, plan, settings, usage, webhooks, research, student profile, students directory and system
status (`Pages/Admin/*`), `OrgMembers`, `Platform*`; stylesheets `admin*.css`, `staff-notice.css`, `demo.css`.

- **Tables.** All 38 data tables sit in a `.table-scroll` box (only the table scrolls sideways at 320px), have a
  caption (visually hidden where a heading already names the table) and `scope="col"` on every column header.
- **Page headers.** The pages' own title rows (`dash-head`, `xm-head`, `al-head`, `live-head`, `people-head`) are now
  `.page-header`: h1 left, actions wrapping under it on a phone.
- **Desk views.** The desk's Exams / Students / Student exams links claimed `role="tab"` without tab panels or arrow
  keys; they are now plain navigation links with `aria-current="page"`, like the console's List / Grid links.
- **Bulk changes.** The assignments console's bulk-change panel was `position: sticky` and covered the grid row with
  focus as you tabbed down (WCAG 2.4.11); it now scrolls with the page.
- **Messages.** Result messages use `.notice.ok` / `.notice.error` (tinted, edge-coloured, the text says what happened)
  instead of page-specific boxes; the `people-ghost` / `people-button` buttons are the design system's `.secondary`
  and `.button`; `sr-only` / `people-sr` are `.visually-hidden`.
- **Targets and focus.** Admin nav, desk and console view links, the exams "Actions" menu, the erase summaries and
  console grid cells are at least `--target-min` (and `--target-touch` for the nav under a coarse pointer). The admin
  nav's own 2px focus outline is gone so the global ring shows; the page-local `button.danger` that put `--panel` text
  on `--error` is gone, so danger buttons use `--danger-ink`.
- **Tokens.** Every spacing, font size, radius and shadow in these stylesheets is a token; status edges use `--warn`
  for "needs a look soon" (was `--accent`) and decorative borders that are a boundary use `--line-strong`. Inline
  styles on the console message, the "To whom" fieldset and the email preview frame moved into classes
  (`fieldset.checks`, `.email-frame`; the frame keeps a white page because it shows an email as a mail client would).
- **Reflow.** The audit log's detail list and the filter rows stack below 40rem; the staff hold dialog scrolls inside
  itself when the screen is shorter than its text.
- **Not done here.** The admin pages' English-only text is unchanged (they were never localized); toggle buttons on the
  students directory and console show their open state by fill as well as by the panel that opens, with no
  `aria-expanded` yet; the sign-in, password-reset and invitation pages were already covered by GAD-T507 and only
  checked.

### Admin, platform and members pages (GAD-T695)

Pages: the admin dashboard, desk, assignments console, audit log, email preview, exam preview, exams manager, live
sessions, LTI, API keys, plan, settings, usage, webhooks, research, student profile, students directory and system
status (`Pages/Admin/*`), `OrgMembers`, `Platform*`; stylesheets `admin*.css`, `staff-notice.css`, `demo.css`.

- **Tables.** All 38 data tables sit in a `.table-scroll` box (only the table scrolls sideways at 320px), have a
  caption (visually hidden where a heading already names the table) and `scope="col"` on every column header.
- **Page headers.** The pages' own title rows (`dash-head`, `xm-head`, `al-head`, `live-head`, `people-head`) are now
  `.page-header`: h1 left, actions wrapping under it on a phone.
- **Desk views.** The desk's Exams / Students / Student exams links claimed `role="tab"` without tab panels or arrow
  keys; they are now plain navigation links with `aria-current="page"`, like the console's List / Grid links.
- **Bulk changes.** The assignments console's bulk-change panel was `position: sticky` and covered the grid row with
  focus as you tabbed down (WCAG 2.4.11); it now scrolls with the page.
- **Messages.** Result messages use `.notice.ok` / `.notice.error` (tinted, edge-coloured, the text says what happened)
  instead of page-specific boxes; the `people-ghost` / `people-button` buttons are the design system's `.secondary`
  and `.button`; `sr-only` / `people-sr` are `.visually-hidden`.
- **Targets and focus.** Admin nav, desk and console view links, the exams "Actions" menu, the erase summaries and
  console grid cells are at least `--target-min` (and `--target-touch` for the nav under a coarse pointer). The admin
  nav's own 2px focus outline is gone so the global ring shows; the page-local `button.danger` that put `--panel` text
  on `--error` is gone, so danger buttons use `--danger-ink`.
- **Tokens.** Every spacing, font size, radius and shadow in these stylesheets is a token; status edges use `--warn`
  for "needs a look soon" (was `--accent`) and decorative borders that are a boundary use `--line-strong`. Inline
  styles on the console message, the "To whom" fieldset and the email preview frame moved into classes
  (`fieldset.checks`, `.email-frame`; the frame keeps a white page because it shows an email as a mail client would).
- **Reflow.** The audit log's detail list and the filter rows stack below 40rem; the staff hold dialog scrolls inside
  itself when the screen is shorter than its text.
- **Not done here.** The admin pages' English-only text is unchanged (they were never localized); toggle buttons on the
  students directory and console show their open state by fill as well as by the panel that opens, with no
  `aria-expanded` yet; the sign-in, password-reset and invitation pages were already covered by GAD-T507 and only
  checked.

### Inbox (GAD-T701)

`/inbox` (Components/Pages/Inbox.razor, `inbox.css`) and the top bar's Inbox link, built on the design system:

- **Structure.** One h1 in a `.page-header` with its actions (Mark all as read, Email preferences); the list is a `<ul>`
  named "Messages"; an opened message is an `<article>` labelled by its h2, with a "Back to inbox" link first.
- **Not by colour alone.** An unread message carries the word "Unread" (a badge), a bold title and an accent edge; the
  count line above the list says "2 unread of 5".
- **Top bar.** The Inbox link reads "Inbox, 2 unread" to a screen reader (visually hidden text; the number pill is
  `aria-hidden`), and below 40rem the Menu toggle shows the same count so it isn't hidden in the closed menu. The count
  is read on load, on each navigation and when the inbox marks messages read, never on a timer.
- **Status.** Marking read and failures are announced through a `role="status"` line, worded "Marked as read." /
  "That didn't work: ...".
- **Controls.** Each "Mark as read" button is named with its message's title; targets meet `--target-min`; dates are
  `<time datetime>`.
- **Reflow.** Each row wraps (badge, title, date, then the button) and long titles and bodies break anywhere, so
  320px has no sideways scroll; the body keeps its line breaks (`pre-line`) at text-spacing overrides.
- **Links.** A message's "Open" button follows only app-relative links.
