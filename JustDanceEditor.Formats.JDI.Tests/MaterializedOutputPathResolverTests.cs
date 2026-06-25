using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public sealed class MaterializedOutputPathResolverTests
{
    [Fact]
    public void Resolve_WhenRequestedPathDoesNotOverlapSource_KeepsRequestedPath()
    {
        string root = CreatePathRoot();
        string requested = Path.Combine(root, "output", "makeba");
        string source = Path.Combine(root, "source", "makeba");

        MaterializedOutputPath result = MaterializedOutputPathResolver.Resolve(requested, source, "makeba");

        Assert.Equal(Path.GetFullPath(requested), Path.GetFullPath(result.Path));
        Assert.False(result.WasRedirected);
        Assert.False(result.IsTemporary);
    }

    [Fact]
    public void Resolve_WhenRequestedPathEqualsSource_RedirectsOutsideSource()
    {
        string root = CreatePathRoot();
        string source = Path.Combine(root, "makeba");

        MaterializedOutputPath result = MaterializedOutputPathResolver.Resolve(source, source, "makeba");

        Assert.True(result.WasRedirected);
        Assert.False(MaterializedOutputPathResolver.PathsOverlap(source, result.Path));
    }

    [Fact]
    public void Resolve_WhenRequestedPathIsAncestorOfSource_RedirectsOutsideSource()
    {
        string root = CreatePathRoot();
        string requested = Path.Combine(root, "makeba");
        string source = Path.Combine(requested, "source");

        MaterializedOutputPath result = MaterializedOutputPathResolver.Resolve(requested, source, "makeba");

        Assert.True(result.WasRedirected);
        Assert.False(MaterializedOutputPathResolver.PathsOverlap(source, result.Path));
    }

    [Fact]
    public void Resolve_WhenRequestedPathIsDescendantOfSource_RedirectsOutsideSource()
    {
        string root = CreatePathRoot();
        string source = Path.Combine(root, "makeba");
        string requested = Path.Combine(source, "exports", "makeba");

        MaterializedOutputPath result = MaterializedOutputPathResolver.Resolve(requested, source, "makeba");

        Assert.True(result.WasRedirected);
        Assert.False(MaterializedOutputPathResolver.PathsOverlap(source, result.Path));
    }

    private static string CreatePathRoot() =>
        Path.Combine(Path.GetTempPath(), "JustDanceEditor.JDI.Tests", Guid.NewGuid().ToString("N"));
}