namespace JustDanceEditor.Formats.JDI.Video;

public record VideoQualityProfile(
    string FileName,
    int? Width,
    int? Height,
    string Bitrate,
    string MaxBitrate,
    string BufferSize
);

public static class JdiVideoProfiles
{
    public static readonly VideoQualityProfile[] Masters =
    [
        new("master_low.webm", 480, 270, "700k", "800k", "1400k"),
        new("master_med.webm", 768, 432, "1500k", "1800k", "3000k"),
        new("master_high.webm", 1280, 720, "3000k", "3500k", "60000k"),
        new("master_ultra.webm", 1920, 1080, "4500k", "5200k", "90000k")
    ];

    public static readonly VideoQualityProfile[] Previews =
    [
        new("preview_low.webm", 768, 432, "650k", "750k", "1300k"),
        new("preview_med.webm", 768, 432, "1500k", "1800k", "3000k"),
        new("preview_high.webm", 768, 432, "3000k", "3500k", "6000k"),
        new("preview_ultra.webm", 768, 432, "6000k", "7000k", "12000k")
    ];
}