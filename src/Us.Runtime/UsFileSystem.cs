namespace Us.Runtime;

public sealed class MemoryFileSystem : IUsFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal) { string.Empty };
    private readonly HashSet<string> _links = new(StringComparer.Ordinal);

    public void AddLink(string path) => _links.Add(Normalize(path, checkLink: false));

    public string Read(string path)
    {
        var full = Normalize(path, checkLink: true);
        if (!_files.TryGetValue(full, out var text))
        {
            throw new UsRuntimeException(UsRuntimeCodes.FileNotFound, "Dosya bulunamadı.", 0, 0);
        }

        return text;
    }

    public void Write(string path, string text)
    {
        var full = Normalize(path, checkLink: true);
        EnsureParent(full);
        _files[full] = text;
    }

    public void Append(string path, string text)
    {
        var full = Normalize(path, checkLink: true);
        EnsureParent(full);
        _files[full] = _files.TryGetValue(full, out var existing) ? existing + text : text;
    }

    public void Delete(string path)
    {
        var full = Normalize(path, checkLink: true);
        if (!_files.Remove(full))
        {
            throw new UsRuntimeException(UsRuntimeCodes.FileNotFound, "Dosya bulunamadı.", 0, 0);
        }
    }

    public bool Exists(string path)
    {
        var full = Normalize(path, checkLink: true);
        return _files.ContainsKey(full);
    }

    public void CreateDirectory(string path)
    {
        var full = Normalize(path, checkLink: true);
        if (full.Length == 0)
        {
            return;
        }

        EnsureParent(full);
        _directories.Add(full);
    }

    public void DeleteDirectory(string path)
    {
        var full = Normalize(path, checkLink: true);
        if (full.Length == 0 || !_directories.Contains(full))
        {
            throw new UsRuntimeException(UsRuntimeCodes.FileNotFound, "Klasör bulunamadı.", 0, 0);
        }

        if (_files.Keys.Any(item => item.StartsWith(full + "/", StringComparison.Ordinal)) || _directories.Any(item => item.StartsWith(full + "/", StringComparison.Ordinal)))
        {
            throw new UsRuntimeException(UsRuntimeCodes.IoError, "Klasör boş değil.", 0, 0);
        }

        _directories.Remove(full);
    }

    public bool DirectoryExists(string path)
    {
        var full = Normalize(path, checkLink: true);
        return _directories.Contains(full);
    }

    public IReadOnlyList<string> ListFiles(string path)
    {
        var full = Normalize(path, checkLink: true);
        if (!_directories.Contains(full))
        {
            throw new UsRuntimeException(UsRuntimeCodes.FileNotFound, "Klasör bulunamadı.", 0, 0);
        }

        var prefix = full.Length == 0 ? string.Empty : full + "/";
        return _files.Keys
            .Where(item => item.StartsWith(prefix, StringComparison.Ordinal) && item[prefix.Length..].Contains('/', StringComparison.Ordinal) == false)
            .Select(item => item[prefix.Length..])
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    public string Combine(string left, string right) => Normalize(Join(left, right), checkLink: true);

    private void EnsureParent(string full)
    {
        var slash = full.LastIndexOf('/');
        var parent = slash < 0 ? string.Empty : full[..slash];
        if (!_directories.Contains(parent))
        {
            throw new UsRuntimeException(UsRuntimeCodes.InvalidPath, "Üst klasör yok.", 0, 0);
        }
    }

    private string Normalize(string path, bool checkLink)
    {
        if (IsAbsolute(path))
        {
            throw new UsRuntimeException(UsRuntimeCodes.PathEscape, "Yol çalışma kökünün dışına çıkıyor.", 0, 0);
        }

        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    throw new UsRuntimeException(UsRuntimeCodes.PathEscape, "Yol çalışma kökünün dışına çıkıyor.", 0, 0);
                }

                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            if (part.Length >= 2 && char.IsAsciiLetter(part[0]) && part[1] == ':')
            {
                throw new UsRuntimeException(UsRuntimeCodes.PathEscape, "Yol çalışma kökünün dışına çıkıyor.", 0, 0);
            }

            parts.Add(part);
            if (checkLink && _links.Contains(string.Join('/', parts)))
            {
                throw new UsRuntimeException(UsRuntimeCodes.SymbolicLink, "Sembolik bağlantı bu sürümde yok.", 0, 0);
            }
        }

        return string.Join('/', parts);
    }

    private static bool IsAbsolute(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (path[0] is '/' or '\\')
        {
            return true;
        }

        return path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';
    }

    private static string Join(string left, string right)
    {
        if (left.Length == 0)
        {
            return right;
        }

        if (right.Length == 0)
        {
            return left;
        }

        return left + "/" + right;
    }
}

public sealed class DiskFileSystem : IUsFileSystem
{
    private readonly string _root;

    public DiskFileSystem(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new UsRuntimeException(UsRuntimeCodes.PathEscape, "Yol çalışma kökünün dışına çıkıyor.", 0, 0);
        }

        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public string Read(string path)
    {
        var full = Resolve(path);
        try
        {
            return File.ReadAllText(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw Map(exception);
        }
    }

    public void Write(string path, string text)
    {
        var full = Resolve(path);
        var parent = Path.GetDirectoryName(full);
        if (parent is null || !Directory.Exists(parent))
        {
            throw new UsRuntimeException(UsRuntimeCodes.InvalidPath, "Üst klasör yok.", 0, 0);
        }

        try
        {
            File.WriteAllText(full, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw Map(exception);
        }
    }

    public void Append(string path, string text)
    {
        var full = Resolve(path);
        var parent = Path.GetDirectoryName(full);
        if (parent is null || !Directory.Exists(parent))
        {
            throw new UsRuntimeException(UsRuntimeCodes.InvalidPath, "Üst klasör yok.", 0, 0);
        }

        try
        {
            File.AppendAllText(full, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw Map(exception);
        }
    }

    public void Delete(string path)
    {
        var full = Resolve(path);
        if (!File.Exists(full))
        {
            throw new UsRuntimeException(UsRuntimeCodes.FileNotFound, "Dosya bulunamadı.", 0, 0);
        }

        try
        {
            File.Delete(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Map(exception);
        }
    }

    public bool Exists(string path) => File.Exists(Resolve(path));

    public void CreateDirectory(string path)
    {
        var full = Resolve(path);
        var parent = Path.GetDirectoryName(full);
        if (parent is null || !Directory.Exists(parent))
        {
            throw new UsRuntimeException(UsRuntimeCodes.InvalidPath, "Üst klasör yok.", 0, 0);
        }

        Directory.CreateDirectory(full);
    }

    public void DeleteDirectory(string path)
    {
        var full = Resolve(path);
        if (!Directory.Exists(full))
        {
            throw new UsRuntimeException(UsRuntimeCodes.FileNotFound, "Klasör bulunamadı.", 0, 0);
        }

        if (Directory.EnumerateFileSystemEntries(full).Any())
        {
            throw new UsRuntimeException(UsRuntimeCodes.IoError, "Klasör boş değil.", 0, 0);
        }

        Directory.Delete(full);
    }

    public bool DirectoryExists(string path) => Directory.Exists(Resolve(path));

    public IReadOnlyList<string> ListFiles(string path)
    {
        var full = Resolve(path);
        if (!Directory.Exists(full))
        {
            throw new UsRuntimeException(UsRuntimeCodes.FileNotFound, "Klasör bulunamadı.", 0, 0);
        }

        return Directory.EnumerateFiles(full)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public string Combine(string left, string right)
    {
        var full = Resolve(Join(left, right));
        var relative = Path.GetRelativePath(_root, full);
        return relative.Replace('\\', '/');
    }

    private string Resolve(string path)
    {
        var full = Path.GetFullPath(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
        var root = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !string.Equals(full, _root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new UsRuntimeException(UsRuntimeCodes.PathEscape, "Yol çalışma kökünün dışına çıkıyor.", 0, 0);
        }

        var cursor = full;
        while (!string.Equals(cursor.TrimEnd(Path.DirectorySeparatorChar), _root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(cursor) || File.Exists(cursor))
            {
                var attributes = File.GetAttributes(cursor);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new UsRuntimeException(UsRuntimeCodes.SymbolicLink, "Sembolik bağlantı bu sürümde yok.", 0, 0);
                }
            }

            var parent = Path.GetDirectoryName(cursor);
            if (parent is null || parent == cursor)
            {
                break;
            }

            cursor = parent;
        }

        return full;
    }

    private static string Join(string left, string right) =>
        left.Length == 0 ? right : right.Length == 0 ? left : left + "/" + right;

    private static UsRuntimeException Map(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => new UsRuntimeException(UsRuntimeCodes.FileNotFound, "Dosya bulunamadı.", 0, 0),
        UnauthorizedAccessException => new UsRuntimeException(UsRuntimeCodes.FileAccess, "Dosyaya erişilemedi.", 0, 0),
        ArgumentException => new UsRuntimeException(UsRuntimeCodes.InvalidPath, "Yol geçersiz.", 0, 0),
        _ => new UsRuntimeException(UsRuntimeCodes.IoError, "Dosya işlemi tamamlanamadı.", 0, 0),
    };
}
