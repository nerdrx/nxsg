# NXSG validation and release research

**Research date:** 2026-09-17  
**Scope:** reproducible Unity/VPM validation, platform evidence, packaging, CI safety, and release gates for NXSG.  
**Evidence labels:** **Verified** is source-backed; **Recommendation** is an NXSG policy; **Spike** requires an implementation experiment. Buckets are **S** small, **M** medium, **L** large.

## Executive decision

NXSG can automate portable graph, compiler, serialization, package, and editor tests without proving VRChat rendering. The release gate needs a supported Windows Unity Editor run using VRChat’s exact baseline: Unity **2022.3.22f1**, PC/Mac/Linux Standalone switched to **DX11**, and a pinned VPM dependency graph. VRChat’s creator guide tells users to use Windows for a first project and to check that the Unity title bar ends in `<DX11>`. ([VRChat getting started](https://creators.vrchat.com/sdk/), [current Unity version](https://creators.vrchat.com/sdk/upgrade/current-unity-version/))

Linux is useful for source-level checks and package resolution, but it is not evidence of Windows DX11 behavior. Unity 2022.3 lists Windows graphics support as DX10/DX11/DX12 and Linux support as OpenGL 3.2+ or Vulkan. The root environment has `/usr/bin/unityhub`, headless-capable `gamescope` 3.16.25, and .NET runtimes 8/9 but no SDK; an Editor was not found in checked Unity paths. A Windows/headset validation environment has not been established. Headless Gamescope does not change this boundary. ([Unity 2022.3 system requirements](https://docs.unity3d.com/2022.3/Documentation/Manual/system-requirements.html), [Unity graphics API arguments](https://docs.unity3d.com/2022.3/Documentation/Manual/EditorCommandLineArguments.html))

## 1. Editor tests, batch mode, and licensing

**Verified.** Unity’s command-line documentation describes `-batchmode`, `-quit`, `-projectPath`, `-executeMethod`, `-buildTarget`, `-accept-apiupdate`, and `-logFile`; failures return exit code 1. `-nographics` avoids graphics-device initialization, but cannot bake Enlighten GI; this is parsing/emission/import evidence only, not proof that a GPU shader compiler ran or rendering works. ([Unity Editor command-line arguments](https://docs.unity.cn/Manual/EditorCommandLineArguments.html))

**Verified.** Unity says shader import performs minimal processing; variants compile when needed for display and remaining variants at build. **Recommendation:** treat import success/no console errors as insufficient; invoke explicit target-variant compilation and record its output. ([Shader compilation](https://docs.unity3d.com/2022.3/Documentation/Manual/shader-compilation.html))

**Verified.** Unity Test Framework supports EditMode and PlayMode tests. The package documentation lists `com.unity.test-framework` 1.1.33 as released for Unity 2022.3. Command-line tests use `-runTests`, `-testPlatform EditMode|PlayMode`, `-testResults`, and optional filters. ([Unity Test Framework](https://docs.unity3d.com/2022.3/Documentation/Manual/com.unity.test-framework.html), [Test Framework command-line reference](https://docs.unity3d.com/Packages/com.unity.test-framework@1.1/manual/reference-command-line.html))

**Recommendation.** Keep the first CI lane `-batchmode -nographics -runTests -testPlatform EditMode`; save XML, Editor log, lock, Unity version, OS, and SHA. Record parsing/emission/import evidence unless explicit shader-compiler output is captured. Add a graphics lane only on a known Windows runner with DX11. Do not hide compiler errors with `-ignorecompilererrors` or auto-mutate the pinned fixture with `-accept-apiupdate`; fail visibly and review API changes.

**Licensing boundary.** Unity documents command-line serial activation and `-returnlicense` in its command-line reference, but credentials must never be placed in a pull-request workflow or command line visible to other processes. Prefer a dedicated, trusted release environment with an appropriate Unity license and short-lived/isolated credentials. An Editor was not found in the checked paths, so local batch testing is not established. Installing tools or activating a paid/managed license is outside this research task. ([Unity licensing command-line options](https://docs.unity.cn/2020.1/Documentation/Manual/CommandLineArguments.html))

## 2. Windows DX11 versus Linux proof

**Verified.** VRChat instructs creators to switch to `PC, Mac & Linux Standalone` and verify `<DX11>` in the Unity title bar. Unity’s Editor accepts `-force-d3d11` only on Windows; `-force-vulkan` and OpenGL options represent different renderer paths. Unity’s 2022.3 GPU Usage Profiler documentation says Windows PlayMode profiling in the Editor is supported with Direct3D 11/12, while Linux has OpenGL Core support and Vulkan is not supported by that profiler. ([VRChat SDK setup](https://creators.vrchat.com/sdk/), [Unity command-line graphics options](https://docs.unity3d.com/2022.3/Documentation/Manual/EditorCommandLineArguments.html), [GPU Usage Profiler](https://docs.unity3d.com/2022.3/Documentation/Manual/ProfilerGPU.html))

**Recommendation.** Label evidence records by renderer: `backend-only`, `Linux-OpenGL/Vulkan`, `Windows-DX11`, `Quest/Android-device`, or `iOS-device`. A Linux `-nographics` shader compile can prove syntax, graph lowering, and asset generation. It cannot prove DX11 driver compilation, stereo rendering, mirror behavior, VRChat fallback handling, or VRChat upload acceptance. A Windows DX11 editor test can close some of those gaps, but only VRChat Build & Test or in-client viewing proves the final avatar behavior.

**Spike (M).** Create one tiny graph fixture and one representative avatar fixture. On Windows, run Unity 2022.3.22f1 with DX11 and capture title-bar/Editor.log evidence, shader compiler output, material preview, and Build & Test result. On Linux, run only the backend lane and record the active renderer. Compare generated ShaderLab and diagnostics, never screenshots as if they were interchangeable platform proof.

## 3. Supported creator OS and Linux alternatives

**Verified.** VRChat’s current Creator Companion guide says Windows 10/11 is for the full GUI, while Linux/macOS have command-line functionality. The VCC docs say the only fully supported platform at present is Windows 10 and that Linux is untested; the CLI may work on Linux. `vpm` requires the .NET 8 SDK, and its Linux setup says Unity/Hub discovery must be configured manually. ([VCC getting started](https://vcc.docs.vrchat.com/guides/getting-started/), [VCC requirements](https://vcc.docs.vrchat.com/), [VPM CLI](https://vcc.docs.vrchat.com/vpm/cli/))

The community `vrc-get` project is an alternative cross-platform VPM client and explicitly says it is community-developed, not VRChat. It supports Windows, macOS, and Linux, but does not turn Linux Unity output into Windows/DX11 evidence. Pin its provenance and version in CI. ([vrc-get repository](https://github.com/vrc-get/vrc-get))

**Recommendation.** Support Linux for NXSG’s portable compiler, JSON/schema tests, VPM lock inspection, and docs. Report “Windows Unity validation unavailable” when no supported Editor is found. Do not advertise Linux as a complete VRChat avatar authoring/upload environment. Keep a Windows runner or user-provided Windows handoff for release claims.

Observed local minimum: Unity Hub exists, but an Editor was not found in checked `$HOME/Unity` or `$HOME/UnityHub` locations; `dotnet --list-sdks` is empty; a Windows/headset validation environment has not been established. Headless Gamescope is an execution primitive, not Unity/VRChat proof.

## 4. Deterministic package payload and asset identity

**Verified.** Unity recommends a package root with `package.json`, `Editor`, `Runtime`, `Tests/Editor`, `Tests/Runtime`, `Samples~`, and `Documentation~`. Editor-specific code belongs in an Editor assembly definition; runtime code belongs in a runtime assembly; test assemblies are separate. Unity’s package guidance recommends `CHANGELOG.md`, license/third-party notices, and `documentationUrl`. ([custom package layout](https://docs.unity3d.com/2022.3/Documentation/Manual/CustomPackages.html), [assembly definitions and packages](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-asmdef.html), [package layout](https://docs.unity3d.com/2022.2/Documentation/Manual/cus-layout.html))

`Samples~` and `Documentation~` are ignored by Unity’s package importer unless copied or referenced, making them appropriate for optional examples and docs. Keep tests out of a creator runtime payload; strip them from production only if the chosen VPM workflow supports it. Verify the resulting zip contents instead of assuming tilde folders are enough.

**Verified.** Unity’s `.meta` files contain asset GUIDs and import settings. Losing a `.meta` breaks references; moving or renaming outside the Unity Project window without moving its `.meta` can create a new GUID and orphan old references. ([Unity asset metadata](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetMetadata.html))

**Recommendation.** Release archives must be deterministic: normalized paths, stable ordering/JSON, and no `Library/`, `Logs/`, absolute machine paths, caches, credentials, or temporary previews. Preserve `.meta` files and verify GUID stability across two clean extractions. Reuse generated `.shader` and `.mat` paths and `.meta` files after successful validation.

Unity Package Manager writes `Packages/packages-lock.json` after resolving dependencies and uses it for deterministic results. Commit the lock file for NXSG’s test fixture. Git dependencies should be pinned to immutable commit hashes; Unity warns that branch/tag references can move and that an unlocked dependency can become non-deterministic. ([UPM lock files](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-conflicts-auto.html), [Git dependencies](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-git.html))

## 5. Generation rollback and material preservation

**Recommendation.** NXSG’s build transaction should be two-phase:

1. Snapshot graph revision, output paths, existing `.meta` GUIDs, material property values, texture/object references, and generated-file hashes.
2. Validate graph, target capabilities, dependencies, and generated ShaderLab in a temporary staging folder.
3. Import/compile staging assets and run structural checks.
4. Promote files individually with a recovery journal, reusing existing asset paths and `.meta` files where identity must remain stable.
5. Reimport and validate the promoted set; if final import fails, restore the recorded backup and journal the recovery. Unity multi-file promotion must not be assumed atomic.

Material defaults and user assignments are data. Compiler regeneration must use a schema-aware migration, report removed properties, and require an explicit map for renamed properties. This is an NXSG requirement, not a Unity/VRChat guarantee.

**Spike (M).** Build a fixture with texture, color, float, keyword, animation binding, material variant, and a missing asset. Rebuild after graph edits, simulated compiler failure, package upgrade, and output move. Record GUIDs, serialized material values, generated hashes, and whether the last-good output remains loadable after each failure.

## 6. CI trust boundaries and provenance

**Verified.** GitHub says forked `pull_request` workflows receive a read-only token and no repository secrets. It warns that `pull_request_target` has access to secrets and becomes unsafe if it checks out and executes fork code. GitHub also states that secret masking is not a complete security boundary. ([compromised runners](https://docs.github.com/en/actions/concepts/security/compromised-runners), [workflow secrets](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets), [secure `pull_request_target`](https://docs.github.com/en/enterprise-server@3.17/actions/reference/security/securely-using-pull_request_target))

**Recommendation.** Untrusted PRs run source/schema/package checks read-only with no Unity license, VPM credentials, or publish token. Trusted releases run from a protected branch/tag and check out only trusted code. A tagged, checksummed release is sufficient initially; signatures and protected approval environments are optional hardening. Never switch a PR workflow to `pull_request_target` while checking out the PR head.

Maintain a dependency provenance record: package name/version, source URL, commit or release, SHA256, license identifier, notices, and whether it is runtime, editor-only, test-only, or sample-only. Unity requires/recommends package license and third-party notice files; NXSG should preserve upstream notices even when dependencies are not redistributed in the final avatar. ([Unity custom packages](https://docs.unity3d.com/2022.3/Documentation/Manual/CustomPackages.html), [VPM package format](https://vcc.docs.vrchat.com/vpm/packages/))

**VRCFury release pin (read-only check, 2026-09-17).** The VPM listing at [`https://vcc.vrcfury.com`](https://vcc.vrcfury.com) reports `com.vrcfury.vrcfury` **1.1429.0**, Unity `2022.3`, download `https://vcc.vrcfury.com/download/1.1429.0`. The matching release tag, published 2026-09-07, is commit `0e1b9217bf6b491e4cada2eba4145553e89599ce`; its tagged manifest reports the same version. Moving `main` reports `0.0.0`, not a release pin. The listing exposes no package SHA256; record the archive hash in NXSG’s fixture. ([VCC listing](https://vcc.vrcfury.com), [GitHub release](https://github.com/VRCFury/VRCFury/releases/tag/com.vrcfury.vrcfury/1.1429.0), [tagged manifest](https://github.com/VRCFury/VRCFury/blob/0e1b9217bf6b491e4cada2eba4145553e89599ce/com.vrcfury.vrcfury/package.json))

## 7. Prioritized verification matrix

| Priority | Fixture and environment | Pass condition | Required record |
|---|---|---|---|
| P0 | Portable graph/compiler fixtures; Linux or any .NET host | Stable serialized graph, deterministic ShaderLab/report, migration and malformed-input tests pass | Commit, schema/compiler versions, golden hashes, test XML |
| P0 | Package zip extracted twice; no Unity required | Expected payload only; stable `package.json`, `.meta`, notices, docs/samples/tests boundaries | File manifest, SHA256, GUID list, license ledger |
| P0 | Clean Unity 2022.3.22f1 EditMode project; `-batchmode -nographics` | Import, domain reload, compiler, graph asset, rollback and material-preservation tests pass | Unity log, `packages-lock.json`, XML results, OS/Editor version |
| P1 | Windows Unity 2022.3.22f1, PC/Mac/Linux target with DX11 | Same fixture imports and previews without shader/compiler errors | Title-bar/DX11 evidence, Editor log, shader compiler output, screenshots |
| P1 | Windows VRChat SDK avatar Build & Test | Avatar loads, animation properties work, fallback and validation messages are correct | SDK version, target, build report, test avatar result |
| P1 | Android/Quest device Build & Test | Mobile-reduced material passes policy/validation and renders on hardware | SDK/platform, device, texture settings, captured result |
| P2 | iOS VTP Build & Test | iOS fixture builds/tests; differences documented | SDK/platform, device/app version, VTP log, visual record |
| P2 | Trusted release workflow, pinned dependencies | Package, docs, notices, changelog, checksums and tagged release are reproducible | Workflow run, artifact hashes, provenance/SBOM, release record |

Automated backend and Editor tests can establish deterministic generation, package integrity, and rollback. They cannot establish human-visible quality, VR stereo parity, mirror behavior, Quest/iOS policy acceptance, or upload success. Those require live Windows/VRChat/device records.

## Practical blockers and source ledger

The immediate blockers are that an Editor was not found in the checked paths, the .NET SDK is missing for the official VPM CLI, and a Windows/headset validation environment has not been established. Do not install or activate them implicitly. Local environment can still run source-level checks, package-zip audits, and deterministic compiler tests once implementation exists.

| Source | Fact used | Checked |
|---|---|---|
| VRChat Creator Docs | Windows-first setup, `<DX11>`, Unity 2022.3.22f1 | 2026-09-17 |
| VRChat VCC docs | Windows fully supported; Linux CLI untested/partial; .NET 8 SDK for `vpm` | 2026-09-17 |
| Unity 2022.3 manuals | Batch mode, test framework, graphics APIs, package layout, `.meta`, lock files | 2026-09-17 |
| GitHub Actions docs | Fork PR secret/token restrictions and `pull_request_target` risks | 2026-09-17 |
| VRCFury VCC/GitHub | Published `com.vrcfury.vrcfury` 1.1429.0; tag commit `0e1b921...`; moving main is `0.0.0` | 2026-09-17 |
| Local observation | `/usr/bin/unityhub`; no checked Editor; .NET runtimes 8/9 but no SDK; headless Gamescope available; no graphics tests | 2026-09-17 |
