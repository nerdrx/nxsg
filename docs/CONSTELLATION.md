# Constellation node

Open `Samples~/Constellation.nxsg` for a ready-to-edit emission graph.

**Constellation** builds animated points and links from UV cells. Connect **Points** and **Lines** separately to color or emission branches, or use **Mask** for their union. **Scale** sets cells per UV unit. **Point Size** and **Line Width** use cell units. **Link Chance** controls the fraction of horizontal and vertical neighbor links. **Twinkle** changes point brightness over time.

Connect Time for animation and AudioLink for reactivity. Without a Time connection, the node uses the graph preview or Unity shader clock. Audio defaults to 1. The effect is procedural: it does not track actual mesh vertices, discover shapes in a texture, or store previous positions. Each pixel checks nine nearby cells, so measure cost before covering large screen areas or stacking many copies. Vertex displacement cannot use this fragment-only node.
