# NXSG 2.3.0

## Import Poiyomi and lilToon materials

Select a material asset, then choose **Tools → NXSG → Import Poiyomi or lilToon material…**. NXSG creates an editable graph, builds a shader, saves a copy of the original material, and switches the original material asset to the generated shader. Avatar material slots and scene references retain the same material asset.

The import maps common albedo textures and tint, texture tiling, normal maps and strength, emission, clipping mask, cutoff, culling, and opaque/cutout/transparent render mode. An optional PBR surface also maps basic metallic and roughness inputs. A text report beside the graph lists properties that need manual review. Complex source-specific layers and effects are not reproduced automatically; compare the result before upload, and keep the backup until satisfied.

Install or update **NX Shader Graph** through the [VPM listing](https://nerdrx.github.io/nxsg/index.json). This is a stable package; prerelease packages need not be enabled.
