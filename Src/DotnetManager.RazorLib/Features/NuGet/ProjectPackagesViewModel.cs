using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using DotnetManager.RazorLib.Abstractions;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Features.NuGet;

public sealed partial class ProjectPackagesViewModel : ViewModelBase
{
    private readonly IDotnetCli _cli;
    private readonly IDialogService _dialogs;
    private readonly IAppSettingsStore _settings;

    [ObservableProperty]
    private string workingDirectory = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ProjectTargetOption> availableTargets = [];

    [ObservableProperty]
    private string? selectedTargetPath;

    [ObservableProperty]
    private bool includeTransitive;

    [ObservableProperty]
    private bool includePrerelease;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private PackageFilterMode filterMode = PackageFilterMode.All;

    [ObservableProperty]
    private IReadOnlyList<ProjectPackageItem> packages = [];

    [ObservableProperty]
    private IReadOnlyList<ProjectPackageItem> filteredPackages = [];

    [ObservableProperty]
    private bool isCheckingVulnerabilities;

    [ObservableProperty]
    private bool isCheckingOutdated;

    public int TotalCount => Packages.Count;

    public int VulnerableCount => Packages.Count(p => p.HasVulnerabilities);

    public int OutdatedCount => Packages.Count(p => p.IsOutdated);

    public int UpToDateCount => Packages.Count(p => !p.IsOutdated && !p.HasVulnerabilities);

    public ProjectPackagesViewModel(
        IDotnetCli cli,
        IDialogService dialogs,
        IAppSettingsStore settings)
    {
        _cli = cli;
        _dialogs = dialogs;
        _settings = settings;
        workingDirectory = settings.Current.LastWorkingDirectory ?? Environment.CurrentDirectory;
    }

    protected override IDotnetCli Cli => _cli;

    public async Task InitializeAsync()
    {
        ScanTargets();
        await RefreshAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnFilterModeChanged(PackageFilterMode value) => ApplyFilter();

    partial void OnPackagesChanged(IReadOnlyList<ProjectPackageItem> value)
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VulnerableCount));
        OnPropertyChanged(nameof(OutdatedCount));
        OnPropertyChanged(nameof(UpToDateCount));
        ApplyFilter();
    }

    public async Task PickDirectoryAsync()
    {
        var picked = _dialogs.PickFolder("选择包含 .NET 项目或解决方案的目录", WorkingDirectory);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        WorkingDirectory = picked;
        _settings.Current.LastWorkingDirectory = picked;
        await _settings.SaveAsync();

        ScanTargets();
        await RefreshAsync();
    }

    public async Task PickFileAsync()
    {
        var picked = _dialogs.PickFile(
            "选择项目或解决方案文件",
            "项目与解决方案 (*.sln;*.slnx;*.csproj;*.fsproj;*.vbproj)|*.sln;*.slnx;*.csproj;*.fsproj;*.vbproj|所有文件 (*.*)|*.*",
            WorkingDirectory);

        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        var directory = Path.GetDirectoryName(picked);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            WorkingDirectory = directory;
            _settings.Current.LastWorkingDirectory = directory;
            await _settings.SaveAsync();
        }

        ScanTargets();
        SelectedTargetPath = picked;
        await RefreshAsync();
    }

    public void OpenCurrentFolder()
    {
        if (!string.IsNullOrWhiteSpace(WorkingDirectory) && Directory.Exists(WorkingDirectory))
        {
            _dialogs.RevealInShell(WorkingDirectory);
        }
    }

    public async Task SelectTargetAsync(string? targetPath)
    {
        SelectedTargetPath = targetPath;
        await RefreshAsync();
    }

    public async Task ToggleTransitiveAsync()
    {
        IncludeTransitive = !IncludeTransitive;
        await RefreshAsync();
    }

    public async Task TogglePrereleaseAsync()
    {
        IncludePrerelease = !IncludePrerelease;
        await RefreshAsync();
    }

    public void SetFilter(PackageFilterMode mode)
    {
        FilterMode = mode;
    }

    public void ScanTargets()
    {
        var targets = new List<ProjectTargetOption>();

        if (Directory.Exists(WorkingDirectory))
        {
            var solutions = SafeFindFiles(WorkingDirectory, "*.sln", maxDepth: 1)
                .Concat(SafeFindFiles(WorkingDirectory, "*.slnx", maxDepth: 1))
                .ToList();

            foreach (var sln in solutions)
            {
                targets.Add(new ProjectTargetOption(
                    $"解决方案: {Path.GetFileName(sln)}",
                    sln,
                    IsSolution: true));
            }

            var projects = SafeFindFiles(WorkingDirectory, "*.*proj", maxDepth: 3)
                .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                            p.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                            p.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (projects.Count > 1)
            {
                targets.Insert(0, new ProjectTargetOption($"所有项目 (共 {projects.Count} 个)", null, IsSolution: false));
            }

            foreach (var proj in projects)
            {
                var relativePath = Path.GetRelativePath(WorkingDirectory, proj);
                targets.Add(new ProjectTargetOption(
                    $"{Path.GetFileName(proj)} ({relativePath})",
                    proj,
                    IsSolution: false));
            }
        }

        AvailableTargets = targets;

        if (SelectedTargetPath is not null && !targets.Any(t => string.Equals(t.TargetPath, SelectedTargetPath, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedTargetPath = targets.FirstOrDefault()?.TargetPath;
        }
        else if (SelectedTargetPath is null && targets.Count > 0)
        {
            SelectedTargetPath = targets[0].TargetPath;
        }
    }

    public async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            if (!Directory.Exists(WorkingDirectory))
            {
                SetStatus("指定的工作目录不存在。", isError: true);
                Packages = [];
                return;
            }

            var targetList = DetermineTargetsToRun();
            if (targetList.Count == 0)
            {
                Packages = [];
                SetStatus("当前目录下未找到任何 .sln 或 *.*proj 项目文件。", isError: true);
                return;
            }

            SetStatus("正在扫描项目引用的 NuGet 包…");
            var loadedPackages = new List<ProjectPackageItem>();

            foreach (var target in targetList)
            {
                var targetArgs = new List<string> { "list", target, "package", "--format", "json" };
                if (IncludeTransitive)
                {
                    targetArgs.Add("--include-transitive");
                }

                var baseResult = await RunCliAsync(
                    targetArgs,
                    $"dotnet list {Path.GetFileName(target)} package --format json",
                    workingDirectory: WorkingDirectory);

                if (baseResult.Success || !string.IsNullOrWhiteSpace(baseResult.StandardOutput))
                {
                    ParseBasePackagesJson(baseResult.StandardOutput, loadedPackages, target);
                }
            }

            Packages = loadedPackages;

            if (loadedPackages.Count == 0)
            {
                SetStatus("未发现任何 NuGet 包引用。");
                return;
            }

            // Next: Check Outdated
            IsCheckingOutdated = true;
            var outdatedFailed = false;
            try
            {
                SetStatus($"已加载 {loadedPackages.Count} 个包，正在检查新版本与过期情况…");
                foreach (var target in targetList)
                {
                    var outdatedArgs = new List<string> { "list", target, "package", "--outdated", "--format", "json" };
                    if (IncludeTransitive)
                    {
                        outdatedArgs.Add("--include-transitive");
                    }
                    if (IncludePrerelease)
                    {
                        outdatedArgs.Add("--include-prerelease");
                    }

                    var outdatedResult = await RunCliAsync(
                        outdatedArgs,
                        $"dotnet list {Path.GetFileName(target)} package --outdated --format json",
                        workingDirectory: WorkingDirectory);

                    if (!string.IsNullOrWhiteSpace(outdatedResult.StandardOutput))
                    {
                        var merged = TryParseAndMerge(outdatedResult.StandardOutput, loadedPackages, MergeOutdatedPackagesJson);
                        outdatedFailed |= !merged && !outdatedResult.Success;
                    }
                    else
                    {
                        outdatedFailed |= !outdatedResult.Success;
                    }
                }
            }
            catch (Exception ex)
            {
                outdatedFailed = true;
                AppendOutput($"检查新版本时出错: {ex.Message}");
            }
            finally
            {
                IsCheckingOutdated = false;
            }

            // Next: Check Vulnerable
            IsCheckingVulnerabilities = true;
            var vulnerableFailed = false;
            try
            {
                SetStatus($"正在检查已知安全漏洞 (CVE)…");
                foreach (var target in targetList)
                {
                    var vulnerableArgs = new List<string> { "list", target, "package", "--vulnerable", "--format", "json" };
                    if (IncludeTransitive)
                    {
                        vulnerableArgs.Add("--include-transitive");
                    }

                    var vulnResult = await RunCliAsync(
                        vulnerableArgs,
                        $"dotnet list {Path.GetFileName(target)} package --vulnerable --format json",
                        workingDirectory: WorkingDirectory);

                    if (!string.IsNullOrWhiteSpace(vulnResult.StandardOutput))
                    {
                        var merged = TryParseAndMerge(vulnResult.StandardOutput, loadedPackages, MergeVulnerablePackagesJson);
                        vulnerableFailed |= !merged && !vulnResult.Success;
                    }
                    else
                    {
                        vulnerableFailed |= !vulnResult.Success;
                    }
                }
            }
            catch (Exception ex)
            {
                vulnerableFailed = true;
                AppendOutput($"检查安全漏洞时出错: {ex.Message}");
            }
            finally
            {
                IsCheckingVulnerabilities = false;
            }

            Packages = [.. loadedPackages];

            var summary = $"共检测到 {Packages.Count} 个包引用";
            if (VulnerableCount > 0)
            {
                summary += $"，⚠️ 发现 {VulnerableCount} 个包存在已知安全漏洞";
            }
            if (OutdatedCount > 0)
            {
                summary += $"，可更新 {OutdatedCount} 个";
            }
            summary += "。";

            // A failed remote check must never look like "clean" — say so explicitly.
            var failedChecks = new List<string>();
            if (outdatedFailed)
            {
                failedChecks.Add("新版本检查");
            }
            if (vulnerableFailed)
            {
                failedChecks.Add("安全漏洞检查");
            }

            if (failedChecks.Count > 0)
            {
                summary += $" ⚠️ {string.Join("、", failedChecks)}失败（通常是 NuGet 源连接问题），结果不完整，详见运行输出。";
            }

            SetStatus(summary, isError: VulnerableCount > 0 || failedChecks.Count > 0);
        });
    }

    public async Task UpgradePackageAsync(ProjectPackageItem? item)
    {
        if (item is null || !item.CanUpgrade)
        {
            return;
        }

        var upgraded = false;

        await RunAsync(async () =>
        {
            var projectFile = item.ProjectPath;
            if (!File.Exists(projectFile))
            {
                SetStatus($"找不到项目文件：{projectFile}", isError: true);
                return;
            }

            var arguments = new List<string> { "add", projectFile, "package", item.PackageId };
            if (!string.IsNullOrWhiteSpace(item.LatestVersion))
            {
                arguments.Add("--version");
                arguments.Add(item.LatestVersion);
            }
            else if (IncludePrerelease)
            {
                arguments.Add("--prerelease");
            }

            var description = $"dotnet add {Path.GetFileName(projectFile)} package {item.PackageId}";
            if (!string.IsNullOrWhiteSpace(item.LatestVersion))
            {
                description += $" --version {item.LatestVersion}";
            }

            var result = await RunCliAsync(arguments, description, workingDirectory: Path.GetDirectoryName(projectFile));

            if (result.Success)
            {
                upgraded = true;
                SetStatus($"成功升级 {item.PackageId} 为 {item.LatestVersion ?? "最新版"}。");
            }
            else
            {
                SetStatus($"升级 {item.PackageId} 失败，详见运行输出。", isError: true);
            }
        });

        // Reloading has to happen after the busy scope ends: RunAsync deliberately ignores
        // re-entrant calls, so a refresh from inside the operation above would never run.
        if (upgraded)
        {
            var message = StatusMessage;
            await RefreshAsync();
            SetStatus(message);
        }
    }

    public async Task UpgradeAllAsync()
    {
        // Transitive packages are only bulk-upgraded when the user explicitly asked to
        // include them; upgrading one adds a direct <PackageReference> to the project.
        // Group by (project, package) so multi-targeted projects are not upgraded twice.
        var outdatedItems = Packages
            .Where(p => p.CanUpgrade && p.IsOutdated && (IncludeTransitive || !p.IsTransitive))
            .GroupBy(p => (p.ProjectPath, p.PackageId))
            .Select(g => g.OrderByDescending(p => p.MaxSeverityWeight).First())
            .ToList();
        if (outdatedItems.Count == 0)
        {
            SetStatus("没有需要升级的包。");
            return;
        }

        var confirmed = _dialogs.Confirm(
            $"是否确认将检测到的 {outdatedItems.Count} 个过期包升级至最新版本？",
            "一键升级全部");

        if (!confirmed)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var succeeded = 0;
            var failed = 0;

            for (var i = 0; i < outdatedItems.Count; i++)
            {
                var item = outdatedItems[i];
                SetStatus($"正在升级 ({i + 1}/{outdatedItems.Count}): {item.PackageId}…");

                var projectFile = item.ProjectPath;
                if (!File.Exists(projectFile))
                {
                    failed++;
                    continue;
                }

                var arguments = new List<string> { "add", projectFile, "package", item.PackageId };
                if (!string.IsNullOrWhiteSpace(item.LatestVersion))
                {
                    arguments.Add("--version");
                    arguments.Add(item.LatestVersion);
                }
                else if (IncludePrerelease)
                {
                    arguments.Add("--prerelease");
                }

                var result = await RunCliAsync(
                    arguments,
                    $"dotnet add {Path.GetFileName(projectFile)} package {item.PackageId} --version {item.LatestVersion}",
                    workingDirectory: Path.GetDirectoryName(projectFile));

                if (result.Success)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                }
            }

            SetStatus($"全部升级完成：成功 {succeeded} 个，失败 {failed} 个。", isError: failed > 0);
        });

        // Same as the single-package upgrade: the reload must not run inside the busy scope.
        var summary = StatusMessage;
        var hadErrors = StatusIsError;
        await RefreshAsync();
        SetStatus(summary, hadErrors);
    }

    private List<string> DetermineTargetsToRun()
    {
        if (!string.IsNullOrWhiteSpace(SelectedTargetPath) &&
            (File.Exists(SelectedTargetPath) || Directory.Exists(SelectedTargetPath)))
        {
            return [SelectedTargetPath];
        }

        // All projects
        var projects = AvailableTargets
            .Where(t => !string.IsNullOrWhiteSpace(t.TargetPath) && !t.IsSolution)
            .Select(t => t.TargetPath!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (projects.Count > 0)
        {
            return projects;
        }

        var sln = AvailableTargets.FirstOrDefault(t => t.IsSolution)?.TargetPath;
        if (sln is not null)
        {
            return [sln];
        }

        return [WorkingDirectory];
    }

    private void ApplyFilter()
    {
        var query = SearchText?.Trim();
        var filtered = Packages.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(p =>
                p.PackageId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.ProjectName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Framework.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Vulnerabilities.Any(v =>
                    (v.Cve?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (v.Severity?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)));
        }

        filtered = FilterMode switch
        {
            PackageFilterMode.VulnerableOnly => filtered.Where(p => p.HasVulnerabilities),
            PackageFilterMode.OutdatedOnly => filtered.Where(p => p.IsOutdated),
            PackageFilterMode.UpToDateOnly => filtered.Where(p => !p.IsOutdated && !p.HasVulnerabilities),
            PackageFilterMode.TopLevelOnly => filtered.Where(p => !p.IsTransitive),
            PackageFilterMode.TransitiveOnly => filtered.Where(p => p.IsTransitive),
            _ => filtered,
        };

        FilteredPackages = filtered
            .OrderByDescending(p => p.MaxSeverityWeight)
            .ThenByDescending(p => p.IsOutdated)
            .ThenBy(p => p.PackageId)
            .ToList();
    }

    internal static void ParseBasePackagesJson(string stdout, List<ProjectPackageItem> resultList, string fallbackTarget)
    {
        var json = ExtractJson(stdout);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("projects", out var projectsElem) ||
                projectsElem.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var proj in projectsElem.EnumerateArray())
            {
                var projPath = GetStringProp(proj, "path") ?? fallbackTarget;
                if (!proj.TryGetProperty("frameworks", out var frameworksElem) ||
                    frameworksElem.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var fw in frameworksElem.EnumerateArray())
                {
                    var fwName = GetStringProp(fw, "framework") ?? string.Empty;

                    if (fw.TryGetProperty("topLevelPackages", out var topPackages) &&
                        topPackages.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var pkg in topPackages.EnumerateArray())
                        {
                            var id = GetStringProp(pkg, "id");
                            if (string.IsNullOrWhiteSpace(id))
                            {
                                continue;
                            }

                            var autoRef = string.Equals(GetStringProp(pkg, "autoReferenced"), "true", StringComparison.OrdinalIgnoreCase);

                            var item = new ProjectPackageItem
                            {
                                ProjectPath = projPath,
                                Framework = fwName,
                                PackageId = id,
                                RequestedVersion = GetStringProp(pkg, "requestedVersion"),
                                ResolvedVersion = GetStringProp(pkg, "resolvedVersion"),
                                IsTransitive = false,
                                IsAutoReferenced = autoRef,
                            };

                            var existing = resultList.FirstOrDefault(p =>
                                string.Equals(p.ProjectPath, projPath, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(p.Framework, fwName, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(p.PackageId, id, StringComparison.OrdinalIgnoreCase));

                            if (existing is null)
                            {
                                resultList.Add(item);
                            }
                        }
                    }

                    if (fw.TryGetProperty("transitivePackages", out var transPackages) &&
                        transPackages.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var pkg in transPackages.EnumerateArray())
                        {
                            var id = GetStringProp(pkg, "id");
                            if (string.IsNullOrWhiteSpace(id))
                            {
                                continue;
                            }

                            var item = new ProjectPackageItem
                            {
                                ProjectPath = projPath,
                                Framework = fwName,
                                PackageId = id,
                                ResolvedVersion = GetStringProp(pkg, "resolvedVersion"),
                                IsTransitive = true,
                                IsAutoReferenced = false,
                            };

                            var existing = resultList.FirstOrDefault(p =>
                                string.Equals(p.ProjectPath, projPath, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(p.Framework, fwName, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(p.PackageId, id, StringComparison.OrdinalIgnoreCase));

                            if (existing is null)
                            {
                                resultList.Add(item);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed json
        }
    }

    internal static void MergeOutdatedPackagesJson(string stdout, List<ProjectPackageItem> packages)
    {
        var json = ExtractJson(stdout);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("projects", out var projectsElem) ||
                projectsElem.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var proj in projectsElem.EnumerateArray())
            {
                var projPath = GetStringProp(proj, "path") ?? string.Empty;
                if (!proj.TryGetProperty("frameworks", out var frameworksElem) ||
                    frameworksElem.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var fw in frameworksElem.EnumerateArray())
                {
                    var fwName = GetStringProp(fw, "framework") ?? string.Empty;

                    void ProcessOutdatedList(JsonElement packageList)
                    {
                        if (packageList.ValueKind != JsonValueKind.Array)
                        {
                            return;
                        }

                        foreach (var pkg in packageList.EnumerateArray())
                        {
                            var id = GetStringProp(pkg, "id");
                            var latest = GetStringProp(pkg, "latestVersion");
                            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(latest))
                            {
                                continue;
                            }

                            var matches = packages.Where(p =>
                                string.Equals(p.PackageId, id, StringComparison.OrdinalIgnoreCase) &&
                                (string.IsNullOrEmpty(fwName) || string.Equals(p.Framework, fwName, StringComparison.OrdinalIgnoreCase)) &&
                                (string.IsNullOrEmpty(projPath) ||
                                 string.Equals(p.ProjectPath, projPath, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(Path.GetFileName(p.ProjectPath), Path.GetFileName(projPath), StringComparison.OrdinalIgnoreCase)));

                            foreach (var match in matches)
                            {
                                match.LatestVersion = latest;
                            }
                        }
                    }

                    if (fw.TryGetProperty("topLevelPackages", out var topList))
                    {
                        ProcessOutdatedList(topList);
                    }
                    if (fw.TryGetProperty("transitivePackages", out var transList))
                    {
                        ProcessOutdatedList(transList);
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed json
        }
    }

    internal static void MergeVulnerablePackagesJson(string stdout, List<ProjectPackageItem> packages)
    {
        var json = ExtractJson(stdout);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("projects", out var projectsElem) ||
                projectsElem.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var proj in projectsElem.EnumerateArray())
            {
                var projPath = GetStringProp(proj, "path") ?? string.Empty;
                if (!proj.TryGetProperty("frameworks", out var frameworksElem) ||
                    frameworksElem.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var fw in frameworksElem.EnumerateArray())
                {
                    var fwName = GetStringProp(fw, "framework") ?? string.Empty;

                    void ProcessVulnerableList(JsonElement packageList)
                    {
                        if (packageList.ValueKind != JsonValueKind.Array)
                        {
                            return;
                        }

                        foreach (var pkg in packageList.EnumerateArray())
                        {
                            var id = GetStringProp(pkg, "id");
                            if (string.IsNullOrWhiteSpace(id))
                            {
                                continue;
                            }

                            var matches = packages.Where(p =>
                                string.Equals(p.PackageId, id, StringComparison.OrdinalIgnoreCase) &&
                                (string.IsNullOrEmpty(fwName) || string.Equals(p.Framework, fwName, StringComparison.OrdinalIgnoreCase)) &&
                                (string.IsNullOrEmpty(projPath) ||
                                 string.Equals(p.ProjectPath, projPath, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(Path.GetFileName(p.ProjectPath), Path.GetFileName(projPath), StringComparison.OrdinalIgnoreCase))).ToList();

                            if (matches.Count == 0)
                            {
                                continue;
                            }

                            var vulns = new List<PackageVulnerability>();
                            if (pkg.TryGetProperty("vulnerabilities", out var vulnArray) &&
                                vulnArray.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var vElem in vulnArray.EnumerateArray())
                                {
                                    var sev = GetStringProp(vElem, "severity") ?? "High";
                                    var adv = GetStringProp(vElem, "advisoryurl", "advisoryUrl");
                                    var cve = GetStringProp(vElem, "cve", "cveId", "id");
                                    vulns.Add(new PackageVulnerability(sev, adv, cve));
                                }
                            }

                            var topSev = GetStringProp(pkg, "severity");
                            if (vulns.Count == 0 && !string.IsNullOrWhiteSpace(topSev))
                            {
                                vulns.Add(new PackageVulnerability(topSev, null, null));
                            }

                            foreach (var match in matches)
                            {
                                match.Vulnerabilities = [.. vulns];
                            }
                        }
                    }

                    if (fw.TryGetProperty("topLevelPackages", out var topList))
                    {
                        ProcessVulnerableList(topList);
                    }
                    if (fw.TryGetProperty("transitivePackages", out var transList))
                    {
                        ProcessVulnerableList(transList);
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed json
        }
    }

    internal static bool TryParseAndMerge(
        string stdout,
        List<ProjectPackageItem> packages,
        Action<string, List<ProjectPackageItem>> merge)
    {
        var json = ExtractJson(stdout);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("projects", out var projectsElem) ||
                projectsElem.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            merge(stdout, packages);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string ExtractJson(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return string.Empty;
        }

        var start = stdout.IndexOf('{');
        var end = stdout.LastIndexOf('}');
        return (start >= 0 && end > start)
            ? stdout.Substring(start, end - start + 1)
            : string.Empty;
    }

    private static string? GetStringProp(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => null,
                    };
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> SafeFindFiles(string directory, string searchPattern, int maxDepth)
    {
        var results = new List<string>();
        SafeEnumerate(directory, searchPattern, 0, maxDepth, results);
        return results;
    }

    private static void SafeEnumerate(string currentDir, string pattern, int currentDepth, int maxDepth, List<string> results)
    {
        if (currentDepth > maxDepth || !Directory.Exists(currentDir))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(currentDir, pattern))
            {
                results.Add(file);
            }

            foreach (var subDir in Directory.EnumerateDirectories(currentDir))
            {
                var name = Path.GetFileName(subDir);
                if (name.StartsWith('.') ||
                    string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SafeEnumerate(subDir, pattern, currentDepth + 1, maxDepth, results);
            }
        }
        catch
        {
            // Ignore unreadable directories
        }
    }
}
