
--| DSR Texture Packer & Unpacker
--| https://www.nexusmods.com/darksoulsremastered/mods/9
--| https://github.com/JKAnderson/DSR-TPUP

A tool to extract and override textures in Dark Souls: Remastered.
Requires .NET 4.7.2: https://www.microsoft.com/net/download/thank-you/net472
Windows 10 users should already have this.

TPUP serves two purposes:
- To allow mod authors to unpack every texture in the game in order to edit them, and
- To allow players to easily merge and install texture packs from said authors.


--| Instructions

If you're more of a visual learner, please watch this video going over the instructions: https://youtu.be/D7zEDHe-Acw
Otherwise, read on.

First, download the tool and extract the entire folder wherever you like. After launching it, make sure the Game Directory field points to the folder where your game is installed (the folder that contains DarkSoulsRemastered.exe.) You may also change where unpacked textures are dumped to or where texture overrides are loaded from, but I recommend leaving those with the default.


--| Creating Texture Mods

Unpacking the game's textures is only required if you want to make your own mods. Once the Game Directory is set correctly, switch to the Unpack tab, click the Unpack button, then go make yourself a sandwich; it will take quite a while to finish. You must have at least 7 GB of free disk space to complete a full unpack.  
Once it finishes, find the texture you want to edit in the Dump folder, and place your replacement in the same relative directory in the Override folder. For instance, if you want to override Texture Dump\menu\menu_0\Title.dds, the replacement file should be at Texture Override\menu\menu_0\Title.dds. Please note that only .dds files are supported.  
Additionally, DSR uses some modern .dds formats which are not well supported in most image editors.
Paint.NET users will need this plugin to open them: https://forums.getpaint.net/topic/111731-dds-filetype-plus-2018-06-03/
Photoshop users will need this one: https://gametechdev.github.io/Intel-Texture-Works-Plugin/
To distribute your textures, I recommend including the entire override folder (with unwanted textures removed, of course,) so that users can easily merge it with their own.


--| Installing Texture Mods

To install texture mods, copy the contents of their override folder into your own, or otherwise follow the author's instructions. Once the files are in place, open the app, verify that the Game Directory is set correctly, then switch to the Repack tab and click the Repack button. The game's files will be edited to include your texture overrides, and any modified files will be backed up. To revert all modded textures, click the Restore button in the app to restore all backed-up files.


--| Credits

LowLevelHooking by Joseph N. Musser II
https://github.com/jnm2/LowLevelHooking

Octokit by GitHub
https://github.com/octokit/octokit.net

Semver by Max Hauser
https://github.com/maxhauser/semver
