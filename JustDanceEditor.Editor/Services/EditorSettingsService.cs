using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Formats.JDI.Recordings;

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Editor.Services;

public sealed partial class EditorSettingsService : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    [ObservableProperty]
    public partial MotionRecordingScoringProfile ScoringProfile { get; set; } = MotionRecordingScoringProfile.JDNext;

    [ObservableProperty]
    public partial bool UseAdvancedScoringTuning { get; set; }

    public EditorSettingsService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            localAppData = Path.GetTempPath();

        _settingsPath = Path.Combine(localAppData, "JustDanceEditor", "EditorSettings.json");
        Load();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath) ?? Path.GetTempPath());
        using FileStream stream = File.Create(_settingsPath);
        JsonSerializer.Serialize(stream, new EditorSettingsState
        {
            ScoringProfile = ScoringProfile,
            UseAdvancedScoringTuning = UseAdvancedScoringTuning
        }, JsonOptions);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return;

            using FileStream stream = File.OpenRead(_settingsPath);
            EditorSettingsState? state = JsonSerializer.Deserialize<EditorSettingsState>(stream, JsonOptions);
            if (state == null)
                return;

            if (Enum.IsDefined(state.ScoringProfile))
                ScoringProfile = state.ScoringProfile;

            UseAdvancedScoringTuning = state.UseAdvancedScoringTuning;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            EditorLog.Fallback(ex, "Load editor settings");
            ScoringProfile = MotionRecordingScoringProfile.JDNext;
            UseAdvancedScoringTuning = false;
        }
    }

    private sealed class EditorSettingsState
    {
        public MotionRecordingScoringProfile ScoringProfile { get; set; } = MotionRecordingScoringProfile.JDNext;
        public bool UseAdvancedScoringTuning { get; set; }
    }
}
