using System.Text;

namespace JustDanceEditor.IPK.Tests;

public class IntegrationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _inputDir;
    private readonly string _outputDir;
    private readonly string _ipkPath;

    public IntegrationTests()
    {
        // Create a unique temporary folder structure
        _testRoot = Path.Combine(Path.GetTempPath(), "JDIPK_Test_" + Guid.NewGuid());
        _inputDir = Path.Combine(_testRoot, "Input");
        _outputDir = Path.Combine(_testRoot, "Output");
        _ipkPath = Path.Combine(_testRoot, "test.ipk");

        Directory.CreateDirectory(_inputDir);
        Directory.CreateDirectory(_outputDir);
    }

    [Fact]
    public void PackAndUnpack_FullCycle_VerifyIntegrity()
    {
        // 1. Arrange: Create dummy files
        CreateFile("file1.txt", "Hello World Content");
        CreateFile("subdir/file2.bin", [0xDE, 0xAD, 0xBE, 0xEF]);

        // This file extension (.ckd) triggers the compression logic in JustDanceIPKWriter
        string compressibleContent = new('A', 1000); // 1000 'A's compresses well
        CreateFile("assets/texture.png.ckd", Encoding.ASCII.GetBytes(compressibleContent));

        // 2. Act: Pack the folder
        JustDanceIPKWriter writer = new(_inputDir, _ipkPath);
        writer.Pack();

        Assert.True(File.Exists(_ipkPath), "IPK file was not created.");

        // 3. Act: Extract the IPK
        JustDanceIPKParser parser = new(_ipkPath, _outputDir);
        parser.Parse(ShowInfo: false);

        // 4. Assert: Verify file existence and content
        AssertFileMatches("file1.txt");
        AssertFileMatches("subdir/file2.bin");
        AssertFileMatches("assets/texture.png.ckd");
    }

    private void CreateFile(string relativePath, string content)
        => CreateFile(relativePath, Encoding.UTF8.GetBytes(content));

    private void CreateFile(string relativePath, byte[] content)
    {
        string fullPath = Path.Combine(_inputDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
        File.WriteAllBytes(fullPath, content);
    }

    private void AssertFileMatches(string relativePath)
    {
        string originalPath = Path.Combine(_inputDir, relativePath);
        string extractedPath = Path.Combine(_outputDir, relativePath);

        Assert.True(File.Exists(extractedPath), $"Extracted file missing: {relativePath}");

        byte[] originalBytes = File.ReadAllBytes(originalPath);
        byte[] extractedBytes = File.ReadAllBytes(extractedPath);

        Assert.Equal(originalBytes, extractedBytes);
    }

    public void Dispose()
    {
        // Cleanup temp folder after tests run
        if (Directory.Exists(_testRoot))
        {
            try
            {
                Directory.Delete(_testRoot, true);
            }
            catch
            {
                // Ignored: Sometimes OS locks temp files briefly
            }
        }

        GC.SuppressFinalize(this);
    }
}