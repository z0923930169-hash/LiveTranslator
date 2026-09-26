using System.Runtime.InteropServices;
using System.Text;

namespace LiveTranslator;

public static class SmokeTests
{
    public static int RunNativeSmoke()
    {
        string reportPath = Path.Combine(AppContext.BaseDirectory, "smoke-report.txt");
        var report = new StringBuilder();

        try
        {
            NativeRuntimeBootstrap.Configure();

            report.AppendLine("LiveTranslator native smoke test");
            report.AppendLine("Base: " + AppContext.BaseDirectory);
            report.AppendLine("OS: " + Environment.OSVersion);
            report.AppendLine("64-bit process: " + Environment.Is64BitProcess);
            report.AppendLine();

            var whisperFiles = new[]
            {
                "ggml-base-whisper.dll",
                "ggml-cpu-whisper.dll",
                "ggml-whisper.dll",
                "whisper.dll"
            };

            var llamaFiles = new[]
            {
                "ggml-base.dll",
                "ggml.dll",
                "ggml-cpu.dll",
                "llama.dll"
            };

            var handles = new List<IntPtr>();

            try
            {
                report.AppendLine("[Whisper]");
                LoadLibraries(
                    NativeRuntimeBootstrap.WhisperRuntimeDirectory,
                    whisperFiles,
                    handles,
                    report);

                report.AppendLine();
                report.AppendLine("[Qwen / llama.cpp]");
                report.AppendLine(
                    "Selected native folder: " +
                    NativeRuntimeBootstrap.LlamaRuntimeDirectory);

                LoadLibraries(
                    NativeRuntimeBootstrap.LlamaRuntimeDirectory,
                    llamaFiles,
                    handles,
                    report);

                report.AppendLine();
                report.AppendLine("RESULT: PASS");
                File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
                return 0;
            }
            finally
            {
                for (int i = handles.Count - 1; i >= 0; i--)
                {
                    try { NativeLibrary.Free(handles[i]); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            report.AppendLine();
            report.AppendLine("RESULT: FAIL");
            report.AppendLine(ex.ToString());
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
            return 2;
        }
    }

    private static void LoadLibraries(
        string directory,
        IEnumerable<string> files,
        List<IntPtr> handles,
        StringBuilder report)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(
                "Native runtime directory missing: " + directory);

        foreach (string file in files)
        {
            string path = Path.Combine(directory, file);

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "Native DLL missing: " + path);

            IntPtr handle = NativeLibrary.Load(path);
            handles.Add(handle);

            report.AppendLine("PASS " + path);
        }
    }

    [STAThread]
    public static int RunUiSmoke()
    {
        string marker =
            Path.Combine(AppContext.BaseDirectory, "ui-smoke-report.txt");

        try
        {
            using var form = new MainForm();

            form.Shown += (_, __) =>
            {
                File.WriteAllText(
                    marker,
                    "RESULT: PASS" + Environment.NewLine +
                    "MainForm created and shown successfully.",
                    Encoding.UTF8);

                var timer = new System.Windows.Forms.Timer
                {
                    Interval = 1000
                };

                timer.Tick += (_, __) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    form.Close();
                };

                timer.Start();
            };

            Application.Run(form);

            return File.Exists(marker) ? 0 : 3;
        }
        catch (Exception ex)
        {
            File.WriteAllText(
                marker,
                "RESULT: FAIL" + Environment.NewLine + ex,
                Encoding.UTF8);

            return 4;
        }
    }
}
