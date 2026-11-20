namespace EsLuncher.Core.Downloads;

/// <summary>
/// 数据目录布局。全部落在用户指定的数据根（默认 <c>D:\mc-data</c>），
/// 绝不写用户自己的 <c>.minecraft</c>。
/// </summary>
public sealed class DataLayout
{
    public DataLayout(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string ManifestFile => Path.Combine(Root, "version_manifest_v2.json");

    public string LibrariesDir => Path.Combine(Root, "libraries");

    public string AssetsDir => Path.Combine(Root, "assets");

    public string IndexesDir => Path.Combine(AssetsDir, "indexes");

    public string ObjectsDir => Path.Combine(AssetsDir, "objects");

    public string VersionsDir => Path.Combine(Root, "versions");

    public string InstancesDir => Path.Combine(Root, "instances");

    public string LogsDir => Path.Combine(Root, "logs");

    public string AuthDir => Path.Combine(Root, "auth");

    public string AuthAccountsDir => Path.Combine(AuthDir, "accounts");

    /// <summary>Mojang 官方运行时（按组件/版本分目录，与系统 JDK 隔离）。</summary>
    public string JavaDir => Path.Combine(Root, "java");

    public string InstanceDir(string instanceId) => Path.Combine(InstancesDir, instanceId);

    /// <summary>游戏工作目录（<c>--gameDir</c>），与 natives 目录分开。</summary>
    public string GameDir(string instanceId) => Path.Combine(InstanceDir(instanceId), "game");

    public string VersionDir(string versionId) => Path.Combine(VersionsDir, versionId);

    public string VersionJson(string versionId) => Path.Combine(VersionDir(versionId), versionId + ".json");

    public string ClientJar(string versionId) => Path.Combine(VersionDir(versionId), versionId + ".jar");

    public string IndexFile(string indexId) => Path.Combine(IndexesDir, indexId + ".json");

    public string NativeDir(string instanceId) => Path.Combine(InstancesDir, instanceId, "natives");

    /// <summary>资源对象按哈希前两位分目录（与官方启动器一致）。</summary>
    public string AssetObjectPath(string hash) => Path.Combine(ObjectsDir, hash[..2], hash);
}
