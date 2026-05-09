namespace JustDanceEditor.Conversion.Abstractions;

public sealed record ConversionTargetDefinition(
    string FormatCode,
    string FormatName,
    string TargetCode,
    PlatformDescriptor Platform,
    TargetVersionDescriptor Version,
    string DisplayName,
    IReadOnlyList<ConversionPrompt>? ExportPrompts = null,
    int Priority = 100)
{
    public IReadOnlyList<ConversionPrompt> ExportPrompts { get; init; } = ExportPrompts ?? [];
}

public sealed record PlatformDescriptor(string PlatformCode, string DisplayName);

public abstract record TargetVersionDescriptor
{
    public abstract string Label { get; }
    public abstract int SortOrder { get; }
    public virtual IReadOnlyCollection<int> Years => [];

    public sealed record None(string VersionLabel, int VersionSortOrder = 0) : TargetVersionDescriptor
    {
        public override string Label => VersionLabel;
        public override int SortOrder => VersionSortOrder;
        public override IReadOnlyCollection<int> Years => [];
    }

    public sealed record SpecificYear(int Year) : TargetVersionDescriptor
    {
        public override string Label => $"JD{Year}";
        public override int SortOrder => Year;
        public override IReadOnlyCollection<int> Years => [Year];
    }

    public sealed record YearSet(IReadOnlySet<int> Values) : TargetVersionDescriptor
    {
        public override string Label => Values.Count == 0
            ? "Versions"
            : string.Join(", ", Values.Order());

        public override int SortOrder => Values.Count == 0 ? int.MaxValue : Values.Min();
        public override IReadOnlyCollection<int> Years => [.. Values.Order()];
    }

    public sealed record OpenEnded(int StartYear, string VersionLabel) : TargetVersionDescriptor
    {
        public override string Label => VersionLabel;
        public override int SortOrder => StartYear;
        public override IReadOnlyCollection<int> Years => [StartYear];
    }

    public sealed record Custom(string Code, string VersionLabel, int VersionSortOrder = 100) : TargetVersionDescriptor
    {
        public override string Label => VersionLabel;
        public override int SortOrder => VersionSortOrder;
        public override IReadOnlyCollection<int> Years => [];
    }
}
