using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Downloads;

/// <summary>裁剪：哪些部分要下。<c>--only</c> 语义，默认全下。</summary>
public sealed record FetchSelection(bool Client = true, bool Libraries = true, bool Assets = true, bool Natives = true)
{
    public static FetchSelection All { get; } = new();
}

/// <summary>
/// 从 version.json 生成下载计划（纯函数，不碰网络）。资产对象需要先下索引再展开，
/// 所以资产部分由 <see cref="BuildAssets"/> 单独产出。
/// </summary>
public sealed class DownloadPlanBuilder(DataLayout layout)
{
    public DownloadPlan Build(
        VersionMetadata version,
        LaunchEnvironment environment,
        string instanceId,
        FetchSelection? selection = null)
    {
        selection ??= FetchSelection.All;
        var files = new List<DownloadJob>();
        var natives = new List<NativeExtraction>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(DownloadJob job)
        {
            if (seenTargets.Add(job.TargetPath))
            {
                files.Add(job);
            }
        }

        if (selection.Client && version.Client is { } client && !string.IsNullOrEmpty(client.Url))
        {
            var uri = new Uri(client.Url);
            Add(new DownloadJob
            {
                Primary = uri,
                Mirrors = Mirrors.Collect(Mirrors.ForClientJar(version.Id)),
                TargetPath = layout.ClientJar(version.Id),
                ExpectedSize = client.Size,
                ExpectedSha1 = client.Sha1,
            });
        }

        // 资源索引是资产展开的前置，只在需要资产时下载；日志配置很小，始终带上。
        if (selection.Assets && version.AssetIndex is { } assetIndex)
        {
            Add(new DownloadJob
            {
                Primary = new Uri(assetIndex.Url),
                TargetPath = layout.IndexFile(assetIndex.Id ?? version.Assets ?? version.Id),
                ExpectedSize = assetIndex.Size,
                ExpectedSha1 = assetIndex.Sha1,
            });
        }

        if (version.Logging?.File is { } logFile && !string.IsNullOrEmpty(logFile.Url))
        {
            var fileName = Path.GetFileName(new Uri(logFile.Url).AbsolutePath);
            Add(new DownloadJob
            {
                Primary = new Uri(logFile.Url),
                TargetPath = Path.Combine(layout.AssetsDir, "log_configs", fileName),
                ExpectedSize = logFile.Size,
                ExpectedSha1 = logFile.Sha1,
            });
        }

        foreach (var library in version.Libraries)
        {
            if (!library.IsAllowed(environment))
            {
                continue;
            }

            var coordinate = MavenCoordinate.Parse(library.Name);
            var relativePath = library.Artifact?.Path ?? coordinate.ToMavenPath();
            var targetPath = ToLocalPath(relativePath);

            // 旧版里存在"只有 natives、没有 artifact"的库（1.7.10 的 lwjgl-platform / jinput-platform 等）。
            var classifier = library.ResolveNativeClassifier(environment.OsName, environment.NativeArch);
            var nativeArtifact = classifier is not null && library.Classifiers.TryGetValue(classifier, out var found)
                ? found
                : null;

            if (selection.Libraries)
            {
                if (library.Artifact is { } artifact)
                {
                    Add(new DownloadJob
                    {
                        Primary = new Uri(artifact.Url),
                        Mirrors = Mirrors.Collect(Mirrors.ForLibrary(new Uri(artifact.Url))),
                        TargetPath = targetPath,
                        ExpectedSize = artifact.Size,
                        ExpectedSha1 = artifact.Sha1,
                    });
                }
                else if (library.BaseUrl is { } baseUrl)
                {
                    // 早期元数据只有 maven 基址：URL 自己拼，没有哈希可校验。
                    Add(new DownloadJob
                    {
                        Primary = new Uri(baseUrl.TrimEnd('/') + "/" + relativePath),
                        TargetPath = targetPath,
                    });
                }
                else if (nativeArtifact is null)
                {
                    throw new MetadataFormatException(
                        "$.libraries",
                        $"库 '{library.Name}' 既没有 downloads.artifact、也没有 url、当前平台也没有 natives");
                }
            }

            if (!selection.Natives || nativeArtifact is null || classifier is null)
            {
                continue;
            }

            var nativeRelative = nativeArtifact.Path ?? coordinate.ToMavenPath(classifier);
            var nativeTarget = ToLocalPath(nativeRelative);
            Add(new DownloadJob
            {
                Primary = new Uri(nativeArtifact.Url),
                Mirrors = Mirrors.Collect(Mirrors.ForLibrary(new Uri(nativeArtifact.Url))),
                TargetPath = nativeTarget,
                ExpectedSize = nativeArtifact.Size,
                ExpectedSha1 = nativeArtifact.Sha1,
            });

            natives.Add(new NativeExtraction(
                nativeTarget,
                layout.NativeDir(instanceId),
                library.ExtractExclude));
        }

        return new DownloadPlan(files, natives);
    }

    /// <summary>把资源索引展开成对象下载计划（按哈希去重）。</summary>
    public DownloadPlan BuildAssets(AssetIndexDocument index)
    {
        var files = new List<DownloadJob>();
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, asset) in index.Objects)
        {
            if (!seenHashes.Add(asset.Hash))
            {
                continue;
            }

            var uri = new Uri($"https://resources.download.minecraft.net/{asset.Hash[..2]}/{asset.Hash}");
            files.Add(new DownloadJob
            {
                Primary = uri,
                Mirrors = Mirrors.Collect(Mirrors.ForAsset(uri)),
                TargetPath = layout.AssetObjectPath(asset.Hash),
                ExpectedSize = asset.Size,
                ExpectedSha1 = asset.Hash,
            });
        }

        return new DownloadPlan(files, []);
    }

    private string ToLocalPath(string mavenPath)
        => Path.Combine(layout.LibrariesDir, mavenPath.Replace('/', Path.DirectorySeparatorChar));
}
