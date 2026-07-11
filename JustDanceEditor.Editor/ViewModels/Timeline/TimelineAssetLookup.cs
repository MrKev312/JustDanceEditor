using Avalonia.Media;

using JustDanceEditor.Formats.JDI.Timelines;

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal static class TimelineAssetLookup
{
    public static bool TryGetCoachMoveColor(TimelineEditorViewModel timeline, string moveId, out Color color)
    {
        CoachMoveDefinition? definition = null;
        if (timeline.Package.HandCoachMoves.TryGetValue(moveId, out CoachMoveDefinition? handDefinition)
            || timeline.Package.FullBodyCoachMoves.TryGetValue(moveId, out handDefinition))
        {
            definition = handDefinition;
        }

        if (definition != null)
        {
            color = ClipViewModel.ParseRgbaHex(definition.Color);
            return true;
        }

        color = Colors.LightGray;
        return false;
    }

    public static IEnumerable<string> GetAvailablePictograms(string rootPath)
    {
        string directory = Path.Combine(rootPath, "assets", "pictograms");
        if (!Directory.Exists(directory))
            return [];

        return Directory.GetFiles(directory, "*.*")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .OrderBy(static id => id);
    }
}