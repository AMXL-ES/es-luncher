using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Loaders;

/// <summary>
/// <c>inheritsFrom</c> 合并：加载器版本 JSON 只写"增量"，其余继承原版。
/// 规则（与官方启动器/Prism 一致）：库按 parent → child 顺序拼接，主类 child 覆盖，
/// 参数 parent 在前 child 在后，缺失字段（assets/assetIndex/javaVersion/logging/client）继承 parent。
/// </summary>
public static class VersionMerger
{
    public static VersionMetadata Merge(VersionMetadata parent, VersionMetadata child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);

        return new VersionMetadata
        {
            Id = child.Id,
            Type = child.Type,
            MainClass = string.IsNullOrWhiteSpace(child.MainClass) ? parent.MainClass : child.MainClass,
            InheritsFrom = null,
            Assets = child.Assets ?? parent.Assets,
            AssetIndex = child.AssetIndex ?? parent.AssetIndex,
            JavaVersion = child.JavaVersion ?? parent.JavaVersion,
            LegacyMinecraftArguments = child.LegacyMinecraftArguments ?? parent.LegacyMinecraftArguments,
            Arguments = MergeArguments(parent.Arguments, child.Arguments),
            Libraries = [.. parent.Libraries, .. child.Libraries],
            Client = child.Client ?? parent.Client,
            Server = child.Server ?? parent.Server,
            Logging = child.Logging ?? parent.Logging,
            MinimumLauncherVersion = child.MinimumLauncherVersion ?? parent.MinimumLauncherVersion,
            ComplianceLevel = child.ComplianceLevel ?? parent.ComplianceLevel,
            ReleaseTime = child.ReleaseTime ?? parent.ReleaseTime,
            ClientJarVersionId = child.ClientJarVersionId ?? parent.ClientJarVersionId ?? parent.Id,
        };
    }

    /// <summary>按 <paramref name="loadParent"/> 展开 <c>inheritsFrom</c> 链并逐级合并，返回最终元数据。</summary>
    public static VersionMetadata Resolve(VersionMetadata version, Func<string, VersionMetadata> loadParent, int maxDepth = 10)
    {
        var chain = new List<VersionMetadata> { version };
        var current = version;
        var depth = 0;
        while (!string.IsNullOrWhiteSpace(current.InheritsFrom))
        {
            if (++depth > maxDepth)
            {
                throw new InvalidOperationException($"inheritsFrom 链过深或成环：{version.Id}");
            }

            current = loadParent(current.InheritsFrom!)
                ?? throw new InvalidOperationException($"找不到父版本：{current.InheritsFrom}");
            chain.Add(current);
        }

        var merged = chain[^1];
        for (var index = chain.Count - 2; index >= 0; index--)
        {
            merged = Merge(merged, chain[index]);
        }

        return merged;
    }

    private static Arguments? MergeArguments(Arguments? parent, Arguments? child)
    {
        if (parent is null)
        {
            return child;
        }

        if (child is null)
        {
            return parent;
        }

        return new Arguments([.. parent.Game, .. child.Game], [.. parent.Jvm, .. child.Jvm]);
    }
}
