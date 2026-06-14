Thanks for downloading the Staff Scanner V2! As a reminder, this is REQUIRED for all Beasts at Club Maul, and highly recommended for Trial Beasts.

INSTALLATION:
- Drag and drop the "Staff Scanner V2" prefab into an empty space in your hierarchy, THEN onto your avatar
- Under "Source Renderers", drag and drop all meshes that you would like to appear to others using the scanner (Your body is the important one)
- Pick your role in the dropdown (Beast, Security, or Photography)
- Adjust "Decimation Amount" to your liking (0.5 is recommended)
- Under "World Features", tick any features you want (e.g. Slow, Rumble, Unique). Each checked box adds that feature's contact sender plus an in-game menu toggle when you build, so you can switch it on/off and the Club Maul world can react. Leave them unchecked to skip.
- All done!

HOW TO USE:
- Navigate to "Staff Scanner V2" in your avatar menu
- "Broadcast Self" to let others using the scanner see you
- "See Others" to show others using the scanner
- Each World Feature you enabled (e.g. Slow, Rumble, Unique) gets its own toggle in this menu — flip it to turn that feature's contact on or off

HOW DOES IT WORK?
- When you build your avatar, the script creates a duplicate of all source renderers and heavily decimates them before disabling them
- Then, it applies the StaffScanner material, which is a Poiyomi material that is visible through walls and fades away when at a certain distance
- Two contacts are constrained to the world origin; a receiver, and a sender
- The sender is disabled by default, and is enabled only locally when the "See Others"(ClubMaul/Sender) parameter is enabled
- When enabled, the local player will detect contact receivers sent out by others with the Staff Scanner on, which from the local player's view enables the newly created mesh