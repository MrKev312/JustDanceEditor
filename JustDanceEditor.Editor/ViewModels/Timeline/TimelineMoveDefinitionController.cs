using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal sealed class TimelineMoveDefinitionController(
    TimelineEditorViewModel timeline,
    Dictionary<(string id, bool isFullBody), MoveDefinitionViewModel> moveDefinitions)
{
    public MoveDefinitionViewModel GetOrRegisterMove(string moveId, bool isFullBody)
    {
        if (string.IsNullOrEmpty(moveId))
            return new MoveDefinitionViewModel { Id = moveId, IsFullBody = isFullBody };

        (string moveId, bool isFullBody) key = (moveId, isFullBody);
        if (moveDefinitions.TryGetValue(key, out MoveDefinitionViewModel? def))
        {
            def.HasAsset = CheckMoveFileExists(moveId, isFullBody);
            return def;
        }

        Color color = Colors.LightGray;
        double duration = 24.0;
        if (timeline.Package.HandCoachMoves.TryGetValue(moveId, out CoachMoveDefinition? d)
            || timeline.Package.FullBodyCoachMoves.TryGetValue(moveId, out d))
        {
            if (d != null)
            {
                color = ClipViewModel.ParseRgbaHex(d.Color);
                if (d.Duration > 0)
                    duration = d.Duration;
            }
        }

        def = new MoveDefinitionViewModel
        {
            Id = moveId,
            IsFullBody = isFullBody,
            Color = color,
            DefaultDuration = duration,
            HasAsset = CheckMoveFileExists(moveId, isFullBody)
        };

        moveDefinitions[key] = def;
        return def;
    }

    public void RefreshMoveAssetStatus()
    {
        foreach (KeyValuePair<(string id, bool isFullBody), MoveDefinitionViewModel> entry in moveDefinitions)
            entry.Value.HasAsset = CheckMoveFileExists(entry.Key.id, entry.Key.isFullBody);
    }

    public MoveDefinitionViewModel? RegisterNewMoveDefinition(string id, bool isFullBody, int durationFrames, Color color)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        Dictionary<string, CoachMoveDefinition> catalog = isFullBody
            ? timeline.Package.FullBodyCoachMoves
            : timeline.Package.HandCoachMoves;

        if (catalog.ContainsKey(id))
            return GetOrRegisterMove(id, isFullBody);

        string colorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        CoachMoveDefinition pkgDef = new()
        {
            Color = colorHex,
            Duration = durationFrames,
            MoveType = isFullBody ? CoachMoveType.FullBodyTracking : CoachMoveType.HandTracking
        };

        MoveDefinitionViewModel vm = new()
        {
            Id = id,
            IsFullBody = isFullBody,
            Color = color,
            DefaultDuration = durationFrames,
            HasAsset = CheckMoveFileExists(id, isFullBody)
        };

        timeline.PushUndo(
            undo: () =>
            {
                catalog.Remove(id);
                moveDefinitions.Remove((id, isFullBody));
                timeline.NotifyAvailableMovesChanged(isFullBody);
            },
            redo: () =>
            {
                catalog[id] = pkgDef;
                moveDefinitions[(id, isFullBody)] = vm;
                timeline.NotifyAvailableMovesChanged(isFullBody);
            });

        catalog[id] = pkgDef;
        moveDefinitions[(id, isFullBody)] = vm;
        timeline.NotifyAvailableMovesChanged(isFullBody);

        return vm;
    }

    public bool RenameMoveId(string oldId, string newId, bool isFullBody)
    {
        oldId = oldId?.Trim() ?? string.Empty;
        newId = newId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId))
            return false;

        if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
            return false;

        Dictionary<string, CoachMoveDefinition> catalog = isFullBody ? timeline.Package.FullBodyCoachMoves : timeline.Package.HandCoachMoves;
        if (!catalog.TryGetValue(oldId, out CoachMoveDefinition? definition) || definition == null)
            return false;

        if (catalog.ContainsKey(newId))
            return false;

        TrackType targetTrackType = isFullBody ? TrackType.CoachFullBody : TrackType.CoachHand;
        List<MoveClipViewModel> affected = [.. timeline.Tracks
            .Where(t => t.TrackType == targetTrackType)
            .SelectMany(t => t.Clips)
            .OfType<MoveClipViewModel>()
            .Where(c => string.Equals(c.MoveId, oldId, StringComparison.OrdinalIgnoreCase))];

        void ApplyRename(string fromId, string toId)
        {
            catalog.Remove(fromId);
            catalog[toId] = definition;

            if (moveDefinitions.TryGetValue((fromId, isFullBody), out MoveDefinitionViewModel? vm))
            {
                moveDefinitions.Remove((fromId, isFullBody));
                vm.Id = toId;
                moveDefinitions[(toId, isFullBody)] = vm;
            }

            foreach (MoveClipViewModel clip in affected)
                clip.MoveId = toId;

            timeline.NotifyAvailableMovesChanged(isFullBody);
            timeline.NotifyTracksChanged();
        }

        ApplyRename(oldId, newId);

        timeline.PushUndo(
            undo: () => ApplyRename(newId, oldId),
            redo: () => ApplyRename(oldId, newId));

        return true;
    }

    private bool CheckMoveFileExists(string moveId, bool isFullBody)
    {
        try
        {
            if (string.IsNullOrEmpty(timeline.RootPath) || string.IsNullOrEmpty(moveId))
                return false;

            if (isFullBody)
            {
                string gesturesFolderAbs = IntermediatePackageLayout.Resolve(timeline.RootPath, IntermediatePackageLayout.Assets.GesturesFolder);
                if (!Directory.Exists(gesturesFolderAbs))
                    return false;

                foreach (string folderAbs in Directory.GetDirectories(gesturesFolderAbs))
                {
                    string candidate = Path.Combine(folderAbs, moveId + ".gesture");
                    if (File.Exists(candidate))
                        return true;
                }

                return false;
            }

            string movesFolderAbs = IntermediatePackageLayout.Resolve(timeline.RootPath, IntermediatePackageLayout.Assets.MovesV7Folder);
            string moveCandidate = Path.Combine(movesFolderAbs, moveId + ".msm");
            return File.Exists(moveCandidate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EditorLog.Unexpected(ex, $"Locate move asset '{moveId}'");
            return false;
        }
    }
}
