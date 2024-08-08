﻿namespace JustDanceEditor.Converter;

public class ConversionRequest
{
    // Folder where the input files are located
    public required string InputPath { get; set; }
    // Folder where the output will be saved
    public required string OutputPath { get; set; }
    // Folder where the template is located
    public required string TemplatePath { get; set; }
    // Should the cover be looked up online
    public bool OnlineCover { get; set; } = true;
    // Name of the song (optional)
    public string? SongName { get; set; } = null;
    // GUID of the song
    public string SongID { get; set; } = Guid.NewGuid().ToString();
    // Cache number
    public uint CacheNumber { get; set; } = 123;
    public uint? JDVersion { get; set; } = null;
}
