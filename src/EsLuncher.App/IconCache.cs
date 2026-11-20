using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using EsLuncher.Core.Downloads;

namespace EsLuncher.App;

/// <summary>图标缓存：Modrinth 项目图标 / 玩家皮肤头，下载到 数据根/cache/icons 并做内存缓存。</summary>
public static class IconCache
{
    private static readonly Dictionary<string, Bitmap?> Memory = new(StringComparer.OrdinalIgnoreCase);

    public static void ClearAll()
    {
        Memory.Clear();
        try
        {
            var directory = Path.Combine(AppServices.Layout.Root, "cache", "icons");
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
    }

    public static async Task<Bitmap?> GetAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (Memory.TryGetValue(url, out var cached))
        {
            return cached;
        }

        try
        {
            var directory = Path.Combine(AppServices.Layout.Root, "cache", "icons");
            Directory.CreateDirectory(directory);
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();
            var path = Path.Combine(directory, hash + ".img");

            if (!File.Exists(path))
            {
                using var client = new HttpRemoteFileClient();
                using var response = await client.GetAsync(new Uri(url), 0, CancellationToken.None);
                if (!response.IsSuccess || response.Content is null)
                {
                    Memory[url] = null;
                    return null;
                }

                var partial = path + ".part";
                await using (var file = File.Create(partial))
                {
                    await response.Content.CopyToAsync(file, CancellationToken.None);
                }

                File.Move(partial, path, overwrite: true);
            }

            var bitmap = new Bitmap(path);
            Memory[url] = bitmap;
            return bitmap;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or ArgumentException)
        {
            Memory[url] = null;
            return null;
        }
    }
}
