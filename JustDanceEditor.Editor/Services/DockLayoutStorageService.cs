using JustDanceEditor.Editor.Docking;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Editor.Services;

public sealed class DockLayoutStorageService
{
    private const string LayoutExtension = ".jdelayout.json";
    private const string DefaultLayoutResourceSuffix = ".Layouts.DefaultLayout.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _appDataDirectory;
    private readonly string _layoutDirectory;
    private readonly string _statePath;

    public DockLayoutStorageService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            localAppData = Path.GetTempPath();

        _appDataDirectory = Path.Combine(localAppData, "JustDanceEditor");
        _layoutDirectory = Path.Combine(_appDataDirectory, "Layouts");
        _statePath = Path.Combine(_appDataDirectory, "LayoutState.json");
    }

    public IReadOnlyList<string> ListLayoutNames()
    {
        if (!Directory.Exists(_layoutDirectory))
            return [];

        return Directory.EnumerateFiles(_layoutDirectory, "*" + LayoutExtension)
            .Select(TryLoadFromFile)
            .Where(layout => layout != null && !string.IsNullOrWhiteSpace(layout.Name))
            .Select(layout => layout!.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public bool Exists(string name)
        => File.Exists(GetLayoutPath(name));

    public SavedDockLayout Load(string name)
    {
        SavedDockLayout? layout = TryLoadFromFile(GetLayoutPath(name));
        if (layout == null)
            throw new FileNotFoundException($"Layout '{name}' could not be loaded.");

        return layout;
    }

    public void Save(SavedDockLayout layout)
    {
        if (string.IsNullOrWhiteSpace(layout.Name))
            throw new ArgumentException("Layout name is required.", nameof(layout));

        Directory.CreateDirectory(_layoutDirectory);
        string path = GetLayoutPath(layout.Name);
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, layout, JsonOptions);
    }

    public void Delete(string name)
    {
        string path = GetLayoutPath(name);
        if (File.Exists(path))
            File.Delete(path);

        if (string.Equals(GetLastLayoutName(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            SetLastLayoutName(null);
    }

    public void Rename(string oldName, string newName)
    {
        bool wasLastLayout = string.Equals(GetLastLayoutName(), oldName.Trim(), StringComparison.OrdinalIgnoreCase);
        SavedDockLayout layout = Load(oldName);
        Delete(oldName);
        layout.Name = newName;
        Save(layout);
        if (wasLastLayout)
            SetLastLayoutName(newName);
    }

    public string? GetLastLayoutName()
    {
        try
        {
            if (!File.Exists(_statePath))
                return null;

            using FileStream stream = File.OpenRead(_statePath);
            DockLayoutState? state = JsonSerializer.Deserialize<DockLayoutState>(stream, JsonOptions);
            return string.IsNullOrWhiteSpace(state?.LastLayoutName) ? null : state.LastLayoutName.Trim();
        }
        catch
        {
            return null;
        }
    }

    public void SetLastLayoutName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            if (File.Exists(_statePath))
                File.Delete(_statePath);
            return;
        }

        Directory.CreateDirectory(_appDataDirectory);
        using FileStream stream = File.Create(_statePath);
        JsonSerializer.Serialize(stream, new DockLayoutState { LastLayoutName = name.Trim() }, JsonOptions);
    }

    public SavedDockLayout? TryLoadDefaultLayout()
    {
        Assembly assembly = typeof(DockLayoutStorageService).Assembly;
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(DefaultLayoutResourceSuffix, StringComparison.Ordinal));
        if (resourceName == null)
            return null;

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        SavedDockLayout? layout = JsonSerializer.Deserialize<SavedDockLayout>(stream, JsonOptions);
        if (layout != null && string.IsNullOrWhiteSpace(layout.Name))
            layout.Name = "Default";

        return layout;
    }

    private SavedDockLayout? TryLoadFromFile(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<SavedDockLayout>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private string GetLayoutPath(string name)
    {
        string encodedName = Convert.ToBase64String(Encoding.UTF8.GetBytes(name.Trim()))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return Path.Combine(_layoutDirectory, encodedName + LayoutExtension);
    }

    private sealed class DockLayoutState
    {
        public string? LastLayoutName { get; set; }
    }
}