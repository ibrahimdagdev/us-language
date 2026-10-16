using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Us.Compiler;
using Us.Core;
using Us.Interpreter;
using Us.Tooling;
using Us.Tooling.Lsp;

namespace Us.Cli;

public static class UsCli
{
    public static readonly string UsageText =
        """
        Kullanım:
          us yeni konsol|kütüphane|web <ad>
          us çalıştır [dosya|klasör]
          us derle
          us test
          us yayınla
          us paket ekle <ad> [sürüm]
          us paket kur <arşiv.zip> [ad] [sürüm]
          us veritabanı senkronize [onay]
          us biçimlendir [dosya]
          us dil-sunucusu
        """.Replace("\r\n", "\n", StringComparison.Ordinal);

    public static Task<CliResult> RunAsync(string[] args, CliHost host)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(host);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        return DispatchAsync(args, host);
    }

    private static async Task<CliResult> DispatchAsync(string[] args, CliHost host)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            host.Out.WriteLine(UsageText);
            return CliResult.Ok(UsageText);
        }

        if (IsVersion(args[0]))
        {
            var informational = (System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(
                typeof(UsCli).Assembly,
                typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            var version = informational?.InformationalVersion
                ?? typeof(UsCli).Assembly.GetName().Version?.ToString()
                ?? "0.0.0";
            var plus = version.IndexOf('+', StringComparison.Ordinal);
            if (plus >= 0)
            {
                version = version[..plus];
            }

            host.Out.WriteLine(version);
            return CliResult.Ok(version);
        }

        var command = args[0];
        return command switch
        {
            "yeni" => NewProject(args.Skip(1).ToArray(), host),
            "çalıştır" => await RunCommandAsync(args.Skip(1).ToArray(), host).ConfigureAwait(false),
            "derle" => BuildCommand(host),
            "test" => await TestCommandAsync(host).ConfigureAwait(false),
            "yayınla" => PublishCommand(host),
            "paket" => PackageCommand(args.Skip(1).ToArray(), host),
            "veritabanı" => DatabaseCommand(args.Skip(1).ToArray(), host),
            "biçimlendir" => FormatCommand(args.Skip(1).ToArray(), host),
            "dil-sunucusu" => LanguageServerCommand(host),
            _ => Unknown(command, host),
        };
    }

    private static CliResult LanguageServerCommand(CliHost host)
    {
        var code = StdioLanguageServerHost.Run(host.In, host.Out, host.Error);
        return code == 0 ? CliResult.Ok("dil-sunucusu") : CliResult.Fail(code, []);
    }

    private static CliResult FormatCommand(string[] args, CliHost host)
    {
        try
        {
            string path;
            if (args.Length == 0)
            {
                var project = ProjectLoader.TryLoadFromDirectory(host.WorkingDirectory);
                if (project is null || project.EntryPath is null)
                {
                    var missing = ProjectDiagnostic(DiagnosticCodes.MissingEntry, "Biçimlendirilecek giriş dosyası yok.", host.WorkingDirectory, "us biçimlendir ana.us");
                    DiagnosticWriter.Write(host.Error, [missing]);
                    return CliResult.Fail(1, [missing]);
                }

                path = project.EntryPath;
            }
            else
            {
                path = Path.IsPathRooted(args[0]) ? args[0] : Path.Combine(host.WorkingDirectory, args[0]);
            }

            if (!File.Exists(path))
            {
                var missing = ProjectDiagnostic(DiagnosticCodes.MissingEntry, $"'{path}' bulunamadı.", host.WorkingDirectory, "Dosya yolunu kontrol edin.");
                DiagnosticWriter.Write(host.Error, [missing]);
                return CliResult.Fail(1, [missing]);
            }

            var source = File.ReadAllText(path);
            var formatted = Formatter.Format(source, path);
            File.WriteAllText(path, formatted.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
            host.Out.WriteLine($"Biçimlendirildi: {path}");
            return CliResult.Ok($"Biçimlendirildi: {path}");
        }
        catch (Exception exception)
        {
            var diagnostic = ProjectDiagnostic(DiagnosticCodes.UnknownCommand, $"Biçimlendirme başarısız: {exception.Message}", host.WorkingDirectory, "Kaynak metni kontrol edin.");
            DiagnosticWriter.Write(host.Error, [diagnostic]);
            return CliResult.Fail(1, [diagnostic]);
        }
    }

    private static CliResult Unknown(string command, CliHost host)
    {
        var diagnostic = new Diagnostic(
            DiagnosticCodes.UnknownCommand,
            DiagnosticSeverity.Error,
            $"'{command}' komutu bilinmiyor.",
            host.WorkingDirectory,
            new TextSpan(0, 0),
            1,
            1,
            UsageText);
        DiagnosticWriter.Write(host.Error, [diagnostic]);
        host.Error.WriteLine(UsageText);
        return CliResult.Fail(1, [diagnostic], UsageText);
    }

    private static CliResult NewProject(string[] args, CliHost host)
    {
        if (args.Length < 2)
        {
            return Unknown("yeni", host);
        }

        var kind = args[0];
        var name = args[1];
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            var bad = ProjectDiagnostic(DiagnosticCodes.UnknownProjectKey, $"'{name}' proje adı geçersiz.", host.WorkingDirectory, "Geçerli bir dizin adı yazın.");
            DiagnosticWriter.Write(host.Error, [bad]);
            return CliResult.Fail(1, [bad]);
        }

        var target = kind switch
        {
            "konsol" => ProjectTarget.Application,
            "kütüphane" => ProjectTarget.Library,
            "web" => ProjectTarget.Web,
            _ => (ProjectTarget?)null,
        };
        if (target is null)
        {
            return Unknown($"yeni {kind}", host);
        }

        var directory = Path.Combine(host.WorkingDirectory, name);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            var exists = ProjectDiagnostic(DiagnosticCodes.ExistingPath, $"'{name}' dizini zaten dolu.", directory, "Boş bir ad seçin.");
            DiagnosticWriter.Write(host.Error, [exists]);
            return CliResult.Fail(1, [exists]);
        }

        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, ProjectLoader.ManifestFileName);
        if (File.Exists(manifestPath))
        {
            var exists = ProjectDiagnostic(DiagnosticCodes.ExistingPath, $"'{ProjectLoader.ManifestFileName}' zaten var.", manifestPath, "Var olan dosyanın üstüne yazılmaz.");
            DiagnosticWriter.Write(host.Error, [exists]);
            return CliResult.Fail(1, [exists]);
        }

        var hedef = target switch
        {
            ProjectTarget.Application => "uygulama",
            ProjectTarget.Library => "kütüphane",
            ProjectTarget.Web => "web",
            _ => "uygulama",
        };
        var manifest = $"""
            proje "{name}"
            sürüm "1.0.0"
            hedef "{hedef}"
            başlangıç "ana.us"
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (target == ProjectTarget.Library)
        {
            manifest = $"""
                proje "{name}"
                sürüm "1.0.0"
                hedef "kütüphane"
                """.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        AtomicWrite(manifestPath, manifest + "\n");

        if (target == ProjectTarget.Application)
        {
            var entry = Path.Combine(directory, ProjectLoader.DefaultEntry);
            AtomicWrite(entry, "syaz \"Merhaba US!\"\n");
        }
        else if (target == ProjectTarget.Web)
        {
            var entry = Path.Combine(directory, ProjectLoader.DefaultEntry);
            AtomicWrite(entry, """
                kontrolcü AnaKontrolcü : "/"
                {
                    rota GET "/"
                    fonksiyon ana(): metin
                    {
                        => "Merhaba US!"
                    }
                }
                """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        }

        host.Out.WriteLine($"Proje oluşturuldu: {directory}");
        return CliResult.Ok($"Proje oluşturuldu: {directory}");
    }

    private static async Task<CliResult> RunCommandAsync(string[] args, CliHost host)
    {
        if (args.Length > 0)
        {
            var path = Path.GetFullPath(Path.Combine(host.WorkingDirectory, args[0]));
            if (File.Exists(path) && path.EndsWith(".us", StringComparison.OrdinalIgnoreCase))
            {
                return await RunFileAsync(path, Path.GetDirectoryName(path)!, host).ConfigureAwait(false);
            }

            if (Directory.Exists(path))
            {
                var project = ProjectLoader.Load(path);
                return await RunProjectAsync(project, host).ConfigureAwait(false);
            }

            var missing = ProjectDiagnostic(DiagnosticCodes.MissingEntry, $"'{args[0]}' bulunamadı.", path, "Geçerli bir .us dosyası veya proje klasörü verin.");
            DiagnosticWriter.Write(host.Error, [missing]);
            return CliResult.Fail(1, [missing]);
        }

        var current = ProjectLoader.TryLoadFromDirectory(host.WorkingDirectory);
        if (current is null)
        {
            var missing = ProjectDiagnostic(DiagnosticCodes.MissingEntry, $"'{ProjectLoader.ManifestFileName}' bulunamadı.", host.WorkingDirectory, "Proje kökünde us çalıştır veya dosya yolu verin.");
            DiagnosticWriter.Write(host.Error, [missing]);
            return CliResult.Fail(1, [missing]);
        }

        return await RunProjectAsync(current, host).ConfigureAwait(false);
    }

    private static async Task<CliResult> RunProjectAsync(UsProject project, CliHost host)
    {
        if (project.HasErrors)
        {
            DiagnosticWriter.Write(host.Error, project.Diagnostics);
            return CliResult.Fail(1, project.Diagnostics);
        }

        if (project.Target == ProjectTarget.Library)
        {
            var build = global::Us.Compiler.Compiler.Compile(project);
            DiagnosticWriter.Write(host.Error, build.Diagnostics);
            if (!build.Success)
            {
                return CliResult.Fail(1, build.Diagnostics);
            }

            host.Out.WriteLine("Kütüphane derlemesi hazır. Çalıştırılacak giriş yok.");
            return CliResult.Ok("Kütüphane derlemesi hazır. Çalıştırılacak giriş yok.");
        }

        if (project.EntryPath is null)
        {
            var missing = ProjectDiagnostic(DiagnosticCodes.MissingEntry, "Giriş dosyası yok.", project.Root, "ana.us ekleyin.");
            DiagnosticWriter.Write(host.Error, [missing]);
            return CliResult.Fail(1, [missing]);
        }

        var files = ProjectLoader.LoadSourceFiles(project);
        var entry = files.First(item => string.Equals(item.Path, project.EntryPath, StringComparison.OrdinalIgnoreCase));
        var result = await UsApplicationHost.StartAsync(project.Root, files, entry).ConfigureAwait(false);
        return FinishExecution(result, host);
    }

    private static async Task<CliResult> RunFileAsync(string path, string root, CliHost host)
    {
        var file = SourceFile.FromUtf8(path, File.ReadAllBytes(path));
        var result = await UsApplicationHost.StartAsync(root, file).ConfigureAwait(false);
        return FinishExecution(result, host);
    }

    private static CliResult FinishExecution(ExecutionResult result, CliHost host)
    {
        foreach (var line in result.Output.Lines)
        {
            host.Out.WriteLine(line);
        }

        if (!result.Evaluated || result.Diagnostics.Count > 0)
        {
            DiagnosticWriter.Write(host.Error, result.Diagnostics);
            return CliResult.Fail(1, result.Diagnostics, [.. result.Output.Lines]);
        }

        return new CliResult(0, [], [.. result.Output.Lines]);
    }

    private static CliResult BuildCommand(CliHost host)
    {
        var project = ProjectLoader.TryLoadFromDirectory(host.WorkingDirectory);
        if (project is null)
        {
            var missing = ProjectDiagnostic(DiagnosticCodes.MissingEntry, $"'{ProjectLoader.ManifestFileName}' bulunamadı.", host.WorkingDirectory, "Proje kökünde us derle çalıştırın.");
            DiagnosticWriter.Write(host.Error, [missing]);
            return CliResult.Fail(1, [missing]);
        }

        if (project.HasErrors)
        {
            DiagnosticWriter.Write(host.Error, project.Diagnostics);
            return CliResult.Fail(1, project.Diagnostics);
        }

        var outputDirectory = Path.Combine(project.Root, "cikti");
        var compiled = global::Us.Compiler.Compiler.Compile(project, new DotNetEmitBackend(), outputDirectory);
        DiagnosticWriter.Write(host.Error, compiled.Diagnostics);
        if (!compiled.Success)
        {
            return CliResult.Fail(1, compiled.Diagnostics);
        }

        var message = compiled.ArtifactPath is null
            ? "Bağlanmış derleme hazır."
            : $".NET derlemesi hazır: {compiled.ArtifactPath}";
        host.Out.WriteLine(message);
        return CliResult.Ok(message);
    }

    private static async Task<CliResult> TestCommandAsync(CliHost host)
    {
        var project = ProjectLoader.TryLoadFromDirectory(host.WorkingDirectory);
        SourceFile[] files;
        if (project is not null)
        {
            if (project.HasErrors)
            {
                DiagnosticWriter.Write(host.Error, project.Diagnostics);
                return CliResult.Fail(1, project.Diagnostics);
            }

            files = [.. ProjectLoader.LoadSourceFiles(project)];
        }
        else
        {
            var ana = Path.Combine(host.WorkingDirectory, ProjectLoader.DefaultEntry);
            if (!File.Exists(ana))
            {
                var missing = ProjectDiagnostic(DiagnosticCodes.MissingEntry, "Test için proje veya ana.us yok.", host.WorkingDirectory, "us.proje veya ana.us ekleyin.");
                DiagnosticWriter.Write(host.Error, [missing]);
                return CliResult.Fail(1, [missing]);
            }

            files = [SourceFile.FromUtf8(ana, File.ReadAllBytes(ana))];
        }

        var run = await TestRunner.RunAsync(files).ConfigureAwait(false);
        var lines = new List<string>();
        foreach (var test in run.Tests)
        {
            var mark = test.Status == TestStatus.Passed ? "✓" : "✗";
            var line = $"{mark} {test.Name}";
            lines.Add(line);
            host.Out.WriteLine(line);
        }

        var summary = $"{run.Tests.Count} test\n{run.Passed} geçti\n{run.Failed + run.Errors} başarısız";
        foreach (var line in summary.Split('\n'))
        {
            lines.Add(line);
            host.Out.WriteLine(line);
        }

        if (!run.Compiled || run.Failed > 0 || run.Errors > 0 || run.Diagnostics.Count > 0)
        {
            DiagnosticWriter.Write(host.Error, run.Diagnostics);
            return CliResult.Fail(1, run.Diagnostics, [.. lines]);
        }

        return new CliResult(0, [], [.. lines]);
    }

    private static CliResult PublishCommand(CliHost host)
    {
        var project = ProjectLoader.TryLoadFromDirectory(host.WorkingDirectory);
        if (project is null)
        {
            var missing = ProjectDiagnostic(DiagnosticCodes.MissingEntry, $"'{ProjectLoader.ManifestFileName}' bulunamadı.", host.WorkingDirectory, "Proje kökünde us yayınla çalıştırın.");
            DiagnosticWriter.Write(host.Error, [missing]);
            return CliResult.Fail(1, [missing]);
        }

        if (project.HasErrors)
        {
            DiagnosticWriter.Write(host.Error, project.Diagnostics);
            return CliResult.Fail(1, project.Diagnostics);
        }

        var compiled = global::Us.Compiler.Compiler.Compile(project);
        if (!compiled.Success)
        {
            DiagnosticWriter.Write(host.Error, compiled.Diagnostics);
            return CliResult.Fail(1, compiled.Diagnostics);
        }

        var publishRoot = Path.Combine(project.Root, "yayin");
        if (Directory.Exists(publishRoot))
        {
            var exists = ProjectDiagnostic(DiagnosticCodes.ExistingPublish, "'yayin' klasörü zaten var.", publishRoot, "Klasörü kaldırın veya başka bir çıktı kullanın.");
            DiagnosticWriter.Write(host.Error, [exists]);
            return CliResult.Fail(1, [exists]);
        }

        Directory.CreateDirectory(publishRoot);
        File.Copy(Path.Combine(project.Root, ProjectLoader.ManifestFileName), Path.Combine(publishRoot, ProjectLoader.ManifestFileName));
        var summary = new StringBuilder();
        summary.AppendLine($"sürüm {project.Manifest.Version}");
        summary.AppendLine("kaynaklar:");
        foreach (var source in project.SourcePaths)
        {
            summary.AppendLine(Path.GetRelativePath(project.Root, source).Replace('\\', '/'));
        }

        AtomicWrite(Path.Combine(publishRoot, "ozet.txt"), summary.ToString());
        host.Out.WriteLine($"Yayın hazır: {publishRoot}");
        return CliResult.Ok($"Yayın hazır: {publishRoot}");
    }

    private static CliResult PackageCommand(string[] args, CliHost host)
    {
        if (args.Length < 2)
        {
            return Unknown("paket", host);
        }

        return args[0] switch
        {
            "ekle" => PackageAddCommand(args.Skip(1).ToArray(), host),
            "kur" => PackageInstallCommand(args.Skip(1).ToArray(), host),
            _ => Unknown("paket", host),
        };
    }

    private static CliResult PackageAddCommand(string[] args, CliHost host)
    {
        if (args.Length < 1)
        {
            return Unknown("paket", host);
        }

        var packageName = args[0];
        var version = args.Length >= 2 ? args[1] : "1.0.0";
        var project = ProjectLoader.TryLoadFromDirectory(host.WorkingDirectory);
        if (project is null)
        {
            var missing = ProjectDiagnostic(DiagnosticCodes.MissingEntry, $"'{ProjectLoader.ManifestFileName}' bulunamadı.", host.WorkingDirectory, "Proje kökünde us paket ekle çalıştırın.");
            DiagnosticWriter.Write(host.Error, [missing]);
            return CliResult.Fail(1, [missing]);
        }

        var manifestPath = Path.Combine(project.Root, ProjectLoader.ManifestFileName);
        var text = File.ReadAllText(manifestPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (HasExactPackageLine(text, packageName, version))
        {
            host.Out.WriteLine($"Paket zaten kayıtlı: {packageName} {version}");
            return CliResult.Ok($"Paket zaten kayıtlı: {packageName} {version}");
        }

        var conflicting = project.Dependencies.FirstOrDefault(item =>
            string.Equals(item.Name, packageName, StringComparison.Ordinal) &&
            !string.Equals(item.Version, version, StringComparison.Ordinal));
        if (conflicting is not null)
        {
            var conflict = ProjectDiagnostic(
                DiagnosticCodes.PackageConflict,
                $"'{packageName}' için çakışan sürümler: '{conflicting.Version}' ve '{version}'.",
                manifestPath,
                "Aynı paket için tek kesin sürüm kullanın.");
            DiagnosticWriter.Write(host.Error, [conflict]);
            return CliResult.Fail(1, [conflict]);
        }

        if (!text.EndsWith('\n') && text.Length > 0)
        {
            text += "\n";
        }

        text += $"paket \"{packageName}\" sürüm \"{version}\"\n";
        AtomicWrite(manifestPath, text);
        host.Out.WriteLine($"Paket eklendi: {packageName} {version}");
        return CliResult.Ok($"Paket eklendi: {packageName} {version}");
    }

    private static CliResult PackageInstallCommand(string[] args, CliHost host)
    {
        if (args.Length < 1)
        {
            return Unknown("paket", host);
        }

        var archivePath = Path.GetFullPath(Path.Combine(host.WorkingDirectory, args[0]));
        var nameOverride = args.Length >= 2 ? args[1] : null;
        var versionOverride = args.Length >= 3 ? args[2] : null;
        var project = ProjectLoader.TryLoadFromDirectory(host.WorkingDirectory);
        var root = project?.Root ?? host.WorkingDirectory;

        string? expectedHash = null;
        var hashPath = archivePath + ".sha256";
        if (File.Exists(hashPath))
        {
            expectedHash = File.ReadAllText(hashPath).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        }

        var installed = PackageCache.InstallFromArchive(root, archivePath, nameOverride, versionOverride, expectedHash);
        if (!installed.Succeeded)
        {
            DiagnosticWriter.Write(host.Error, installed.Diagnostics);
            return CliResult.Fail(1, installed.Diagnostics.ToImmutableArray());
        }

        host.Out.WriteLine($"Paket kuruldu: {installed.Name} {installed.Version} → {PackageResolver.CacheDirectoryName}/{installed.Name}/{installed.Version}");

        if (project is not null)
        {
            var add = PackageAddCommand([installed.Name!, installed.Version!], host);
            if (add.ExitCode != 0)
            {
                return add;
            }
        }

        return CliResult.Ok($"Paket kuruldu: {installed.Name} {installed.Version}");
    }

    private static bool HasExactPackageLine(string manifestText, string name, string version)
    {
        foreach (var raw in manifestText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!line.StartsWith("paket", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Contains($"\"{name}\"", StringComparison.Ordinal) &&
                line.Contains($"\"{version}\"", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static CliResult DatabaseCommand(string[] args, CliHost host)
    {
        if (args.Length == 0 || args[0] != "senkronize")
        {
            return Unknown("veritabanı", host);
        }

        var approve = args.Length >= 2 && args[1] == "onay";
        var root = host.WorkingDirectory;
        var project = ProjectLoader.TryLoadFromDirectory(root);
        if (project is not null)
        {
            root = project.Root;
        }

        var result = SyncDatabase.Run(root, approve);
        DiagnosticWriter.Write(host.Error, result.Diagnostics);
        if (result.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
        {
            return CliResult.Fail(1, result.Diagnostics);
        }

        host.Out.WriteLine(result.Applied ? "Şema uygulandı." : "Şema değişikliği yok veya uygulanmadı.");
        return CliResult.Ok(result.Applied ? "Şema uygulandı." : "Şema değişikliği yok veya uygulanmadı.");
    }

    private static Diagnostic ProjectDiagnostic(string code, string message, string path, string suggestion) =>
        new(code, DiagnosticSeverity.Error, message, path, new TextSpan(0, 0), 1, 1, suggestion);

    private static void AtomicWrite(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    private static bool IsHelp(string value) =>
        value is "--help" or "-h" or "yardım" or "/?";

    private static bool IsVersion(string value) =>
        value is "--version" or "-v" or "sürüm";
}
