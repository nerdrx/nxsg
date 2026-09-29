# Pathing node

**Pathing** draws four parallel lanes around the line from Start to End in UV space. **Channels** contains each lane as R, G, B or A. **Value** is their combined mask. **Phase** reports animated progress along the center path, and **Direction** gives its UV direction. Use Split Color on Channels to color lanes separately.

Width and Spacing use UV units. Speed measures cycles per second; negative values reverse travel. Tail controls the visible length behind each moving head. Travel 0 shows complete lines; Travel 1 shows moving pulses. Time defaults to the graph preview or Unity shader clock. Audio and Mask scale the result, with neutral defaults of 1.

The effect uses fragment derivatives for smooth edges, so it cannot feed vertex displacement. It is a procedural UV effect, not a stored trail or a route fitted to texture artwork. See `Samples~/Pathing.nxsg` for an emission example.
