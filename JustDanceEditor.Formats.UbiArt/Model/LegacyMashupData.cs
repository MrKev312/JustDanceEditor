namespace JustDanceEditor.Formats.UbiArt.Model;

public sealed class LegacyMashupData
{
    public string MapName { get; set; } = string.Empty;
    public string BaseSongName { get; set; } = string.Empty;
    public List<LegacyMashupBlock> Blocks { get; set; } = [];

    public int DurationBeats => Blocks.Sum(block => block.DurationBeats);
}

public sealed class LegacyMashupBlock
{
    public int Index { get; set; }
    public int AbsoluteStartBeat { get; set; }
    public LegacyMashupBlockDescriptor BaseBlock { get; set; } = new();
    public LegacyMashupBlockDescriptor SourceBlock { get; set; } = new();
    public bool UsesAlternativeBlock { get; set; }

    public int DurationBeats => Math.Max(0, SourceBlock.LastBeat - SourceBlock.FirstBeat);
}

public sealed class LegacyMashupBlockDescriptor
{
    public string SongName { get; set; } = string.Empty;
    public int? DatabaseGameId { get; set; }
    public int FirstBeat { get; set; }
    public int LastBeat { get; set; }
    public bool SongSwitch { get; set; }
    public int SongSwitchValue { get; set; }
    public float VideoCoachOffsetX { get; set; }
    public float VideoCoachOffsetY { get; set; }
    public float VideoCoachScale { get; set; } = 1.0f;
    public string DanceStepName { get; set; } = string.Empty;
    public float PlayingSpeed { get; set; } = 1.0f;
    public bool IsEntryPoint { get; set; }
    public bool IsEmptyBlock { get; set; }
    public bool IsNoScoreBlock { get; set; }
    public string Guid { get; set; } = string.Empty;

    public int DurationBeats => Math.Max(0, LastBeat - FirstBeat);
}