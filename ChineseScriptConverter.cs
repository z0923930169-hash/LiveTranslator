using System.Runtime.InteropServices;
using System.Text;

namespace LiveTranslator;

public static class ChineseScriptConverter
{
    private const uint LCMAP_TRADITIONAL_CHINESE = 0x04000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(
        string? lpLocaleName,
        uint dwMapFlags,
        string lpSrcStr,
        int cchSrc,
        StringBuilder lpDestStr,
        int cchDest,
        IntPtr lpVersionInformation,
        IntPtr lpReserved,
        IntPtr sortHandle);

    public static string ToTraditionalTaiwan(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var buffer = new StringBuilder(text.Length * 2 + 16);

        int written = LCMapStringEx(
            "zh-TW",
            LCMAP_TRADITIONAL_CHINESE,
            text,
            text.Length,
            buffer,
            buffer.Capacity,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        return written > 0 ? buffer.ToString() : text;
    }
}
