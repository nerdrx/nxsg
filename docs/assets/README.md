# NXSG repository artwork

[`nxsg-banner.svg`](nxsg-banner.svg) is the editable, self-contained repository banner. Its node graph is concept art, not a screenshot of an implemented editor. It uses ordinary system typography and contains no scripts, external images, webfonts, or animation.

The compact lowercase **nx** paths are copied unchanged from the approved NX Hub wordmark. Surrounding NXSG typography, node layout, and faceted material illustration are new artwork for this repository.

## Canonical source

Checked **2026-09-17** against the local NX Hub working tree:

- Design: `nx-hub/docs/DESIGN.md`, **v1.8**, especially §8.1 and the true-black ground rules.
- Wordmark: `nx-hub/assets/brand/nx-wordmark-violet.svg`, original `1098 × 552` viewBox, uniform scaling only.
- Brand ink: exact **`#7700FF`**. No gradients, cyan, glow, or independent letter changes in the mark. Cyan is limited to small graph/status accents outside it.

The canonical design and brand assets had local changes at inspection time, so a repository commit would not identify this exact source snapshot. Recorded SHA-256 values:

```text
DESIGN.md
74f50b6812ff80d794f89a5585fc9cd7e65d23bca40ce513d41c3776bfa976a1

nx-wordmark-violet.svg
d867eef346502a0929967ea16641d0428a34093b6e5c05ccd9f485b09872c9c5
```

## Editing

Keep the wordmark geometry and aspect ratio intact. Change the surrounding composition in the SVG, render it with an SVG renderer, and inspect the result at full size and a typical README width. Keep the project-stage label accurate and retain meaningful image alt text in the README.
