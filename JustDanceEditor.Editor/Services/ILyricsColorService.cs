namespace JustDanceEditor.Editor.Services;

public interface ILyricsColorService
{
    void UpdateLyricsColor(string rgbaHexColor);
    string GetLyricsColor();
}