using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Export.Generators;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Export;

internal sealed class UbiArtEngineContentWriter
{
    public async Task WriteAsync(UbiArtExportPlan plan)
    {
        IntermediateSongPackage package = plan.Package;
        ExportContext context = plan.Context;
        IPlatformExporter exporter = plan.PlatformExporter;
        IEngineContentGenerator generator = plan.EngineGenerator;
        UbiArtPlatform platform = plan.Platform;
        UbiArtEngineVersion version = plan.EngineVersion;
        string mapName = plan.MapName;
        string mapNameLower = plan.MapNameLower;
        string mapWorldBase = plan.MapWorldBase;
        string mapWorldRelative = plan.Layout.GetMapWorldFolder("", mapNameLower, platform, version);

        string audioFolder = Path.Combine(plan.PlatformRoot, plan.Layout.GetAudioFolder("", mapNameLower, platform, version));
        string timelineFolder = Path.Combine(plan.PlatformRoot, plan.Layout.GetTimelineFolder("", mapNameLower, platform, version));
        string cinematicsFolder = Path.Combine(mapWorldBase, "cinematics");
        string menuArtFolder = Path.Combine(mapWorldBase, "menuart");
        string autodanceFolder = Path.Combine(mapWorldBase, "autodance");
        string videosFolder = Path.Combine(mapWorldBase, "videoscoach");
        string graphFolder = Path.Combine(mapWorldBase, "graph");

        bool isLegacyFormat = generator is LegacyEngineContentGenerator;
        bool useLegacyConvertedData = isLegacyFormat && version >= UbiArtEngineVersion.JD2016;
        bool writeAutodanceResources = platform != UbiArtPlatform.Uncooked && ShouldWriteAutodanceResources(platform, isLegacyFormat);
        bool writeSoundSequence = version != UbiArtEngineVersion.JD2014 && ShouldWriteSoundSequence(package);
        List<Task> tasks = [];

        if (useLegacyConvertedData)
        {
            string legacyFolder = Path.Combine(plan.PlatformRoot, "cache", "legacyconverteddata", mapNameLower);
            tasks.Add(exporter.WriteBinaryFileAsync(context, Path.Combine(legacyFolder, "songdesc.main_legacy.tpl"), generator.GenerateSongDesc(package)));
            tasks.Add(exporter.WriteBinaryFileAsync(context, Path.Combine(mapWorldBase, "songdesc.act"), generator.GenerateGenericActor("JD_SongDescTemplate", $"cache/legacyconverteddata/{mapNameLower}/songdesc.main_legacy.tpl")));
        }
        else
        {
            tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(mapWorldBase, "songdesc.tpl"), generator.GenerateSongDesc(package)));
            if (version != UbiArtEngineVersion.JD2014)
                tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(mapWorldBase, "songdesc.act"), generator.GenerateGenericActor("JD_SongDescTemplate", $"{mapWorldRelative}/songdesc.tpl")));
        }

        if (platform != UbiArtPlatform.Uncooked)
        {
            if (useLegacyConvertedData)
            {
                string legacyAudioFolder = Path.Combine(plan.PlatformRoot, "cache", "legacyconverteddata", mapNameLower, "audio");
                tasks.Add(exporter.WriteBinaryFileAsync(context, Path.Combine(legacyAudioFolder, $"{mapNameLower}_musictrack.main_legacy.tpl"), generator.GenerateMusicTrack(package)));
            }
            else
            {
                tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(audioFolder, $"{mapNameLower}_musictrack.tpl"), generator.GenerateMusicTrack(package)));
            }

            if (writeSoundSequence)
            {
                tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(audioFolder, $"{mapNameLower}_sequence.tpl"), generator.GenerateSequenceTpl()));
                tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(audioFolder, $"{mapNameLower}.stape"), generator.GenerateSoundTape(mapName)));
            }

            if (package.TimelineStructure.StartBeat < 0)
            {
                string ambienceTemplateName = version == UbiArtEngineVersion.JD2014
                    ? $"set_amb_{mapNameLower}_intro.tpl"
                    : $"amb_{mapNameLower}_intro.tpl";
                tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(audioFolder, "amb", ambienceTemplateName), generator.GenerateAmbTpl(mapName)));
            }
        }
        else
        {
            tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(audioFolder, $"{mapNameLower}_musictrack.tpl"), generator.GenerateMusicTrack(package)));
            tasks.Add(WriteUncookedTrackAsync(package, context, Path.Combine(audioFolder, $"{mapNameLower}.trk")));

            if (package.TimelineStructure.StartBeat < 0)
                tasks.Add(WriteUncookedAmbienceAsync(plan, Path.Combine(mapWorldBase, "Audio", "AMB")));
        }

        if (version == UbiArtEngineVersion.JD2014 && generator is LegacyEngineContentGenerator legacyGenerator)
        {
            tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(timelineFolder, "timeline.tpl"), legacyGenerator.GenerateJd2014Timeline(package)));
            tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(timelineFolder, "timeline.act"), legacyGenerator.GenerateJd2014TimelineActor(mapName)));
        }
        else
        {
            tasks.AddRange([
                exporter.WriteEngineResourceAsync(context, Path.Combine(timelineFolder, $"{mapNameLower}_tml_dance.dtape"), generator.GenerateDanceTape(package)),
                exporter.WriteEngineResourceAsync(context, Path.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.ktape"), generator.GenerateKaraokeTape(package)),
                exporter.WriteEngineResourceAsync(context, Path.Combine(timelineFolder, $"{mapNameLower}_tml_dance.act"), generator.GenerateGenericActor("TapeCase_Template", $"{mapWorldRelative}/timeline/{mapNameLower}_tml_dance.tpl")),
                exporter.WriteEngineResourceAsync(context, Path.Combine(timelineFolder, $"{mapNameLower}_tml_dance.tpl"), generator.GenerateTapeCaseTpl(mapName, "dance")),
                exporter.WriteEngineResourceAsync(context, Path.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.act"), generator.GenerateGenericActor("TapeCase_Template", $"{mapWorldRelative}/timeline/{mapNameLower}_tml_karaoke.tpl")),
                exporter.WriteEngineResourceAsync(context, Path.Combine(timelineFolder, $"{mapNameLower}_tml_karaoke.tpl"), generator.GenerateTapeCaseTpl(mapName, "karaoke"))
            ]);
        }

        tasks.AddRange([
            exporter.WriteEngineResourceAsync(context, Path.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.tape"), generator.GenerateMainSequenceTape(package)),
            exporter.WriteEngineResourceAsync(context, Path.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.act"), generator.GenerateGenericActor("MasterTape", $"{mapWorldRelative}/cinematics/{mapNameLower}_mainsequence.tpl")),
            exporter.WriteEngineResourceAsync(context, Path.Combine(cinematicsFolder, $"{mapNameLower}_mainsequence.tpl"), generator.GenerateMainSequenceTpl(mapName)),
            exporter.WriteEngineResourceAsync(context, Path.Combine(mapWorldBase, $"{mapNameLower}_main_scene.isc"), generator.GenerateMainScene(package)),
            exporter.WriteEngineResourceAsync(context, Path.Combine(audioFolder, $"{mapNameLower}_audio.isc"), generator.GenerateAudioScene(package)),
            exporter.WriteEngineResourceAsync(context, Path.Combine(timelineFolder, $"{mapNameLower}_tml.isc"), generator.GenerateTimelineScene(package)),
            exporter.WriteEngineResourceAsync(context, Path.Combine(cinematicsFolder, $"{mapNameLower}_cine.isc"), generator.GenerateCinematicsScene(package)),
            exporter.WriteEngineResourceAsync(context, Path.Combine(menuArtFolder, $"{mapNameLower}_menuart.isc"), generator.GenerateMenuArtScene(package)),
            exporter.WriteEngineResourceAsync(context, Path.Combine(graphFolder, $"{mapNameLower}_graph.isc"), generator.GenerateGraphScene(mapName)),
            exporter.WriteEngineResourceAsync(context, Path.Combine(mapWorldBase, $"{mapNameLower}_main_scene.sgs"), generator.GenerateSgs())
        ]);

        if (writeAutodanceResources)
        {
            tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(autodanceFolder, $"{mapNameLower}_autodance.tpl"), generator.GenerateAutodanceTape(package)));
            tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(autodanceFolder, $"{mapNameLower}_autodance.isc"), generator.GenerateAutodanceScene(package)));
        }

        if (platform != UbiArtPlatform.Uncooked)
        {
            tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(videosFolder, $"{mapNameLower}_video.isc"), generator.GenerateVideoScene(mapName)));
            bool writeVideoMapPreviewScene = version > UbiArtEngineVersion.JD2015 && (!isLegacyFormat || platform == UbiArtPlatform.Xenon);
            if (writeVideoMapPreviewScene)
                tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(videosFolder, $"{mapNameLower}_video_map_preview.isc"), generator.GenerateVideoMapPreviewScene(mapName)));

            if (!isLegacyFormat || version > UbiArtEngineVersion.JD2015)
                tasks.Add(exporter.WriteBinaryFileAsync(context, Path.Combine(videosFolder, "video_player_main.act"), generator.GenerateVideoPlayerActor(mapName, false)));
            if (version > UbiArtEngineVersion.JD2015 && !isLegacyFormat)
                tasks.Add(exporter.WriteBinaryFileAsync(context, Path.Combine(videosFolder, "video_player_map_preview.act"), generator.GenerateVideoPlayerActor(mapName, true)));
            if (!isLegacyFormat)
                tasks.Add(exporter.WriteBinaryFileAsync(context, Path.Combine(videosFolder, $"{mapNameLower}.mpd"), generator.GenerateMpd()));
            if (writeAutodanceResources)
                tasks.Add(exporter.WriteBinaryFileAsync(context, Path.Combine(autodanceFolder, $"{mapNameLower}_autodance.act"), generator.GenerateAutodanceActor(mapName)));
        }
        else
        {
            tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(videosFolder, $"{mapNameLower}_video.isc"), generator.GenerateVideoScene(mapName)));
            tasks.Add(exporter.WriteEngineResourceAsync(context, Path.Combine(videosFolder, "video_player_main.act"), generator.GenerateVideoPlayerActor(mapName, false)));
        }

        await Task.WhenAll(tasks);
    }

    private static bool ShouldWriteAutodanceResources(UbiArtPlatform platform, bool isLegacyFormat)
        => !isLegacyFormat || platform != UbiArtPlatform.Revolution;

    private static bool ShouldWriteSoundSequence(IntermediateSongPackage package)
    {
        bool hasIntroAmbience = package.TimelineStructure.StartBeat < 0 && package.TimelineStructure.Markers.Count > 1;
        return hasIntroAmbience || package.Vibrations.Clips.Count > 0 || package.HideUserInterface.Clips.Count > 0;
    }

    private static async Task WriteUncookedTrackAsync(IntermediateSongPackage package, ExportContext context, string relativePath)
    {
        object structure = new
        {
            MusicTrackStructure = new
            {
                markers = package.TimelineStructure.Markers.Select(marker => new { VAL = marker }).ToArray(),
                signatures = package.TimelineStructure.Signatures.Select(signature => new
                {
                    MusicSignature = new { beats = signature.Beats, marker = signature.Marker, comment = signature.Comment ?? string.Empty }
                }).ToArray(),
                sections = package.TimelineStructure.Sections.Select(section => new
                {
                    MusicSection = new { sectionType = (int)section.SectionType, marker = section.StartBeat, comment = section.Comment ?? string.Empty }
                }).ToArray(),
                comments = Array.Empty<object>(),
                startBeat = package.TimelineStructure.StartBeat,
                endBeat = package.TimelineStructure.EndBeat,
                videoStartTime = package.TimelineStructure.VideoStartOffset,
                previewEntry = package.TimelineStructure.PreviewEntryBeat,
                previewLoopStart = package.TimelineStructure.PreviewLoopStartBeat,
                previewLoopEnd = package.TimelineStructure.PreviewLoopEndBeat,
                previewDuration = package.TimelineStructure.PreviewDuration
            }
        };

        string fullPath = context.IO.Combine(context.OutputFolder, relativePath);
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
        await File.WriteAllTextAsync(fullPath, LuaDocumentWriter.Write(structure, assignment: "structure"));
    }

    private static async Task WriteUncookedAmbienceAsync(UbiArtExportPlan plan, string relativeFolder)
    {
        string audioWorldPath = $"{plan.RawMapWorldBase.Replace('\\', '/')}/audio/amb/amb_{plan.MapNameLower}_intro.wav";
        object descriptor = new[]
        {
            new
            {
                SoundDescriptor_Template = new
                {
                    name = $"amb_{plan.MapNameLower}_intro",
                    volume = -50,
                    category = "AMB",
                    limitMode = new LuaExpression("LimiterMode.RejectNew"),
                    @params = new
                    {
                        SoundParams = new
                        {
                            numChannels = 2,
                            loop = 0,
                            playMode = new LuaExpression("PlayMode.Random"),
                            randomVolMin = 0.0,
                            randomVolMax = 0.0,
                            randomPitchMin = 1.0,
                            randomPitchMax = 1.0,
                            fadeInTime = 0.0,
                            fadeOutTime = 0.0
                        }
                    },
                    files = new[] { new { VAL = audioWorldPath } }
                }
            }
        };

        string iluContent = LuaDocumentWriter.Write(
            descriptor,
            assignment: "DESCRIPTOR",
            trailingStatements: ["appendTable(component.SoundComponent_Template.soundList, DESCRIPTOR)"]);
        string iluPath = plan.Context.IO.Combine(plan.Context.OutputFolder, relativeFolder, $"AMB_{plan.MapName}_Intro.ilu");
        plan.Context.IO.CreateDirectory(Path.GetDirectoryName(iluPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{iluPath}'."));
        await File.WriteAllTextAsync(iluPath, iluContent);

        object template = new
        {
            NAME = "Actor_Template",
            Actor_Template = new { COMPONENTS = Array.Empty<object>() }
        };
        string tplContent = LuaDocumentWriter.Write(template, includes: [audioWorldPath]);
        string tplPath = plan.Context.IO.Combine(plan.Context.OutputFolder, relativeFolder, $"AMB_{plan.MapName}_Intro.tpl");
        await File.WriteAllTextAsync(tplPath, tplContent);
    }
}
