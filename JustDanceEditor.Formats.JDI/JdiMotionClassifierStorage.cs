using JustDanceEditor.Scoring;

namespace JustDanceEditor.Formats.JDI;

public static class JdiMotionClassifierStorage
{
    public static IReadOnlyList<MotionClassifierFormatVersion> StoredVersions => MotionClassifierFormats.NativeVersions;

    public static void EnsureVersionFolders(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        foreach (MotionClassifierFormatVersion version in StoredVersions)
            Directory.CreateDirectory(GetVersionFolder(packageRoot, version));
    }

    public static void ImportClassifier(string packageRoot, string fileName, ReadOnlySpan<byte> source)
    {
        ValidateFileName(fileName);

        byte[] sourceBytes = source.ToArray();
        Dictionary<MotionClassifierFormatVersion, byte[]> variants = StoredVersions.ToDictionary(
            static version => version,
            version => MotionClassifierConverter.ConvertToVersion(sourceBytes, version));

        foreach ((MotionClassifierFormatVersion version, byte[] classifier) in variants)
            WriteClassifier(packageRoot, fileName, version, classifier);
    }

    public static void WriteClassifier(
        string packageRoot,
        string fileName,
        MotionClassifierFormatVersion version,
        ReadOnlySpan<byte> source)
    {
        ValidateFileName(fileName);
        if (!StoredVersions.Contains(version))
            throw new NotSupportedException("JDI stores MSM versions 4 through 7.");
        if (MotionClassifierConverter.GetFormatVersion(source) != version)
            throw new InvalidDataException($"The classifier data is not MSM version {(uint)version}.");

        WriteClassifier(packageRoot, GetRelativeFolder(version), fileName, source);
    }

    public static void UpdateHeaders(string packageRoot, string fileName, MotionClassifierHeaderUpdate update)
    {
        ValidateFileName(fileName);
        ArgumentNullException.ThrowIfNull(update);

        Dictionary<MotionClassifierFormatVersion, byte[]> updated = [];
        foreach (MotionClassifierFormatVersion version in StoredVersions)
        {
            string path = GetVersionPath(packageRoot, fileName, version);
            if (!File.Exists(path))
                throw new FileNotFoundException($"The JDI MSM version set is incomplete; version {(uint)version} is missing.", path);

            byte[] source = File.ReadAllBytes(path);
            updated.Add(version, MotionClassifierHeaderEditor.UpdateHeader(source, update));
        }

        foreach ((MotionClassifierFormatVersion version, byte[] classifier) in updated)
            WriteClassifier(packageRoot, fileName, version, classifier);
    }

    public static string GetVersion4Path(string packageRoot, string fileName)
        => GetPath(packageRoot, IntermediatePackageLayout.Assets.MovesV4Folder, fileName);

    public static string GetVersion5Path(string packageRoot, string fileName)
        => GetPath(packageRoot, IntermediatePackageLayout.Assets.MovesV5Folder, fileName);

    public static string GetVersion6Path(string packageRoot, string fileName)
        => GetPath(packageRoot, IntermediatePackageLayout.Assets.MovesV6Folder, fileName);

    public static string GetVersion7Path(string packageRoot, string fileName)
        => GetPath(packageRoot, IntermediatePackageLayout.Assets.MovesV7Folder, fileName);

    public static string GetVersionPath(string packageRoot, string fileName, MotionClassifierFormatVersion version)
        => GetPath(packageRoot, GetRelativeFolder(version), fileName);

    public static string GetVersionFolder(string packageRoot, MotionClassifierFormatVersion version)
        => IntermediatePackageLayout.Resolve(packageRoot, GetRelativeFolder(version));

    private static void WriteClassifier(string packageRoot, string relativeFolder, string fileName, ReadOnlySpan<byte> source)
    {
        string folder = IntermediatePackageLayout.Resolve(packageRoot, relativeFolder);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, fileName), source);
    }

    private static string GetPath(string packageRoot, string relativeFolder, string fileName)
    {
        ValidateFileName(fileName);
        return Path.Combine(IntermediatePackageLayout.Resolve(packageRoot, relativeFolder), fileName);
    }

    private static string GetRelativeFolder(MotionClassifierFormatVersion version)
        => version switch
        {
            MotionClassifierFormatVersion.Version4 => IntermediatePackageLayout.Assets.MovesV4Folder,
            MotionClassifierFormatVersion.Version5 => IntermediatePackageLayout.Assets.MovesV5Folder,
            MotionClassifierFormatVersion.Version6 => IntermediatePackageLayout.Assets.MovesV6Folder,
            MotionClassifierFormatVersion.Version7 => IntermediatePackageLayout.Assets.MovesV7Folder,
            _ => throw new NotSupportedException("JDI stores MSM versions 4 through 7.")
        };

    private static void ValidateFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!fileName.Equals(Path.GetFileName(fileName), StringComparison.Ordinal) ||
            !fileName.EndsWith(".msm", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("An MSM file name without directory components is required.", nameof(fileName));
        }
    }
}
