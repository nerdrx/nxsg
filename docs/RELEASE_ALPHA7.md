# NXSG alpha.7 — bug fixes and interaction polish

- Placement preview preserves the last successful material on compiler errors, shows the error, and avoids endlessly recompiling unchanged broken graphs.
- Placement stays bound to its original graph; opening another graph cannot redirect an existing placement window.
- Visible Undo/Redo, centering, view reset, precise numeric placement, and Escape-to-cancel dragging.
- Placement previews inherit the edited material's matching properties.
- New FX merges copy their motion states, trees and clips into the destination controller. Undo/Redo and playback survive deleting the source driver.
- Ambiguous renderer animation paths are rejected before creating assets.
- Playground material switching invalidates stale frames before taking snapshots. Reset preview controls keeps A/B comparisons.

Older alpha.6 FX merges still depend on their original driver assets; keep those assets unless replacing the old motion layers.

Validation: portable checks, packaging checks, and hidden Linux Unity 2022.3.22f1/OpenGLCore tests for placement gestures/cancel/Undo, error recovery, graph identity, snapshot freshness, and native Animator merge/playback. Live VRChat, Windows and headset checks remain open.
