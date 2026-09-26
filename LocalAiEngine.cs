using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Whisper.net;

namespace LiveTranslator;

public sealed class LocalAiEngine : IAsyncDisposable
{
    private readonly ModelManager models;
    private WhisperFactory? whisperFactory;
    private LLamaWeights? weights;
    private StatelessExecutor? executor;
    private Channel<AudioJob>? audioQueue;
    private CancellationTokenSource? workerCts;
    private Task? workerTask;
    private readonly SemaphoreSlim llmLock = new(1, 1);
    private bool initialized;

    private sealed record AudioJob(byte[] Pcm16, string TargetLanguage);

    public event Action<string>? Recognized;
    public event Action<string>? TranslationReady;
    public event Action<string>? Status;
    public event Action<string>? Error;

    public LocalAiEngine(ModelManager models)
    {
        this.models = models;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (initialized) return;

        Status?.Invoke("正在載入 Whisper 語音辨識模型…");
        whisperFactory = WhisperFactory.FromPath(models.WhisperPath);

        Status?.Invoke("正在載入 Qwen3 本機翻譯模型…第一次可能要等一下。");

        var parameters = new ModelParams(models.TranslationPath)
        {
            ContextSize = 2048,
            GpuLayerCount = 0
        };

        weights = await Task.Run(() => LLamaWeights.LoadFromFile(parameters), ct);

        executor = new StatelessExecutor(weights, parameters)
        {
            ApplyTemplate = true,
            SystemMessage =
                "You are a translation engine. Translate accurately and naturally. " +
                "Preserve names, numbers, punctuation, and the tone of dialogue. " +
                "Never add explanations, commentary, labels, or quotation marks. " +
                "Return only the translation."
        };

        audioQueue = Channel.CreateBounded<AudioJob>(
            new BoundedChannelOptions(3)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        workerCts = new CancellationTokenSource();
        workerTask = Task.Run(() => AudioWorkerAsync(workerCts.Token));

        initialized = true;
        Status?.Invoke("本機 AI 已載入完成。");
    }

    public bool QueueAudio(byte[] pcm16, string targetLanguage)
    {
        if (!initialized || audioQueue == null) return false;
        return audioQueue.Writer.TryWrite(new AudioJob(pcm16, targetLanguage));
    }

    private async Task AudioWorkerAsync(CancellationToken ct)
    {
        if (audioQueue == null) return;

        try
        {
            await foreach (var job in audioQueue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    string source = await RecognizeAsync(job.Pcm16, ct);
                    if (string.IsNullOrWhiteSpace(source)) continue;

                    Recognized?.Invoke(source);

                    Status?.Invoke("本機 AI 正在翻譯…");
                    string translated = await TranslateTextAsync(source, job.TargetLanguage, ct);

                    if (!string.IsNullOrWhiteSpace(translated))
                        TranslationReady?.Invoke(translated);

                    Status?.Invoke("翻譯中：正在等待下一段語音。");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Error?.Invoke("本機翻譯錯誤：" + ex.Message);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task<string> RecognizeAsync(byte[] pcm16, CancellationToken ct)
    {
        if (whisperFactory == null) throw new InvalidOperationException("Whisper 尚未載入。");
        if (IsMostlySilent(pcm16)) return "";

        using var processor = whisperFactory.CreateBuilder()
            .WithLanguage("auto")
            .Build();

        using var wav = CreateWavStream(pcm16, 16000);
        var text = new StringBuilder();

        await foreach (var segment in processor.ProcessAsync(wav))
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(segment.Text))
            {
                if (text.Length > 0) text.Append(' ');
                text.Append(segment.Text.Trim());
            }
        }

        return text.ToString().Trim();
    }

    public async Task<string> TranslateTextAsync(
        string sourceText,
        string targetLanguage,
        CancellationToken ct)
    {
        if (executor == null) throw new InvalidOperationException("Qwen 翻譯模型尚未載入。");
        if (string.IsNullOrWhiteSpace(sourceText)) return "";

        await llmLock.WaitAsync(ct);
        try
        {
            string prompt =
                $"Translate the following text into {targetLanguage}. " +
                "Keep proper nouns, game character names, numbers, punctuation, and subtitle tags intact. " +
                "Use natural language suitable for subtitles. Return only the translated text. " +
                "/no_think\n\n" + sourceText.Trim();

            var inference = new InferenceParams
            {
                MaxTokens = 220,
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.1f
                }
            };

            var output = new StringBuilder();

            await foreach (var piece in executor.InferAsync(prompt, inference, ct))
                output.Append(piece);

            return CleanTranslation(output.ToString());
        }
        finally
        {
            llmLock.Release();
        }
    }

    private static string CleanTranslation(string value)
    {
        string text = Regex.Replace(
            value,
            "<think>[\\s\\S]*?</think>",
            "",
            RegexOptions.IgnoreCase);

        text = Regex.Replace(
            text,
            "^\\s*(Translation|Translated text|Answer)\\s*:\\s*",
            "",
            RegexOptions.IgnoreCase);

        text = text.Trim();

        if (text.Length >= 2 &&
            ((text.StartsWith('"') && text.EndsWith('"')) ||
             (text.StartsWith('“') && text.EndsWith('”'))))
        {
            text = text[1..^1].Trim();
        }

        return text;
    }

    private static bool IsMostlySilent(byte[] pcm16)
    {
        if (pcm16.Length < 2) return true;

        double sum = 0;
        int count = pcm16.Length / 2;

        for (int i = 0; i < pcm16.Length - 1; i += 2)
        {
            short s = BitConverter.ToInt16(pcm16, i);
            double n = s / 32768.0;
            sum += n * n;
        }

        double rms = Math.Sqrt(sum / Math.Max(1, count));
        return rms < 0.004;
    }

    private static MemoryStream CreateWavStream(byte[] pcm16, int sampleRate)
    {
        var ms = new MemoryStream(44 + pcm16.Length);
        using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + pcm16.Length);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(pcm16.Length);
            bw.Write(pcm16);
        }

        ms.Position = 0;
        return ms;
    }

    public async ValueTask DisposeAsync()
    {
        try { audioQueue?.Writer.TryComplete(); } catch { }
        try { workerCts?.Cancel(); } catch { }

        try
        {
            if (workerTask != null)
                await workerTask;
        }
        catch { }

        try { whisperFactory?.Dispose(); } catch { }
        try { weights?.Dispose(); } catch { }

        workerCts?.Dispose();
        llmLock.Dispose();
    }
}