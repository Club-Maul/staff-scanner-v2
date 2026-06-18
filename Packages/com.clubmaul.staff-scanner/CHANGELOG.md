# Changelog

All notable changes to this package are documented here. This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
