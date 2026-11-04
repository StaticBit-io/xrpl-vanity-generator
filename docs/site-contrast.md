# Site contrast

WCAG 2.2 AA, computed from `site/src/css/tokens.css` by `node site/tools/contrast.mjs`.
`site/test/theme.test.mjs` fails when a required pair misses its threshold or when this file is out
of date. Ratios are the WCAG 2.x relative-luminance contrast, truncated to two decimals.

- Text: at least 4.5:1. Controls and graphics that carry information: at least 3:1.
- Rows marked "exempt" carry no information and have no threshold.

Result: every required pair passes.

| Pair | Foreground | Background | Ratio | Needs | Result |
|---|---|---|---|---|---|
| Body text | `--color-ink` #e1f1de | `--color-paper` #040805 | 17.08:1 | 4.5:1 | pass |
| Body text in a well | `--color-ink` #e1f1de | `--color-paper-2` #080f0a | 16.38:1 | 4.5:1 | pass |
| Body text on a hovered row | `--color-ink` #e1f1de | `--color-paper-3` #101a12 | 15.08:1 | 4.5:1 | pass |
| Secondary text | `--color-ink-2` #b2cdaf | `--color-paper` #040805 | 11.76:1 | 4.5:1 | pass |
| Secondary text in a well | `--color-ink-2` #b2cdaf | `--color-paper-2` #080f0a | 11.28:1 | 4.5:1 | pass |
| Secondary text on a hovered row | `--color-ink-2` #b2cdaf | `--color-paper-3` #101a12 | 10.38:1 | 4.5:1 | pass |
| Muted text (labels, comments, disabled control) | `--color-muted` #769676 | `--color-paper` #040805 | 6.15:1 | 4.5:1 | pass |
| Muted text in a well | `--color-muted` #769676 | `--color-paper-2` #080f0a | 5.90:1 | 4.5:1 | pass |
| Accent as text | `--color-accent` #7ef473 | `--color-paper` #040805 | 14.49:1 | 4.5:1 | pass |
| Accent as text in a well | `--color-accent` #7ef473 | `--color-paper-2` #080f0a | 13.90:1 | 4.5:1 | pass |
| Text on the accent fill (primary control, matched word) | `--color-accent-ink` #040b05 | `--color-accent` #7ef473 | 14.25:1 | 4.5:1 | pass |
| Text on the accent wash (selected row) | `--color-ink` #e1f1de | `--color-accent-wash` #062409 | 14.07:1 | 4.5:1 | pass |
| Focus ring | `--color-focus` #7ef473 | `--color-paper` #040805 | 14.49:1 | 3:1 | pass |
| Focus ring in a well | `--color-focus` #7ef473 | `--color-paper-2` #080f0a | 13.90:1 | 3:1 | pass |
| Secondary control border | `--color-ink-2` #b2cdaf | `--color-paper` #040805 | 11.76:1 | 3:1 | pass |
| Rule between sections (decorative) | `--color-rule` #1d2a1f | `--color-paper` #040805 | 1.35:1 | exempt | n/a |
| Table head and well rule (decorative) | `--color-rule-2` #334934 | `--color-paper` #040805 | 2.05:1 | exempt | n/a |
| Dimmest glyph in the field (decorative) | `--glyph-0` #18361b | `--color-paper` #040805 | 1.51:1 | exempt | n/a |
| Brightest glyph in the field (decorative) | `--glyph-7` #c4ffb0 | `--color-paper` #040805 | 17.52:1 | exempt | n/a |
