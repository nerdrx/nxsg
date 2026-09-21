# NXSG alpha.16 — stable Panosphere filtering

Fixes the Panosphere speckling regression introduced in alpha.14. Longitude chart
selection now has a tolerance so floating-point noise cannot switch charts in
smooth regions. GPU tests reproduce the regression and verify the fix, including
fractional tiling away from the wrap.

Polar UVs now labels its source selector **Input coordinates** and explains that
Polar is applied after that source. For ordinary Panosphere, choose **Panosphere**
from the node header; choosing it inside Polar produces a combined projection.

Update and Build for VRChat to regenerate existing shaders. Includes cards-only
fur from alpha.15. Noninteger tiling across the longitude wrap and nonlinear
operations after Panosphere can still create discontinuities; see PANOSPHERE.md.
