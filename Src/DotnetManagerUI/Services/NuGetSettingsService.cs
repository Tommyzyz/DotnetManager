using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DotnetManager.RazorLib.Abstractions;
using NuGet.Configuration;

namespace DotnetManagerUI.Services;

/// <summary>
/// Reads the effective NuGet configuration through the official NuGet.Configuration stack and
/// writes targeted changes back with a backup plus a post-write validation.
/// </summary>
public sealed class NuGetSettingsService : INuGetSettingsService
{
    private const string PackageSources = "packageSources";
    private const string DisabledPackageSources = "disabledPackageSources";
    private const string FallbackPackageFolders = "fallbackPackageFolders";
    private const string PackageSourceCredentials = "packageSourceCredentials";
    private const string ConfigSection = "config";

    private static readonly string[] ConfigFileNames = ["NuGet.Config", "nuget.config", "NuGet.config"];

    private readonly IEnvironmentService _environment;

    public NuGetSettingsService(IEnvironmentService environment, IElevationService elevation)
    {
        _environment = environment;
        Elevation = elevation;
    }

    private IElevationService Elevation { get; }

    public string UserConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NuGet",
        "NuGet.Config");

    public string MachineConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "NuGet",
        "NuGet.Config");

    private static string VsConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "NuGet",
        "Config");

    public string? FindConfigFile(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        var current = new DirectoryInfo(Path.GetFullPath(directory));
        while (current is not null)
        {
            foreach (var name in ConfigFileNames)
            {
                var candidate = Path.Combine(current.FullName, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            var nested = ConfigFileNames
                .Select(name => Path.Combine(current.FullName, ".nuget", name))
                .FirstOrDefault(File.Exists);
            if (nested is not null)
            {
                return nested;
            }

            current = current.Parent;
        }

        return null;
    }

    public NuGetSettingsSnapshot Load(string? rootDirectory)
    {
        try
        {
            var files = GetConfigFiles(rootDirectory);
            var existing = files.Where(file => file.Exists).ToList();

            var sources = ReadSources(existing);
            var disabled = ReadSectionEntries(existing, DisabledPackageSources);
            var configValues = ReadSectionEntries(existing, ConfigSection);

            return new NuGetSettingsSnapshot
            {
                RootDirectory = rootDirectory,
                GlobalPackagesFolder = ResolveGlobalPackagesFolder(configValues, rootDirectory),
                HttpCacheFolder = ResolveCacheFolder("NUGET_HTTP_CACHE_PATH", Path.Combine(DefaultLocalCacheRoot, "v3-cache")),
                PluginsCacheFolder = ResolveCacheFolder("NUGET_PLUGINS_CACHE_PATH", Path.Combine(DefaultLocalCacheRoot, "plugins-cache")),
                TempFolder = ResolveCacheFolder("NUGET_SCRATCH", Path.Combine(Path.GetTempPath().TrimEnd('\\'), "NuGetScratch")),
                FallbackFolders = ReadFallbackFolders(existing),
                Sources = sources,
                ConfigFiles = files,
                DisabledSources = disabled,
                ConfigValues = configValues,
            };
        }
        catch (Exception ex)
        {
            return new NuGetSettingsSnapshot { RootDirectory = rootDirectory, LoadError = ex.Message };
        }
    }

    public IReadOnlyList<NuGetConfigFileInfo> GetConfigFiles(string? rootDirectory)
    {
        var files = new List<NuGetConfigFileInfo>
        {
            Describe(MachineConfigPath, "Machine"),
        };

        if (Directory.Exists(VsConfigDirectory))
        {
            files.AddRange(Directory
                .EnumerateFiles(VsConfigDirectory, "*.config")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => Describe(path, "VisualStudio")));
        }

        files.Add(Describe(UserConfigPath, "User"));

        foreach (var path in EnumerateDirectoryChain(rootDirectory))
        {
            files.Add(Describe(path, "Project"));
        }

        return files;
    }

    public string ReadRawFile(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    public bool RequiresElevation(string path)
    {
        if (EnvironmentService.CurrentUserIsAdministrator())
        {
            return false;
        }

        var normalized = Path.GetFullPath(path);
        return IsUnder(normalized, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
            || IsUnder(normalized, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
            || IsUnder(normalized, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
    }

    public Task<OperationResult> SetConfigValueAsync(string configFile, string key, string? value, CancellationToken cancellationToken = default) =>
        UpdateAsync(configFile, document =>
        {
            var section = GetOrCreateSection(document, ConfigSection);
            var existing = FindAdd(section, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                existing?.Remove();
                return;
            }

            if (existing is null)
            {
                section.Add(new XElement("add", new XAttribute("key", key), new XAttribute("value", value)));
            }
            else
            {
                existing.SetAttributeValue("value", value);
            }
        }, cancellationToken);

    public Task<OperationResult> SetFallbackFoldersAsync(string configFile, IReadOnlyList<string> folders, CancellationToken cancellationToken = default) =>
        UpdateAsync(configFile, document =>
        {
            var section = GetOrCreateSection(document, FallbackPackageFolders);
            section.RemoveNodes();
            foreach (var folder in folders.Where(folder => !string.IsNullOrWhiteSpace(folder)))
            {
                section.Add(new XElement("add", new XAttribute("key", folder), new XAttribute("value", folder)));
            }
        }, cancellationToken);

    public Task<OperationResult> AddOrUpdateSourceAsync(string configFile, string name, string url, CancellationToken cancellationToken = default) =>
        UpdateAsync(configFile, document =>
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("Both the source name and URL are required.");
            }

            var section = GetOrCreateSection(document, PackageSources);
            var existing = FindAdd(section, name);
            if (existing is null)
            {
                section.Add(new XElement("add", new XAttribute("key", name), new XAttribute("value", url)));
            }
            else
            {
                existing.SetAttributeValue("value", url);
            }
        }, cancellationToken);

    public Task<OperationResult> RemoveSourceAsync(string configFile, string name, CancellationToken cancellationToken = default) =>
        UpdateAsync(configFile, document =>
        {
            FindSection(document, PackageSources)?.Elements("add")
                .FirstOrDefault(element => IsKey(element, name))?.Remove();

            FindSection(document, DisabledPackageSources)?.Elements("add")
                .FirstOrDefault(element => IsKey(element, name))?.Remove();

            FindSection(document, PackageSourceCredentials)?.Elements()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Remove();
        }, cancellationToken);

    public Task<OperationResult> SetSourceEnabledAsync(string configFile, string name, bool enabled, CancellationToken cancellationToken = default) =>
        UpdateAsync(configFile, document =>
        {
            var section = FindSection(document, DisabledPackageSources);
            var existing = section is null ? null : FindAdd(section, name);

            if (enabled)
            {
                existing?.Remove();
                return;
            }

            section ??= AddSection(document, DisabledPackageSources);
            if (existing is null)
            {
                section.Add(new XElement("add", new XAttribute("key", name), new XAttribute("value", "true")));
            }
            else
            {
                existing.SetAttributeValue("value", "true");
            }
        }, cancellationToken);

    public async Task<OperationResult> WriteRawFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return OperationResult.Fail("No config file was selected.");
        }

        try
        {
            _ = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            return OperationResult.Fail("The content is not valid XML and was not saved.", ex.Message);
        }

        var original = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        return await WriteAsync(path, original, content, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult> UpdateAsync(string configFile, Action<XDocument> mutate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configFile))
        {
            return OperationResult.Fail("No config file was selected.");
        }

        var original = File.Exists(configFile) ? File.ReadAllText(configFile) : EmptyConfig;
        XDocument document;
        try
        {
            document = XDocument.Parse(original, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            return OperationResult.Fail($"'{configFile}' is not valid XML and was left untouched.", ex.Message);
        }

        if (document.Root is null)
        {
            return OperationResult.Fail($"'{configFile}' has no root element and was left untouched.");
        }

        try
        {
            mutate(document);
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail(ex.Message);
        }

        var updated = Serialize(document);
        try
        {
            _ = XDocument.Parse(updated);
        }
        catch (XmlException ex)
        {
            return OperationResult.Fail("The updated configuration is not valid XML; nothing was written.", ex.Message);
        }

        return await WriteAsync(configFile, original, updated, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult> WriteAsync(string path, string original, string updated, CancellationToken cancellationToken)
    {
        if (RequiresElevation(path))
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"dotnetmanager-config-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(tempFile, updated, cancellationToken).ConfigureAwait(false);
                var result = await Elevation.RunElevatedAsync(
                    HeadlessOperations.WriteFile,
                    new Dictionary<string, string> { ["source"] = tempFile, ["target"] = path },
                    cancellationToken).ConfigureAwait(false);

                return result.Success
                    ? OperationResult.Ok($"Wrote {path} (a .bak backup was created).")
                    : result;
            }
            finally
            {
                TryDelete(tempFile);
            }
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Validate what is currently on disk before replacing it, so a broken edit cannot destroy a good file.
            if (!string.IsNullOrEmpty(original))
            {
                try
                {
                    _ = XDocument.Parse(original);
                }
                catch (XmlException)
                {
                    return OperationResult.Fail($"'{path}' is not valid XML and was left untouched.");
                }

                File.Copy(path, path + ".bak", overwrite: true);
            }

            await File.WriteAllTextAsync(path, updated, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok($"Wrote {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"'{path}' could not be written: {ex.Message}");
        }
    }

    private NuGetValue ResolveGlobalPackagesFolder(
        IReadOnlyList<NuGetConfigEntry> configValues,
        string? rootDirectory)
    {
        var fromEnvironment = ResolveEnvironmentValue("NUGET_PACKAGES");
        if (fromEnvironment is not null)
        {
            return fromEnvironment;
        }

        var fromConfig = configValues.FirstOrDefault(entry =>
            string.Equals(entry.Key, "globalPackagesFolder", StringComparison.OrdinalIgnoreCase));
        if (fromConfig is not null)
        {
            return new NuGetValue(Expand(fromConfig.Value, rootDirectory), NuGetValueSource.ConfigFile, fromConfig.ConfigFile);
        }

        // Fall back to the official resolver so inheritance and case differences stay correct.
        var fromApi = TryResolveGlobalPackagesFolderFromApi(rootDirectory);
        if (!string.IsNullOrWhiteSpace(fromApi))
        {
            return new NuGetValue(fromApi, NuGetValueSource.Default, null);
        }

        var defaultFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");

        return new NuGetValue(defaultFolder, NuGetValueSource.Default, null);
    }

    private NuGetValue ResolveCacheFolder(string variableName, string defaultPath)
    {
        var fromEnvironment = ResolveEnvironmentValue(variableName);
        return fromEnvironment ?? new NuGetValue(defaultPath, NuGetValueSource.Default, null);
    }

    private NuGetValue? ResolveEnvironmentValue(string variableName)
    {
        var machine = _environment.Get(variableName, EnvScope.Machine);
        if (!string.IsNullOrWhiteSpace(machine))
        {
            return new NuGetValue(machine, NuGetValueSource.Environment, $"{variableName} ({EnvScope.Machine})");
        }

        var user = _environment.Get(variableName, EnvScope.User);
        if (!string.IsNullOrWhiteSpace(user))
        {
            return new NuGetValue(user, NuGetValueSource.Environment, $"{variableName} ({EnvScope.User})");
        }

        return null;
    }

    private static string? TryResolveGlobalPackagesFolderFromApi(string? rootDirectory)
    {
        try
        {
            var settings = Settings.LoadDefaultSettings(rootDirectory);
            var folder = SettingsUtility.GetGlobalPackagesFolder(settings);
            return string.IsNullOrWhiteSpace(folder) ? null : folder;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IReadOnlyList<PackageSourceInfo> ReadSources(IReadOnlyList<NuGetConfigFileInfo> files)
    {
        var sources = new Dictionary<string, PackageSourceBuilder>(StringComparer.OrdinalIgnoreCase);
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var credentials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var document = TryLoad(file.Path);
            if (document?.Root is null)
            {
                continue;
            }

            if (FindSection(document, DisabledPackageSources) is { } disabledSection)
            {
                foreach (var element in disabledSection.Elements("add"))
                {
                    var key = element.Attribute("key")?.Value;
                    if (!string.IsNullOrWhiteSpace(key) && IsTruthy(element.Attribute("value")?.Value))
                    {
                        disabled.Add(key);
                    }
                }
            }

            if (FindSection(document, PackageSourceCredentials) is { } credentialsSection)
            {
                foreach (var element in credentialsSection.Elements())
                {
                    credentials.Add(element.Name.LocalName);
                }
            }

            if (FindSection(document, PackageSources) is not { } sourceSection)
            {
                continue;
            }

            foreach (var node in sourceSection.Nodes())
            {
                if (node is XElement clear && string.Equals(clear.Name.LocalName, "clear", StringComparison.OrdinalIgnoreCase))
                {
                    sources.Clear();
                    continue;
                }

                if (node is not XElement add || !string.Equals(add.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = add.Attribute("key")?.Value;
                var url = add.Attribute("value")?.Value;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                sources[name] = new PackageSourceBuilder
                {
                    Name = name,
                    Url = url,
                    ConfigFile = file.Path,
                    IsMachineWide = file.IsMachineWide,
                    ProtocolVersion = add.Attribute("protocolVersion")?.Value,
                };
            }
        }

        return sources.Values
            .OrderBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .Select(source => new PackageSourceInfo(
                source.Name,
                source.Url,
                !disabled.Contains(source.Name),
                source.ConfigFile,
                source.IsMachineWide,
                IsOfficial(source.Url),
                credentials.Contains(source.Name),
                source.ProtocolVersion))
            .ToList();
    }

    private static IReadOnlyList<NuGetPathEntry> ReadFallbackFolders(IReadOnlyList<NuGetConfigFileInfo> files)
    {
        var folders = new Dictionary<string, NuGetPathEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var document = TryLoad(file.Path);
            if (document is null)
            {
                continue;
            }

            if (FindSection(document, FallbackPackageFolders) is not { } section)
            {
                continue;
            }

            foreach (var node in section.Nodes())
            {
                if (node is XElement clear && string.Equals(clear.Name.LocalName, "clear", StringComparison.OrdinalIgnoreCase))
                {
                    folders.Clear();
                    continue;
                }

                if (node is not XElement add)
                {
                    continue;
                }

                var value = add.Attribute("value")?.Value ?? add.Attribute("key")?.Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    folders[value] = new NuGetPathEntry(value, file.Path);
                }
            }
        }

        return folders.Values.ToList();
    }

    private static IReadOnlyList<NuGetConfigEntry> ReadSectionEntries(IReadOnlyList<NuGetConfigFileInfo> files, string sectionName)
    {
        var entries = new Dictionary<string, NuGetConfigEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var document = TryLoad(file.Path);
            if (document is null)
            {
                continue;
            }

            if (FindSection(document, sectionName) is not { } section)
            {
                continue;
            }

            foreach (var node in section.Nodes())
            {
                if (node is XElement clear && string.Equals(clear.Name.LocalName, "clear", StringComparison.OrdinalIgnoreCase))
                {
                    entries.Clear();
                    continue;
                }

                if (node is not XElement add)
                {
                    continue;
                }

                var key = add.Attribute("key")?.Value;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                entries[key] = new NuGetConfigEntry(key, add.Attribute("value")?.Value ?? string.Empty, file.Path);
            }
        }

        return entries.Values.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> EnumerateDirectoryChain(string? rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            yield break;
        }

        var chain = new List<string>();
        var current = new DirectoryInfo(Path.GetFullPath(rootDirectory));
        while (current is not null)
        {
            chain.Add(current.FullName);
            current = current.Parent;
        }

        // Lowest precedence first so that nearer directories win.
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            foreach (var name in ConfigFileNames)
            {
                var candidate = Path.Combine(chain[index], name);
                if (File.Exists(candidate))
                {
                    yield return candidate;
                }
            }

            foreach (var name in ConfigFileNames)
            {
                var candidate = Path.Combine(chain[index], ".nuget", name);
                if (File.Exists(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private NuGetConfigFileInfo Describe(string path, string scope)
    {
        var machineWide = scope is "Machine" or "VisualStudio";
        return new NuGetConfigFileInfo(path, scope, File.Exists(path), machineWide, RequiresElevation(path));
    }

    private static XDocument? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return XDocument.Parse(File.ReadAllText(path), LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static XElement? FindSection(XDocument document, string name) =>
        document.Root?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));

    private static XElement GetOrCreateSection(XDocument document, string name) =>
        FindSection(document, name) ?? AddSection(document, name);

    private static XElement AddSection(XDocument document, string name)
    {
        var section = new XElement(name);
        document.Root!.Add(section);
        return section;
    }

    private static XElement? FindAdd(XElement section, string key) =>
        section.Elements("add").FirstOrDefault(element => IsKey(element, key));

    private static bool IsKey(XElement element, string key) =>
        string.Equals(element.Attribute("key")?.Value, key, StringComparison.OrdinalIgnoreCase);

    private static bool IsTruthy(string? value) =>
        value is not null
        && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static bool IsOfficial(string url) =>
        url.Contains("api.nuget.org", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string root) =>
        !string.IsNullOrEmpty(root)
        && path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static string Expand(string value, string? rootDirectory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value);
        if (!Path.IsPathRooted(expanded) && !string.IsNullOrWhiteSpace(rootDirectory) && Directory.Exists(rootDirectory))
        {
            return Path.GetFullPath(Path.Combine(rootDirectory, expanded));
        }

        return expanded;
    }

    private static string Serialize(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Best effort cleanup only.
        }
    }

    private static string DefaultLocalCacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NuGet");

    private const string EmptyConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
        </configuration>
        """;

    private sealed class PackageSourceBuilder
    {
        public required string Name { get; init; }

        public required string Url { get; init; }

        public required string ConfigFile { get; init; }

        public bool IsMachineWide { get; init; }

        public string? ProtocolVersion { get; init; }
    }
}
