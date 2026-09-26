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

    public static async Task<int> RunEngineSmokeAsync()
    {
        string reportPath =
            Path.Combine(AppContext.BaseDirectory, "engine-smoke-report.txt");

        var report = new StringBuilder();
        LocalAiEngine? engine = null;

        try
        {
            NativeRuntimeBootstrap.Configure();

            report.AppendLine("LiveTranslator full local-engine smoke test");
            report.AppendLine("Model directory: " +
                (Environment.GetEnvironmentVariable("LIVETRANSLATOR_MODEL_DIR") ?? "(default)"));

            var models = new ModelManager();
            int lastPercent = -10;

            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                if (p.Percent >= lastPercent + 10 || p.Percent == 100)
                {
                    lastPercent = p.Percent;
                    report.AppendLine(
                        $"DOWNLOAD {p.Name}: {p.Percent}% ({p.Received} / {p.Total?.ToString() ?? "?"})");
                }
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));

            report.AppendLine("STEP: Ensure models");
            await models.EnsureModelsAsync(progress, cts.Token);

            if (!File.Exists(models.WhisperPath))
                throw new FileNotFoundException("Whisper model missing after download.", models.WhisperPath);

            if (!File.Exists(models.TranslationPath))
                throw new FileNotFoundException("Qwen model missing after download.", models.TranslationPath);

            report.AppendLine($"Whisper model bytes: {new FileInfo(models.WhisperPath).Length}");
            report.AppendLine($"Qwen model bytes: {new FileInfo(models.TranslationPath).Length}");

            engine = new LocalAiEngine(models);
            engine.Status += x => report.AppendLine("STATUS: " + x);
            engine.Error += x => report.AppendLine("ENGINE ERROR EVENT: " + x);

            report.AppendLine("STEP: Initialize Whisper + Qwen");
            await engine.InitializeAsync(cts.Token);

            report.AppendLine("STEP: Run real Whisper processor");
            await engine.VerifySpeechPipelineAsync(cts.Token);
            report.AppendLine("Whisper processing completed.");

            report.AppendLine("STEP: Run real Qwen translation");
            string translated = await engine.TranslateTextAsync(
                "Hello, world! This is a local translation test.",
                "Traditional Chinese (Taiwan)",
                cts.Token);

            report.AppendLine("Translation output: " + translated);

            if (string.IsNullOrWhiteSpace(translated))
                throw new InvalidOperationException("Qwen returned an empty translation.");

            if (string.Equals(
                translated.Trim(),
                "Hello, world! This is a local translation test.",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Qwen returned the source text unchanged.");
            }

            report.AppendLine();
            report.AppendLine("RESULT: PASS");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
            return 0;
        }
        catch (Exception ex)
        {
            report.AppendLine();
            report.AppendLine("RESULT: FAIL");
            report.AppendLine(ex.ToString());
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
            return 5;
        }
        finally
        {
            if (engine != null)
            {
                try { await engine.DisposeAsync(); } catch { }
            }
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
