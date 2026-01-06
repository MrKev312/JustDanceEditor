namespace JustDanceEditor.Formats.UbiArt.Files;

public class CookedFile
{
    public CookedFile(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentNullException(nameof(relativePath));

        // Normalize to platform separators
        RelativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar);

        string inputFileName = Path.GetFileName(RelativePath);
        Extension = Path.GetExtension(inputFileName);

        if (Extension == ".ckd")
        {
            IsCooked = true;
            inputFileName = Path.GetFileNameWithoutExtension(inputFileName);
            Extension = Path.GetExtension(inputFileName);
        }

        Name = Path.GetFileNameWithoutExtension(inputFileName);
    }

    // The relative path used for UbiArt resolution (e.g. "world/maps/song/songdesc.tpl")
    public string RelativePath { get; private set; }
    public string Name { get; private set; }
    public string Extension { get; private set; }
    public bool IsCooked { get; private set; }

    // Uncooked (logical) path derived from the relative path components
    public string UncookedPath => Path.Combine(Path.GetDirectoryName(RelativePath) ?? string.Empty, Name + Extension);

    public static implicit operator string(CookedFile v) => v.ToString();

    public override string ToString() => RelativePath;
}