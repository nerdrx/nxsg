# NXSG development

NXSG is developed and tested primarily on Linux. The current compatibility
tuple is Unity **2022.3.22f1**, VRChat Base/Avatars **3.10.5**, and the pinned
`com.unity.nuget.newtonsoft-json` **3.2.1**. The first implementation is a
small PC VRChat Built-In texture/toon backend. It is not yet a complete avatar
tool, mobile shader adapter, or proof of VRChat client compatibility.

## First-time setup

Open Unity Hub first and open `DevProject` with the installed
`2022.3.22f1` Editor. Hub starts its supported licensing helper; the direct
bundled licensing client previously failed with `Unable to read ProductVersion
from:`. The default Hub-connected launch works with helper **1.17.4**, so do
not add a hand-written licensing IPC name to routine commands.

The current machine discovers the Editor at:

```text
/run/media/nerdrx/69452f8b-e527-4a95-8396-66b2901dd59d/home/nerdrx/Unity/Hub/Editor/2022.3.22f1/Editor/Unity
```

The path is on a mounted volume and may differ on another checkout. The test
runner reads `~/.config/unityhub/editors-v2.json` and finds the entry by exact
version. Set `UNITY_EDITOR` explicitly when autodiscovery cannot find it:

```bash
export UNITY_EDITOR=/path/to/2022.3.22f1/Editor/Unity
```

On this rolling Linux host, Unity needs `libxml2.so.2`, while the system
package exposes a newer soname. An isolated compatibility directory was
prepared under `work/unity-libs/usr/lib`; it is an optional environment
override, not a global package install:

```bash
export NXSG_UNITY_LIBS="$PWD/../../work/unity-libs/usr/lib"
```

Only set that variable when the directory exists and the Editor loader needs
it. Keep the directory local and review its provenance before reproducing this
setup elsewhere. The Linux host has an AMD Radeon RX 7900 XTX with Mesa
OpenGL/Vulkan support, but a detected GPU is not shader or VRChat evidence.

Install the verified SDK fixture explicitly from the repository root:

```bash
python3 scripts/setup-vrchat-fixture.py
```

The script downloads the official Base and Avatars 3.10.5 archives into
`work/sdk`, checks their published SHA-256 values, rejects unsafe archive
entries, and installs only clean package directories. Extracted
`DevProject/Packages/com.vrchat.base/` and
`DevProject/Packages/com.vrchat.avatars/` are ignored by Git. Re-running the
script verifies newly extracted archives and preserves existing marker-owned packages at the pinned version. If Unity generated settings or changed metadata, the script reports the changed tree without overwriting it. Unknown package directories and changed package versions are rejected. Running Unity does not download the SDK.

After the first import, retain the generated `Packages/packages-lock.json` as
test evidence. The fixture includes local Base/Avatars packages and the Unity
dependencies declared by Base. No VRCFury or AudioLink package is installed
by this setup.

## Checks

Run the portable graph/core and backend checks with the local .NET 8 SDK when
the system has no SDK on `PATH`:

```bash
DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  /path/to/work/dotnet/dotnet run \
  --project Tests/Portable/NXSG.Portable.Tests.csproj
```

The current machine has that SDK at
`../../work/dotnet/dotnet` relative to this repository. This runner exercises
the shared core/backend sources outside Unity; it cannot prove Unity import,
ShaderLab compilation, graphics output, SDK upload, or VR behavior.

The Unity runner is `scripts/unity-check.py` and accepts three modes:

```bash
python3 scripts/unity-check.py compile
python3 scripts/unity-check.py smoke
python3 scripts/unity-check.py editor
```

`compile` uses Unity batchmode with `-nographics` for package import, domain
reload, and compiler evidence. Import success alone is not graphics shader
compilation proof. `smoke` runs the editor inside headless Gamescope with
OpenGL forced, invokes `NxsgSmoke.Run`, and records JSON/PNG evidence under
`DevProject/Assets/SmokeResults/` plus logs under `work/unity/`. It requires a
graphics-enabled editor and performs explicit shader/material and
RenderTexture checks. `editor` is a hidden Gamescope UI check that invokes
`NxsgSmoke.OpenEditor`; it is useful for automation and canvas startup, not a
visible editing session. Gamescope does not establish native Windows DX11,
headset, or live VRChat behavior.

For normal visible editing, open `DevProject` through Unity Hub. The custom
canvas currently covers the small graph workflow: create/open/save, search and
add nodes, connections, pan/zoom, selection, undo, and local Build. The
prototype backend remains limited to a single texture/toon surface and its
current generated material contract.

## What to record

Every meaningful check should retain the Unity Editor version and revision,
Linux graphics API/device, SDK package versions, `packages-lock.json`, command,
exit status, Editor log, and generated JSON/PNG where applicable. Keep
`-nographics` import results, graphics smoke results, SDK validation, and live
client/VR results as separate records. A Linux editor result does not prove
the PC client’s DX11 path, Proton equivalence, mobile shader policy, upload
acceptance, or headset rendering.

No live VRChat client, avatar upload, login, mobile device, or headset result is
currently claimed. The next integration gate is a clean SDK project with a
representative avatar, explicit target-variant compilation, SDK Build & Test,
and a separately recorded Linux client route if VRChat is exercised through
Proton. Native Windows validation remains a later portability check.

### Closing the hidden editor

For a running `editor` check, create `work/unity/close-editor` from the repository root. The fixture discards unsaved test-window edits and exits. This signal applies only to the development smoke entry point. Use the visible Undo/Redo buttons for graph history; keyboard undo has not been validated on this Linux setup.
