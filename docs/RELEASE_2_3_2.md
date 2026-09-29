# NXSG 2.3.2

## Fix for locked Poiyomi material imports

NXSG 2.3.1 treated emission layers omitted by a locked Poiyomi shader as enabled. Those missing layers produced white emission even when the source material was black. The importer now creates only explicitly enabled layers.

The importer also reads independent UV modes and panning for emission masks, including MatCap mapping; respects inverted glitter masks and Panosphere glitter UVs; and maps common cross, star, and square glitter shapes to NXSG's built-in shapes. Poiyomi glitter contrast and cross size now use NXSG-scaled values. AudioLink on glitter drives motion rather than brightness.

An isolated Unity 2022.3.22f1 render of the actual locked **Emiss** material caught the white-output failure and confirmed the corrected graph renders its purple nebula and small cross sparkles. The shader families still differ in lighting and glitter internals. Custom shape textures remain listed for manual review because their pixels are not imported as exact shapes.
