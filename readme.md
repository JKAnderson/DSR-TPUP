
# DSR Texture Packer & Unpacker
[NexusMods Page](https://www.nexusmods.com/darksoulsremastered/mods/9)  
A tool to extract and override textures in Dark Souls: Remastered. Running as Administrator is recommended.  
Requires [.NET 4.7.2](https://www.microsoft.com/net/download/thank-you/net472) - Windows 10 users should already have this.  
and [Visual C++ 2015](https://www.microsoft.com/en-us/download/details.aspx?id=48145) - Make sure to get the x64 installer.

TPUP serves two purposes:
* To allow mod authors to unpack every texture in the game in order to edit them, and
* To allow players to easily merge and install texture packs from said authors.

# Instructions

If you're more of a visual learner, please watch [this video](https://youtu.be/D7zEDHe-Acw) going over the instructions; otherwise, read on.

First, download the tool and extract the entire folder wherever you like. After launching it, make sure the Game Directory field points to the folder where your game is installed (the folder that contains DarkSoulsRemastered.exe.) You may also change where unpacked textures are dumped to or where texture overrides are loaded from, but I recommend leaving those with the default.

### Creating Texture Mods

Unpacking the game's textures is only required if you want to make your own mods. Once the Game Directory is set correctly, switch to the Unpack tab, click the Unpack button, then go make yourself a sandwich; it will take quite a while to finish. You must have at least 7 GB of free disk space to complete a full unpack.

Once it finishes, find the texture you want to edit in the Dump folder, and place your replacement in the same relative directory in the Override folder. For instance, if you want to override *Texture Dump\menu\menu_0\Title.dds*, the replacement file should be at *Texture Override\menu\menu_0\Title.dds*.

If you're trying to port old dsfix textures, search for the hash in dsfix.txt; in many cases the DSR path will be different from the DS1 path, but it should give you a good idea of where to start looking.

When repacking the textures, you will be warned if any files are in formats which do not match the vanilla files. It is highly recommended to go back afterwards and resave these textures yourself instead of relying on the automatic conversion.

Additionally, DSR uses some modern .dds formats which are not well supported in most image editors. Paint.NET users will need [this plugin](https://forums.getpaint.net/topic/111731-dds-filetype-plus-2018-06-03/) to open them, and Photoshop users will need [this one](https://gametechdev.github.io/Intel-Texture-Works-Plugin/).

To distribute your textures, I recommend including the entire override folder (with unwanted textures removed, of course,) so that users can easily merge it with their own.

### Installing Texture Mods

To install texture mods, copy the contents of their override folder into your own, or otherwise follow the author's instructions. Once the files are in place, open the app, verify that the Game Directory is set correctly, then switch to the Repack tab and click the Repack button. The game's files will be edited to include your texture overrides, and any modified files will be backed up. To revert all modded textures, click the Restore button in the app to restore all backed-up files.

# Credits
[DirectXTex](https://github.com/Microsoft/DirectXTex) by Microsoft Corp

[LowLevelHooking](https://github.com/jnm2/LowLevelHooking) by Joseph N. Musser II

[Octokit](https://github.com/octokit/octokit.net) by GitHub

[Semver](https://github.com/maxhauser/semver) by Max Hauser

# Changelog

### 1.1
* Improperly formatted textures will now be automatically converted when repacking
* Included dsfix.txt to aid porting DS1 texture packs
