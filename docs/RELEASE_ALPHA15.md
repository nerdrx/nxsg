# NXSG alpha.15 — cards-only fur

Select a Fur node and choose **Fur geometry → Cards only**. NXSG generates
root-to-tip cards along the source mesh triangle edges, across the whole surface.
The base material stays visible, and no fur shell passes are generated.

Length, mask, root/tip color, grooming, wind and card opacity remain available.
The included **Fur Cards** sample provides a starting graph.

This first mode uses three edge cards per selected triangle. Shared edges overlap,
and coverage follows the mesh topology. It does not bake or export card meshes.
The card pass uses PC geometry shaders; expand renderer bounds for fur length.
Distance shell LOD is hidden because it does not affect cards. Shadowing remains
a local fur-volume approximation, not exact card-to-card shadows.

Includes the alpha.14 Panosphere filtering fix. Hidden Unity GPU checks verify
visible front-surface cards, mask response and absence of shell passes. Headset
appearance and native Windows behavior remain unverified.
