# Core graph fixtures

These `.nxsg` files are the first persistence and validation fixtures for the
portable core:

- `default-texture-toon-output.nxsg` is the minimal UV → Texture2D → Toon →
  Output graph and uses the explicit `builtin://white` fallback resource.
- `layout-a.nxsg` and `layout-b.nxsg` have identical graph meaning with
  different node positions; their semantic hashes must match.
- `mutable-parameter.nxsg` checks that an animated material binding and zero
  default remain present.
- `unknown-node.nxsg` checks inert unknown operation and extension payload
  preservation.
- `cycle.nxsg` checks data-cycle rejection.

The local standalone smoke runner used during development reads these files,
round-trips the default graph, checks semantic hashes, and verifies the safety
cap. Unity package import and Unity 2022.3 assembly compatibility remain
separate gates.
