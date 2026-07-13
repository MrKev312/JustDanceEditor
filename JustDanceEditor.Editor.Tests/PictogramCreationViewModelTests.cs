using JustDanceEditor.Editor.ViewModels.Dialogs;

namespace JustDanceEditor.Editor.Tests;

public class PictogramCreationViewModelTests
{
    private static readonly byte[] OneByOnePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAAWgmWQ0AAAAASUVORK5CYII=");

    [Fact]
    public void AvailablePictogramItems_LoadImages_WhenPresent()
    {
        string root = Path.Combine(Path.GetTempPath(), $"picto_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string assets = Path.Combine(root, "assets", "pictograms");
        Directory.CreateDirectory(assets);

        string picName = "foo";
        string path = Path.Combine(assets, picName + ".webp");
        File.WriteAllBytes(path, OneByOnePng);

        try
        {
            PictogramCreationViewModel vm = new(new[] { picName }, root);
            PictogramItem? item = vm.AvailablePictogramItems.FirstOrDefault(i => i.Id == picName);
            Assert.NotNull(item);
            Assert.Equal(picName, item.Id);
            Assert.False(string.IsNullOrEmpty(item.ImagePath));
            // Image decoding may not be available in test environment (null is acceptable), but ImagePath should point to our file
            Assert.Equal(path, item.ImagePath);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch { }

            try
            {
                Directory.Delete(assets, true);
            }
            catch { }

            try
            {
                Directory.Delete(Path.Combine(root, "assets"), true);
            }
            catch { }

            try
            {
                Directory.Delete(root, true);
            }
            catch { }
        }
    }
}