using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Serialization;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

using System.IO.Hashing;
using System.Text;

using JdiGoldEffectClip = JustDanceEditor.Formats.JDI.Timelines.GoldEffectClip;
using JdiKaraokeClip = JustDanceEditor.Formats.JDI.Timelines.KaraokeClip;
using JdiPictogramClip = JustDanceEditor.Formats.JDI.Timelines.PictogramClip;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;

internal static class LegacyJd2014TimelineSerializer
{
    public static byte[] Serialize(IntermediateSongPackage package, LegacyPathContext paths)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();

        using MemoryStream bodyStream = new();
        using (BigEndianBinaryWriter body = new(bodyStream))
        {
            body.WriteUbiArtString(package.Metadata.MapName);
            WriteTimelineDefaults(body);

            // This table is ignored by our importer and by the public JD2014 extraction tools.
            // The clip payload that follows is the data we can author faithfully from JDI.
            body.Write(0);

            WritePictogramEntries(body, package.Pictograms.Clips, mapNameLower, paths);
            WriteMotionEntries(body, package.CoachTimelines, package.HandCoachMoves, mapNameLower, paths, ".msm");
            WriteMotionEntries(body, package.FullBodyCoachTimelines, package.FullBodyCoachMoves, mapNameLower, paths, ".gesture");
            WriteLyricsEntries(body, package.Lyrics.Clips);
            WriteEventEntries(body, package.GoldEffects.Clips);
        }

        byte[] bodyBytes = bodyStream.ToArray();
        using MemoryStream stream = new();
        using BigEndianBinaryWriter writer = new(stream);

        writer.Write(1);
        writer.Write(checked(bodyBytes.Length + 56));
        writer.Write(LegacyBinarySerializer.GetTypeId<LegacyResourceBaseBinary>());
        writer.Write(0xACu);
        WriteZeroes(writer, 28);
        writer.Write(1);
        writer.Write(LegacyBinarySerializer.GetTypeId<LegacyJd2014TimelineComponentBinary>());
        writer.Write(0x19Cu);
        writer.Write(bodyBytes);

        return stream.ToArray();
    }

    private static void WriteTimelineDefaults(BigEndianBinaryWriter writer)
    {
        writer.Write(0x38);
        writer.Write(5);
        writer.Write(0);
        writer.Write(0);
        writer.Write(unchecked((uint)-7));
        writer.Write(0x50);
        writer.Write(0x38);
        writer.Write(0x400);
        writer.Write(0x400);
        writer.Write(500.0f);
        writer.Write(0.7f);
        writer.Write(0.9f);
        writer.Write(0.75f);
        writer.Write(90.0f);
        writer.Write(900.0f);
        writer.Write(10);
        writer.Write(7);
        writer.Write(0.9f);
        writer.Write(0);
    }

    private static void WritePictogramEntries(
        BigEndianBinaryWriter writer,
        IEnumerable<JdiPictogramClip> clips,
        string mapNameLower,
        LegacyPathContext paths)
    {
        JdiPictogramClip[] ordered = [.. clips.OrderBy(clip => clip.StartTime)];
        writer.Write(ordered.Length);

        string folder = paths.MapSubFolder(mapNameLower, "timeline/pictos");
        foreach (JdiPictogramClip clip in ordered)
        {
            string fileName = $"{clip.PictogramId.ToLowerInvariant()}.tga";
            writer.Write(0xA0);
            writer.Write(ToTimelineBeat(clip.StartTime));
            writer.WriteUbiArtString(Path.GetFileNameWithoutExtension(fileName));
            writer.Write(2);
            WriteTimelinePath(writer, folder, fileName);
        }
    }

    private static void WriteMotionEntries(
        BigEndianBinaryWriter writer,
        IEnumerable<MoveTimeline> timelines,
        IReadOnlyDictionary<string, CoachMoveDefinition> moveCatalog,
        string mapNameLower,
        LegacyPathContext paths,
        string extension)
    {
        List<(MoveTimeline Timeline, MoveClip Clip, CoachMoveDefinition Move)> clips = [];
        foreach (MoveTimeline timeline in timelines)
        {
            foreach (MoveClip clip in timeline.Clips)
            {
                if (moveCatalog.TryGetValue(clip.MoveId, out CoachMoveDefinition? move))
                    clips.Add((timeline, clip, move));
            }
        }

        clips.Sort((left, right) =>
        {
            int start = left.Clip.StartTime.CompareTo(right.Clip.StartTime);
            if (start != 0)
                return start;

            return left.Timeline.CoachId.CompareTo(right.Timeline.CoachId);
        });

        writer.Write(clips.Count);
        string folder = paths.MapSubFolder(mapNameLower, "timeline/moves");
        foreach ((MoveTimeline timeline, MoveClip clip, CoachMoveDefinition move) in clips)
        {
            string moveId = clip.MoveId.ToLowerInvariant();
            string fileName = $"{moveId}{extension}";
            writer.Write(0x9C);
            writer.WriteUbiArtString(moveId);
            writer.Write(timeline.CoachId);
            WriteTimelinePath(writer, folder, fileName);
            writer.Write(ToTimelineBeat(clip.StartTime));
            writer.Write(ToTimelineBeat(clip.StartTime + move.Duration));
            writer.Write(clip.IsGoldMove ? 1 : 0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
        }
    }

    private static void WriteLyricsEntries(BigEndianBinaryWriter writer, IEnumerable<JdiKaraokeClip> clips)
    {
        JdiKaraokeClip[] ordered = [.. clips.OrderBy(clip => clip.StartTime)];
        writer.Write(ordered.Length);

        foreach (JdiKaraokeClip clip in ordered)
        {
            writer.Write(0x30);
            writer.WriteUbiArtString(clip.Lyrics ?? string.Empty);
            writer.Write(11);
            writer.Write(clip.IsEndOfLine ? 1 : 0);
            writer.Write(ToTimelineBeat(clip.StartTime));
            writer.Write(ToTimelineBeat(clip.StartTime + clip.Duration));
        }
    }

    private static void WriteEventEntries(BigEndianBinaryWriter writer, IEnumerable<JdiGoldEffectClip> clips)
    {
        JdiGoldEffectClip[] ordered = [.. clips.OrderBy(clip => clip.StartTime)];
        writer.Write(ordered.Length);

        foreach (JdiGoldEffectClip clip in ordered)
        {
            writer.Write(0);
            writer.Write(0);
            writer.Write(ToTimelineBeat(clip.StartTime));
            writer.Write(ToTimelineBeat(clip.StartTime + clip.Duration));
            writer.Write(0);
            writer.WriteUbiArtString("goldmove");
            WriteZeroes(writer, 16);
            writer.WriteUbiArtString(string.Empty);
        }
    }

    private static void WriteTimelinePath(BigEndianBinaryWriter writer, string folder, string fileName)
    {
        writer.WriteUbiArtString(folder);
        writer.WriteUbiArtString(fileName);
        writer.Write(Crc32.HashToUInt32(Encoding.UTF8.GetBytes(fileName)));
        writer.Write(0);
    }

    private static float ToTimelineBeat(int frame) => frame / 24.0f;

    private static void WriteZeroes(BinaryWriter writer, int count)
    {
        for (int i = 0; i < count; i++)
            writer.Write((byte)0);
    }
}