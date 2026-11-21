using System.Globalization;
using EsLuncher.Core.Auth;
using EsLuncher.Core.Content;
using EsLuncher.Core.Diagnostics;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;
using EsLuncher.Core.Java;
using EsLuncher.Core.Launching;
using EsLuncher.Core.Loaders;
using EsLuncher.Core.Metadata;
using EsLuncher.Core.Updates;

namespace EsLuncher.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args) => await RunAsync(args).ConfigureAwait(false);

    private static async Task<int> RunAsync(string[] args)
    {
        // update 子命令要 Velopack 的定位器（安装根与包目录）；开发态克隆一份空参数启动即可。
        // 其它子命令不需要，避免无谓开销。
        if (args.Length > 0 && args[0].Equals("update", StringComparison.OrdinalIgnoreCase))
        {
            Velopack.VelopackApp.Build().SetArgs(Array.Empty<string>()).Run();
        }

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            if (command == "instance")
            {
                return await InstanceCommand(args).ConfigureAwait(false);
            }

            if (command == "loader")
            {
                return await LoaderCommandAsync(args).ConfigureAwait(false);
            }

            if (command == "content")
            {
                return await ContentCommandAsync(args).ConfigureAwait(false);
            }

            if (command == "world")
            {
                return WorldCommand(args);
            }

            if (command == "modrinth")
            {
                return await ModrinthCommandAsync(args).ConfigureAwait(false);
            }

            if (command == "pack")
            {
                return await PackCommandAsync(args).ConfigureAwait(false);
            }

            if (command == "diagnose")
            {
                return await DiagnoseCommandAsync(args).ConfigureAwait(false);
            }

            if (command == "deps")
            {
                return await DepsCommandAsync(args).ConfigureAwait(false);
            }

            if (command == "update")
            {
                return await UpdateCommandAsync(args[1..]).ConfigureAwait(false);
            }

            if (command == "java")
            {
                if (args.Length > 1 && args[1].Equals("install", StringComparison.OrdinalIgnoreCase))
                {
                    return await JavaInstallAsync(CommandOptions.Parse(args[2..])).ConfigureAwait(false);
                }

                return ListJava(CommandOptions.Parse(args[1..]));
            }

            var options = CommandOptions.Parse(args[1..]);
            return command switch
            {
                "fetch" => await FetchAsync(options).ConfigureAwait(false),
                "launch" => await LaunchAsync(options).ConfigureAwait(false),
                "versions" => await VersionsAsync(options).ConfigureAwait(false),
                "login" => await LoginAsync(options).ConfigureAwait(false),
                "accounts" => ListAccounts(options),
                "logout" => await LogoutAsync(options).ConfigureAwait(false),
                _ => Fail($"未知命令：{command}"),
            };
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex) when (ex is MetadataFormatException or InvalidOperationException or IOException or HttpRequestException or TimeoutException)
        {
            return Fail(ex.Message);
        }
    }

    private static int ListJava(CommandOptions options)
    {
        var layout = new DataLayout(options.DataRoot);
        var runtimes = JavaDiscovery.Discover([layout.JavaDir]);
        Console.WriteLine($"发现 {runtimes.Count} 个可用 Java（PATH 上的 java 一律忽略）：");
        foreach (var runtime in runtimes)
        {
            var managed = runtime.JavaExePath.StartsWith(layout.JavaDir, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"  Java {runtime.MajorVersion,2}  {runtime.JavaExePath}{(managed ? "  [Mojang 托管]" : string.Empty)}");
        }

        return runtimes.Count > 0 ? 0 : 1;
    }

    private static async Task<int> JavaInstallAsync(CommandOptions options)
    {
        var layout = new DataLayout(options.DataRoot);
        using var client = new HttpRemoteFileClient();
        var downloader = new FileDownloader(client);

        var component = options.Component;
        if (string.IsNullOrWhiteSpace(component))
        {
            var versionId = options.RequireVersion();
            var catalog = new VersionCatalog(layout, downloader);
            var manifest = await catalog.GetManifestAsync(options.Refresh, CancellationToken.None).ConfigureAwait(false);
            var version = await catalog.GetVersionAsync(versionId, manifest, options.Refresh, CancellationToken.None)
                .ConfigureAwait(false);
            component = version.JavaVersion?.Component ?? "jre-legacy";
            Console.WriteLine($"{version.Id} 需要运行时组件 {component}（Java {version.JavaVersion?.MajorVersion}）");
        }

        var runtime = await InstallComponentAsync(layout, downloader, component, options, new ConsoleProgress(), CancellationToken.None)
            .ConfigureAwait(false);
        Console.WriteLine($"完成：Java {runtime.MajorVersion} @ {runtime.JavaExePath}");
        return 0;
    }

    private static async Task<int> ContentCommandAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Fail("用法：esluncher content <list|add|remove|enable|disable|outdated|update> --instance <名字> [--kind ...] [--world <世界>] [文件]");
        }

        var action = args[1].ToLowerInvariant();
        var (positionals, optionTokens) = SplitOptions(args[2..]);
        var options = CommandOptions.Parse(optionTokens);
        if (string.IsNullOrWhiteSpace(options.InstanceId))
        {
            return Fail("content 命令需要 --instance <名字>");
        }

        var kind = ContentKind.Mods;
        if (!string.IsNullOrWhiteSpace(options.Kind)
            && !InstanceContentService.TryParseKind(options.Kind!, out kind))
        {
            return Fail($"未知内容类型：{options.Kind}（mods|resourcepacks|shaderpacks|worlds|datapacks）");
        }

        var layout = new DataLayout(options.DataRoot);
        var service = new InstanceContentService(layout, new InstanceStore(layout));
        var world = options.World;
        var label = InstanceContentService.FolderName(kind) + (kind == ContentKind.Datapacks ? $"（世界 {world}）" : string.Empty);

        switch (action)
        {
            case "list":
            {
                var files = service.List(options.InstanceId!, kind, world);
                if (files.Count == 0)
                {
                    Console.WriteLine($"{options.InstanceId} 的 {label}：空");
                    return 0;
                }

                Console.WriteLine($"{options.InstanceId} 的 {label}（{files.Count} 个）：");
                foreach (var file in files)
                {
                    var state = file.Enabled ? "启用" : "禁用";
                    var size = file.IsDirectory ? "<目录>" : $"{file.Size} B";
                    Console.WriteLine($"  [{state}] {file.Name,-40} {size,10}  {file.Modified:yyyy-MM-dd HH:mm}");
                }

                return 0;
            }

            case "add":
            {
                if (positionals.Count < 1)
                {
                    return Fail("content add 需要文件或目录路径");
                }

                var added = service.Add(options.InstanceId!, kind, positionals[0], world);
                Console.WriteLine($"已添加 {added.Name} → {service.DirectoryFor(options.InstanceId!, kind, world)}");
                return 0;
            }

            case "remove":
            {
                if (positionals.Count < 1)
                {
                    return Fail("content remove 需要文件名");
                }

                var removed = service.Remove(options.InstanceId!, kind, positionals[0], world);
                Console.WriteLine(removed
                    ? $"已移除 {positionals[0]}（移入数据根 trash/content，可手动找回）"
                    : $"没有这个内容：{positionals[0]}");
                return removed ? 0 : 1;
            }

            case "enable":
            case "disable":
            {
                if (positionals.Count < 1)
                {
                    return Fail($"content {action} 需要文件名");
                }

                try
                {
                    var enabled = action == "enable";
                    var changed = service.SetEnabled(options.InstanceId!, kind, positionals[0], enabled, world);
                    Console.WriteLine(changed
                        ? $"已{(enabled ? "启用" : "禁用")} {positionals[0]}"
                        : $"没有这个内容：{positionals[0]}");
                    return changed ? 0 : 1;
                }
                catch (NotSupportedException exception)
                {
                    return Fail(exception.Message);
                }
            }

            case "outdated":
            case "update":
            {
                var store = new InstanceStore(layout);
                var instance = store.TryLoad(options.InstanceId!)
                    ?? throw new InvalidOperationException($"实例不存在：{options.InstanceId}");
                var (minecraftVersion, loader) = InstanceVersionInfo.Derive(instance.VersionId);
                loader = ModrinthLoaderName(loader);
                using var client = new HttpRemoteFileClient();
                var updater = new ModUpdater(service, new ModrinthClient(client));

                if (action == "outdated")
                {
                    var statuses = await updater.CheckAsync(instance.Name, minecraftVersion, loader, CancellationToken.None)
                        .ConfigureAwait(false);
                    foreach (var status in statuses)
                    {
                        Console.WriteLine($"  {ModUpdater.Describe(status)}");
                    }

                    Console.WriteLine($"共 {statuses.Count(status => status.UpdateAvailable)} 个可更新");
                    return 0;
                }

                var updated = await updater.UpdateAsync(instance.Name, minecraftVersion, loader, null, CancellationToken.None)
                    .ConfigureAwait(false);
                Console.WriteLine($"已更新 {updated} 个模组（旧文件在 trash/content）");
                return 0;
            }

            default:
                return Fail($"未知 content 动作：{action}（list|add|remove|enable|disable|outdated|update）");
        }
    }

    private static int WorldCommand(string[] args)
    {
        if (args.Length < 2)
        {
            return Fail("用法：esluncher world <list|size|backup|restore|rename> --instance <名字> ...");
        }

        var action = args[1].ToLowerInvariant();
        var (positionals, optionTokens) = SplitOptions(args[2..]);
        var options = CommandOptions.Parse(optionTokens);
        if (string.IsNullOrWhiteSpace(options.InstanceId))
        {
            return Fail("world 命令需要 --instance <名字>");
        }

        var layout = new DataLayout(options.DataRoot);
        var manager = new WorldManager(layout, new InstanceStore(layout));
        var instanceName = options.InstanceId!;

        switch (action)
        {
            case "list":
            {
                var worlds = manager.List(instanceName);
                if (worlds.Count == 0)
                {
                    Console.WriteLine($"{instanceName} 没有世界");
                    return 0;
                }

                Console.WriteLine($"{instanceName} 的世界（{worlds.Count} 个）：");
                foreach (var world in worlds)
                {
                    Console.WriteLine($"  {world.Name,-28} {world.SizeBytes / 1048576.0,8:F1} MiB  {world.Modified:yyyy-MM-dd HH:mm}");
                }

                return 0;
            }

            case "size":
            {
                var name = positionals.Count > 0 ? positionals[0] : options.World;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var path = Path.Combine(manager.SavesDirectory(instanceName), name!);
                    Console.WriteLine($"{name}: {manager.DirectorySize(path) / 1048576.0:F1} MiB");
                    return 0;
                }

                var worlds = manager.List(instanceName);
                foreach (var world in worlds)
                {
                    Console.WriteLine($"  {world.Name,-28} {world.SizeBytes / 1048576.0,8:F1} MiB");
                }

                return 0;
            }

            case "backup":
            {
                var world = positionals.Count > 0 ? positionals[0] : options.World;
                if (string.IsNullOrWhiteSpace(world))
                {
                    return Fail("world backup 需要世界名");
                }

                var outPath = string.IsNullOrWhiteSpace(options.Out)
                    ? Path.Combine(Environment.CurrentDirectory, $"{world}-backup.zip")
                    : options.Out!;
                Console.WriteLine($"已备份 → {manager.Backup(instanceName, world!, outPath)}");
                return 0;
            }

            case "restore":
            {
                if (positionals.Count < 1)
                {
                    return Fail("world restore 需要 <zip 路径>（世界名用 --world）");
                }

                var name = string.IsNullOrWhiteSpace(options.World)
                    ? Path.GetFileNameWithoutExtension(positionals[0])
                    : options.World!;
                Console.WriteLine($"已还原 → {manager.Restore(instanceName, positionals[0], name)}");
                return 0;
            }

            case "rename":
            {
                if (positionals.Count < 2)
                {
                    return Fail("world rename 需要 <旧名> <新名>");
                }

                manager.Rename(instanceName, positionals[0], positionals[1]);
                Console.WriteLine($"已重命名 {positionals[0]} → {positionals[1]}");
                return 0;
            }

            default:
                return Fail($"未知 world 动作：{action}（list|size|backup|restore|rename）");
        }
    }

    private static async Task<int> DepsCommandAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Fail("用法：esluncher deps --instance <名字> [--fix]");
        }

        var (_, optionTokens) = SplitOptions(args[1..]);
        var options = CommandOptions.Parse(optionTokens);
        if (string.IsNullOrWhiteSpace(options.InstanceId))
        {
            return Fail("deps 需要 --instance <名字>");
        }

        var layout = new DataLayout(options.DataRoot);
        var store = new InstanceStore(layout);
        var instance = store.TryLoad(options.InstanceId!)
            ?? throw new InvalidOperationException($"实例不存在：{options.InstanceId}");
        var content = new InstanceContentService(layout, store);
        var checker = new DependencyChecker(content);
        var result = checker.Check(instance.Name);

        Console.WriteLine($"已装模组：{(result.Installed.Count == 0 ? "无" : string.Join(", ", result.Installed))}");
        Console.WriteLine(result.Missing.Count == 0
            ? "依赖检查：满足（静态检查）"
            : $"可能缺少前置：{string.Join(", ", result.Missing)}（静态检查，mod id 与依赖 id 不一致时可能误报）");

        if (!options.Fix || result.Missing.Count == 0)
        {
            return 0;
        }

        var (minecraftVersion, loader) = InstanceVersionInfo.Derive(instance.VersionId);
        loader = ModrinthLoaderName(loader);
        using var client = new HttpRemoteFileClient();
        var modrinth = new ModrinthClient(client);

        foreach (var dependency in result.Missing)
        {
            try
            {
                var hits = await modrinth
                    .SearchAsync(dependency, minecraftVersion, loader, 5, CancellationToken.None)
                    .ConfigureAwait(false);
                var hit = hits.FirstOrDefault(candidate =>
                    string.Equals(candidate.Slug, dependency, StringComparison.OrdinalIgnoreCase));
                if (hit is null)
                {
                    Console.WriteLine($"  ! 无法自动补装 {dependency}（Modrinth 没有同名项目，请手动找）");
                    continue;
                }

                var file = await modrinth
                    .InstallAsync(content, instance.Name, hit.Slug, minecraftVersion, loader, CancellationToken.None)
                    .ConfigureAwait(false);
                Console.WriteLine($"  + 已安装 {dependency} → {file}");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"  ! {dependency} 安装失败：{exception.Message}");
            }
        }

        var after = checker.Check(instance.Name);
        Console.WriteLine(after.Missing.Count == 0
            ? "补装后：依赖满足"
            : $"补装后仍缺：{string.Join(", ", after.Missing)}");
        return after.Missing.Count == 0 ? 0 : 1;
    }

    private static async Task<JavaRuntime> InstallComponentAsync(
        DataLayout layout,
        FileDownloader downloader,
        string component,
        CommandOptions options,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        var packages = await FetchRuntimeCatalogAsync(layout, options.Refresh, cancellationToken).ConfigureAwait(false);
        var package = JavaRuntimeCatalog.Find(packages, component)
            ?? throw new InvalidOperationException(
                $"运行时目录里没有组件 {component}；可用：{string.Join(", ", packages.Select(p => p.Component))}");

        Console.WriteLine($"安装 {package.Component} {package.VersionName} → {Path.Combine(layout.JavaDir, package.Component, package.VersionName)}");
        var installer = new JavaRuntimeInstaller(layout, downloader);
        return await installer.InstallAsync(package, options.Concurrency, progress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<JavaRuntimePackage>> FetchRuntimeCatalogAsync(
        DataLayout layout,
        bool refresh,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(layout.JavaDir);
        var path = Path.Combine(layout.JavaDir, "java-runtimes.json");

        if (!File.Exists(path) || refresh)
        {
            using var client = new HttpRemoteFileClient();
            using var response = await client.GetAsync(JavaRuntimeCatalog.AllRuntimes, 0, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                throw new HttpRequestException($"获取运行时目录失败：HTTP {response.StatusCode}");
            }

            await using var file = File.Create(path);
            await response.Content!.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JavaRuntimeCatalog.Parse(json, JavaPlatform.Detect());
    }

    private static async Task<int> VersionsAsync(CommandOptions options)
    {
        var layout = new DataLayout(options.DataRoot);
        using var client = new HttpRemoteFileClient();
        var catalog = new VersionCatalog(layout, new FileDownloader(client));
        var manifest = await catalog.GetManifestAsync(options.Refresh, CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine(
            $"manifest: {manifest.Versions.Count} 个版本，latest release={manifest.Latest.Release}，snapshot={manifest.Latest.Snapshot}");

        var type = string.IsNullOrWhiteSpace(options.Type) ? "release" : options.Type!;
        var entries = VersionQuery.Latest(manifest, type, options.Limit);
        Console.WriteLine($"type={type} 共取 {entries.Count} 条（新→旧）：");
        foreach (var entry in entries)
        {
            Console.WriteLine($"  {entry.Id,-20} {entry.Type,-10} {(entry.ReleaseTime?.ToString("yyyy-MM-dd") ?? "-")}");
        }

        return 0;
    }

    private static async Task<int> ModrinthCommandAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Fail("用法：esluncher modrinth <search|install> ...");
        }

        var action = args[1].ToLowerInvariant();
        var (positionals, optionTokens) = SplitOptions(args[2..]);
        var options = CommandOptions.Parse(optionTokens);
        var layout = new DataLayout(options.DataRoot);
        using var client = new HttpRemoteFileClient();
        var modrinth = new ModrinthClient(client);
        var kindText = options.Kind?.ToLowerInvariant();
        var isModpack = kindText is "modpack" or "modpacks";
        var kind = ContentKind.Mods;
        if (!isModpack && !string.IsNullOrWhiteSpace(kindText) && !InstanceContentService.TryParseKind(kindText!, out kind))
        {
            return Fail($"未知内容类型：{options.Kind}");
        }

        if (kind == ContentKind.Worlds)
        {
            return Fail("世界不支持从 Modrinth 安装");
        }

        switch (action)
        {
            case "search":
            {
                var query = options.Query ?? string.Join(' ', positionals);
                if (string.IsNullOrWhiteSpace(query))
                {
                    return Fail("modrinth search 需要 --query <关键词>");
                }

                var hits = await modrinth
                    .SearchAsync(
                        query,
                        options.Version,
                        options.Loader,
                        options.Limit,
                        CancellationToken.None,
                        isModpack ? ContentKind.Mods : kind,
                        isModpack ? "modpack" : null)
                    .ConfigureAwait(false);
                if (hits.Count == 0)
                {
                    Console.WriteLine("没有结果");
                    return 0;
                }

                Console.WriteLine($"Modrinth 检索「{query}」：{hits.Count} 条");
                foreach (var hit in hits)
                {
                    Console.WriteLine($"  {hit.Slug,-32} {hit.Downloads,12:N0} 下载  {hit.Title}");
                }

                return 0;
            }

            case "install":
            {
                if (string.IsNullOrWhiteSpace(options.InstanceId) || positionals.Count < 1)
                {
                    return Fail("用法：esluncher modrinth install --instance <名字> <项目 slug/ID>");
                }

                var store = new InstanceStore(layout);
                var instance = store.TryLoad(options.InstanceId!)
                    ?? throw new InvalidOperationException($"实例不存在：{options.InstanceId}");
                var (mcVersion, loader) = InstanceVersionInfo.Derive(instance.VersionId);
                loader = kind == ContentKind.Mods ? options.Loader ?? ModrinthLoaderName(loader) : null;

                var content = new InstanceContentService(layout, store);
                var file = await modrinth
                    .InstallAsync(
                        content,
                        instance.Name,
                        positionals[0],
                        options.Version ?? mcVersion,
                        loader,
                        CancellationToken.None,
                        kind,
                        options.World)
                    .ConfigureAwait(false);
                Console.WriteLine($"已安装 {positionals[0]} → {file}（MC {options.Version ?? mcVersion}, 类型 {InstanceContentService.FolderName(kind)}）");
                return 0;
            }

            case "modpack":
            {
                if (string.IsNullOrWhiteSpace(options.InstanceId) || positionals.Count < 1)
                {
                    return Fail("用法：esluncher modrinth modpack --instance <新实例名> <项目 slug/ID> [--version <mc>]");
                }

                var store = new InstanceStore(layout);
                if (store.TryLoad(options.InstanceId!) is not null)
                {
                    return Fail($"实例已存在：{options.InstanceId}");
                }

                var versions = await modrinth
                    .GetVersionsAsync(positionals[0], options.Version, null, CancellationToken.None)
                    .ConfigureAwait(false);
                var version = versions.FirstOrDefault()
                    ?? throw new InvalidOperationException($"Modrinth 上没有可用的整合包版本：{positionals[0]}");
                var file = version.Files.FirstOrDefault(candidate =>
                        candidate.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"整合包 {positionals[0]} 的最新版本没有 .mrpack 文件");

                if (options.DryRun)
                {
                    Console.WriteLine($"计划：{file.Filename}（{file.Size / 1048576.0:F1} MiB）→ 实例 {options.InstanceId}");
                    Console.WriteLine($"依赖：{version.Id}");
                    return 0;
                }

                var downloader = new FileDownloader(client);
                var zipPath = Path.Combine(layout.Root, "cache", "packs", file.Filename);
                Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
                var downloaded = await downloader
                    .DownloadAsync(new DownloadJob { Primary = new Uri(file.Url), TargetPath = zipPath }, CancellationToken.None)
                    .ConfigureAwait(false);
                if (downloaded.Status == FileDownloadStatus.Failed)
                {
                    return Fail($"下载整合包失败：{downloaded.Error}");
                }

                var installer = new ModpackInstaller(
                    layout,
                    store,
                    new InstancePackService(layout, store, new InstanceContentService(layout, store)),
                    new LoaderProvisioner(layout, downloader, client),
                    new VersionFetcher(layout, downloader),
                    client);
                var instance = await installer
                    .InstallAsync(zipPath, options.InstanceId!, 2048, CancellationToken.None)
                    .ConfigureAwait(false);
                Console.WriteLine($"已创建实例 {instance.Name} ← {instance.VersionId}");
                return 0;
            }

            default:
                return Fail($"未知 modrinth 动作：{action}（search|install）");
        }
    }

    private static string? ModrinthLoaderName(string? loader) => loader switch
    {
        "fabric-loader" => "fabric",
        "quilt-loader" => "quilt",
        "forge" => "forge",
        "neoforge" => "neoforge",
        _ => loader,
    };

    private static async Task<int> PackCommandAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Fail("用法：esluncher pack <export|import> ...");
        }

        var action = args[1].ToLowerInvariant();
        var (positionals, optionTokens) = SplitOptions(args[2..]);
        var options = CommandOptions.Parse(optionTokens);
        if (string.IsNullOrWhiteSpace(options.InstanceId))
        {
            return Fail("pack 命令需要 --instance <名字>");
        }

        var layout = new DataLayout(options.DataRoot);
        var store = new InstanceStore(layout);
        var service = new InstancePackService(layout, store, new InstanceContentService(layout, store));

        switch (action)
        {
            case "export":
            {
                var outPath = string.IsNullOrWhiteSpace(options.Out)
                    ? Path.Combine(Environment.CurrentDirectory, options.InstanceId + "-pack.mrpack")
                    : options.Out!;
                await service.ExportAsync(options.InstanceId!, outPath, CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"已导出 {options.InstanceId} → {outPath}");
                return 0;
            }

            case "import":
            {
                if (positionals.Count < 1)
                {
                    return Fail("pack import 需要 <zip 路径>");
                }

                using var client = new HttpRemoteFileClient();
                var (downloaded, overridden, skipped) = await service
                    .ImportAsync(positionals[0], options.InstanceId!, client, CancellationToken.None)
                    .ConfigureAwait(false);
                Console.WriteLine($"已导入到 {options.InstanceId}：下载 {downloaded}，覆盖 {overridden}，跳过 {skipped}");
                return 0;
            }

            case "install":
            {
                if (positionals.Count < 1)
                {
                    return Fail("pack install 需要 <zip 路径>（会创建新实例）");
                }

                using var client = new HttpRemoteFileClient();
                var downloader = new FileDownloader(client);
                var installer = new ModpackInstaller(
                    layout,
                    store,
                    service,
                    new LoaderProvisioner(layout, downloader, client),
                    new VersionFetcher(layout, downloader),
                    client);
                var instance = await installer
                    .InstallAsync(positionals[0], options.InstanceId!, 2048, CancellationToken.None)
                    .ConfigureAwait(false);
                Console.WriteLine($"已创建实例 {instance.Name} ← {instance.VersionId}（可在「实例」页启动）");
                return 0;
            }

            default:
                return Fail($"未知 pack 动作：{action}（export|import）");
        }
    }

    private static async Task<int> DiagnoseCommandAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Fail("用法：esluncher diagnose --instance <名字> [--export <zip>]");
        }

        var (_, optionTokens) = SplitOptions(args[1..]);
        var options = CommandOptions.Parse(optionTokens);
        if (string.IsNullOrWhiteSpace(options.InstanceId))
        {
            return Fail("diagnose 需要 --instance <名字>");
        }

        var layout = new DataLayout(options.DataRoot);
        var diagnostics = new InstanceDiagnostics(layout, new InstanceStore(layout));
        var report = diagnostics.Analyze(options.InstanceId!);

        Console.WriteLine($"实例：{report.Instance}");
        Console.WriteLine($"日志：{report.LogPath ?? "(没有 latest.log)"}");
        Console.WriteLine($"错误行：{report.Errors.Count}");
        foreach (var error in report.Errors.Take(15))
        {
            Console.WriteLine($"  {error}");
        }

        if (report.Crashes.Count > 0)
        {
            Console.WriteLine($"崩溃报告：{report.Crashes.Count}");
            foreach (var crash in report.Crashes)
            {
                Console.WriteLine($"  {crash.Path} :: {crash.Summary}");
            }
        }

        if (report.Hints.Count > 0)
        {
            Console.WriteLine("提示：");
            foreach (var hint in report.Hints)
            {
                Console.WriteLine($"  - {hint}");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.Out))
        {
            var zip = diagnostics.Export(options.InstanceId!, options.Out!);
            Console.WriteLine($"诊断包：{zip}");
        }

        return 0;
    }

    private static async Task<int> LoginAsync(CommandOptions options)
    {
        var layout = new DataLayout(options.DataRoot);
        var store = new DpapiTokenStore(layout);
        var provider = (options.Provider ?? "offline").ToLowerInvariant();

        if (provider == "offline")
        {
            if (string.IsNullOrWhiteSpace(options.Username))
            {
                return Fail("login offline 需要 --username <名字>");
            }

            var session = await new OfflineAuthProvider(options.Username).LoginAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await store.SaveAsync(session.Profile.Name.ToLowerInvariant(), session, CancellationToken.None)
                .ConfigureAwait(false);
            Console.WriteLine($"已保存离线账号：{session.Describe()}");
            return 0;
        }

        if (provider == "microsoft")
        {
            var clientId = options.ClientId ?? Environment.GetEnvironmentVariable("ESLUNCHER_MSA_CLIENT_ID");
            if (string.IsNullOrWhiteSpace(clientId))
            {
                Console.Error.WriteLine("错误：微软登录需要一个 Azure 客户端 ID（公共客户端应用）。");
                Console.Error.WriteLine("  1) portal.azure.com 注册应用（个人账号免费），重定向填 http://localhost");
                Console.Error.WriteLine("  2) 在「身份验证」里允许公共客户端流（设备代码）");
                Console.Error.WriteLine("  3) esluncher login --provider microsoft --client-id <id>，或设置 ESLUNCHER_MSA_CLIENT_ID");
                return 2;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var client = new MicrosoftAuthClient(http, clientId);
            var session = await client.LoginAsync(
                device =>
                {
                    Console.WriteLine();
                    Console.WriteLine($"浏览器打开 {device.VerificationUri}，输入代码：{device.UserCode}");
                    Console.WriteLine(device.Message);
                    Console.WriteLine();
                },
                CancellationToken.None).ConfigureAwait(false);

            await store.SaveAsync(session.Profile.Name.ToLowerInvariant(), session, CancellationToken.None)
                .ConfigureAwait(false);
            Console.WriteLine($"已保存微软账号：{session.Describe()}");
            return 0;
        }

        return Fail($"未知认证方式：{provider}（支持 offline / microsoft）");
    }

    private static int ListAccounts(CommandOptions options)
    {
        var layout = new DataLayout(options.DataRoot);
        var store = new DpapiTokenStore(layout);
        var accounts = store.ListAccounts();
        if (accounts.Count == 0)
        {
            Console.WriteLine("没有已保存的账号。用 `esluncher login --provider offline --username <名字>` 添加。");
            return 0;
        }

        foreach (var key in accounts)
        {
            try
            {
                var session = store.LoadAsync(key, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"  {key,-16} {session?.Describe() ?? "(无法解密)"}");
            }
            catch (AuthenticationException ex)
            {
                Console.WriteLine($"  {key,-16} 解密失败：{ex.Message}");
            }
        }

        return 0;
    }

    private static async Task<int> LogoutAsync(CommandOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Account))
        {
            return Fail("logout 需要 --account <名字>");
        }

        var layout = new DataLayout(options.DataRoot);
        var store = new DpapiTokenStore(layout);
        var removed = await store.DeleteAsync(options.Account, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine(removed ? $"已删除账号 {options.Account}" : $"没有这个账号：{options.Account}");
        return removed ? 0 : 1;
    }

    private static InstanceConfig? LoadInstance(DataLayout layout, CommandOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.InstanceId))
        {
            return null;
        }

        return new InstanceStore(layout).TryLoad(options.InstanceId!)
            ?? throw new InvalidOperationException(
                $"实例不存在：{options.InstanceId}。先 `esluncher instance create <名字> --version <id>`");
    }

    private static IReadOnlyList<string> WindowArguments(InstanceConfig instance)
    {
        if (instance.WindowWidth is not int width || instance.WindowHeight is not int height)
        {
            return [];
        }

        return
        [
            "--width", width.ToString(CultureInfo.InvariantCulture),
            "--height", height.ToString(CultureInfo.InvariantCulture),
        ];
    }

    private static (List<string> Positionals, string[] OptionTokens) SplitOptions(string[] tokens)
    {
        var positionals = new List<string>();
        var options = new List<string>();
        HashSet<string> booleanFlags = new(StringComparer.Ordinal) { "--force", "--dry-run", "--refresh", "--install-java" };

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(token);
                continue;
            }

            options.Add(token);
            if (!booleanFlags.Contains(token) && i + 1 < tokens.Length && !tokens[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options.Add(tokens[++i]);
            }
        }

        return (positionals, options.ToArray());
    }

    private static LoaderKind ParseLoader(string value) => value.ToLowerInvariant() switch
    {
        "fabric" => LoaderKind.Fabric,
        "quilt" => LoaderKind.Quilt,
        "forge" => LoaderKind.Forge,
        "neoforge" or "neo" => LoaderKind.NeoForge,
        _ => throw new ArgumentException($"未知加载器：{value}（fabric|quilt|forge|neoforge）"),
    };

    private static async Task<JavaRuntime?> SelectJavaForVersionAsync(
        DataLayout layout,
        FileDownloader downloader,
        string mcVersion,
        CommandOptions options,
        CancellationToken cancellationToken)
    {
        var catalog = new VersionCatalog(layout, downloader);
        var manifest = await catalog.GetManifestAsync(options.Refresh, cancellationToken).ConfigureAwait(false);
        var version = await catalog.GetVersionAsync(mcVersion, manifest, options.Refresh, cancellationToken).ConfigureAwait(false);
        var required = version.JavaVersion?.MajorVersion ?? 8;
        return JavaSelector.Select(required, JavaDiscovery.Discover([layout.JavaDir]), null).Runtime;
    }

    private static async Task<int> LoaderCommandAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return Fail("用法：esluncher loader <list|install> --version <mc> --loader <fabric|quilt|forge|neoforge>");
        }

        var action = args[1].ToLowerInvariant();
        var (_, optionTokens) = SplitOptions(args[2..]);
        var options = CommandOptions.Parse(optionTokens);
        if (string.IsNullOrWhiteSpace(options.Loader))
        {
            return Fail("loader 命令需要 --loader <fabric|quilt|forge|neoforge>");
        }

        var kind = ParseLoader(options.Loader!);
        var mcVersion = options.RequireVersion();
        var layout = new DataLayout(options.DataRoot);
        using var client = new HttpRemoteFileClient();
        var downloader = new FileDownloader(client);
        var provisioner = new LoaderProvisioner(layout, downloader, client);

        switch (action)
        {
            case "list":
            {
                var versions = await provisioner.GetVersionsAsync(kind, mcVersion, CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"{kind} / MC {mcVersion}：{versions.Count} 个版本（新→旧）");
                foreach (var version in versions.Take(options.Limit))
                {
                    Console.WriteLine($"  {version.Version,-24} {(version.Stable ? "stable" : "beta")}");
                }

                return 0;
            }

            case "install":
            {
                var java = kind is LoaderKind.Forge or LoaderKind.NeoForge
                    ? await SelectJavaForVersionAsync(layout, downloader, mcVersion, options, CancellationToken.None).ConfigureAwait(false)
                    : null;
                var installed = await provisioner
                    .InstallAsync(kind, mcVersion, options.LoaderVersion, java, CancellationToken.None)
                    .ConfigureAwait(false);
                Console.WriteLine($"已安装 {kind} → 版本 id：{installed}");
                return 0;
            }

            default:
                return Fail($"未知 loader 动作：{action}（list|install）");
        }
    }

    private static async Task<VersionMetadata> ResolveInheritanceAsync(
        VersionMetadata version,
        VersionCatalog catalog,
        VersionManifest manifest,
        bool refresh,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(version.InheritsFrom))
        {
            return version;
        }

        var parents = new Dictionary<string, VersionMetadata>(StringComparer.Ordinal);
        var pending = version;
        while (!string.IsNullOrWhiteSpace(pending.InheritsFrom))
        {
            var parentId = pending.InheritsFrom!;
            if (!parents.TryGetValue(parentId, out var parent))
            {
                parent = await catalog.GetVersionAsync(parentId, manifest, refresh, cancellationToken).ConfigureAwait(false);
                parents[parentId] = parent;
            }

            pending = parent;
        }

        return VersionMerger.Resolve(version, id => parents[id]);
    }

    private static async Task<int> InstanceCommand(string[] args)
    {
        if (args.Length < 2)
        {
            return Fail("用法：esluncher instance <create|list|delete|clone|export|import> ...");
        }

        var action = args[1].ToLowerInvariant();
        var (positionals, optionTokens) = SplitOptions(args[2..]);
        var options = CommandOptions.Parse(optionTokens);
        var layout = new DataLayout(options.DataRoot);
        var store = new InstanceStore(layout);

        switch (action)
        {
            case "create":
            {
                if (positionals.Count < 1)
                {
                    return Fail("instance create 需要实例名：esluncher instance create <名字> --version <id>");
                }

                var versionId = options.RequireVersion();
                if (!string.IsNullOrWhiteSpace(options.Loader))
                {
                    var kind = ParseLoader(options.Loader!);
                    using var client = new HttpRemoteFileClient();
                    var downloader = new FileDownloader(client);
                    var java = kind is LoaderKind.Forge or LoaderKind.NeoForge
                        ? await SelectJavaForVersionAsync(layout, downloader, versionId, options, CancellationToken.None).ConfigureAwait(false)
                        : null;
                    var provisioner = new LoaderProvisioner(layout, downloader, client);
                    versionId = await provisioner
                        .InstallAsync(kind, versionId, options.LoaderVersion, java, CancellationToken.None)
                        .ConfigureAwait(false);
                    Console.WriteLine($"已安装 {kind} → {versionId}");
                }

                var config = store.Create(positionals[0], versionId, options.MemoryMb, options.Username);
                Console.WriteLine($"已创建实例 {config.Name} ← {config.VersionId}，内存 {config.MemoryMb} MB");
                Console.WriteLine($"目录：{layout.InstanceDir(config.Name)}");
                return 0;
            }

            case "list":
            {
                var instances = store.List();
                if (instances.Count == 0)
                {
                    Console.WriteLine("还没有实例。用 `esluncher instance create <名字> --version <id>` 创建。");
                    return 0;
                }

                Console.WriteLine($"共 {instances.Count} 个实例：");
                foreach (var config in instances)
                {
                    Console.WriteLine($"  {config.Name,-20} {config.VersionId,-14} {config.MemoryMb,5} MB  {config.CreatedAt:yyyy-MM-dd}");
                }

                return 0;
            }

            case "delete":
            {
                if (positionals.Count < 1)
                {
                    return Fail("instance delete 需要实例名");
                }

                var removed = store.Delete(positionals[0], options.Force);
                Console.WriteLine(removed ? $"已删除实例 {positionals[0]}" : $"实例不存在：{positionals[0]}");
                return removed ? 0 : 1;
            }

            case "clone":
            {
                if (positionals.Count < 2)
                {
                    return Fail("instance clone 需要 <来源> <目标>");
                }

                var cloned = store.Clone(positionals[0], positionals[1]);
                Console.WriteLine($"已克隆 {positionals[0]} → {cloned.Name}（{cloned.VersionId}）");
                return 0;
            }

            case "export":
            {
                if (positionals.Count < 1)
                {
                    return Fail("instance export 需要实例名");
                }

                var outPath = string.IsNullOrWhiteSpace(options.Out)
                    ? Path.Combine(Environment.CurrentDirectory, positionals[0] + ".zip")
                    : options.Out!;
                store.Export(positionals[0], outPath);
                Console.WriteLine($"已导出 {positionals[0]} → {outPath}");
                return 0;
            }

            case "import":
            {
                if (positionals.Count < 2)
                {
                    return Fail("instance import 需要 <zip 路径> <实例名>");
                }

                var imported = store.Import(positionals[0], positionals[1]);
                Console.WriteLine($"已导入实例 {imported.Name} ← {imported.VersionId}");
                return 0;
            }

            case "import-dir":
            {
                if (positionals.Count < 1)
                {
                    return Fail("instance import-dir 需要实例名（--source <目录> --version <id>）");
                }

                if (string.IsNullOrWhiteSpace(options.Source))
                {
                    return Fail("instance import-dir 需要 --source <目录>");
                }

                var importer = new InstanceImporter(layout, store);
                var imported = importer.Import(positionals[0], options.Source!, options.RequireVersion(), options.MemoryMb);
                Console.WriteLine($"已导入实例 {imported.Name} ← {imported.VersionId}（来源 {options.Source}）");
                return 0;
            }

            case "size":
            {
                var name = positionals.Count > 0 ? positionals[0] : options.InstanceId;
                if (string.IsNullOrWhiteSpace(name))
                {
                    return Fail("instance size 需要实例名");
                }

                var manager = new WorldManager(layout, store);
                var total = manager.DirectorySize(layout.GameDir(name!));
                Console.WriteLine($"{name}: 游戏目录 {total / 1048576.0:F1} MiB");
                return 0;
            }

            case "group":
            {
                if (positionals.Count < 2)
                {
                    return Fail("instance group 需要 <实例名> <分组名>（分组名传空字符串可清除）");
                }

                var config = store.TryLoad(positionals[0])
                    ?? throw new InvalidOperationException($"实例不存在：{positionals[0]}");
                store.Save(config with { Group = string.IsNullOrWhiteSpace(positionals[1]) ? null : positionals[1] });
                Console.WriteLine($"已分组 {positionals[0]} → {(string.IsNullOrWhiteSpace(positionals[1]) ? "(无)" : positionals[1])}");
                return 0;
            }

            default:
                return Fail($"未知 instance 动作：{action}（create|list|delete|clone|export|import）");
        }
    }

    private static async Task<int> LaunchAsync(CommandOptions options)
    {
        var layout = new DataLayout(options.DataRoot);
        var instance = LoadInstance(layout, options);
        var versionId = instance?.VersionId ?? options.RequireVersion();
        using var client = new HttpRemoteFileClient();
        var downloader = new FileDownloader(client);
        var catalog = new VersionCatalog(layout, downloader);

        var manifest = await catalog.GetManifestAsync(options.Refresh, CancellationToken.None).ConfigureAwait(false);
        var version = await catalog.GetVersionAsync(versionId, manifest, options.Refresh, CancellationToken.None)
            .ConfigureAwait(false);
        version = await ResolveInheritanceAsync(version, catalog, manifest, options.Refresh, CancellationToken.None)
            .ConfigureAwait(false);

        var environment = LaunchEnvironment.Detect();
        var requiredMajor = version.JavaVersion?.MajorVersion;
        if (requiredMajor is null)
        {
            Console.WriteLine($"警告：{version.Id} 的 version.json 没有 javaVersion，按 Java 8 处理");
            requiredMajor = 8;
        }

        JavaRuntime? overridden = null;
        var javaPathOverride = options.JavaPath ?? instance?.JavaPath;
        if (!string.IsNullOrWhiteSpace(javaPathOverride))
        {
            overridden = JavaRuntime.TryProbe(javaPathOverride);
            if (overridden is null)
            {
                return Fail($"--java 指向的不是可用的 java：{javaPathOverride}");
            }
        }

        var installedRuntimes = JavaDiscovery.Discover([layout.JavaDir]);
        var selection = JavaSelector.Select(requiredMajor.Value, installedRuntimes, overridden);

        if (!selection.IsSatisfied && options.InstallJava && overridden is null)
        {
            var component = version.JavaVersion?.Component ?? "jre-legacy";
            Console.WriteLine($"本机缺少 Java {requiredMajor}，开始安装 Mojang 运行时组件 {component} …");
            await InstallComponentAsync(layout, downloader, component, options, new ConsoleProgress(), CancellationToken.None)
                .ConfigureAwait(false);
            installedRuntimes = JavaDiscovery.Discover([layout.JavaDir]);
            selection = JavaSelector.Select(requiredMajor.Value, installedRuntimes, null);
        }

        Console.WriteLine(selection.Message);
        if (!selection.IsSatisfied)
        {
            Console.WriteLine($"提示：加 --install-java 可让启动器自动装 Mojang 运行时（组件 {(version.JavaVersion?.Component ?? "jre-legacy")}）");
            return 3;
        }

        AuthSession? session = null;
        var accountKey = options.Account ?? instance?.AccountKey;
        if (!string.IsNullOrWhiteSpace(accountKey))
        {
            var store = new DpapiTokenStore(layout);
            session = await store.LoadAsync(accountKey, CancellationToken.None).ConfigureAwait(false);
            if (session is null)
            {
                return Fail($"没有找到账号 {accountKey}，先 `esluncher login`");
            }

            if (session.IsExpired(DateTimeOffset.UtcNow))
            {
                Console.WriteLine("警告：会话已过期；启动可能被服务端拒绝（M3b 的自动续期尚未接入）");
            }

            Console.WriteLine($"账号：{session.Describe()}");
        }

        var request = new LaunchRequest
        {
            Username = session?.Profile.Name ?? instance?.Username ?? options.Username ?? "Player",
            Uuid = session?.Profile.Id,
            AccessToken = session?.AccessToken,
            UserType = session?.UserType ?? "legacy",
            InstanceId = instance?.Name ?? options.InstanceId,
            MemoryMb = instance?.MemoryMb ?? options.MemoryMb,
            ExtraJvmArguments = instance?.ExtraJvmArguments ?? [],
            ExtraGameArguments = instance is null
                ? []
                : [.. instance.ExtraGameArguments, .. WindowArguments(instance)],
        };
        var command = LaunchCommandBuilder.Build(version, environment, layout, selection.Runtime!, request);

        // 实例级 natives 目录可能与共享库不同步（例如实例是新建/克隆/导入的），启动前补齐。
        var nativesExtracted = VersionFetcher.EnsureNatives(
            version,
            environment,
            layout,
            request.InstanceId ?? version.Id);
        if (nativesExtracted > 0)
        {
            Console.WriteLine($"natives 已就绪：{nativesExtracted} 个文件");
        }

        var classpathEntries = command.Classpath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var missing = classpathEntries.Where(path => !File.Exists(path)).ToArray();
        var nativesFiles = Directory.Exists(command.NativesDirectory)
            ? Directory.GetFiles(command.NativesDirectory).Length
            : 0;

        Console.WriteLine();
        Console.WriteLine($"version   : {version.Id} ({version.Type})  java required={requiredMajor}");
        Console.WriteLine($"java      : {command.JavaExePath}");
        Console.WriteLine($"working   : {command.WorkingDirectory}");
        Console.WriteLine($"natives   : {command.NativesDirectory} ({nativesFiles} files)");
        Console.WriteLine($"classpath : {classpathEntries.Length} entries, missing {missing.Length}");
        foreach (var path in missing.Take(5))
        {
            Console.WriteLine($"  ! missing {path}");
        }

        Console.WriteLine($"arguments : {command.Arguments.Count}");
        foreach (var argument in command.Arguments.Take(14))
        {
            Console.WriteLine($"  {argument}");
        }

        if (command.Arguments.Count > 14)
        {
            Console.WriteLine("  ...");
        }

        if (!string.IsNullOrWhiteSpace(options.DumpPath))
        {
            File.WriteAllLines(
                options.DumpPath,
                new[] { command.JavaExePath }.Concat(command.Arguments));
            Console.WriteLine($"command dumped: {options.DumpPath}");
        }

        if (options.DryRun)
        {
            Console.WriteLine("dry-run：未启动进程");
            return missing.Length == 0 ? 0 : 4;
        }
        if (missing.Length > 0)
        {
            return Fail($"classpath 缺 {missing.Length} 个文件，先跑 `esluncher fetch --version {version.Id}`");
        }

        Directory.CreateDirectory(command.WorkingDirectory);
        // 同一版本多实例并行时日志文件名必须唯一：带上实例名 + 时间戳 + 短随机后缀。
        var logStem = $"{request.InstanceId ?? version.Id}-{version.Id}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
        var safeStem = string.Concat(logStem.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var logPath = Path.Combine(layout.LogsDir, $"launch-{safeStem}.log");
        Console.WriteLine($"log       : {logPath}");
        Console.WriteLine(options.AutoStopSeconds > 0
            ? $"启动游戏（{options.AutoStopSeconds}s 后自动停止，用于验收）…"
            : "启动游戏（等待进程退出）…");

        var autoStop = options.AutoStopSeconds > 0 ? TimeSpan.FromSeconds(options.AutoStopSeconds) : (TimeSpan?)null;
        var result = await ProcessRunner.RunLoggedAsync(command, logPath, autoStop, CancellationToken.None)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("== launch result ==");
        Console.WriteLine($"autoStopped : {result.AutoStopped}");
        Console.WriteLine($"exit code   : {(result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "(killed)")}");
        Console.WriteLine($"duration    : {result.Duration.TotalSeconds:0.0} s");
        Console.WriteLine($"log         : {result.LogPath}");

        if (result.AutoStopped)
        {
            // 验收判据：起来且撑到时间点 → 成功（不是"崩溃秒退"）
            return 0;
        }

        return result.ExitCode == 0 ? 0 : 1;
    }

    private static async Task<int> FetchAsync(CommandOptions options)
    {
        var versionId = options.RequireVersion();
        var layout = new DataLayout(options.DataRoot);
        Console.WriteLine($"ES Luncher CLI · fetch {versionId}");
        Console.WriteLine($"data root : {layout.Root}");
        Console.WriteLine($"concurrency: {options.Concurrency}   selection: {options.Selection.Describe()}");

        using var client = new HttpRemoteFileClient();
        var downloader = new FileDownloader(client);
        var fetcher = new VersionFetcher(layout, downloader);
        var fetchOptions = new FetchOptions
        {
            Selection = options.Selection,
            Concurrency = options.Concurrency,
            Refresh = options.Refresh,
        };

        if (options.DryRun)
        {
            var (version, plan) = await fetcher.BuildPlanAsync(versionId, fetchOptions, CancellationToken.None)
                .ConfigureAwait(false);
            PrintPlan(version, plan);
            return 0;
        }

        var progress = new ConsoleProgress();
        var report = await fetcher.FetchAsync(versionId, fetchOptions, progress, CancellationToken.None)
            .ConfigureAwait(false);
        PrintReport(report);
        return report.Success ? 0 : 1;
    }

    private static void PrintPlan(VersionMetadata version, DownloadPlan plan)
    {
        var bytes = plan.Files.Sum(job => job.ExpectedSize ?? 0);
        Console.WriteLine();
        Console.WriteLine($"version   : {version.Id} ({version.Type})  java={version.JavaVersion?.MajorVersion.ToString(CultureInfo.InvariantCulture) ?? "?"}");
        Console.WriteLine($"libraries : {version.Libraries.Count} 条（过滤后进计划 {plan.Files.Count(job => job.TargetPath.Contains("libraries", StringComparison.OrdinalIgnoreCase))} 条）");
        Console.WriteLine($"natives   : {plan.Natives.Count} 个解压目标");
        Console.WriteLine($"files     : {plan.Files.Count} 个文件，预计 {FormatBytes(bytes)}");
        Console.WriteLine("first files:");
        foreach (var job in plan.Files.Take(8))
        {
            Console.WriteLine($"  - {job.Primary}");
            Console.WriteLine($"    -> {job.TargetPath}");
        }
    }

    private static void PrintReport(FetchReport report)
    {
        Console.WriteLine();
        Console.WriteLine("== report ==");
        Console.WriteLine($"version.json : {report.VersionJsonPath}");
        Console.WriteLine($"client jar   : {report.ClientJarPath ?? "(skipped)"}");
        Console.WriteLine($"files        : planned={report.FilesPlanned} downloaded={report.Downloaded} skipped={report.Skipped} failed={report.Failed}");
        Console.WriteLine($"bytes        : {FormatBytes(report.BytesDownloaded)}");
        Console.WriteLine($"assets       : {report.AssetObjects} objects");
        Console.WriteLine($"natives      : {report.NativesExtracted} files extracted");

        if (report.Failures.Count > 0)
        {
            Console.WriteLine($"failures     : {report.Failures.Count}");
            foreach (var failure in report.Failures.Take(10))
            {
                Console.WriteLine($"  - {failure.Path}: {failure.Error}");
            }
        }
    }

    private static string FormatBytes(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.0} MiB"
            : $"{bytes / 1024d:0.0} KiB";

    /// <summary>
    /// 自动更新验收面：<c>esluncher update [--source 目录或URL] [--download] [--apply]</c>。
    /// 开发态（未用 Velopack Setup 安装）只做检查，`--apply` 会明确失败而不是假装成功。
    /// </summary>
    private static async Task<int> UpdateCommandAsync(string[] args)
    {
        string? source = null;
        var download = false;
        var apply = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source" when i + 1 < args.Length:
                    source = args[++i];
                    break;
                case "--download":
                    download = true;
                    break;
                case "--apply":
                    apply = true;
                    break;
                default:
                    return Fail($"update 不认识的参数：{args[i]}");
            }
        }

        var service = new VelopackUpdateService(source, message => Console.WriteLine($"  · {message}"));
        var result = await service.CheckAsync().ConfigureAwait(false);
        Console.WriteLine($"当前版本 : {result.CurrentVersion}");
        Console.WriteLine($"安装形态 : {(result.IsInstalled ? "Velopack 安装" : result.IsPortable ? "便携" : "开发态/未安装")}");
        Console.WriteLine($"可用版本 : {result.AvailableVersion ?? "（已是最新）"}");
        if (result.UpdateAvailable)
        {
            Console.WriteLine($"增量可用 : {(result.DeltaAvailable ? "是" : "否")}");
        }

        if (!download && !apply)
        {
            return 0;
        }

        if (!result.UpdateAvailable)
        {
            return 0;
        }

        await service.DownloadAsync(percent => Console.WriteLine($"  下载 {percent}%")).ConfigureAwait(false);
        Console.WriteLine("下载完成");

        if (apply)
        {
            if (!result.IsInstalled)
            {
                return Fail("当前不是 Velopack 安装形态，无法应用更新并重启（请用 Setup.exe 安装后再试）。");
            }

            service.ApplyAndRestart();
        }

        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"错误：{message}");
        Console.Error.WriteLine("用 `esluncher --help` 查看用法。");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        ES Luncher CLI（M1 fetch / M2 launch / M4 java 验收面）

        用法:
          esluncher fetch    --version <id> [选项]
          esluncher launch   --version <id> [选项]
          esluncher launch   --instance <实例名> [选项]
          esluncher instance create <名字> --version <id> [--memory-mb <n>] [--username <name>]
          esluncher instance list
          esluncher instance delete <名字> --force
          esluncher instance clone <来源> <目标>
          esluncher instance export <名字> [--out <zip>]
          esluncher instance import <zip 路径> <名字>
          esluncher instance import-dir <名字> --source <目录> --version <id>
          esluncher instance size <名字>
          esluncher world list|size|backup|restore|rename --instance <名字> ...
          esluncher content outdated|update --instance <名字>
          esluncher loader list --version <mc> --loader <fabric|quilt|forge|neoforge>
          esluncher loader install --version <mc> --loader <kind> [--loader-version <v>]
          esluncher instance create <名字> --version <mc> --loader <kind> [--loader-version <v>]
          esluncher content list   --instance <名字> [--kind mods|resourcepacks|shaderpacks]
          esluncher content add    --instance <名字> [--kind <k>] <文件>
          esluncher content remove --instance <名字> [--kind <k>] <文件名>
          esluncher modrinth search --query <关键词> [--version <mc>] [--loader <l>] [--limit <n>]
          esluncher modrinth install --instance <名字> <项目 slug/ID>
          esluncher modrinth modpack --instance <新实例名> <项目> [--version <mc>] [--dry-run]
          esluncher pack export --instance <名字> [--out <zip>]
          esluncher pack import --instance <名字> <zip>
          esluncher pack install --instance <新实例名> <zip>
          esluncher diagnose --instance <名字> [--export <zip>]
          esluncher deps --instance <名字> [--fix]
          esluncher java
          esluncher java install --component <组件> | --version <id>
          esluncher versions [--type <t>] [--limit <n>]
          esluncher login    --provider offline|microsoft [选项]
          esluncher accounts
          esluncher update   [--source <目录或URL>] [--download] [--apply]
          esluncher logout   --account <名字>

        选项:
          --version <id>     必填，例如 1.7.10 / 1.12.2 / 26.3
          --data <dir>       数据根（默认 %APPDATA%\\ESLuncher，或环境变量 ESLUNCHER_DATA）
          --concurrency <n>  fetch 并发下载数（默认 8）
          --only <parts>     fetch: client,libraries,assets,natives 的子集
          --refresh          强制刷新版本清单与 version.json
          --dry-run          fetch: 只算计划；launch: 只打印命令行
          --username <name>  launch 的离线用户名 / login offline 的名字（默认 Player）
          --instance <名字>  launch 已创建的实例（版本/内存/参数取实例配置）
          --java <path>      launch 指定 java.exe（默认按 version.json 要求自动选）
          --memory-mb <n>    launch 的 -Xmx（默认 2048）；instance create 也用它
          --autostop <s>     launch 启动后 s 秒自动停止（验收用；0 = 等待退出）
          --account <name>   launch 用已保存账号；logout 指定账号
          --provider <p>     login: offline | microsoft
          --client-id <id>   login microsoft 的 Azure 客户端 ID（或 ESLUNCHER_MSA_CLIENT_ID）
          --dump <file>      把完整命令行写到文件（排障/复现）
          --type <t>         versions: release|snapshot|old_alpha|old_beta|all（默认 release）
          --limit <n>        versions: 取几条（默认 10）
          --component <c>    java install: 运行时组件，如 jre-legacy / java-runtime-gamma
          --install-java     launch: 缺 Java 时自动安装 Mojang 运行时
          --force            instance delete: 确认删除（否则拒绝）
          --out <zip>        instance export: 输出路径
          --loader <kind>    fabric|quilt|forge|neoforge（loader/instance create）
          --loader-version <v> 指定加载器版本（默认最新稳定）
          --kind <k>         content: mods|resourcepacks|shaderpacks（默认 mods）
          --query <q>        modrinth search: 关键词
          --export <zip>     diagnose: 导出诊断包路径
          --world <名字>     data pack: 目标世界（content/datapacks 必填）
          --fix              deps: 自动从 Modrinth 补装缺失前置
          --source <目录>     instance import-dir: 源目录（.minecraft / Prism 游戏目录）

        示例:
          esluncher fetch  --version 1.7.10 --dry-run
          esluncher instance create "整合包 1.18" --version 1.18.2 --memory-mb 4096
          esluncher instance list
          esluncher launch --instance "整合包 1.18" --autostop 30
          esluncher login  --provider offline --username Dev
          esluncher login  --provider microsoft --client-id <azure-client-id>
        """);
    }

    private sealed class ConsoleProgress : IProgress<DownloadProgress>
    {
        private readonly object _gate = new();
        private DateTime _nextPrint = DateTime.MinValue;

        public void Report(DownloadProgress value)
        {
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                var finished = value.FilesDone == value.FilesTotal;
                if (!finished && now < _nextPrint)
                {
                    return;
                }

                _nextPrint = now.AddSeconds(2);
                Console.WriteLine($"[{value.FilesDone}/{value.FilesTotal}] {FormatBytes(value.BytesDownloaded)}  {Path.GetFileName(value.CurrentPath)}");
            }
        }
    }
}

internal sealed class CommandOptions
{
    public string? Version { get; private set; }

    public string DataRoot { get; private set; } = DataLayout.DefaultRoot();

    public int Concurrency { get; private set; } = 8;

    public FetchSelection Selection { get; private set; } = FetchSelection.All;

    public bool Refresh { get; private set; }

    public bool DryRun { get; private set; }

    public string? Username { get; private set; }

    public string? InstanceId { get; private set; }

    public string? JavaPath { get; private set; }

    public int MemoryMb { get; private set; } = 2048;

    public int AutoStopSeconds { get; private set; }

    public string? DumpPath { get; private set; }

    public string? Provider { get; private set; }

    public string? Account { get; private set; }

    public string? ClientId { get; private set; }

    public string? Type { get; private set; }

    public int Limit { get; private set; } = 10;

    public string? Component { get; private set; }

    public bool InstallJava { get; private set; }

    public bool Force { get; private set; }

    public string? Out { get; private set; }

    public string? Loader { get; private set; }

    public string? LoaderVersion { get; private set; }

    public string? Kind { get; private set; }

    public string? Query { get; private set; }

    public string? World { get; private set; }

    public bool Fix { get; private set; }

    public string? Source { get; private set; }

    /// <summary>fetch / launch 需要版本号，java 不需要 —— 所以校验放在命令里而不是 Parse 里。</summary>
    public string RequireVersion()
    {
        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new ArgumentException("缺少必填参数 --version <id>");
        }

        return Version!;
    }

    public static CommandOptions Parse(string[] args)
    {
        var options = new CommandOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--version":
                    options.Version = Next(args, ref i);
                    break;
                case "--data":
                    options.DataRoot = Next(args, ref i);
                    break;
                case "--concurrency":
                    options.Concurrency = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--only":
                    options.Selection = ParseOnly(Next(args, ref i));
                    break;
                case "--refresh":
                    options.Refresh = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--username":
                    options.Username = Next(args, ref i);
                    break;
                case "--instance":
                    options.InstanceId = Next(args, ref i);
                    break;
                case "--java":
                    options.JavaPath = Next(args, ref i);
                    break;
                case "--memory-mb":
                    options.MemoryMb = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--autostop":
                    options.AutoStopSeconds = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--dump":
                    options.DumpPath = Next(args, ref i);
                    break;
                case "--provider":
                    options.Provider = Next(args, ref i);
                    break;
                case "--account":
                    options.Account = Next(args, ref i);
                    break;
                case "--client-id":
                    options.ClientId = Next(args, ref i);
                    break;
                case "--type":
                    options.Type = Next(args, ref i);
                    break;
                case "--limit":
                    options.Limit = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--component":
                    options.Component = Next(args, ref i);
                    break;
                case "--install-java":
                    options.InstallJava = true;
                    break;
                case "--force":
                    options.Force = true;
                    break;
                case "--out":
                    options.Out = Next(args, ref i);
                    break;
                case "--export":
                    options.Out = Next(args, ref i);
                    break;
                case "--loader":
                    options.Loader = Next(args, ref i);
                    break;
                case "--loader-version":
                    options.LoaderVersion = Next(args, ref i);
                    break;
                case "--kind":
                    options.Kind = Next(args, ref i);
                    break;
                case "--query":
                    options.Query = Next(args, ref i);
                    break;
                case "--world":
                    options.World = Next(args, ref i);
                    break;
                case "--fix":
                    options.Fix = true;
                    break;
                case "--source":
                    options.Source = Next(args, ref i);
                    break;
                default:
                    throw new ArgumentException($"未知参数：{args[i]}");
            }
        }

        if (options.Concurrency < 1 || options.Concurrency > 64)
        {
            throw new ArgumentException("--concurrency 必须在 1..64");
        }

        if (options.MemoryMb < 512)
        {
            throw new ArgumentException("--memory-mb 至少 512");
        }

        if (options.AutoStopSeconds < 0)
        {
            throw new ArgumentException("--autostop 不能为负");
        }

        if (options.Limit < 1 || options.Limit > 200)
        {
            throw new ArgumentException("--limit 必须在 1..200");
        }

        return options;
    }

    private static string Next(string[] args, ref int index)
    {
        index++;
        if (index >= args.Length)
        {
            throw new ArgumentException($"参数 {args[index - 1]} 缺少值");
        }

        return args[index];
    }

    private static FetchSelection ParseOnly(string value)
    {
        var client = false;
        var libraries = false;
        var assets = false;
        var natives = false;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "client": client = true; break;
                case "libraries": libraries = true; break;
                case "assets": assets = true; break;
                case "natives": natives = true; break;
                default: throw new ArgumentException($"--only 不认识的部分：{part}");
            }
        }

        if (!client && !libraries && !assets && !natives)
        {
            throw new ArgumentException("--only 至少要给一个部分");
        }

        return new FetchSelection(client, libraries, assets, natives);
    }

    private static string DefaultDataRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ESLUNCHER_DATA");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        // 宪章：数据不占 C 盘。D 盘存在就用 D，否则退回用户目录。
        return Directory.Exists("D:\\") ? "D:\\mc-data" : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EsLuncher");
    }
}

internal static class FetchSelectionExtensions
{
    public static string Describe(this FetchSelection selection)
    {
        var parts = new List<string>(4);
        if (selection.Client) { parts.Add("client"); }
        if (selection.Libraries) { parts.Add("libraries"); }
        if (selection.Assets) { parts.Add("assets"); }
        if (selection.Natives) { parts.Add("natives"); }
        return string.Join(',', parts);
    }
}
