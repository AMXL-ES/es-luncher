using System.Text.Json;
using System.Text.Json.Nodes;
using EsLuncher.Core.Content;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Content;

public sealed record ModrinthHit(string ProjectId, string Slug, string Title, string Description, long Downloads);

public sealed record ModrinthFile(string Url, string Filename, string Sha1, bool Primary, long Size);

public sealed record ModrinthVersion(string Id, string VersionNumber, IReadOnlyList<ModrinthFile> Files);

/// <summary>
/// Modrinth 只读 API：检索与按"MC 版本 + 加载器"找可装版本。
/// 只依赖 IRemoteFileClient，便于单测与以后换源。
/// </summary>
public sealed class ModrinthClient(IRemoteFileClient client)
{
    private const string BaseUrl = "https://api.modrinth.com/v2";

    public static Uri BuildSearchUri(
        string query,
        string? mcVersion,
        string? loader,
        int limit,
        ContentKind kind = ContentKind.Mods,
        string? projectTypeOverride = null)
    {
        var facets = new List<string> { $"[\"project_type:{projectTypeOverride ?? ProjectType(kind)}\"]" };
        if (!string.IsNullOrWhiteSpace(mcVersion))
        {
            facets.Add($"[\"versions:{mcVersion}\"]");
        }

        if (kind == ContentKind.Mods && !string.IsNullOrWhiteSpace(loader))
        {
            facets.Add($"[\"categories:{loader}\"]");
        }

        var facetsPart = facets.Count == 0
            ? string.Empty
            : "&facets=" + Uri.EscapeDataString("[" + string.Join(",", facets) + "]");
        return new Uri($"{BaseUrl}/search?query={Uri.EscapeDataString(query)}&limit={limit}{facetsPart}");
    }

    public static string ProjectType(ContentKind kind) => kind switch
    {
        ContentKind.Mods => "mod",
        ContentKind.ResourcePacks => "resourcepack",
        ContentKind.ShaderPacks => "shader",
        ContentKind.Datapacks => "datapack",
        _ => throw new NotSupportedException($"{kind} 不支持从 Modrinth 安装"),
    };

    public static Uri BuildVersionsUri(string project, string? mcVersion, string? loader)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(mcVersion))
        {
            query.Add("game_versions=" + Uri.EscapeDataString($"[\"{mcVersion}\"]"));
        }

        if (!string.IsNullOrWhiteSpace(loader))
        {
            query.Add("loaders=" + Uri.EscapeDataString($"[\"{loader}\"]"));
        }

        var suffix = query.Count == 0 ? string.Empty : "?" + string.Join("&", query);
        return new Uri($"{BaseUrl}/project/{Uri.EscapeDataString(project)}/version{suffix}");
    }

    public static IReadOnlyList<ModrinthHit> ParseSearch(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<ModrinthHit>();
        foreach (var hit in hits.EnumerateArray())
        {
            results.Add(new ModrinthHit(
                hit.TryGetProperty("project_id", out var projectId) ? projectId.GetString() ?? string.Empty : string.Empty,
                hit.TryGetProperty("slug", out var slug) ? slug.GetString() ?? string.Empty : string.Empty,
                hit.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty,
                hit.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty,
                hit.TryGetProperty("downloads", out var downloads) && downloads.ValueKind == JsonValueKind.Number
                    ? downloads.GetInt64()
                    : 0));
        }

        return results;
    }

    public static IReadOnlyList<ModrinthVersion> ParseVersions(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var versions = new List<ModrinthVersion>();
        foreach (var version in document.RootElement.EnumerateArray())
        {
            var files = new List<ModrinthFile>();
            if (version.TryGetProperty("files", out var fileArray) && fileArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var file in fileArray.EnumerateArray())
                {
                    files.Add(new ModrinthFile(
                        file.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
                        file.TryGetProperty("filename", out var filename) ? filename.GetString() ?? string.Empty : string.Empty,
                        file.TryGetProperty("hashes", out var hashes) && hashes.TryGetProperty("sha1", out var sha1)
                            ? sha1.GetString() ?? string.Empty
                            : string.Empty,
                        file.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True,
                        file.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0));
                }
            }

            versions.Add(new ModrinthVersion(
                version.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                version.TryGetProperty("version_number", out var number) ? number.GetString() ?? string.Empty : string.Empty,
                files));
        }

        return versions;
    }

    public async Task<IReadOnlyList<ModrinthHit>> SearchAsync(
        string query,
        string? mcVersion,
        string? loader,
        int limit,
        CancellationToken cancellationToken,
        ContentKind kind = ContentKind.Mods,
        string? projectTypeOverride = null)
    {
        var json = await GetStringAsync(
                BuildSearchUri(query, mcVersion, loader, limit, kind, projectTypeOverride),
                cancellationToken)
            .ConfigureAwait(false);
        return ParseSearch(json);
    }

    public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
        string project,
        string? mcVersion,
        string? loader,
        CancellationToken cancellationToken)
    {
        var json = await GetStringAsync(BuildVersionsUri(project, mcVersion, loader), cancellationToken).ConfigureAwait(false);
        return ParseVersions(json);
    }

    /// <summary>下载某个项目最新的匹配版本的主文件，装到实例对应目录，返回落盘文件名。</summary>
    public async Task<string> InstallAsync(
        InstanceContentService content,
        string instanceName,
        string project,
        string? mcVersion,
        string? loader,
        CancellationToken cancellationToken,
        ContentKind kind = ContentKind.Mods,
        string? world = null)
    {
        var versions = await GetVersionsAsync(project, mcVersion, loader, cancellationToken).ConfigureAwait(false);
        var version = versions.FirstOrDefault() ?? throw new InvalidOperationException($"Modrinth 上没有匹配的版本：{project}");
        var file = version.Files.FirstOrDefault(candidate => candidate.Primary) ?? version.Files.FirstOrDefault()
            ?? throw new InvalidOperationException($"版本 {version.VersionNumber} 没有可下载文件");

        var targetDirectory = content.DirectoryFor(instanceName, kind, world);
        Directory.CreateDirectory(targetDirectory);
        var target = Path.Combine(targetDirectory, file.Filename);
        await DownloadToAsync(new Uri(file.Url), target, cancellationToken).ConfigureAwait(false);
        return file.Filename;
    }

    private async Task DownloadToAsync(Uri uri, string target, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, 0, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Content is null)
        {
            throw new HttpRequestException($"下载失败 HTTP {response.StatusCode}：{uri}");
        }

        var temporary = target + ".part";
        await using (var file = File.Create(temporary))
        {
            await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, target, overwrite: true);
    }

    private async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, 0, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Content is null)
        {
            throw new HttpRequestException($"Modrinth 请求失败 HTTP {response.StatusCode}：{uri}");
        }

        using var reader = new StreamReader(response.Content);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
