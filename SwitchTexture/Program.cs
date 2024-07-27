using SwitchTexture.TextureType;

Console.Write("Enter a path to a .xtx file: ");

string path = Console.ReadLine();

// Filestream
FileStream fileStream = new(path, FileMode.Open, FileAccess.Read);
XTX xtx = new();
xtx.LoadFile(fileStream);

(byte[][] data, byte[] hdr) output = xtx.DeswizzleData(0);
// Save output to a dds in the same directory with the same name
using FileStream ddsStream = new(Path.ChangeExtension(path, ".dds"), FileMode.Create, FileAccess.Write);
ddsStream.Write(output.hdr);
foreach (byte[] block in output.data)
{
    ddsStream.Write(block);
}