# NXSG examples

Copy `Animated Palette.nxsg` into your project's Assets folder, open it, and choose **Build for VRChat**. The graph blends warm and cool colors using animated noise, with a small emission contribution. The Time node controls animation speed. Emission adds unlit color; a bloom halo depends on the world's post-processing.

Noise currently animates by moving through a smooth noise field. It is not true 4D noise. This is a PC Built-In prototype; mobile and live VRChat validation remain separate.
