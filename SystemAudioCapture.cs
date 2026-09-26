using NAudio.Wave;

namespace LiveTranslator;

public sealed class SystemAudioCapture : IDisposable
{
    private WasapiLoopbackCapture? capture;
    private readonly List<short> pending = new();
    private readonly object sync = new();

    private const int TargetRate = 16000;
    private const int ChunkSeconds = 4;
    private const int ChunkSamples = TargetRate * ChunkSeconds;

    public event Action<byte[]>? Pcm16Chunk;
    public event Action<string>? Status;
    public event Action<string>? Error;

    public void Start()
    {
        Stop();

        lock (sync) pending.Clear();

        capture = new WasapiLoopbackCapture();
        Status?.Invoke($"系統音訊：{capture.WaveFormat.SampleRate} Hz / {capture.WaveFormat.Channels} ch");

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null)
                Error?.Invoke("收音停止：" + e.Exception.Message);
        };
        capture.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            if (capture == null || e.BytesRecorded <= 0) return;

            var src = capture.WaveFormat;
            float[] mono = ToMonoFloat(e.Buffer, e.BytesRecorded, src);
            short[] resampled = LinearResample(mono, src.SampleRate, TargetRate);

            List<byte[]> ready = new();

            lock (sync)
            {
                pending.AddRange(resampled);

                while (pending.Count >= ChunkSamples)
                {
                    short[] chunk = pending.GetRange(0, ChunkSamples).ToArray();
                    pending.RemoveRange(0, ChunkSamples);

                    byte[] pcm = new byte[chunk.Length * 2];
                    Buffer.BlockCopy(chunk, 0, pcm, 0, pcm.Length);
                    ready.Add(pcm);
                }
            }

            foreach (var pcm in ready)
                Pcm16Chunk?.Invoke(pcm);
        }
        catch (Exception ex)
        {
            Error?.Invoke("音訊處理錯誤：" + ex.Message);
        }
    }

    private static float[] ToMonoFloat(byte[] data, int length, WaveFormat wf)
    {
        int channels = Math.Max(1, wf.Channels);
        bool isFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat && wf.BitsPerSample == 32;
        bool isPcm16 = wf.Encoding == WaveFormatEncoding.Pcm && wf.BitsPerSample == 16;

        if (wf is WaveFormatExtensible ext)
        {
            var floatGuid = new Guid("00000003-0000-0010-8000-00aa00389b71");
            var pcmGuid = new Guid("00000001-0000-0010-8000-00aa00389b71");
            isFloat = ext.SubFormat == floatGuid && wf.BitsPerSample == 32;
            isPcm16 = ext.SubFormat == pcmGuid && wf.BitsPerSample == 16;
        }

        if (isFloat)
        {
            int frames = length / (4 * channels);
            float[] mono = new float[frames];

            for (int i = 0; i < frames; i++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += BitConverter.ToSingle(data, (i * channels + c) * 4);
                mono[i] = (float)(sum / channels);
            }
            return mono;
        }

        if (isPcm16)
        {
            int frames = length / (2 * channels);
            float[] mono = new float[frames];

            for (int i = 0; i < frames; i++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += BitConverter.ToInt16(data, (i * channels + c) * 2) / 32768f;
                mono[i] = (float)(sum / channels);
            }
            return mono;
        }

        throw new NotSupportedException(
            $"目前不支援此 Windows 音訊格式：{wf.Encoding}, {wf.BitsPerSample} bit");
    }

    private static short[] LinearResample(float[] input, int inputRate, int outputRate)
    {
        if (input.Length == 0) return Array.Empty<short>();

        if (inputRate == outputRate)
            return input.Select(FloatToShort).ToArray();

        double ratio = (double)inputRate / outputRate;
        int outputLength = Math.Max(1, (int)Math.Round(input.Length / ratio));
        short[] output = new short[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            double srcPos = i * ratio;
            int i0 = Math.Min((int)Math.Floor(srcPos), input.Length - 1);
            int i1 = Math.Min(i0 + 1, input.Length - 1);
            double frac = srcPos - i0;

            float sample = (float)(
                input[i0] * (1.0 - frac) +
                input[i1] * frac);

            output[i] = FloatToShort(sample);
        }

        return output;
    }

    private static short FloatToShort(float x)
    {
        x = Math.Clamp(x, -1f, 1f);
        return (short)Math.Round(x * 32767f);
    }

    public void Stop()
    {
        if (capture == null) return;

        try { capture.StopRecording(); } catch { }
        capture.Dispose();
        capture = null;
    }

    public void Dispose() => Stop();
}