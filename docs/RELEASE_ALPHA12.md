# NXSG alpha.12 — named texture slots

Select a texture node and edit **Slot name**. Its header and material inspector
now use the same label. Applies to Texture, Matcap and other texture-resource
nodes. Shared resources share a name; slot numbers distinguish duplicates.

Renaming preserves shader property names and existing texture assignments.
Names survive save/load and copy/paste. Build or Auto scene refreshes material
labels. Clearing a name restores Texture N.

Validation: portable checks and hidden Unity 2022.3.22f1 editor tests for the
rename field, node/material label matching and texture retention in both backends.

Slot numbering is deterministic across save/reload, including graphs with multiple texture resources.
