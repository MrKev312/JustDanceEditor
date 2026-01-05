using JustDanceEditor.Formats.UbiArt.Files;

using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.Services.Assets;

public interface IUbiArtAssetResolver
{
    // Pictograms
    CookedFile[] GetPictograms();

    // Cover and coach art
    CookedFile? GetCoverArt();
    CookedFile[] GetCoachTextures();
    CookedFile? GetAlbumCoach();
    CookedFile? GetBackgroundTexture();

    // Video
    string? FindFirstVideoFile();

    // Moves
    CookedFile[] GetMoveFiles();

    // Audio
    bool TryFindAudio(string relativePath, [NotNullWhen(true)] out CookedFile? file);

    bool TryFindMainAudio(JDUbiArtSong songData, [NotNullWhen(true)] out CookedFile? file, out bool isPreMerged);

    // Generalized search helper
    bool TryFindFileWithExtensions(string folderRelative, string baseName, IEnumerable<string> extensions, out CookedFile? file);
}