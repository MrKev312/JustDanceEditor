using System;

namespace JustDanceEditor.Converter.Files;

public class TemplateFiles
{
    public TemplateFiles(FileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
    }

    readonly FileSystem fileSystem;

    public string TemplateFolder => fileSystem.ConversionRequest.TemplatePath;

    public string CoachesLarge => Directory.GetFiles(Path.Combine(TemplateFolder, "CoachesLarge"))[0];
    public string CoachesSmall => Directory.GetFiles(Path.Combine(TemplateFolder, "CoachesSmall"))[0];
    public string Cover => Directory.GetFiles(Path.Combine(TemplateFolder, "Cover"))[0];
    public string MapPackage => Directory.GetFiles(Path.Combine(TemplateFolder, "MapPackage"))[0];
    public string SongTitleLogo => Directory.GetFiles(Path.Combine(TemplateFolder, "SongTitleLogo"))[0];
}
