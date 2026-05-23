using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.Export.Platform;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using Microsoft.Extensions.Logging;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class Ps3AudioExporterTests
{
    [Fact]
    public async Task WriteAudioAsync_WrapsInMemoryMp3Payload()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        FakeMediaProcessor mediaProcessor = new();

        try
        {
            Directory.CreateDirectory(root);
            ExportContext context = new(root, new UbiArtLayoutResolver(), new SystemFileSystem(), UbiArtEngineVersion.JD2015, mediaProcessor);
            Ps3CookedPlatformExporter exporter = new();

            await exporter.WriteAudioAsync(
                context,
                Path.Combine("cache", "itf_cooked", "ps3", "world", "maps", "song", "audio", "song.wav"),
                new UbiArtAudioExportSource("source.opus", Start: TimeSpan.FromSeconds(2), Duration: TimeSpan.FromSeconds(3)));

            string outputPath = Path.Combine(root, "cache", "itf_cooked", "ps3", "world", "maps", "song", "audio", "song.wav.ckd");
            byte[] output = File.ReadAllBytes(outputPath);

            Assert.Equal("RAKI", Encoding.ASCII.GetString(output, 0, 4));
            Assert.Equal(9U, BinaryPrimitives.ReadUInt32BigEndian(output.AsSpan(4, sizeof(uint))));
            Assert.Equal("PS3 ", Encoding.ASCII.GetString(output, 8, 4));
            Assert.Equal("mp3 ", Encoding.ASCII.GetString(output, 12, 4));
            Assert.NotNull(mediaProcessor.Request);
            Assert.Equal("source.opus", mediaProcessor.Request.SourcePath);
            Assert.Equal("mp3", mediaProcessor.Request.OutputFormat);
            Assert.Equal("mp3", mediaProcessor.Request.Codec);
            Assert.Equal(48000, mediaProcessor.Request.SampleRate);
            Assert.Equal(2, mediaProcessor.Request.Channels);
            Assert.Equal("192k", mediaProcessor.Request.Bitrate);
            Assert.Equal(TimeSpan.FromSeconds(2), mediaProcessor.Request.Start);
            Assert.Equal(TimeSpan.FromSeconds(3), mediaProcessor.Request.Duration);
            Assert.Equal(mediaProcessor.Payload, output[^mediaProcessor.Payload.Length..]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private sealed class FakeMediaProcessor : IMediaProcessor
    {
        public byte[] Payload { get; } = "fake-mp3-payload"u8.ToArray();
        public JdiAudioEncodeRequest? Request { get; private set; }

        public Task EncodeAudioAsync(JdiAudioEncodeRequest request, string outputPath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MemoryStream> EncodeAudioToMemoryAsync(JdiAudioEncodeRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new MemoryStream(Payload, writable: false));
        }

        public Task<string?> GetOrCreateVideoAsync(JdiVideoEncodeRequest request, ILogger logger, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
