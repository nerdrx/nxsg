# NXSG contributor guidance

Read `NXSG_DESIGN.md`, `docs/DECISIONS.md`, and `docs/IMPLEMENTATION_PLAN.md` before implementation. This repository starts in the research phase; proposed features are not implemented capabilities.

- Prefer the smallest maintainable implementation that satisfies the current milestone. Keep graph meaning independent of Unity UI types.
- Research technical claims against primary documentation or source; record versions, source revisions, and the retrieval date. Label recommendations and unresolved assumptions.
- Use inexpensive Luna agents for independent research and review when delegation is requested. Give each agent a bounded topic and separate file ownership; integrate and resolve conflicting findings centrally.
- Keep repository prose clear and professional. Put conversational style in chat, not persisted documents.
- Preserve stable graph, node, socket, property, asset, and Unity metadata identities. Do not silently migrate, overwrite, or remove user data.
- Treat imported graphs and node packs as untrusted inputs. Raw HLSL and C# packages are not sandboxed declarations.
- Run checks appropriate to the change. Separate pure-core checks, Unity imports, graphics rendering, SDK validation, and live VR evidence.
- When appropriate, use headless Gamescope for Linux GUI checks without disturbing the desktop. It cannot establish Windows DX11 or headset compatibility.
- Do not import upstream source without reviewing file-level licenses and retaining required notices. Never infer all dependencies share a repository's root license.
