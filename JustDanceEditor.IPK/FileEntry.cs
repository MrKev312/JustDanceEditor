namespace JustDanceEditor.IPK;

public class FileEntry
{
    public int Dummy1 { get; set; }
    public int Size { get; set; }
    public int ZSize { get; set; }
    public long TimeStamp { get; set; }
    public long Offset { get; set; }
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public int Crc { get; set; }
    public int Dummy2 { get; set; }
}
