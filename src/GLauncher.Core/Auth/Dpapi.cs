using System.Runtime.InteropServices;
using System.Text;

namespace GLauncher.Core.Auth;

/// <summary>
/// Windows DPAPI（<c>CryptProtectData</c>）。凭据只以密文落盘，且只能用当前用户解开。
/// 非 Windows 平台返回明确的"不支持"，不偷偷降级成明文。
/// </summary>
public static class Dpapi
{
    private const int CryptProtectUiForbidden = 0x1;

    public static byte[] Protect(byte[] plaintext)
    {
        EnsureWindows();
        return Transform(plaintext, protect: true);
    }

    public static byte[] Unprotect(byte[] ciphertext)
    {
        EnsureWindows();
        return Transform(ciphertext, protect: false);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI 仅 Windows 可用；其他平台需要 keyring 实现（M3 之后再做）");
        }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var inputBlob = new DataBlob { cbData = input.Length, pbData = inputHandle.AddrOfPinnedObject() };
        DataBlob outputBlob;
        try
        {
            var ok = protect
                ? CryptProtectData(ref inputBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob);

            if (!ok)
            {
                throw new AuthenticationException(
                    $"DPAPI 调用失败：{Marshal.GetLastWin32Error()}",
                    "dpapi");
            }
        }
        finally
        {
            inputHandle.Free();
        }

        try
        {
            var result = new byte[outputBlob.cbData];
            Marshal.Copy(outputBlob.pbData, result, 0, outputBlob.cbData);
            return result;
        }
        finally
        {
            LocalFree(outputBlob.pbData);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn, string? description, IntPtr optionalEntropy, IntPtr reserved, IntPtr promptStruct, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn, IntPtr description, IntPtr optionalEntropy, IntPtr reserved, IntPtr promptStruct, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    internal static string ToUtf8(byte[] data) => Encoding.UTF8.GetString(data);
}
