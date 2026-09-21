# Install NXSG with VPM

**[Open the package listing](https://nerdrx.github.io/nxsg/)** and choose **Add to VCC**.

Manual repository URL:

```text
https://nerdrx.github.io/nxsg/index.json
```

**Repository added, but no package appears?** Enable prerelease packages in ALCOM or Creator Companion. All current NXSG releases are alpha versions.

## Creator Companion

1. Open **Settings → Packages → Add Repository**, paste the URL and confirm the listed repository.
2. Enable **Show Pre-Release Packages**. The current package is `0.1.0-alpha.11`.
3. Manage your Unity project, locate **NX Shader Graph**, and add it.
4. Open Unity and choose **Tools → NXSG → Open Graph Editor**.

Linux users can add the same JSON URL in a VPM-compatible manager. The website's button uses the `vcc://vpm/addRepo` protocol and needs a registered handler; it is not a browser download button.

The package targets Unity **2022.3.22f1**, PC Built-In rendering. Newtonsoft JSON **3.2.1** remains a Unity package dependency. NXSG does not depend on VRChat SDK C# APIs, so it does not force an SDK replacement, install VRCFury or install AudioLink. Start with a test project. Windows/D3D, headset stereo and VRChat client acceptance remain unverified.

Import **Example Graphs** from NXSG's entry in Unity Package Manager, or copy `.nxsg` files from the package's `Samples~` directory into Assets. Build for VRChat creates local shaders/materials; avatar upload remains a separate SDK step.

## Package delivery

The listing and install page are served by GitHub Pages from the `vpm` directory. Package ZIPs and standalone manifests are immutable, versioned GitHub Release assets. Each archive contains `package.json` at its root and preserves Unity `.meta` GUIDs. The listing records the archive's SHA-256.

This is an independent community listing. It is not a VRChat-curated package or a claim of client validation. Source license selection remains pending, as described in the main README.

## Publish another version

1. Update the package manifest's `version` and matching release `url`. Commit package changes, including any new files and `.meta` files.
2. Run `python3 scripts/build-vpm.py`. It builds from tracked package files, writes the ZIP and standalone `package.json` under `work/vpm`, and adds the version to `vpm/index.json`. The builder rejects changing a previously indexed version.
3. Test the generated package. Create the matching Git tag and GitHub prerelease (or release, when appropriate), uploading the ZIP and standalone `package.json`. Do not overwrite assets of a published version.
4. Run `python3 scripts/verify-vpm.py vpm/index.json` to download and verify advertised archives.
5. Commit and push the updated listing. **Publish VPM listing** verifies release downloads and deploys Pages. A manual workflow dispatch can retry deployment.
6. Verify `https://nerdrx.github.io/nxsg/index.json` with the same verification script after deployment.

Keep old versions in the listing and retain their release assets so existing projects remain reproducible. Tests for deterministic packaging and version preservation live in `Tests/Packaging/test_vpm.py`.

## Verified initial release

On 2026-09-19, official VPM CLI **0.1.28** added the public NXSG repository and installed `dev.nerdrx.nxsg@0.1.0-alpha.1` into an isolated Unity test project. Prereleases and community repositories were enabled in that test configuration; the installed VPM lock entry recorded the correct version.

The public listing's ZIP passed SHA-256, CRC, archive-path and packaged-manifest checks. The downloaded package then passed the hidden Unity 2022.3.22f1/OpenGLCore `ShinyRenderSmoke`, including rendered effects, generated refraction builds and the four new example shaders. The Pages deployment completed successfully. This does not establish VCC desktop GUI, Windows or headset acceptance.

## References

Checked 2026-09-19 against [VRChat's package format](https://vcc.docs.vrchat.com/vpm/packages/), [repository format](https://vcc.docs.vrchat.com/vpm/repos/), [listing guide](https://vcc.docs.vrchat.com/guides/create-listing/), and [community repository instructions](https://vcc.docs.vrchat.com/guides/community-repositories/). [Poiyomi's listing](https://github.com/poiyomi/vpm) was a structural reference; its site and implementation were not copied.

## Alpha.2 delivery check

The published alpha.2 ZIP was downloaded again, matched its SHA-256, and passed `UsabilitySmoke` in isolated Unity 2022.3.22f1/OpenGLCore, including synthetic Ctrl+Z routing. This supplements the source-tree render and parameter checks recorded in VALIDATION.md.
