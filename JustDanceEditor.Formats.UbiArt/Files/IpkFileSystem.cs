using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.IPK;

namespace JustDanceEditor.Formats.UbiArt.Files;

internal sealed class IpkFileSystem : IFileSystem, IDisposable
{
    private readonly Stream _stream;
    private readonly BinaryReader _reader;
    private readonly bool _ownsStream;

    private readonly Dictionary<string, (long Offset, int Size, int ZSize)> _entries = new(StringComparer.OrdinalIgnoreCase);

    public IpkFileSystem(Stream ipkStream)
    {
        _stream = ipkStream ?? throw new ArgumentNullException(nameof(ipkStream));
        if (!_stream.CanSeek)
            throw new ArgumentException("IPK stream must be seekable.", nameof(ipkStream));
        _reader = new BinaryReader(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
        _ownsStream = false;
        ParseIndex();
    }

    public IpkFileSystem(string ipkPath)
    {
        if (!File.Exists(ipkPath))
            throw new FileNotFoundException("IPK not found", ipkPath);
        _stream = new FileStream(ipkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _reader = new BinaryReader(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
        _ownsStream = true;
        ParseIndex();
    }

    private void ParseIndex()
    {
        // Read header (copying the logic used by JustDanceIPKParser)
        _stream.Seek(0, SeekOrigin.Begin);
        byte[] magic = _reader.ReadBytes(4);
        byte[] IdString = [0x50, 0xEC, 0x12, 0xBA];
        if (!magic.SequenceEqual(IdString))
            throw new InvalidDataException("Invalid IPK file");

        long version = _reader.ReadInt32BigEndian();
        _reader.ReadInt32BigEndian(); // dummy
        long baseOffset = _reader.ReadInt32BigEndian();
        long filesCount = _reader.ReadInt32BigEndian();

        _stream.Seek(0x30, SeekOrigin.Begin);

        // First pass: read all entries to detect if paths are swapped
        List<(int Dummy1, int Size, int ZSize, long TimeStamp, long Offset, string Path, string Name, int Crc, int Dummy2)> tempEntries = [];
        for (int i = 0; i < filesCount; i++)
        {
            var entry = ReadFileEntry(_reader, baseOffset);
            tempEntries.Add(entry);
        }

        // Detect if paths are swapped by checking if most Name entries contain "cache/itf_cooked"
        // This is a marker for WiiU bundles where Name contains folder path and Path contains filename
        bool pathsAreSwapped = false;
        if (tempEntries.Count > 0)
        {
            int cacheCount = tempEntries.Count(e => e.Name.Contains("cache/itf_cooked", StringComparison.OrdinalIgnoreCase));
            pathsAreSwapped = cacheCount > tempEntries.Count / 2;
        }

        // Second pass: add entries with correct path resolution
        foreach (var entry in tempEntries)
        {
            // Determine logical path based on detection
            (string fileName, string folderPath) = pathsAreSwapped
                ? (entry.Path, entry.Name)  // WiiU bundles: Name is folder, Path is file
                : (entry.Name.Contains('.') ? (entry.Name, entry.Path) : (entry.Path, entry.Name));
            string logical = Path.Combine(folderPath ?? string.Empty, fileName ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
            _entries[logical] = (entry.Offset, entry.Size, entry.ZSize);
        }
    }

    private static (int Dummy1, int Size, int ZSize, long TimeStamp, long Offset, string Path, string Name, int Crc, int Dummy2) ReadFileEntry(BinaryReader reader, long baseOffset)
    {
        int dummy1 = reader.ReadInt32BigEndian();
        int size = reader.ReadInt32BigEndian();
        int zsize = reader.ReadInt32BigEndian();
        long timestamp = reader.ReadInt64BigEndian();
        long offset = reader.ReadInt64BigEndian();

        if (dummy1 == 2)
        {
            reader.ReadInt32BigEndian();
            reader.ReadInt32BigEndian();
        }

        string path = reader.ReadNTString();
        string name = reader.ReadNTString();
        int crc = reader.ReadInt32BigEndian();
        int dummy2 = reader.ReadInt32BigEndian();

        offset += baseOffset;
        return (dummy1, size, zsize, timestamp, offset, path, name, crc, dummy2);
    }

    public bool DirectoryExists(string path)
    {
        string norm = NormalizePath(path);
        return _entries.Keys.Any(k => IsChildPath(norm, k));
    }

    public bool FileExists(string path)
    {
        string norm = NormalizePath(path);
        return _entries.ContainsKey(norm);
    }

    public string[] GetDirectories(string path)
    {
        string norm = NormalizePath(path);
        HashSet<string> dirs = new(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _entries.Keys)
        {
            if (!IsChildPath(norm, key))
                continue;
            var rel = key[norm.Length..].TrimStart(Path.DirectorySeparatorChar);
            var first = rel.Split(Path.DirectorySeparatorChar)[0];
            dirs.Add(Path.Combine(norm, first));
        }

        return [.. dirs];
    }

    public string[] GetFiles(string path, string searchPattern = "*")
    {
        string norm = NormalizePath(path);
        List<string> files = [];
        var regex = WildcardToRegex(searchPattern);
        foreach (var key in _entries.Keys)
        {
            if (!IsChildPath(norm, key))
                continue;
            var rel = key[norm.Length..].TrimStart(Path.DirectorySeparatorChar);
            if (rel.Contains(Path.DirectorySeparatorChar))
                continue; // not a direct child
            if (regex.IsMatch(rel))
                files.Add(Path.GetFileName(key));
        }

        return [.. files];
    }

    public string ReadAllText(string path)
    {
        byte[] bytes = ReadAllBytes(path);
        return System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    public byte[] ReadAllBytes(string path)
    {
        string norm = NormalizePath(path);
        if (!_entries.TryGetValue(norm, out var meta))
            throw new FileNotFoundException(path);

        lock (_stream)
        {
            _stream.Seek(meta.Offset, SeekOrigin.Begin);
            if (meta.ZSize == 0)
            {
                return _reader.ReadBytes(meta.Size);
            }
            else
            {
                byte[] compressed = _reader.ReadBytes(meta.ZSize);
                return Decompressor.Decompress(compressed);
            }
        }
    }

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("IPK filesystem is read-only");
    }

    public void Copy(string sourcePath, string destinationPath, bool overwrite = false)
    {
        throw new NotSupportedException("IPK filesystem is read-only");
    }

    public void Move(string sourcePath, string destinationPath, bool overwrite = false)
    {
        throw new NotSupportedException("IPK filesystem is read-only");
    }

    public void DeleteFile(string path) => throw new NotSupportedException("IPK filesystem is read-only");

    public void CreateDirectory(string path) => throw new NotSupportedException("IPK filesystem is read-only");

    public void DeleteDirectory(string path, bool recursive) => throw new NotSupportedException("IPK filesystem is read-only");

    public string Combine(params string[] paths) => Path.Combine(paths);

    public string GetTempPath() => Path.GetTempPath();

    public string GetFullPath(string path) => Path.GetFullPath(path);

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        string p = path.Replace('/', Path.DirectorySeparatorChar);
        p = p.TrimStart('.', Path.DirectorySeparatorChar);
        return p.Replace('\\', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
    }

    private static bool IsChildPath(string parent, string candidate)
    {
        string np = parent.TrimEnd(Path.DirectorySeparatorChar);
        string nc = candidate.TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(np))
            return true;
        return nc.StartsWith(np + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(np, nc, StringComparison.OrdinalIgnoreCase);
    }

    private static System.Text.RegularExpressions.Regex WildcardToRegex(string pattern)
    {
        string rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return new System.Text.RegularExpressions.Regex(rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    public void Dispose()
    {
        try
        {
            _reader?.Dispose();
            if (_ownsStream)
                _stream?.Dispose();
        }
        catch { }
    }
}