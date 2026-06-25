# Just Dance Editor

Just Dance Editor converts, inspects, and edits Just Dance song packages across JDI, UbiArt, JDNext PC, and JD2023+ Unity layouts.

This project is not affiliated with Ubisoft. It is a fan-made tool for educational and preservation-oriented modding workflows. Only convert content you have the right to use.

## What It Does

- Convert songs between JDI, UbiArt, JDNext PC, and JD2023+ Unity formats.
- Import from extracted folders, IPK archives, JDI packages, JDNext PC maps, and Unity server/cache layouts.
- Export to folders, IPKs, UbiArt game-folder layouts, Unity custom servers, Unity offline caches, and Unity platform bundles.
- Patch required UbiArt game metadata during game-folder exports.
- Generate, reuse, or download cover assets during conversion.
- Edit JDI song metadata, timeline clips, lyrics, pictograms, and video offsets.

## Apps

- `JustDanceEditor.GUI` - guided drag-and-drop converter.
- `JustDanceEditor.Cli` - scriptable converter and media/IPK utility.
- `JustDanceEditor.Editor` - JDI timeline editor.

Release bundles are grouped by operating system:

- `JustDanceEditor-Windows`
- `JustDanceEditor-Linux`
- `JustDanceEditor-macOS`

Each release bundle contains one folder per runtime platform, such as `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`. Each runtime folder contains the CLI, GUI, and Editor together with shared binaries de-duplicated. The builds are framework-dependent, so install the .NET 10 runtime before launching them.

## Quick Start

1. Open `JustDanceEditor.GUI`.
2. Drag a source file or folder onto the window, or pick it manually.
3. Choose the target format, platform, and game/version.
4. Answer any target-specific prompts, choose the output folder, and run the conversion.

For scripted CLI conversion:

```powershell
JustDanceEditor.Cli convert --input path\to\source --output path\to\output --target nx-2018
```

Use `JustDanceEditor.Cli targets` to list available targets.

## CLI Drag And Drop

You can pass paths directly to `JustDanceEditor.Cli`; desktop environments that support dropping files or folders onto an executable use the same quick utility mode:

- Folders are packed into `.ipk` archives.
- `.ipk` files are extracted into folders.
- Audio files are converted after you choose a target encoding. Supported inputs include `.wav`, `.wave`, `.mp3`, `.aiff`, `.aif`, `.wma`, `.m4a`, `.aac`, `.flac`, `.opus`, and UbiArt `.wav.ckd` audio.
- Images and texture files are converted after you choose a target encoding. Supported inputs include `.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`, `.gif`, `.tga`, `.dds`, `.ssd`, `.xtx`, `.gtx`, `.ps3tex`, `.tex`, and `.ckd`.

You can drop multiple paths at once. Use `--output` to pick an output file or folder, `--force` to overwrite existing outputs, `--no-wait` to close without waiting for a key, and `--encoding`, `--audio-encoding`, or `--texture-encoding` to skip the encoding prompt.

## Notes

- UbiArt game-folder exports expect the folder that directly contains the game IPKs.
- Unity offline cache exports can use an existing cache root or create a new `SD_Cache` setup when needed.
- FFmpeg is used for media conversion and Editor video/audio preparation. The tools can download it automatically, or you can place `ffmpeg`/`ffmpeg.exe` next to the executable.
- Online cover downloads use [Just Dance Covers](https://github.com/MrKev312/JustDanceCovers) when enabled.
- Some legacy UbiArt targets are experimental or partially supported; the UI/CLI warns before using those paths.

## CLI Utilities

```powershell
JustDanceEditor.Cli extract-ipk --input path\to\song.ipk --output path\to\folder
JustDanceEditor.Cli pack-ipk --input path\to\folder --output path\to\song.ipk
```

## Bug Reports

Please include the tool version, operating system, source and target formats, song codename, error logs, and any relevant target details such as Unity cache layout or UbiArt patch IPK usage.

Report issues at [GitHub Issues](https://github.com/MrKev312/JustDanceEditor/issues).

## License

This project is licensed under the GNU General Public License v3.0. See [LICENSE](LICENSE).

## Credits

- MrKev312: creator and maintainer.
- Stella/AboodXD and KillzXGaming: original XTX/GX2 converter research.
