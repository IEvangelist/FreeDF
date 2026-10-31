# Editor accessibility, theming and motion

This page records how the browser editor (`tools/AdoBye.EditorWeb/wwwroot`) is
built for accessibility, what is verified automatically, and what was checked by
hand. Automated checks are **partial**: axe-core finds roughly a third of WCAG
failures, so the sections below separate what a tool asserts from what a person
verified. Nothing here has been tested with a real screen reader on a real
device; see [Not verified](#not-verified).

## Design system

`theme.css` is a token layer loaded after `editor.css`; it only overrides.
`ui.js` is a progressive layer (deferred, ~25 KB unminified) that adds behaviour
around the editor's own DOM. Neither file makes a network request, loads a font
or script from a CDN, or stores anything except two `localStorage` keys
(`editor.theme`, `editor.motion`) and the panel widths.

| Area | Implementation |
|---|---|
| Colour | `light-dark()` token pairs on `:root` (`--bg`, `--panel`, `--raised`, `--fg`, `--mut`, `--accent`, `--primary`, `--danger`, ...). Follows `prefers-color-scheme`; `html[data-theme]` forces a theme. The brand blue and the Sponsor pink are kept in both themes (the pink is darkened in light mode to reach 4.5:1). |
| Type, radius, space, shadow | `--text-*`, `--radius-*`, `--space-*`, `--shadow-*`; `--h` (control height) is in `rem`. System font stack only. |
| Focus | One ring for every control: 2 px `--ring`, 2 px offset. Asserted at >= 3:1 against the surrounding surface in both themes. |
| Components | Buttons (default, primary, outline, secondary, destructive, `sm`/`lg`/icon), inputs, selects, checkbox/radio (custom, native semantics), switch, segmented control, menus, dialogs, tooltips, toasts, resize handles, command palette, welcome state. |
| Theme control | Header **Appearance** menu, command palette (`Ctrl/Cmd+K`) and `window.editorUi.setTheme()`; persisted; applied before first paint by a one-line head script (no flash). |

### Motion

Tokens: `--dur-1/2/3` = 100/160/240 ms; `--ease` and `--ease-in-out`. Only
`transform` and `opacity` (plus colour for hover) are animated.

| Where | Motion |
|---|---|
| Menus, submenus, popovers | fade + 4 px rise + 2 % scale, 160 ms |
| Dialogs, palette, scrims | fade; dialog also rises 8 px, 240 ms |
| Toasts | rise + fade in; fade out 160 ms |
| Side panels | slide 12 px + fade when opened (the old animated `grid-template-columns` transition, which re-laid out the page, was removed) |
| Thumbnails | opacity fade as each image decodes |
| Buttons | colour/background 100 ms; 0.97 scale on press |

Rules that keep motion safe: nothing is animated on exit (so removal is never
delayed and the Save-confirmation presentation hand-off is unchanged); the main
page image is never animated; selection handles and the object layer, which are
rebuilt on every pointer move, are not animated.

Reduced motion: `prefers-reduced-motion: reduce` removes all animation and
transitions. **Appearance > Reduce motion** (or the palette) does the same
manually via `html[data-motion="reduce"]`, persisted. There are no auto-playing
loops and no parallax.

## What is asserted automatically (`--a11y`)

`dotnet run --project tools/AdoBye.EditorSmoke -- --a11y <site> <sample.pdf>`
injects axe-core (WCAG 2.0/2.1/2.2 A and AA plus best-practice rules) into a
real Chromium page. axe-core is a **dev/test-only** dependency: install it with
`npm ci --prefix tools/AdoBye.EditorSmoke/a11y`; it is not part of the Pages
payload. The mode fails on any serious or critical violation.

Screens run in light and dark: welcome, document open, File menu and submenu,
zoom menu, Appearance menu, command palette (empty and filtered), shortcuts and
about (sponsor link), find bar, Save as, Document properties, New document,
Export images, Protect/Save protected, Merge, Resume previous edit (recovery),
text selected, shape selected, annotations dock, form-mode menu, pages and
properties panels collapsed. Forced-colors (Windows high contrast), 200 % and
400 % zoom (640x400 and 320x200 CSS px), mobile 320/375/390 wide and tablet
820 wide are also swept.

Besides axe, the mode asserts: skip link, single `h1`, landmarks, `lang`, title;
menu arrow/Home/End/Escape/focus return; dialog modality, `aria-labelledby`,
inert background, focus trap and focus return; keyboard page reordering; Enter
selection of page objects; resize-handle arrow keys; target size >= 24x24 for
every control outside the page content; visible focus ring >= 2 px at >= 3:1;
WCAG 1.4.12 text-spacing override clips nothing; reduced-motion removes running
animations; the manual reduce-motion setting persists; zero outbound requests.

### Justified exception

`label-content-name-mismatch` is suppressed for exactly one shape: a
`div[role=menuitem]` that owns an **open** submenu. Its visible text includes the
submenu's items but its accessible name is its own label, which is the correct
ARIA authoring-practice pattern. No other violation is exempted.

### Known non-serious findings (not gated)

Moderate/minor best-practice items remain and are listed in the results table:
`region` for menus appended to `body` as popups, and `aria-allowed-role` for the
file-input label that doubles as the Insert image button (the app handles
Enter/Space on it). They do not block use with a keyboard or screen reader.

## Manually verified

Done by driving the real page with keyboard and pointer in Chromium (Playwright)
and reading the rendered screenshots; this is human-directed review, not a
screen-reader pass.

- **Landmarks and headings**: banner, main, contentinfo, a labelled
  *Pages* region, *Properties* complementary region and *Page canvas* region; a
  visually hidden `h1`, `h2`/`h3` in panels and dialogs.
- **Names, roles, states**: every icon button has an accessible name (from its
  tooltip); names that include visible text contain it (WCAG 2.5.3); toggles use
  `aria-pressed`; menus use `menu`/`menuitem`/`menuitemradio`/`menuitemcheckbox`;
  dialogs are `aria-modal` and labelled by their heading; unlabelled inspector
  fields are labelled from their section heading.
- **Page region**: the page image's `alt` is `Page N of M, <file name>` and a
  polite live region announces page changes and reorders; save, recovery and
  errors continue to use the existing `role=status` / `role=alert` regions.
  Toasts are visual duplicates (`aria-hidden`) so nothing is announced twice.
- **Keyboard only**: Tab order follows the visual order; skip link; `Escape`
  closes menus, dialogs, the palette, find and (on narrow screens) panel sheets;
  roving focus in menus with submenu open/close and typeahead; the page list is
  a roving group (`Arrow`, `Home/End`, `Enter`) with **Alt+Arrow** to reorder
  pages; page objects are one tab stop with `Arrow` to move between them and
  `Enter` to select; the panel resize handles respond to arrow keys.
  `?` opens a shortcut reference.
- **Pointer alternatives to drags**: page reorder (Alt+Arrow), object
  move/resize (existing arrow-key nudging, plus Properties fields), panel resize
  (keyboard on the separator), and long-press as the touch alternative to
  right-click.
- **Colour contrast**: token pairs were chosen for 4.5:1 text and 3:1 UI
  boundaries in both themes (form control borders use `--input-border`, a
  stronger value than decorative separators). axe `color-contrast` is clean in
  both themes on every screen above.
- **No hover-only content**: tooltips duplicate the accessible name and never
  carry unique information; thumbnail actions also appear on focus.
- **Reduced transparency / contrast**: `prefers-reduced-transparency` removes
  blur; `prefers-contrast: more` strengthens borders and muted text;
  `forced-colors: active` uses system colours, visible borders and a
  `Highlight` selected state instead of tints.
- **Reflow**: at 320 CSS px wide there is no horizontal page scroll (the page
  canvas itself scrolls, as a toolbar/canvas application may under WCAG 1.4.10).
  Below 420 px of height the page scrolls vertically rather than squeezing the
  editor.

## Not verified

- No screen-reader testing (NVDA, JAWS, VoiceOver, TalkBack). Behaviour with
  them is inferred from semantics.
- Native `confirm()`/`prompt()` are still used for *Delete page* and *Set
  document title*; they are accessible but unstyled.
- The PDF page itself is a raster image with text overlays; the PDF's own
  structure/tags are not exposed to assistive technology.
- Real iOS/Android hardware (pinch, safe-area notches, software keyboard) was
  not exercised; mobile coverage is Chromium device emulation plus CDP touch
  events.
- Windows high-contrast was emulated with Chromium `forced-colors`, not run on a
  Windows high-contrast theme.
