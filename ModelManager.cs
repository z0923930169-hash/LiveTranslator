namespace LiveTranslator;

public sealed record ModelDownloadProgress(string Name, long Received, long? Total)
{
    public int Percent => Total is > 0
        ? (int)Math.Clamp(Received * 100L / Total.Value, 0, 100)
        : 0;
}

public sealed class ModelManager
{
    private static readonly HttpClient Http = CreateClient();

    public string ModelDirectory { get; } = ResolveModelDirectory();

    private static string ResolveModelDirectory()
    {
        string? overrideDirectory =
            Environment.GetEnvironmentVariable("LIVETRANSLATOR_MODEL_DIR");

        if (!string.IsNullOrWhiteSpace(overrideDirectory))
            return Path.GetFullPath(overrideDirectory);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveTranslator",
            "models");
    }

    public string WhisperPath =>
        Path.Combine(ModelDirectory, "ggml-base.bin");

    public string TranslationPath =>
        Path.Combine(ModelDirectory, "Qwen3-1.7B-Q4_K_M.gguf");

    private const string WhisperUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin?download=true";

    private const string TranslationUrl =
        "https://huggingface.co/ggml-org/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q4_K_M.gguf?download=true";

    public bool ModelsReady =>
        File.Exists(WhisperPath) &&
        File.Exists(TranslationPath);

    public async Task EnsureModelsAsync(
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(ModelDirectory);

        if (!File.Exists(WhisperPath))
        {
            await DownloadAsync(
                "Whisper 多語言語音模型",
                WhisperUrl,
                WhisperPath,
                progress,
                ct);
        }

        if (!File.Exists(TranslationPath))
        {
            await DownloadAsync(
                "Qwen3 1.7B 本機翻譯模型",
                TranslationUrl,
                TranslationPath,
                progress,
                ct);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("LiveTranslator/1.1");
        return client;
    }

    private static async Task DownloadAsync(
        string name,
        string url,
        string destination,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        string temp = destination + ".download";

        try
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch
                {
                    await Task.Delay(500, ct);
                    File.Delete(temp);
                }
            }

            using var response = await Http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;

            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(
                temp,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[1024 * 1024];
                long received = 0;

                while (true)
                {
                    int read = await input.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        ct);

                    if (read <= 0)
                        break;

                    await output.WriteAsync(
                        buffer.AsMemory(0, read),
                        ct);

                    received += read;

                    progress?.Report(
                        new ModelDownloadProgress(
                            name,
                            received,
                            total));
                }

                await output.FlushAsync(ct);
            }

            // Important: the temp file must be fully closed before Windows can rename it.
            GC.Collect();
            GC.WaitForPendingFinalizers();

            Exception? lastError = null;

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    File.Move(temp, destination, true);
                    lastError = null;
                    break;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                    await Task.Delay(350 * attempt, ct);
                }
            }

            if (lastError != null)
                throw lastError;
        }
        catch
        {
            try
            {
                if (File.Exists(temp))
                {
                    await Task.Delay(200);
                    File.Delete(temp);
                }
            }
            catch { }

            throw;
        }
    }
}
