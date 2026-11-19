using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GLauncher.Core.Launching;

/// <summary>一个已探测到、可用的 Java 运行时。</summary>
public sealed record JavaRuntime(string JavaExePath, int MajorVersion, string VersionText)
{
    public static JavaRuntime? TryProbe(string javaExePath)
    {
        if (!File.Exists(javaExePath))
        {
            return null;
        }

        try
        {
            var result = ProcessRunner
                .RunCapturedAsync(javaExePath, ["-version"], TimeSpan.FromSeconds(15), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var text = string.Join('\n', result.StandardError, result.StandardOutput).Trim();
            var major = JavaVersionParser.TryParseMajor(text);
            return major is null ? null : new JavaRuntime(javaExePath, major.Value, text);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}

public static class JavaVersionParser
{
    private static readonly Regex QuotedVersion = new(@"version\s+""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LooseVersion = new(@"\b(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant);

    public static int? TryParseMajor(string javaVersionOutput)
    {
        if (string.IsNullOrWhiteSpace(javaVersionOutput))
        {
            return null;
        }

        var quoted = QuotedVersion.Match(javaVersionOutput);
        if (quoted.Success)
        {
            return ParseVersionString(quoted.Groups[1].Value);
        }

        var loose = LooseVersion.Match(javaVersionOutput);
        return loose.Success ? ParseParts(loose.Groups[1].Value, loose.Groups[2].Value) : null;
    }

    private static int? ParseVersionString(string version)
    {
        var parts = version.Split('.', '_', '-', '+');
        if (!int.TryParse(parts[0], out var first))
        {
            return null;
        }

        // 1.8.0_431 → 8；17.0.12 → 17；21.0.13 → 21
        if (first != 1)
        {
            return first;
        }

        return parts.Length > 1 && int.TryParse(parts[1], out var second) ? second : null;
    }

    private static int ParseParts(string major, string minor)
        => int.Parse(major) == 1 && int.TryParse(minor, out var m) ? m : int.Parse(major);
}

/// <summary>
/// 发现本机 Java。**绝不扫描 PATH**：本机 PATH 上的 java 是坏的（Oracle javapath 会崩），
/// 只认 JAVA_HOME 与常见安装目录，并逐个实测版本。
/// </summary>
public static class JavaDiscovery
{
    private static readonly string[] DefaultRoots =
    [
        @"C:\Program Files\Microsoft",
        @"C:\Program Files\Java",
        @"C:\Program Files\Eclipse Adoptium",
        @"C:\Program Files\Zulu",
        @"C:\Program Files\BellSoft",
        @"C:\Program Files\Amazon Corretto",
        @"D:\dev",
    ];

    public static IReadOnlyList<JavaRuntime> Discover(IEnumerable<string>? extraRoots = null)
    {
        var candidates = new List<string>();

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));
        }

        foreach (var root in DefaultRoots.Concat(extraRoots ?? []))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (var directory in Directory.EnumerateDirectories(root))
                {
                    candidates.Add(Path.Combine(directory, "bin", "java.exe"));
                }
            }
            catch (IOException)
            {
                // 无权限/被占用就跳过
            }
        }

        var runtimes = new List<JavaRuntime>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!seen.Add(candidate) || !File.Exists(candidate))
            {
                continue;
            }

            var runtime = JavaRuntime.TryProbe(candidate);
            if (runtime is not null)
            {
                runtimes.Add(runtime);
            }
        }

        return runtimes.OrderByDescending(runtime => runtime.MajorVersion).ToArray();
    }
}

/// <summary>选 Java 的结果：要么给出运行时，要么给出给人看的解释。</summary>
public sealed record JavaSelection(JavaRuntime? Runtime, string Message)
{
    public bool IsSatisfied => Runtime is not null;
}

public static class JavaSelector
{
    /// <summary>
    /// 严格按 version.json 的 <c>javaVersion.majorVersion</c> 要求匹配；
    /// 找不到就返回解释（M4 才会做"按需下载运行时"）。
    /// </summary>
    public static JavaSelection Select(
        int requiredMajor,
        IReadOnlyList<JavaRuntime> installed,
        JavaRuntime? overridden = null)
    {
        if (overridden is not null)
        {
            var warning = overridden.MajorVersion == requiredMajor
                ? string.Empty
                : $"（警告：该版本要求 Java {requiredMajor}，你指定的是 Java {overridden.MajorVersion}）";
            return new JavaSelection(overridden, $"使用指定的 Java {overridden.MajorVersion}: {overridden.JavaExePath}{warning}");
        }

        var exact = installed.FirstOrDefault(runtime => runtime.MajorVersion == requiredMajor);
        if (exact is not null)
        {
            return new JavaSelection(exact, $"匹配 Java {requiredMajor}: {exact.JavaExePath}");
        }

        var available = installed.Count == 0
            ? "未发现任何可用 Java（PATH 上的 java 不可信，已被忽略）"
            : "本机可用: " + string.Join(", ", installed.Select(r => $"Java {r.MajorVersion}"));

        return new JavaSelection(null, $"这个版本需要 Java {requiredMajor}，但本机没有。{available}。请安装对应版本，或用 --java 指定。");
    }
}
