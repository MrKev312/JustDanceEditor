using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JustDanceEditor.JD2Next;

namespace JustDanceEditor.Helpers;
internal class JDSongFactory
{
    // This is the hardcoded value, taken from Encanto/We don't talk about Bruno
    // Not for any particular reason, just because it's the first song I saw
    // Without this the preview video won't work
    static readonly string audioTrackData = "{\"Markers\":[0,16552,33103,49655,66207,82759,99310,115862,132414,148966,165517,182069,198621,215172,231724,248276,264828,281379,297931,314483,331034,347586,364138,380690,397241,413793,430345,446897,463448,480000,496552,513103,529655,546207,562759,579310,595862,612414,628966,645517,662069,678621,695172,711724,728276,744828,761379,777931,794483,811034,827586,844138,860690,877241,893793,910345,926897,943448,960000,976552,993103,1009655,1026207,1042759,1059310,1075862,1092414,1108966,1125517,1142069,1158621,1175172,1191724,1208276,1224828,1241379,1257931,1274483,1291034,1307586,1324138,1340690,1357241,1373793,1390345,1406897,1423448,1440000,1456552,1473103,1489655,1506207,1522759,1539310,1555862,1572414,1588966,1605517,1622069,1638621,1655172,1671724,1688276,1704828,1721379,1737931,1754483,1771034,1787586,1804138,1820690,1837241,1853793,1870345,1886897,1903448,1920000,1936552,1953103,1969655,1986207,2002759,2019310,2035862,2052414,2068966,2085517,2102069,2118621,2135172,2151724,2168276,2184828,2201379,2217931,2234483,2251034,2267586,2284138,2300690,2317241,2333793,2350345,2366897,2383448,2400000,2416552,2433103,2449655,2466207,2482759,2499310,2515862,2532414,2548966,2565517,2582069,2598621,2615172,2631724,2648276,2664828,2681379,2697931,2714483,2731034,2747586,2764138,2780690,2797241,2813793,2830345,2846897,2863448,2880000,2896552,2913103,2929655,2946207,2962759,2979310,2995862,3012414,3028966,3045517,3062069,3078621,3095172,3111724,3128276,3144828,3161379,3177931,3194483,3211034,3227586,3244138,3260690,3277241,3293793,3310345,3326897,3343448,3360000,3376552,3393103,3409655,3426207,3442759,3459310,3475862,3492414,3508966,3525517,3542069,3558621,3575172,3591724,3608276,3624828,3641379,3657931,3674483,3691034,3707586,3724138,3740690,3757241,3773793,3790345,3806897,3823448,3840000,3856552,3873103,3889655,3906207,3922759,3939310,3955862,3972414,3988966,4005517,4022069,4038621,4055172,4071724,4088276,4104828,4121379,4137931,4154483,4171034,4187586,4204138,4220690,4237241,4253793,4270345,4286897,4303448,4320000,4336552,4353103,4369655,4386207,4402759,4419310,4435862,4452414,4468966,4485517,4502069,4518621,4535172,4551724,4568276,4584828,4601379,4617931,4634483,4651034,4667586,4684138,4700690,4717241,4733793,4750345,4766897,4783448,4800000,4816552,4833103,4849655,4866207,4882759,4899310,4915862,4932414,4948966,4965517,4982069,4998621,5015172,5031724,5048276,5064828,5081379,5097931,5114483,5131034,5147586,5164138,5180690,5197241,5213793,5230345,5246897,5263448,5280000,5296552,5313103,5329655,5346207,5362759,5379310,5395862,5412414,5428966,5445517,5462069,5478621,5495172,5511724,5528276,5544828,5561379,5577931,5594483,5611034,5627586,5644138,5660690,5677241,5693793,5710345,5726897,5743448,5760000,5776552,5793103,5809655,5826207,5842759,5859310,5875862,5892414,5908966,5925517,5942069,5958621,5975172,5991724,6008276,6024828,6041379,6057931,6074483,6091034,6107586,6124138,6140690,6157241,6173793,6190345,6206897,6223448,6240000,6256552,6273103,6289655,6306207,6322759,6339310,6355862,6372414,6388966,6405517,6422069,6438621,6455172,6471724,6488276,6504828,6521379,6537931,6554483,6571034,6587586,6604138,6620690,6637241,6653793,6670345,6686897,6703448,6720000,6736552,6753103,6769655,6786207,6802759,6819310,6835862,6852414,6868966,6885517,6902069,6918621,6935172,6951724,6968276,6984828,7001379,7017931,7034483,7051034,7067586,7084138,7100690,7117241,7133793,7150345,7166897,7183448,7200000,7216552,7233103,7249655,7266207,7282759,7299310,7315862,7332414,7348966,7365517,7382069,7398621,7415172,7431724,7448276,7464828,7481379,7497931,7514483,7531034,7547586,7564138,7580690,7597241,7613793,7630345,7646897,7663448,7680000,7696552,7713103,7729655,7746207,7762759,7779310,7795862,7812414,7828966,7845517,7862069,7878621,7895172,7911724,7928276,7944828,7961379,7977931,7994483,8011034,8027585],\"StartBeat\":-8.0,\"EndBeat\":481.0,\"VideoStartTime\":-14.828,\"PreviewEntry\":327.0,\"PreviewLoopStart\":329.0,\"PreviewLoopEnd\":401.0}";

    public static JDSong CreateSong(SongDatabaseEntry songData, uint cacheNumber, string coverFilename, string coachesSmallFilename, string coachesLargeFilename, string audioPreviewOpusFilename, string videoPreviewMidVp9WebmFilename, string audioOpusFilename, string videoHighVp9WebmFilename, string mapPackageFilename, string? songTitleLogoFilename = null, string? guid = null)
    {
        guid ??= Guid.NewGuid().ToString();

        JDSong song = new()
        {
            SongDatabaseEntry = songData,
            AudioPreviewTrk = audioTrackData,
            AssetFilesDict = new AssetFilesDict
            {
                Cover = new()
                {
                    AssetType = AssetType.Cover,
                    Name = "Cover",
                    Hash = coverFilename,
                    Ready = true,
                    Size = 0,
                    Category = 0,
                    FilePath = $"/CacheStorage_0/MapBaseCache/{guid}/Cover/{coverFilename}"
                },
                CoachesSmall = new()
                {
                    AssetType = AssetType.CoachesSmall,
                    Name = "CoachesSmall",
                    Hash = coachesSmallFilename,
                    Ready = true,
                    Size = 0,
                    Category = 1,
                    FilePath = $"/CacheStorage_{cacheNumber}/MapBaseCache/{guid}/CoachesSmall/{coachesSmallFilename}"
                },
                CoachesLarge = new()
                {
                    AssetType = AssetType.CoachesLarge,
                    Name = "CoachesLarge",
                    Hash = coachesLargeFilename,
                    Ready = true,
                    Size = 0,
                    Category = 1,
                    FilePath = $"/CacheStorage_{cacheNumber}/MapBaseCache/{guid}/CoachesLarge/{coachesLargeFilename}"
                },
                AudioPreview_opus = new()
                {
                    AssetType = AssetType.AudioPreview_opus,
                    Name = "AudioPreview_opus",
                    Hash = audioPreviewOpusFilename,
                    Ready = true,
                    Size = 0,
                    Category = 0,
                    FilePath = $"/CacheStorage_0/MapBaseCache/{guid}/AudioPreview_opus/{audioPreviewOpusFilename}"
                },
                VideoPreview_MID_vp9_webm = new()
                {
                    AssetType = AssetType.VideoPreview_MID_vp9_webm,
                    Name = "VideoPreview_MID_vp9_webm",
                    Hash = videoPreviewMidVp9WebmFilename,
                    Ready = true,
                    Size = 0,
                    Category = 0,
                    FilePath = $"/CacheStorage_0/MapBaseCache/{guid}/VideoPreview_MID_vp9_webm/{videoPreviewMidVp9WebmFilename}"
                },
                Audio_opus = new()
                {
                    AssetType = AssetType.Audio_opus,
                    Name = "Audio_opus",
                    Hash = audioOpusFilename,
                    Ready = true,
                    Size = 0,
                    Category = 1,
                    FilePath = $"/CacheStorage_{cacheNumber}/MapBaseCache/{guid}/Audio_opus/{audioOpusFilename}"
                },
                Video_HIGH_vp9_webm = new()
                {
                    AssetType = AssetType.Video_HIGH_vp9_webm,
                    Name = "Video_HIGH_vp9_webm",
                    Hash = videoHighVp9WebmFilename,
                    Ready = true,
                    Size = 0,
                    Category = 1,
                    FilePath = $"/CacheStorage_{cacheNumber}/MapBaseCache/{guid}/Video_HIGH_vp9_webm/{videoHighVp9WebmFilename}"
                },
                MapPackage = new()
                {
                    AssetType = AssetType.MapPackage,
                    Name = "MapPackage",
                    Hash = mapPackageFilename,
                    Ready = true,
                    Size = 0,
                    Category = 1,
                    FilePath = $"/CacheStorage_{cacheNumber}/MapBaseCache/{guid}/MapPackage/{mapPackageFilename}"
                }
            },
            Sizes = new(),
            HasSongTitleInCover = songTitleLogoFilename != null,
        };

        if (songTitleLogoFilename != null)
        {
            song.AssetFilesDict.SongTitleLogo = new()
            {
                AssetType = AssetType.SongTitleLogo,
                Name = "SongTitleLogo",
                Hash = songTitleLogoFilename,
                Ready = true,
                Size = 0,
                Category = 0,
                FilePath = $"/CacheStorage_0/MapBaseCache/{guid}/SongTitleLogo/{songTitleLogoFilename}"
            };

            song.SongDatabaseEntry.HasSongTitleInCover = true;
        }

        return song;
    }

    public static string CacheJson(uint cacheNumber, string guid)
    {
        return $$"""
            {
              "$type": "JD.CacheSystem.JDNCache, Ubisoft.JustDance.CacheSystem",
              "totalSize": 0,
              "free": 71568604,
              "journal": 0,
              "cachedStreamsDict": {
                "$type": "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[JD.CacheSystem.CacheWriteJob, Ubisoft.JustDance.CacheSystem]], mscorlib"
              },
              "name": "{{guid}}",
              "path": "/CacheStorage_{{cacheNumber}}/{{guid}}",
              "pathNX": "CacheStorage_{{cacheNumber}}/{{guid}}",
              "index": {{cacheNumber}}
            }
            """;
    }

    public static string MapBaseJson()
    {
        return $$"""
            {
              "$type": "JD.CacheSystem.JDNCache, Ubisoft.JustDance.CacheSystem",
              "totalSize": 0,
              "free": 71568604,
              "journal": 0,
              "cachedStreamsDict": {
                "$type": "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[JD.CacheSystem.CacheWriteJob, Ubisoft.JustDance.CacheSystem]], mscorlib"
              },
              "name": "MapBaseCache",
              "path": "/CacheStorage_0/MapBaseCache",
              "pathNX": "CacheStorage_0/MapBaseCache",
              "index": 0
            }
            """;
    }

    public static string AddressablesJson()
    {
        return """
            {
              "$type": "JD.CacheSystem.JDNCache, Ubisoft.JustDance.CacheSystem",
              "totalSize": 524353536,
              "free": 524353536,
              "journal": 104923136,
              "cachedStreamsDict": {
                "$type": "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[JD.CacheSystem.CacheWriteJob, Ubisoft.JustDance.CacheSystem]], mscorlib"
              },
              "name": "Addressables",
              "path": "/CacheStorage_0/Addressables",
              "pathNX": "CacheStorage_0:/Addressables",
              "index": 0
            }
            """;
    }
}
