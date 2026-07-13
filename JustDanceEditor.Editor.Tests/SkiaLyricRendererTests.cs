using JustDanceEditor.Editor.Views.Tools;

using SkiaSharp;

using System.Text;

namespace JustDanceEditor.Editor.Tests;

public sealed class SkiaLyricRendererTests
{
    [Theory]
    [InlineData("これはテストです")]
    [InlineData("这是一次测试。")]
    [InlineData("这 is a テスト")]
    public void CreateTextRuns_UsesTypefacesContainingEveryCharacter(string text)
    {
        IReadOnlyList<SkiaLyricTextRun> runs = SkiaLyricRenderer.CreateTextRuns(text);

        Assert.Equal(text, string.Concat(runs.Select(static run => run.Text)));
        foreach (SkiaLyricTextRun run in runs)
        {
            using SKFont font = new(run.Typeface, 32);
            foreach (Rune rune in run.Text.EnumerateRunes())
                Assert.True(font.ContainsGlyph(rune.Value), $"{run.Typeface.FamilyName} is missing U+{rune.Value:X4}.");

            using SKPath? path = font.GetTextPath(run.Text, new SKPoint(0, 32));
            Assert.NotNull(path);
            Assert.False(path.IsEmpty, $"{run.Typeface.FamilyName} produced no outline for '{run.Text}'.");
        }
    }
}