using System.IO;

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
        public const string EventsFile = "timelines/events.json";
        public const string HandMovesFile = "timelines/coach_moves_hand.json";
        public const string FullBodyMovesFile = "timelines/coach_moves_fullBody.json";
        public const string CoachPattern = "coach_*.json";
        public const string FullBodyPattern = "coach_*_fullBody.json";

        public static string CoachTimelineFile(int coachId) => $"timelines/coach_{coachId:D2}.json";
        public static string FullBodyCoachTimelineFile(int coachId) => $"timelines/coach_{coachId:D2}_fullBody.json";
    }

    public static class Assets
    {
        public const string Root = "assets";

        public const string AudioFolder = $"{Root}/audio";
        public const string AudioMasterFile = $"{AudioFolder}/master.opus";
        public const string AudioPreviewFile = $"{AudioFolder}/preview.opus";

        public const string VideoFolder = $"{Root}/video";
        public const string PreviewVideoFolder = $"{Root}/previewVideo";

        public const string BrandingFolder = $"{Root}/branding";
        public const string BrandingCoverFile = $"{BrandingFolder}/thumbnail.webp";
        public const string BrandingSongTitleFile = $"{BrandingFolder}/songTitleLogo.webp";

        public const string CoachesFolder = $"{Root}/coaches";
        public const string CoachesBackgroundFile = $"{CoachesFolder}/coachesBackground.webp";

        public const string PictogramsFolder = $"{Root}/pictograms";
        public const string MovesFolder = $"{Root}/moves";
        public const string GesturesFolder = $"{Root}/gestures";
    }

    public static string Resolve(string root, string relative)
    {
        string normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(root, normalized);
    }
}
