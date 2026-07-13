using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("JustDanceEditor.Formats.JDI.Tests")]

namespace JustDanceEditor.Formats.JDI;

public static class IntermediatePackageLayout
{
    public const string MetadataFile = "metadata.json";

    public static class Timelines
    {
        public const string Folder = "timelines";
        public const string StructureFile = "timelines/structure.json";
        public const string LyricsFile = "timelines/lyrics.json";
        public const string PictogramsFile = "timelines/pictograms.json";
        public const string GoldEffectsFile = "timelines/gold_effects.json";
        public const string HideUserInterfaceFile = "timelines/hide_user_interface.json";
        public const string VibrationsFile = "timelines/vibrations.json";
        public const string HandMovesFile = "timelines/coach_moves_hand.json";
        public const string FullBodyMovesFile = "timelines/coach_moves_fullBody.json";
        public const string CoachPattern = "coach_??.json";
        public const string FullBodyPattern = "coach_??_fullBody.json";

        public static string CoachTimelineFile(int coachId) => $"timelines/coach_{coachId:D2}.json";
        public static string FullBodyCoachTimelineFile(int coachId) => $"timelines/coach_{coachId:D2}_fullBody.json";
    }

    public static class Recordings
    {
        public const string Folder = "recordings";
        public const string MsmTrainingFile = "recordings/msm_training.json";
        public const string CoachFolderPattern = "coach_??";
        public const string RecordingPattern = "*.json";

        public static string CoachFolder(int coachId) => $"{Folder}/coach_{coachId:D2}";

        public static string RecordingFile(int coachId, string fileName)
        {
            ValidateRecordingFileName(fileName);
            return $"{CoachFolder(coachId)}/{fileName}";
        }

        private static void ValidateRecordingFileName(string fileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

            if (fileName.Contains('/') || fileName.Contains('\\'))
                throw new ArgumentException("Recording file names cannot contain path separators.", nameof(fileName));

            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Recording file names must end with .json.", nameof(fileName));
        }
    }

    /// <summary>
    /// Provides relative paths for asset files within an intermediate package.
    /// Use <see cref="Resolve(string, string)"/> to convert to absolute paths.
    /// </summary>
    public static class Assets
    {
        /// <summary>Root folder for all assets.</summary>
        public const string Root = "assets";

        /// <summary>Audio assets folder.</summary>
        public const string AudioFolder = $"{Root}/audio";

        /// <summary>Master audio file path.</summary>
        public const string AudioMasterFile = $"{AudioFolder}/master.opus";

        /// <summary>Preview audio file path.</summary>
        public const string AudioPreviewFile = $"{AudioFolder}/preview.opus";

        /// <summary>Video assets folder.</summary>
        public const string VideoFolder = $"{Root}/video";

        /// <summary>Preview video assets folder.</summary>
        public const string PreviewVideoFolder = $"{Root}/previewVideo";

        /// <summary>Cover assets folder.</summary>
        public const string CoverAssetsFolder = $"{Root}/coverAssets";

        /// <summary>Main cover image path.</summary>
        public const string CoverFile = $"{CoverAssetsFolder}/cover.webp";

        /// <summary>Square cover image path.</summary>
        public const string SquareCoverFile = $"{CoverAssetsFolder}/coverSquare.webp";

        /// <summary>Song title logo path.</summary>
        public const string SongTitleFile = $"{CoverAssetsFolder}/songTitleLogo.webp";

        /// <summary>Coaches folder.</summary>
        public const string CoachesFolder = $"{Root}/coaches";

        /// <summary>Album coach composite image path.</summary>
        public const string AlbumCoachFile = $"{CoachesFolder}/albumCoach.webp";

        /// <summary>Backgrounds folder.</summary>
        public const string BackgroundsFolder = $"{Root}/backgrounds";

        /// <summary>Map background image path.</summary>
        public const string MapBackgroundFile = $"{BackgroundsFolder}/mapBackground.webp";

        /// <summary>Banner image path.</summary>
        public const string BannerFile = $"{BackgroundsFolder}/banner.webp";

        /// <summary>Album background image path (center-cropped square from coaches background).</summary>
        public const string AlbumBackgroundFile = $"{BackgroundsFolder}/albumBackground.webp";

        /// <summary>Pictograms folder.</summary>
        public const string PictogramsFolder = $"{Root}/pictograms";

        /// <summary>Root folder for MoveSpace classifiers.</summary>
        public const string MovesFolder = $"{Root}/moves";

        /// <summary>Version 4 MoveSpace classifiers.</summary>
        public const string MovesV4Folder = $"{MovesFolder}/v4";

        /// <summary>Version 5 MoveSpace classifiers.</summary>
        public const string MovesV5Folder = $"{MovesFolder}/v5";

        /// <summary>Version 6 MoveSpace classifiers.</summary>
        public const string MovesV6Folder = $"{MovesFolder}/v6";

        /// <summary>Version 7 MoveSpace classifiers used by the editor and modern exports.</summary>
        public const string MovesV7Folder = $"{MovesFolder}/v7";

        /// <summary>Gestures folder.</summary>
        public const string GesturesFolder = $"{Root}/gestures";

        /// <summary>
        /// Gets the relative path for a gesture asset subfolder.
        /// </summary>
        /// <param name="subfolderName">Gesture subfolder name.</param>
        public static string GestureFolder(string subfolderName)
        {
            ValidateGestureSubfolderName(subfolderName);
            return $"{GesturesFolder}/{subfolderName}";
        }

        /// <summary>
        /// Validates that a gesture subfolder name is a simple folder name, not a path.
        /// </summary>
        /// <param name="subfolderName">Gesture subfolder name.</param>
        public static void ValidateGestureSubfolderName(string subfolderName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(subfolderName);

            if (HasTraversalSegment(subfolderName))
                throw new ArgumentException("Gesture subfolder names cannot contain traversal segments.", nameof(subfolderName));

            if (Path.IsPathRooted(subfolderName) || HasWindowsDriveSpecifier(subfolderName))
                throw new ArgumentException("Gesture subfolder names cannot be rooted paths.", nameof(subfolderName));

            if (subfolderName.Contains('/') || subfolderName.Contains('\\'))
                throw new ArgumentException("Gesture subfolder names cannot contain path separators.", nameof(subfolderName));
        }

        /// <summary>
        /// Gets the relative path for an individual coach image.
        /// </summary>
        /// <param name="coachIndex">1-based coach index.</param>
        public static string CoachFile(int coachIndex) => $"{CoachesFolder}/coach_{coachIndex:D2}.webp";

        /// <summary>
        /// Gets the relative path for a pictogram image.
        /// </summary>
        /// <param name="pictogramId">Pictogram identifier (filename without extension).</param>
        public static string PictogramFile(string pictogramId)
        {
            ValidatePictogramId(pictogramId);
            return $"{PictogramsFolder}/{pictogramId}.webp";
        }

        /// <summary>
        /// Validates that a pictogram identifier is a simple file name, not a path.
        /// </summary>
        /// <param name="pictogramId">Pictogram identifier (filename without extension).</param>
        public static void ValidatePictogramId(string pictogramId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pictogramId);

            if (HasTraversalSegment(pictogramId))
                throw new ArgumentException("Pictogram IDs cannot contain traversal segments.", nameof(pictogramId));

            if (Path.IsPathRooted(pictogramId) || HasWindowsDriveSpecifier(pictogramId))
                throw new ArgumentException("Pictogram IDs cannot be rooted paths.", nameof(pictogramId));

            if (pictogramId.Contains('/') || pictogramId.Contains('\\'))
                throw new ArgumentException("Pictogram IDs cannot contain path separators.", nameof(pictogramId));
        }

        private static bool HasTraversalSegment(string value) =>
            value.Split(['/', '\\'], StringSplitOptions.None).Any(segment => segment == "..");

        private static bool HasWindowsDriveSpecifier(string value) =>
            value.Length >= 2
            && value[1] == ':'
            && ((value[0] >= 'A' && value[0] <= 'Z') || (value[0] >= 'a' && value[0] <= 'z'));
    }

    public static string Resolve(string root, string relative)
    {
        string normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(root, normalized);
    }
}