using JustDanceEditor.Formats.UbiArt.Model;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

internal abstract class LegacyResourceFileBinary<TComponent>
    where TComponent : LegacyResourceComponent
{
    public uint Version { get; set; }
    public uint SerializedSize { get; set; }
    public LegacyBinaryTypeId<LegacyResourceBaseBinary> BaseTypeId { get; set; }
    public uint BaseTypeSize { get; set; }

    [LegacyBinaryPadding(28)]
    public LegacyPadding Reserved { get; set; }

    public int ComponentCount { get; set; }
    public TComponent Component { get; set; } = null!;
}

internal abstract class LegacyResourceComponent
{
    public uint ComponentSize { get; set; }
}

internal sealed class LegacyBlockFlow : LegacyResourceFileBinary<LegacyBlockFlowTemplateComponent>
{
    public LegacyMashupData ToMashupData(string mashupMapName, string baseSongName, bool hasDatabaseGameId)
    {
        LegacyBlockReplacement[] replacements = Component.BlockDescriptorVector;
        List<LegacyMashupBlock> blocks = new(replacements.Length);
        int absoluteStartBeat = 0;

        for (int i = 0; i < replacements.Length; i++)
        {
            LegacyBlockReplacement replacement = replacements[i];
            LegacyBlockDescriptor source = replacement.BaseBlock.IsEmptyBlock != 0
                ? replacement.BaseBlock
                : replacement.AlternativeBlocks.Length > 0
                ? replacement.AlternativeBlocks[0]
                : replacement.BaseBlock;

            blocks.Add(new LegacyMashupBlock
            {
                Index = i,
                AbsoluteStartBeat = absoluteStartBeat,
                BaseBlock = replacement.BaseBlock.ToRuntime(hasDatabaseGameId),
                SourceBlock = source.ToRuntime(hasDatabaseGameId),
                UsesAlternativeBlock = replacement.BaseBlock.IsEmptyBlock == 0 && replacement.AlternativeBlocks.Length > 0
            });
            absoluteStartBeat += Math.Max(0, source.LastBeat - source.FirstBeat);
        }

        return new LegacyMashupData { MapName = mashupMapName, BaseSongName = baseSongName, Blocks = blocks };
    }
}

[LegacyBinaryTypeId(0x5B648E44)]
internal sealed class LegacyBlockFlowTemplateComponent : LegacyResourceComponent
{
    public int IsMashUp { get; set; }
    public int IsPartyMaster { get; set; }
    public LegacyBlockReplacement[] BlockDescriptorVector { get; set; } = [];
}

internal sealed class LegacyBlockReplacement
{
    public int SerializedSize { get; set; }
    public LegacyBlockDescriptor BaseBlock { get; set; } = new();
    public LegacyBlockDescriptor[] AlternativeBlocks { get; set; } = [];
}

internal sealed class LegacyBlockDescriptor
{
    public int SerializedSize { get; set; }
    public string SongName { get; set; } = string.Empty;

    [LegacyBinaryEngineVersionCondition(MaxEngineVersion = 2014)]
    public int DatabaseGameId { get; set; }

    public int FirstBeat { get; set; }
    public int LastBeat { get; set; }
    public int SongSwitch { get; set; }
    public float VideoCoachOffsetX { get; set; }
    public float VideoCoachOffsetY { get; set; }
    public float VideoCoachScale { get; set; }
    public string DanceStepName { get; set; } = string.Empty;

    [LegacyBinaryEngineVersionCondition(MinEngineVersion = 2015)] public float PlayingSpeed { get; set; }
    [LegacyBinaryEngineVersionCondition(MinEngineVersion = 2015)] public int IsEntryPoint { get; set; }
    [LegacyBinaryEngineVersionCondition(MinEngineVersion = 2015)] public int IsEmptyBlock { get; set; }
    [LegacyBinaryEngineVersionCondition(MinEngineVersion = 2015)] public int IsNoScoreBlock { get; set; }
    [LegacyBinaryEngineVersionCondition(MinEngineVersion = 2015)] public string Guid { get; set; } = string.Empty;

    public LegacyMashupBlockDescriptor ToRuntime(bool hasDatabaseGameId) => new()
    {
        SongName = SongName,
        DatabaseGameId = hasDatabaseGameId ? DatabaseGameId : null,
        FirstBeat = FirstBeat,
        LastBeat = LastBeat,
        SongSwitch = SongSwitch != 0,
        SongSwitchValue = SongSwitch,
        VideoCoachOffsetX = VideoCoachOffsetX,
        VideoCoachOffsetY = VideoCoachOffsetY,
        VideoCoachScale = VideoCoachScale == 0 ? 1.0f : VideoCoachScale,
        DanceStepName = DanceStepName,
        PlayingSpeed = PlayingSpeed == 0 ? 1.0f : PlayingSpeed,
        IsEntryPoint = IsEntryPoint != 0,
        IsEmptyBlock = IsEmptyBlock != 0,
        IsNoScoreBlock = IsNoScoreBlock != 0,
        Guid = Guid
    };
}
