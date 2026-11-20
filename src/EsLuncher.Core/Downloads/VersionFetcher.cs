using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Downloads;

public sealed record FetchOptions
{
    public FetchSelection Selection { get; init; } = FetchSelection.All;

    public int Concurrency { get; init; } = 8;

    public bool Refresh { get; init; }
}

public sealed record FetchReport(
    string VersionId,
    int FilesPlanned,
    int Downloaded,
    int Skipped,
    int Failed,
    long BytesDownloaded,
    IReadOnlyList<DownloadFailure> Failures,
    int NativesExtracted,
    int AssetObjects,
    string VersionJsonPath,
    string? ClientJarPath)
{
    public bool Success => Failed == 0;
}

/// <summary>
/// M1 的门面：取清单 → 取 version.json → 下 client/库/natives/资源 → 解压 natives。
/// 分两阶段执行，因为资产对象必须先把索引下下来才知道有哪些文件。
/// </summary>
public sealed class VersionFetcher
{
    private readonly DataLayout _layout;
    private readonly FileDownloader _downloader;
    private readonly VersionCatalog _catalog;
    private readonly DownloadPlanBuilder _planBuilder;

    public VersionFetcher(DataLayout layout, FileDownloader downloader)
    {
        _layout = layout;
        _downloader = downloader;
        _catalog = new VersionCatalog(layout, downloader);
        _planBuilder = new DownloadPlanBuilder(layout);
    }

    /// <summary>
    /// 确保实例的 natives 目录就绪：库 jar 已在共享目录里，这里只做解压。
    /// 返回本次解压的文件数（已存在会覆盖，代价很小）。
    /// </summary>
    public static int EnsureNatives(
        VersionMetadata version,
        LaunchEnvironment environment,
        DataLayout layout,
        string instanceId)
    {
        var plan = new DownloadPlanBuilder(layout).Build(version, environment, instanceId);
        var extracted = 0;
        foreach (var extraction in plan.Natives)
        {
            if (!File.Exists(extraction.JarPath))
            {
                continue;
            }

            extracted += NativesExtractor.Extract(extraction.JarPath, extraction.TargetDirectory, extraction.ExcludePrefixes);
        }

        return extracted;
    }

    /// <summary>只算计划、不下载（清单与 version.json 仍会按需获取/复用缓存）。</summary>
    public async Task<(VersionMetadata Version, DownloadPlan Plan)> BuildPlanAsync(
        string versionId,
        FetchOptions options,
        CancellationToken cancellationToken)
    {
        var manifest = await _catalog.GetManifestAsync(options.Refresh, cancellationToken).ConfigureAwait(false);
        var version = await _catalog.GetVersionAsync(versionId, manifest, options.Refresh, cancellationToken)
            .ConfigureAwait(false);
        var plan = _planBuilder.Build(version, LaunchEnvironment.Detect(), versionId, options.Selection);
        return (version, plan);
    }

    public async Task<FetchReport> FetchAsync(
        string versionId,
        FetchOptions options,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var (version, plan) = await BuildPlanAsync(versionId, options, cancellationToken).ConfigureAwait(false);
        var executor = new DownloadExecutor(_downloader, options.Concurrency);

        var baseReport = await executor.RunAsync(plan, progress, cancellationToken).ConfigureAwait(false);

        var failures = new List<DownloadFailure>(baseReport.Failures);
        var filesPlanned = baseReport.Total;
        var downloaded = baseReport.Downloaded;
        var skipped = baseReport.Skipped;
        var failed = baseReport.Failed;
        var bytes = baseReport.BytesDownloaded;

        var assetObjects = 0;
        if (options.Selection.Assets && version.AssetIndex is { } assetIndex)
        {
            var indexPath = _layout.IndexFile(assetIndex.Id ?? version.Assets ?? version.Id);
            if (File.Exists(indexPath))
            {
                var indexJson = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
                var index = AssetIndexParser.Parse(indexJson);
                var assetPlan = _planBuilder.BuildAssets(index);
                assetObjects = assetPlan.Files.Count;

                var assetReport = await executor.RunAsync(assetPlan, progress, cancellationToken).ConfigureAwait(false);
                filesPlanned += assetReport.Total;
                downloaded += assetReport.Downloaded;
                skipped += assetReport.Skipped;
                failed += assetReport.Failed;
                bytes += assetReport.BytesDownloaded;
                failures.AddRange(assetReport.Failures);
            }
            else
            {
                failures.Add(new DownloadFailure(indexPath, "资源索引未下载成功，资源对象未展开"));
                failed++;
            }
        }

        var nativesExtracted = 0;
        foreach (var extraction in plan.Natives)
        {
            if (!File.Exists(extraction.JarPath))
            {
                failures.Add(new DownloadFailure(extraction.JarPath, "natives jar 不存在，跳过解压"));
                failed++;
                continue;
            }

            try
            {
                nativesExtracted += NativesExtractor.Extract(
                    extraction.JarPath,
                    extraction.TargetDirectory,
                    extraction.ExcludePrefixes);
            }
            catch (InvalidDataException ex)
            {
                failures.Add(new DownloadFailure(extraction.JarPath, ex.Message));
                failed++;
            }
        }

        return new FetchReport(
            versionId,
            filesPlanned,
            downloaded,
            skipped,
            failed,
            bytes,
            failures,
            nativesExtracted,
            assetObjects,
            _layout.VersionJson(versionId),
            options.Selection.Client ? _layout.ClientJar(versionId) : null);
    }
}
