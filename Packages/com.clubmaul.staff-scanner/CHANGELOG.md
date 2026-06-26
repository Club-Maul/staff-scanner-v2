# Changelog

All notable changes to this package are documented here. This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.1] - 2026-06-26

### Added

- Cross-version visibility with the old V1 scanner: the V2 presence beacon now also answers V1's contact tag, so anyone still on the V1 scanner can see V2 staff. V2's own staff-only filtering is unchanged. (Staff must re-upload to apply.)

## [1.3.0] - 2026-06-26

### Changed

- The **Slow** and **Rumble** world features are now enabled by default. (They only ever apply to the Beast role, so this is effectively "on by default for Beasts.")
- Role materials are now resolved automatically at build time, so the component no longer has per-role Material fields to wire up — dropping it onto an avatar (or adding it manually) just works. The inspector was tidied up with grouped headers in the process.

## [1.2.9] - 2026-06-24

### Fixed

- **See Others** now actually controls what you see. Previously, turning it off still left other staff's scanner meshes visible to you, because your view was gated by an always-on "viewer is staff" sender while the toggle only affected your own broadcast. The toggle now drives that per-viewer staff sender directly: off hides all other scanner meshes for you, on shows broadcasting staff again. Staff-only filtering is unchanged — the gate's contacts are the same proven design, so non-staff still never see the scanner. The presence beacon other wearers detect is now always-on (it never exposes its own wearer). (Staff must re-upload to apply.)

## [1.2.8] - 2026-06-24

### Fixed

- Reverted the 1.2.7 visibility rework, which broke staff-only filtering — non-staff players could see the scanner mesh again. Restored the 1.2.6 contact/parameter design (network-synced show param plus the per-viewer "viewer is staff" gate). (Staff must re-upload to apply.)

## [1.2.7] - 2026-06-24

### Fixed

- The **See Others** toggle had no effect on what you saw. Scanner visibility used a network-synced parameter driven by a networked sender, so it was decided globally rather than per-viewer — toggling See Others only flipped your own broadcast and never revealed or hid other staff for you. Visibility is now resolved per-viewer: **See Others** is a local-only sender that reveals other staff's scanners to you alone, and turning it off hides them for you only. Staff-only visibility is preserved — it's now inherent (only scanner wearers carry the local sender), so the separate "viewer is staff" gate added in 1.2.6 was removed. (Staff must re-upload to apply.)

  > **Note:** 1.2.7 was faulty — the local-only-sender approach did not actually preserve staff-only filtering. Superseded by 1.2.8.

## [1.2.6] - 2026-06-19

### Fixed

- Scanner visuals are now visible to staff only. Non-staff players could see the scanner mesh/sphere; visibility is now gated on a non-synced "viewer is staff" contact param (a local-only staff sender + always-on receiver per wearer), so only players who also have the scanner see them. (Staff must re-upload to apply.)

## [1.2.5] - 2026-06-19

### Added

- New **Host** staff role, with its own `StaffScannerHost` material wired into the prefab by default.
- Inspector now shows a live **triangle-count preview** of the mesh after decimation (exact, updates as you drag the slider), and an info box naming the **auto-detected mesh** that will be used when no Source Renderer is set.

### Changed

- Clearer Decimation Amount tooltip: 0 = less decimation (more triangles), 1 = more decimation (fewer triangles).

## [1.2.4] - 2026-06-18

### Fixed

- Sphere View sphere is now a fixed world size on every avatar — the Hips bone's scale is divided out, so a scaled armature no longer makes the sphere too big or too small.

## [1.2.3] - 2026-06-18

### Changed

- Reworded the package description and README: the contact senders/receivers and menu toggles are reacted to by "compatible worlds" rather than "the Club Maul world".

## [1.2.2] - 2026-06-18

### Added

- **Sphere View** menu toggle: when enabled, other staff scanners render as a small hips-centered sphere (same role material) instead of the full mesh — a lighter, clearer at-a-glance view. It's per-viewer (only you see the change), built on a local-only contact sender plus an always-on receiver driving a non-synced parameter.

## [1.2.1] - 2026-06-18

### Fixed

- Scanner mesh never appeared in-game: the broadcast contact sender was local-only, so other players' receivers could never detect it across the network (it only worked in-editor, where the show parameter was driven directly). The sender is now networked.
- The wearer could see their own scanner mesh. The FX "On" state is now gated on the built-in `IsLocal` parameter, so the mesh is hidden from its wearer while staying visible to everyone else.

## [1.2.0] - 2026-06-14

### Added

- **Plugins**: drag-in `StaffScannerPlugin` assets with an editable contact list — each contact has a name, a collision tag, and a Toggle/Button control type. On build, each plugin's contacts generate world-locked contact senders and menu toggles under their own menu sub-folder. Beast role only.
- Bundled **Suburbia** plugin (contacts `Suburbia/Flicker` and `Suburbia/Sound`).
- Inspector **Add Plugin** dropdown that discovers plugin assets across the project and packages (so package-shipped plugins don't have to be hunted down), plus a **Create New Plugin** option.
- Configurable **Menu Path** controlling where the Staff Scanner toggles are written in the avatar menu.
- Staff Scanner menu folder icon (Club Maul flames), applied via VRCFury's Override Menu Icon feature.

### Changed

- Renamed the **World Features** section to **Universal** and removed the **Unique** toggle.

## [1.1.0] - 2026-06-14

### Added

- **Tools > Club Maul > Initialize Staff Scanner** menu item that spawns the Staff Scanner V2 prefab into the active scene at the world origin.
- **Club Maul > Staff Scanner** entry in the Hierarchy right-click (GameObject) menu that does the same. Both make the package usable without dragging the prefab from a Project folder.

## [1.0.0] - 2026-06-14

### Added

- Initial VPM release of the Staff Scanner V2.
- Avatar prefab with role selection (Beast, Security, Photography), source-renderer scanning, adjustable decimation, and optional World Features (Slow, Rumble, Unique).
- Bundled optimized (locked) Poiyomi materials so Poiyomi is not required at use time.
