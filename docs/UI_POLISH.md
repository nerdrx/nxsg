# Editor UI pass — alpha.28

Reviewed the actual Unity 2022.3.22f1 Linux editor using compositor captures in headless Gamescope. Focus: dense particle controls, the minimum window size, node labels, and the connected-node picker.

- Shared toolbar/button spacing and inspector field spacing.
- Selected sidebar tab has a visible underline and background; tabs share available space.
- Inspector labels wrap within their column instead of displacing inputs.
- Material preview fits available width and can collapse; its state survives inspector rebuilds.
- Selected-node controls appear before frame controls.
- Connected-node picker accounts for its full height and canvas bounds, keeps the search field inside its border, and scrolls choices.
- Long node headers and port labels use ellipsis; operation/socket tooltips retain context.
- Shorter initial status hint; existing interactions remain available.
- Particle Lifetime sample node positions no longer overlap.

![Actual Unity editor after the pass](images/editor-ui-pass.png)

## Checks

`EditorPolishCapture` exercises wide and 850×500 layouts, numeric field bounds, preview collapse, and a picker opened at the bottom-right canvas edge. `GoodiesEditorSmoke` checks inline edit/undo, frame notes, node finder, and particle curve editing. Packaging checks also pass.

These checks cover the Linux editor fixture and its current display scale, not every OS/theme/DPI combination. Dense inspectors still scroll; the preview is collapsible to reclaim space. Existing user graph positions are not rearranged.
