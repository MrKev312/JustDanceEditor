using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;

internal sealed class LegacyPathContext(string mapRoot, string commonRoot)
{
    private string MapRoot { get; } = NormalizeRoot(mapRoot);
    private string CommonRoot { get; } = NormalizeRoot(commonRoot);

    public string MapFolder(string mapNameLower) => $"{MapRoot}/{mapNameLower}/";
    public string MapSubFolder(string mapNameLower, string folder) => $"{MapFolder(mapNameLower)}{NormalizeRelative(folder)}/";
    public string CommonFolder(string folder) => $"{CommonRoot}/{NormalizeRelative(folder)}/";

    private static string NormalizeRoot(string path) => path.Replace('\\', '/').Trim('/');
    private static string NormalizeRelative(string path) => path.Replace('\\', '/').Trim('/');
}

internal sealed class LegacyTapeFile(string mapName, UbiArtEngineVersion engineVersion, IEnumerable<LegacyTapeClip> clips)
{
    private IReadOnlyList<LegacyTapeClip> ClipList { get; } = [.. clips];

    public uint Version => 1;

    public int TapeVersion => (224 * ClipList.Count) + 166;

    public LegacyBinaryTypeId<LegacyClipTapeBinary> TypeId => new();

    public uint TypeSize => (int)engineVersion == 2015 ? 0x8Cu : 0x9Cu;

    public int ClipCount => ClipList.Count;

    public IReadOnlyList<LegacyTapeClip> Clips => ClipList;

    public LegacyPadding FooterPadding => LegacyBinary.Padding((int)engineVersion == 2015 ? 8 : 12);

    public int TapeClock => 0;

    public int TapeBarCount => 1;

    public int FreeResourcesAfterPlay => 0;

    public string MapName => mapName;
}

internal abstract class LegacyTapeClip
{
    public abstract int SerializedSize { get; }

    public uint Id { get; init; }

    public uint TrackId { get; init; }

    public int IsActive { get; init; } = 1;

    public int StartTime { get; init; }

    public int Duration { get; init; }
}

[LegacyBinaryTypeId(0x955384A1)]
internal sealed class LegacyMotionClip : LegacyTapeClip
{
    public override int SerializedSize => 0x70;

    public LegacyUbiArtPath ClassifierPath { get; init; }

    public int Unknown0 => 0;

    public int GoldMove { get; init; }

    public int CoachId { get; init; }

    public int MoveType { get; init; }

    public LegacyAbgrColor Color { get; init; } = LegacyAbgrColor.White;

    public LegacyMotionPlatformSpecifics MotionPlatformSpecifics { get; } = new();
}

[LegacyBinaryTypeId(0x52EC8962)]
internal sealed class LegacyPictogramClip : LegacyTapeClip
{
    public override int SerializedSize => 0x38;

    public LegacyUbiArtPath PictoPath { get; init; }

    public int Unknown0 => 0;

    public uint CoachCount { get; init; } = uint.MaxValue;
}

[LegacyBinaryTypeId(0xFD69B110)]
internal sealed class LegacyGoldEffectClip : LegacyTapeClip
{
    public override int SerializedSize => 0x1C;

    public int EffectType { get; init; }
}

[LegacyBinaryTypeId(0x68552A41)]
internal sealed class LegacyKaraokeClip : LegacyTapeClip
{
    public override int SerializedSize => 0x50;

    public float Pitch { get; init; }

    public string Lyrics { get; init; } = string.Empty;

    public int IsEndOfLine { get; init; }

    public int ContentType { get; init; }

    public int StartTimeTolerance { get; init; } = 4;

    public int EndTimeTolerance { get; init; } = 4;

    public float SemitoneTolerance { get; init; } = 5;
}

[LegacyBinaryTypeId(0x2D8C885B)]
internal sealed class LegacySoundSetClip : LegacyTapeClip
{
    public override int SerializedSize => 0x40;

    public LegacyUbiArtPath SoundSetPath { get; init; }

    public int SoundChannel => 0;

    public int StopsOnEnd => 0;

    public int AccountedForDuration => 0;

    public int Padding => 0;
}

[LegacyBinaryTypeId(0x52E06A9A)]
internal sealed class LegacyHideUserInterfaceClip(UbiArtEngineVersion engineVersion) : LegacyTapeClip
{
    public override int SerializedSize => 0x48;

    public LegacyPadding VersionPadding => LegacyBinary.Padding((int)engineVersion == 2015 ? 4 : 8);

    public int EventType => 1;

    public int Padding => 0;
}

[LegacyBinaryTypeId(0x101F9D2B)]
internal sealed class LegacyVibrationClip : LegacyTapeClip
{
    public override int SerializedSize => 0x18;
}

internal sealed class LegacyAbgrColor(float alpha, float blue, float green, float red)
{
    public static LegacyAbgrColor White { get; } = new(1, 1, 1, 1);

    public float Alpha => alpha;

    public float Blue => blue;

    public float Green => green;

    public float Red => red;
}

internal sealed class LegacyMotionPlatformSpecifics
{
    public IReadOnlyList<LegacyCameraScoringDefaults> Platforms { get; } =
    [
        new(),
        new(),
        new()
    ];

    public LegacyPadding Terminator => LegacyBinary.Padding(16);
}

internal sealed class LegacyCameraScoringDefaults
{
    public int Platform => 3;

    public int ScoreScale => 1;

    public int ScoreSmoothing => 0;

    public int Thresholds => 0;
}