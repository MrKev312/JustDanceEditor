# Just Dance Editor

## Description
This tool allows you to convert an UbiArt Just Dance song to Just Dance Next's format.

## Legal Disclaimer
This tool is not affiliated with Ubisoft in any way.
It is a fan-made tool created for educational purposes.
Please support the official releases.
This tool assumes you have the rights to the content you are converting and that you got the files legally.
The author is not responsible for any misuse of this tool.

## Features
- Auto converts the tracks to a MapPackage bundle.
- Auto generates a cover.
- Auto generates the coaches bundles.

## Installation
1. Download the latest release.
2. Extract the files.
4. Create a folder called ``template`` in the same directory as the executable. In there create the following 2 folders:
	- ``cache0``: Which should contain a folder called ``Cover``
	- ``cachex``: Which should contain the following folders:
		- ``CoachesSmall``
		- ``CoachesLarge``
		- ``MapPackage``
	
	Each folder should contain a bundle from an official Just Dance Next song.
3. That's it!

## Usage
1. Extract the ``ipk`` file of the song you want to convert by drag and dropping it onto ``JustDanceEditor.IPK.exe``.
2. If the song is a ``mainscene``, in the song folder, create a new folder called ``menuart`` and put in the following files:
	- ``{MapName}_Coach_1.tga.cdk``, ``{MapName}_Coach_2.tga.cdk``, ``{MapName}_Coach_..`` (up to 4 coaches)
	- ``{MapName}_AlbumCoach.tga.cdk`` or a 1024x2048 ``{MapName}_Cover_Generic.tga.png``
	- ``{MapName}_map_bkg.tga.png``
3. You can optionally add in a ``cover.png`` file in the song folder to use as the cover.
4. Launch ``JustDanceEditor.exe`` and select option ``2``.
5. Drag and drop the song folder onto the window.
6. Drag and drop the output folder onto the window.
7. Select whether you want to download a cover from the internet.
8. ???
9. Profit!

## Adding a song to the game
Detailed guide might be added in the future.
Look at existing songs for reference and the ``CachingStatus.json`` in ``SD_Cache.0000``.

## Bug Reporting
Before reporting, make sure that you're using the latest version of the tool!

Please report any bugs you find in the [Issues](https://github.com/MrKev312/JustDanceEditor/issues) section.
Be sure to include the following information:
- The version of the tool you are using.
- The song you are trying to convert and from which platform.
- Which template you are using.
- The error message you are getting.
- Any other relevant information.

## Contributing
Contributions are welcome! Please follow these guidelines:
- Fork the repository.
- Create a new branch.
- Make your changes.
- Submit a pull request.

## License
This project is licensed under the MIT License.

## Contact
For any inquiries, please dm me on Discord: ``mrkev312``
