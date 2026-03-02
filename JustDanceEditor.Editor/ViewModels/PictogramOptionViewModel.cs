using Avalonia.Media.Imaging;

using System.IO;

namespace JustDanceEditor.Editor.ViewModels;

public class PictogramOptionViewModel
{
    public string Name { get; }
    public string ImagePath { get; }
    public Bitmap? PreviewImage { get; }

    public PictogramOptionViewModel(string name, string path)
    {
        Name = name;
        ImagePath = path;
        if (File.Exists(path))
        {
            try
            {
                PreviewImage = new Bitmap(path);
            }
            catch { }
        }
    }

    public override string ToString() => Name;
}
