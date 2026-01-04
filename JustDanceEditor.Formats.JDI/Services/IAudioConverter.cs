namespace JustDanceEditor.Formats.JDI.Services;

public interface IAudioConverter
{
    Task Convert(string sourcePath, string targetPath);
}