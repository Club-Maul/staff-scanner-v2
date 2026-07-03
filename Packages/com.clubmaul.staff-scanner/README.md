# Staff Scanner V2

The Staff Scanner V2 for **Club Maul**. This is **REQUIRED** for all Beasts at Club Maul, and highly recommended for Trial Beasts.

## Installation

1. Drag and drop the **Staff Scanner V2** prefab into an empty space in your hierarchy, **then** onto your avatar.
2. Under **Source Renderers**, drag and drop all meshes that you would like to appear to others using the scanner (your body is the important one). Left empty, your body mesh is auto-detected — the inspector shows which one.
3. Pick your role in the dropdown (**Beast**, **Security**, **Photography**, or **Host**).
4. Adjust **Decimation Amount** to your liking (`0.5` is recommended — the inspector previews the resulting triangle count).
5. Beasts only: the **Universal** features (**Slow** and **Rumble**, on by default) each add a contact sender plus an in-game menu toggle when you build, so you can switch them on/off and compatible worlds can react. Untick them to skip. **Plugins** (e.g. the bundled Suburbia asset) add more world contacts the same way.
6. All done!

## How to use

- Navigate to **Staff Scanner V2** in your avatar menu.
- **Broadcast Self** to let others using the scanner (V2 or the old V1) see you.
- **See Others** to show others using the scanner (V2 wearers' meshes, V1 wearers' orbs).
- **Sphere View** to show other scanners as a small hips-centered sphere instead of the full mesh — only you see the change.
- Each Universal feature or plugin contact you enabled gets its own toggle in this menu — flip it to turn that contact on or off.

## How does it work?

- When you build your avatar, the script creates a duplicate of all source renderers and heavily decimates them before disabling them.
- It then applies the StaffScanner material, a Poiyomi material that is visible through walls and fades away at a certain distance.
- A group of contact senders and receivers is pinned to the world origin, so every scanner user's contacts always overlap.
- Visibility is decided per viewer: **See Others** enables local-only senders that exist solely on your own client, and each wearer's always-on receivers turn them into non-synced per-viewer parameters that gate the scanner mesh in that wearer's FX layer. Only scanner wearers carry those senders, so players without the scanner can never see the mesh.
- **Broadcast Self** drives the synced `ClubMaulShow` parameter, which must also be on for anyone to see you.

## Requirements

- VRChat Avatars SDK (`com.vrchat.avatars`) 3.7.0 or newer
- VRCFury (`com.vrcfury.vrcfury`)

The optimized (locked) Poiyomi shaders are bundled with the package, so Poiyomi does not need to be installed to use the included materials.
