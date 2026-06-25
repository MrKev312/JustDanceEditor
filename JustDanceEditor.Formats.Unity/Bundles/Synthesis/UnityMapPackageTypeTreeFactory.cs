using AssetsTools.NET;
using AssetsTools.NET.Extra;

using System.Globalization;
using System.Text;

namespace JustDanceEditor.Formats.Unity.Bundles.Synthesis;

internal static class UnityMapPackageTypeTreeFactory
{
    private const string MusicTrackTypeHash = "f74ac4d5b4be14a8c0260eed8a605b5c";
    private const string MusicTrackScriptHash = "bb7013f39e50f9eeafbc8eae4a5aa2ab";
    private const string MapTypeHash = "133b031b68cdad3c6199a6377be693c0";
    private const string MapScriptHash = "f7d4cbeefc87480cb50be0ab3bc9b16b";

    public static TypeTreeType CreateMusicTrackTypeTree() => Create(0, MusicTrackTypeHash, MusicTrackScriptHash, MusicTrackSchema);

    public static TypeTreeType CreateMapTypeTree() => Create(1, MapTypeHash, MapScriptHash, MapSchema);

    private static TypeTreeType Create(ushort scriptTypeIndex, string typeHash, string scriptHash, string schema)
    {
        List<TypeTreeNode> nodes = [];
        Dictionary<string, uint> stringOffsets = new(StringComparer.Ordinal);
        List<byte> stringBuffer = [];

        foreach (string line in schema.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split('|');
            if (parts.Length != 5)
                throw new InvalidDataException($"Invalid Unity MapPackage type-tree schema line: '{line}'.");

            string typeName = parts[1];
            string fieldName = parts[2];
            TypeTreeNodeFlags flags = typeName == "Array" ? TypeTreeNodeFlags.Array : TypeTreeNodeFlags.None;

            nodes.Add(new TypeTreeNode
            {
                Level = byte.Parse(parts[0], CultureInfo.InvariantCulture),
                TypeStrOffset = GetStringOffset(typeName),
                NameStrOffset = GetStringOffset(fieldName),
                ByteSize = int.Parse(parts[3], CultureInfo.InvariantCulture),
                Index = (uint)nodes.Count,
                Version = 1,
                TypeFlags = flags,
                MetaFlags = uint.Parse(parts[4], CultureInfo.InvariantCulture),
                RefTypeHash = 0
            });
        }

        return new TypeTreeType
        {
            TypeId = (int)AssetClassID.MonoBehaviour,
            IsStrippedType = false,
            ScriptTypeIndex = scriptTypeIndex,
            ScriptIdHash = CreateHash(scriptHash),
            TypeHash = CreateHash(typeHash),
            Nodes = nodes,
            StringBufferBytes = stringBuffer.ToArray(),
            IsRefType = false,
            TypeDependencies = []
        };

        uint GetStringOffset(string value)
        {
            if (stringOffsets.TryGetValue(value, out uint existing))
                return existing;

            uint offset = (uint)stringBuffer.Count;
            stringOffsets.Add(value, offset);
            stringBuffer.AddRange(Encoding.UTF8.GetBytes(value));
            stringBuffer.Add(0);
            return offset;
        }
    }

    private static Hash128 CreateHash(string value) => new(Convert.FromHexString(value));

    private const string MusicTrackSchema = """
0|MonoBehaviour|Base|-1|32768
1|PPtr<GameObject>|m_GameObject|12|65
2|int|m_FileID|4|65
2|SInt64|m_PathID|8|65
1|UInt8|m_Enabled|1|16641
1|PPtr<MonoScript>|m_Script|12|0
2|int|m_FileID|4|8388609
2|SInt64|m_PathID|8|8388609
1|string|m_Name|-1|557057
2|Array|Array|-1|540673
3|int|size|4|524289
3|char|data|1|524289
1|TrackStructureContainer|m_structure|-1|32768
2|MusicTrackStructure|MusicTrackStructure|-1|32768
3|int|startBeat|4|0
3|int|endBeat|4|0
3|double|videoStartTime|8|0
3|double|volume|8|0
3|double|previewEntry|8|0
3|double|previewLoopStart|8|0
3|double|previewLoopEnd|8|0
3|double|fadeInDuration|8|0
3|double|fadeOutDuration|8|0
3|int|fadeInType|4|0
3|int|fadeOutType|4|0
3|int|fadeStartBeat|4|0
3|int|fadeEndBeat|4|0
3|UInt8|useFadeStartBeat|1|16640
3|UInt8|useFadeEndBeat|1|16640
3|TrackSignatures|signatures|-1|32768
4|Array|Array|-1|32768
5|int|size|4|0
5|TrackSignatures|data|-1|32768
6|TrackSignature|MusicSignature|-1|32768
7|int|beats|4|0
7|double|marker|8|0
7|string|comment|-1|32768
8|Array|Array|-1|16385
9|int|size|4|1
9|char|data|1|1
3|TrackMarker|markers|-1|0
4|Array|Array|-1|0
5|int|size|4|0
5|TrackMarker|data|8|0
6|SInt64|VAL|8|0
3|TrackSections|sections|-1|32768
4|Array|Array|-1|32768
5|int|size|4|0
5|TrackSections|data|-1|32768
6|TrackMusicSection|MusicSection|-1|32768
7|int|sectionType|4|0
7|double|marker|8|0
7|string|comment|-1|32768
8|Array|Array|-1|16385
9|int|size|4|1
9|char|data|1|1
3|TrackComments|comments|-1|32768
4|Array|Array|-1|32768
5|int|size|4|0
5|TrackComments|data|-1|32768
6|TrackComment|Comment|-1|32768
7|double|marker|8|0
7|string|comment|-1|32768
8|Array|Array|-1|16385
9|int|size|4|1
9|char|data|1|1
7|string|commentType|-1|32768
8|Array|Array|-1|16385
9|int|size|4|1
9|char|data|1|1
""";

    private const string MapSchema = """
0|MonoBehaviour|Base|-1|32768
1|PPtr<GameObject>|m_GameObject|12|65
2|int|m_FileID|4|65
2|SInt64|m_PathID|8|65
1|UInt8|m_Enabled|1|16641
1|PPtr<MonoScript>|m_Script|12|0
2|int|m_FileID|4|8388609
2|SInt64|m_PathID|8|8388609
1|string|m_Name|-1|557057
2|Array|Array|-1|540673
3|int|size|4|524289
3|char|data|1|524289
1|string|MapName|-1|32768
2|Array|Array|-1|16385
3|int|size|4|1
3|char|data|1|1
1|SongDesc|SongDesc|-1|32768
2|string|MapName|-1|32768
3|Array|Array|-1|16385
4|int|size|4|1
4|char|data|1|1
2|int|JDVersion|4|0
2|int|OriginalJDVersion|4|0
2|string|Artist|-1|32768
3|Array|Array|-1|16385
4|int|size|4|1
4|char|data|1|1
2|string|DancerName|-1|32768
3|Array|Array|-1|16385
4|int|size|4|1
4|char|data|1|1
2|string|Title|-1|32768
3|Array|Array|-1|16385
4|int|size|4|1
4|char|data|1|1
2|string|Credits|-1|32768
3|Array|Array|-1|16385
4|int|size|4|1
4|char|data|1|1
2|int|NumCoach|4|0
2|int|MainCoach|4|0
2|int|Difficulty|4|0
2|int|SweatDifficulty|4|0
1|KaraokeTape|KaraokeData|-1|32768
2|int|TapeClock|4|0
2|TapeTrackContainer|Tracks|-1|32768
3|Array|Array|-1|32768
4|int|size|4|0
4|TapeTrackContainer|data|-1|32768
5|TapeTrackValue|TapeTrack|-1|32768
6|SInt64|Id|8|0
6|string|Name|-1|32768
7|Array|Array|-1|16385
8|int|size|4|1
8|char|data|1|1
2|KaraokeClipContainer|Clips|-1|32768
3|Array|Array|-1|32768
4|int|size|4|0
4|KaraokeClipContainer|data|-1|32768
5|KaraokeClipValue|KaraokeClip|-1|32768
6|int|StartTime|4|0
6|int|Duration|4|0
6|string|Lyrics|-1|32768
7|Array|Array|-1|16385
8|int|size|4|1
8|char|data|1|1
6|UInt8|IsActive|1|16640
6|SInt64|TrackId|8|0
6|float|Pitch|4|0
6|UInt8|IsEndOfLine|1|16640
6|int|ContentType|4|0
6|SInt64|Id|8|0
6|int|SemitoneTolerance|4|0
6|int|StartTimeTolerance|4|0
6|int|EndTimeTolerance|4|0
2|int|TapeBarCount|4|0
2|string|SoundwichEvent|-1|32768
3|Array|Array|-1|16385
4|int|size|4|1
4|char|data|1|1
2|string|MapName|-1|32768
3|Array|Array|-1|16385
4|int|size|4|1
4|char|data|1|1
2|UInt8|FreeResourcesAfterPlay|1|16640
1|DanceTapeData|DanceData|-1|32768
2|int|TapeClock|4|0
2|int|TapeBarCount|4|0
2|UInt8|FreeResourcesAfterPlay|1|16640
2|string|MapName|-1|32768
3|Array|Array|-1|16385
4|int|size|4|1
4|char|data|1|1
2|string|SoundwichEvent|-1|32768
3|Array|Array|-1|16385
4|int|size|4|1
4|char|data|1|1
2|MotionClipData|MotionClips|-1|32768
3|Array|Array|-1|32768
4|int|size|4|0
4|MotionClipData|data|-1|32768
5|int|StartTime|4|0
5|int|Duration|4|0
5|SInt64|Id|8|0
5|SInt64|TrackId|8|0
5|UInt8|IsActive|1|16640
5|string|MoveName|-1|32768
6|Array|Array|-1|16385
7|int|size|4|1
7|char|data|1|1
5|UInt8|GoldMove|1|16640
5|int|CoachId|4|0
5|int|MoveType|4|0
5|string|Color|-1|32768
6|Array|Array|-1|16385
7|int|size|4|1
7|char|data|1|1
2|PictogramClipData|PictoClips|-1|32768
3|Array|Array|-1|32768
4|int|size|4|0
4|PictogramClipData|data|-1|32768
5|int|StartTime|4|0
5|int|Duration|4|0
5|SInt64|Id|8|0
5|SInt64|TrackId|8|0
5|UInt8|IsActive|1|16640
5|string|PictoPath|-1|32768
6|Array|Array|-1|16385
7|int|size|4|1
7|char|data|1|1
5|unsigned int|CoachCount|4|0
2|GoldEffectClipData|GoldEffectClips|-1|32768
3|Array|Array|-1|32768
4|int|size|4|0
4|GoldEffectClipData|data|29|32768
5|int|StartTime|4|0
5|int|Duration|4|0
5|int|GoldEffectType|4|0
5|SInt64|Id|8|0
5|SInt64|TrackId|8|0
5|UInt8|IsActive|1|16640
2|HideHudClipData|HideHudClips|-1|32768
3|Array|Array|-1|32768
4|int|size|4|0
4|HideHudClipData|data|9|32768
5|int|StartTime|4|0
5|int|Duration|4|0
5|UInt8|IsActive|1|16640
1|PPtr<$MusicTrack>|TrackData|12|0
2|int|m_FileID|4|8388609
2|SInt64|m_PathID|8|8388609
1|SerializableDictionary`2|CameraMoveModels|-1|32768
2|KeyValuePair|list|-1|32768
3|Array|Array|-1|32768
4|int|size|4|0
4|KeyValuePair|data|-1|32768
5|string|Key|-1|32768
6|Array|Array|-1|16385
7|int|size|4|1
7|char|data|1|1
5|CameraMoveModelsData|Value|-1|0
6|CameraMoveModelResources|Resources|-1|0
7|Array|Array|-1|0
8|int|size|4|0
8|CameraMoveModelResources|data|20|0
9|PPtr<$TextAsset>|MoveAsset|12|0
10|int|m_FileID|4|8388609
10|SInt64|m_PathID|8|8388609
9|CameraMoveTuningValues|TuningValues|8|0
10|float|ScoreScale|4|0
10|float|ScoreSmoothing|4|0
2|UInt8|keyCollision|1|16641
1|SerializableDictionary`2|HandDeviceMoveModels|-1|32768
2|KeyValuePair|list|-1|32768
3|Array|Array|-1|32768
4|int|size|4|0
4|KeyValuePair|data|-1|32768
5|string|Key|-1|32768
6|Array|Array|-1|16385
7|int|size|4|1
7|char|data|1|1
5|PPtr<$TextAsset>|Value|12|0
6|int|m_FileID|4|8388609
6|SInt64|m_PathID|8|8388609
2|UInt8|keyCollision|1|16641
1|PPtr<$SpriteAtlas>|PictogramAtlas|12|0
2|int|m_FileID|4|8388609
2|SInt64|m_PathID|8|8388609
1|CoachData|FullBodyCoachDatas|-1|0
2|Array|Array|-1|0
3|int|size|4|0
3|CoachData|data|8|0
4|unsigned int|GoldMovesCount|4|0
4|unsigned int|StandardMovesCount|4|0
1|CoachData|HandOnlyCoachDatas|-1|0
2|Array|Array|-1|0
3|int|size|4|0
3|CoachData|data|8|0
4|unsigned int|GoldMovesCount|4|0
4|unsigned int|StandardMovesCount|4|0
""";
}