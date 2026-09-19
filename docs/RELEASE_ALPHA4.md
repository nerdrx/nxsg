# NXSG alpha.4 — optional LTCGI lighting

Adds LTCGI Lighting to the Surface node menu, with albedo, tangent-space normal, roughness, metallic and strength inputs. Connect Color to Emission; use Add to combine existing emission. Includes an LTCGI Receiver example and setup guide.

Install LTCGI separately. Graphs using the node show an actionable error if it is absent; ordinary graphs have no new dependency. Generated shaders use avatar mode and advertise the receiver tag. An active LTCGI world/controller is still required.

Validated against LTCGI 1.7.3 using its real controller in isolated Linux Unity 2022.3.22f1/OpenGLCore. On/off, zero strength, finite darkness and ForwardAdd isolation passed. This is not live VRChat, Windows or headset validation.

LTCGI by _pi_: https://github.com/PiMaker/ltcgi — NXSG includes the installed API rather than bundling its source. Setup: docs/LTCGI.md.
