# Staff Scanner V2 — VPM Listing

This repository hosts the **Staff Scanner V2** package for [Club Maul](https://github.com/Club-Maul) and a [VPM](https://vcc.docs.vrchat.com/vpm/) listing so it can be installed and updated through the VRChat Creator Companion (VCC) or ALCOM.

## Install via VCC / ALCOM

1. Add this listing to your VCC:

   **[➕ Add to VCC](https://Club-Maul.github.io/staff-scanner-v2/)** — or paste the listing URL manually:

   ```
   https://Club-Maul.github.io/staff-scanner-v2/index.json
   ```

2. Open your avatar project, go to **Manage Project**, and add **Staff Scanner V2**.
3. See the [package README](Packages/com.clubmaul.staff-scanner/README.md) for setup and usage.

## Requirements

- VRChat Avatars SDK 3.7.0+
- VRCFury

## Repository layout

- `Packages/com.clubmaul.staff-scanner/` — the package itself.
- `.github/workflows/` — automation that builds the release `.zip`/`.unitypackage` and publishes the VPM listing to GitHub Pages.
- `Website/` — source for the listing page served at the URL above.

## Releasing a new version

1. Bump `version` in [`Packages/com.clubmaul.staff-scanner/package.json`](Packages/com.clubmaul.staff-scanner/package.json) and add a `CHANGELOG.md` entry.
2. Commit and push to `main`.
3. Run the **Build Release** workflow (Actions tab → Build Release → Run workflow). It tags the version, builds the artifacts, and publishes a GitHub Release.
4. The **Build Repo Listing** workflow then regenerates the listing and redeploys the Pages site automatically.

---

Built from VRChat's [template-package](https://github.com/vrchat-community/template-package).
