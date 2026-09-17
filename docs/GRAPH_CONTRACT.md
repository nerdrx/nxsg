# NXSG graph contract (S01/S02)

This is the first portable contract for `.nxsg`. It is deliberately small and
ordinary C#: the core has no `UnityEngine` or `UnityEditor` dependency. The
editor owns canvas state and Unity asset metadata; a backend consumes the core
graph. The contract is C# 8 source compatible and targets the project's
`.NET Standard 2.1` profile. JSON serialization uses the SDK's pinned
`com.unity.nuget.newtonsoft-json` dependency.

## Public API

The core namespace is `NXSG.Core`.

```csharp
public static class GraphJson
{
    public static ShaderGraph Parse(string json,
        CancellationToken cancellationToken = default(CancellationToken));

    public static string Serialize(ShaderGraph graph, bool indented = false);

    // SHA-256, lowercase hexadecimal, over canonical semantic JSON.
    // Layout and source formatting are excluded.
    public static string ComputeSemanticHash(ShaderGraph graph);
}

public static class GraphValidator
{
    public static ValidationResult Validate(ShaderGraph graph,
        CancellationToken cancellationToken = default(CancellationToken));
}
```

`GraphJson.Parse` enforces the byte and JSON-depth limits before allocating the
graph. It throws `GraphParseException` for malformed JSON or a violated parser
limit. A valid document may still have validation diagnostics, including an
unknown operation. `GraphJson.Serialize` preserves unknown node fields and
mutable parameter bindings. `GraphValidator.Validate` is deterministic and
returns diagnostics with a severity, code, JSON path, message, actual value,
and limit where one applies. A caller must not publish generated output when
validation contains an error or an affected unknown node.

## JSON shape

The required top-level fields are `format`, `schemaVersion`, `graphId`,
`nodes`, and `connections`. Optional arrays are `parameters`, `resources`, and
`patterns`; `layout` and `adapter` are non-semantic metadata. Unknown fields
are retained where Newtonsoft extension data permits it.

```json
{
  "format": "nxsg",
  "schemaVersion": 1,
  "graphId": "graph-main",
  "nodes": [
    {"id":"uv0","operation":"core.uv0","version":1,"properties":{}},
    {"id":"texture","operation":"core.texture2D","version":1,
     "properties":{"resourceId":"albedo"}},
    {"id":"toon","operation":"core.toonSurface","version":1,"properties":{}},
    {"id":"output","operation":"core.output","version":1,"properties":{}}
  ],
  "connections": [
    {"id":"e-uv","from":{"nodeId":"uv0","portId":"uv"},
     "to":{"nodeId":"texture","portId":"uv"}},
    {"id":"e-sample","from":{"nodeId":"texture","portId":"color"},
     "to":{"nodeId":"toon","portId":"albedo"}},
    {"id":"e-surface","from":{"nodeId":"toon","portId":"surface"},
     "to":{"nodeId":"output","portId":"surface"}}
  ],
  "parameters": [],
  "resources": [{"id":"albedo","kind":"texture2D",
                 "uri":"project://Textures/albedo.png"}],
  "layout": {"nodes":{"toon":{"x":220,"y":80}}}
}
```

Connections always name both node and socket. Socket declarations are supplied
by the operation registry, not inferred from connection order. This keeps
copy/paste and migrations stable. The initial operation IDs are:

| Operation | Outputs and inputs |
| --- | --- |
| `core.constant` | `value` (typed property) |
| `core.parameter` | `parameterId` property → `value` (declared parameter type) |
| `core.uv0` | `uv` (`vector2`, UV0) |
| `core.texture2D` | `uv` (`vector2`) → `color` (`color`) |
| `core.multiply` | `a`, `b` → `value` (numeric; scalar splat is explicit) |
| `core.toonSurface` | `albedo` (`color`), optional `normal` (`vector3`, tangent space) → `surface` |
| `core.output` | `surface` → final output |

The MVP type set is `float`, `vector2`, `vector3`, `vector4`, `color`, `bool`,
`texture2D`, and `surface`. Color is data with explicit color semantics; it is
not silently a normal or a coordinate. UV0 is the only coordinate input in
this slice. Matrices, general integers, implicit coordinate-space conversion,
and shader closures are deferred.

## Identity, mutability, and unknown nodes

Node records may carry an optional `version`; omitted node versions default to
`1`. Known operations currently accept version `1` only. A future known version
is retained and reported as unsupported until an explicit migration handles it.
Unknown operations remain inert regardless of their version.

Node, connection, parameter, resource, and pattern IDs are authored stable
strings and must be unique within their collection. IDs are not array indexes.
`GraphParameter` contains a stable ID, name, type, binding, and default value.
Binding is one of `constant`, `material`, `animatedMaterial`, `global`, or
`audioLink`. Only `constant` is eligible for build specialization. A current
zero default never makes a mutable binding dead. Exported declarations and
bindings are retained independently from executable liveness.

The known operation registry is closed for this slice. An unknown operation is
loaded as an inert node, retained with its original JSON fields, and reported
as an affected compilation error. Opening or saving it never executes package
code. A later migration may replace it explicitly; it must not silently turn
it into another operation.

## Semantic hashing and deterministic saves

The semantic projection includes schema and graph identity, nodes and their
properties, connections, parameters, resources, patterns, and adapter data
that the compiler declares meaningful. It excludes `layout` and transient
source formatting. Objects are emitted with invariant JSON values and sorted
keys. ID-bearing arrays are sorted by ID in the projection; connection endpoint
fields remain explicit. The resulting UTF-8 bytes are SHA-256 hashed. Two
graphs that differ only in node position therefore share a semantic hash;
changing a resource URI, operation, connection, default, or mutable binding
does not.

## Safety and validation

The current core parser and validator enforce these S02 defaults: 4 MiB input,
JSON depth 64, 4,096 authored nodes, 16,384 connections, 1,024 resources, and
32 nested Pattern levels. Values must be finite. Duplicate JSON keys, duplicate
IDs, missing endpoints, undeclared socket IDs, data cycles, resource path
escapes, and unsupported schema versions are errors. Relative/project resource
URIs are allowed; absolute paths and `..` escapes are rejected. Cancellation is
checked while traversing large collections. The 32,768 expanded-node, 8 MiB
generated-source, and 256-variant limits are reserved for the future pattern
expander/backend; this core slice does not claim to enforce them.

Unknown nodes can be displayed and copied without becoming executable. Pattern
expansion is a later bounded operation; recursive calls are rejected before
allocation. Validation does not claim a graph renders or that Unity accepted
generated shader variants; those are backend/editor gates.

## Backend boundary

The renderer may consume `ShaderGraph` directly:

```csharp
EmissionResult Emit(ShaderGraph graph, EmitterOptions options);
```

The core owns graph meaning, validation, canonical serialization, and hashes.
ShaderLab/HLSL source generation, source maps, pass construction, and Unity
material assets remain backend/editor responsibilities. A backend must use the
same validator and report source locations back to stable node and port IDs.
