using System.Runtime.Intrinsics.X86;
using Whisper.net.LibraryLoader;

namespace LiveTranslator;

public static class NativeRuntimeBootstrap
{
    public static string BaseDirectory => AppContext.BaseDirectory;

    public static string WhisperRuntimeDirectory =>
        Path.Combine(BaseDirectory, "runtimes", "win-x64");

    public static string WhisperLibraryPath =>
        Path.Combine(WhisperRuntimeDirectory, "whisper.dll");

    public static string LlamaRuntimeDirectory
    {
        get
        {
            string nativeRoot =
                Path.Combine(BaseDirectory, "runtimes", "win-x64", "native");

            if (Avx2.IsSupported)
                return Path.Combine(nativeRoot, "avx2");

            if (Avx.IsSupported)
                return Path.Combine(nativeRoot, "avx");

            return Path.Combine(nativeRoot, "noavx");
        }
    }

    public static void Configure()
    {
        // Whisper.net supports an explicit full path to the native library.
        // Setting it here avoids relying on the current working directory.
        if (File.Exists(WhisperLibraryPath))
            RuntimeOptions.LibraryPath = WhisperLibraryPath;

        PrependPath(WhisperRuntimeDirectory);
        PrependPath(LlamaRuntimeDirectory);
    }

    private static void PrependPath(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        string current = Environment.GetEnvironmentVariable("PATH") ?? "";
        string normalized = directory.TrimEnd(Path.DirectorySeparatorChar);

        bool alreadyPresent = current
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(x => string.Equals(
                x.TrimEnd(Path.DirectorySeparatorChar),
                normalized,
                StringComparison.OrdinalIgnoreCase));

        if (!alreadyPresent)
        {
            Environment.SetEnvironmentVariable(
                "PATH",
                normalized + Path.PathSeparator + current);
        }
    }
}
