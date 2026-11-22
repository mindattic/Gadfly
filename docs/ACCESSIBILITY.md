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
