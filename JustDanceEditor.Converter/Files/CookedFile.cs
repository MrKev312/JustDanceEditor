
namespace JustDanceEditor.Converter.Files;

public class CookedFile
{
    public CookedFile(string path)
    {
        DirectoryPath = Path.GetDirectoryName(path) + Path.DirectorySeparatorChar;
        string inputFileName = Path.GetFileName(path);
        Extension = Path.GetExtension(inputFileName);

        if (Extension == ".ckd")
        {
            IsCooked = true;
            inputFileName = Path.GetFileNameWithoutExtension(inputFileName);
            Extension = Path.GetExtension(inputFileName);
        }

        Name = Path.GetFileNameWithoutExtension(inputFileName);
    }

    public string DirectoryPath { get; private set; }
    public string Name { get; private set; }
    public string Extension { get; private set; }
    public bool IsCooked { get; private set; }

    public string FullPath => this;
    public string UncookedPath => DirectoryPath + Name + Extension;


    public static implicit operator string(CookedFile v) => v.ToString();

    public override string ToString()
    {
        string fullPath = DirectoryPath + Name + Extension;

        if (IsCooked)
            fullPath += ".ckd";

        return fullPath;
    }
}
