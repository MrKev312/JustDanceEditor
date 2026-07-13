using JustDanceEditor.Editor.ViewModels.Dialogs;

namespace JustDanceEditor.Editor.Tests;

public class PictogramCreationDialogTests
{
    [Fact]
    public void Accept_UsesSelectedPictogramId_And_CalculatesFrames()
    {
        string root = Path.Combine(Path.GetTempPath(), $"picto_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string assets = Path.Combine(root, "assets", "pictograms");
        Directory.CreateDirectory(assets);

        string picName = "foo";
        string path = Path.Combine(assets, picName + ".webp");
        // minimal PNG
        File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAAWgmWQ0AAAAASUVORK5CYII="));

        try
        {
            PictogramCreationViewModel vm = new(new[] { picName }, root);
            PictogramItem item = Enumerable.First(vm.AvailablePictogramItems);
            vm.SelectedPictogram = item;
            vm.DurationBeats = 1.5M;

            vm.Accept();

            PictogramCreationResult result = vm.Result ?? throw new InvalidOperationException("Expected a result after accepting the dialog.");
            Assert.Equal(picName, result.PictogramId);
            Assert.Equal((int)(1.5 * 24.0), result.Frames);
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