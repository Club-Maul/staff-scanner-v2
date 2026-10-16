# Staff Scanner V2

The Staff Scanner V2 for **Club Maul**. This is **REQUIRED** for all Beasts at Club Maul, and highly recommended for Trial Beasts.

## Installation

1. Install the package through the VRChat Creator Companion. The VPM listing can be found [here](https://club-maul.github.io/vpm-listing/).
2. In the top menu bar, navigate to `Tools > Club Maul` and click `Initialize Staff Scanner`.
3. Move the newly spawned **Staff Scanner V2** onto your avatar in the Hierarchy window.
4. Under **Source Renderers**, drag and drop all meshes that you would like to appear to others using the scanner (your body is the important one).
5. Pick your role in the dropdown (**Beast**, **Security**, or **Photography**).
6. Adjust **Decimation Amount** to your liking (`0.5` is recommended).
7. Under **World Features**, tick any features you want (e.g. Slow, Rumble, Unique). Each checked box adds that feature's contact sender plus an in-game menu toggle when you build, so you can switch it on/off and compatible worlds can react. Leave them unchecked to skip.
8. All done!

## How to use

- Navigate to **Staff Scanner V2** in your avatar menu.
- Turn on **Broadcast Self** to let others using the scanner see you.
- Turn on **See Others** to show others using the scanner.
- Each World Feature you enabled (e.g. Slow, Rumble, Unique) gets its own toggle in this menu — flip it to turn that feature's contact on or off.

## How does it work?

- When you build your avatar, the script creates a duplicate of all source renderers and heavily decimates them before disabling them.
- It then applies the StaffScanner material, a Poiyomi material that is visible through walls and fades away at a certain distance.
- Two contacts are constrained to the world origin: a receiver and a sender.
- The sender is disabled by default, and is enabled only locally when the **See Others** (`ClubMaul/Sender`) parameter is enabled.
- When enabled, the local player detects contact receivers sent out by others running the Staff Scanner, which from the local player's view enables the newly created mesh.

## Requirements

- VRChat Avatars SDK (`com.vrchat.avatars`) 3.7.0 or newer
- VRCFury (`com.vrcfury.vrcfury`)
