using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace LiveTranslator;

public sealed class OpenAIRealtimeTranslationClient : IAsyncDisposable
{
    private readonly ClientWebSocket ws = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private CancellationTokenSource? receiveCts;
    private Task? receiveTask;
    private readonly TaskCompletionSource<bool> closedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action<string>? SourceDelta;
    public event Action<string>? TranslationDelta;
    public event Action<string>? Status;
    public event Action<string>? Error;

    public bool IsConnected => ws.State == WebSocketState.Open;

    public async Task ConnectAsync(string apiKey, string targetLanguage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API Key 不可為空白。");

        ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey.Trim()}");
        ws.Options.SetRequestHeader("OpenAI-Safety-Identifier", "live-translator-desktop");
        var uri = new Uri("wss://api.openai.com/v1/realtime/translations?model=gpt-realtime-translate");

        Status?.Invoke("正在連線 OpenAI 即時翻譯…");
        await ws.ConnectAsync(uri, ct);

        receiveCts = new CancellationTokenSource();
        receiveTask = Task.Run(() => ReceiveLoopAsync(receiveCts.Token));

        await SendJsonAsync(new
        {
            type = "session.update",
            session = new
            {
                audio = new
                {
                    output = new { language = targetLanguage }
                }
            }
        }, ct);

        Status?.Invoke("OpenAI 已連線。");
    }

    public async Task SendPcm16Async(byte[] pcm24kMono16, CancellationToken ct)
    {
        if (!IsConnected || pcm24kMono16.Length == 0) return;
        await SendJsonAsync(new
        {
            type = "session.input_audio_buffer.append",
            audio = Convert.ToBase64String(pcm24kMono16)
        }, ct);
    }

    private async Task SendJsonAsync(object payload, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await sendLock.WaitAsync(ct);
        try
        {
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        finally { sendLock.Release(); }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            while (!ct.IsCancellationRequested &&
                  (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived))
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(rented, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        closedTcs.TrySetResult(true);
                        return;
                    }
                    ms.Write(rented, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                    HandleEvent(Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error?.Invoke($"OpenAI 連線錯誤：{ex.Message}"); }
        finally { ArrayPool<byte>.Shared.Return(rented); }
    }

    private void HandleEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;
            string type = typeProp.GetString() ?? "";

            if (type == "session.output_transcript.delta" &&
                root.TryGetProperty("delta", out var outDelta))
                TranslationDelta?.Invoke(outDelta.GetString() ?? "");
            else if (type == "session.input_transcript.delta" &&
                     root.TryGetProperty("delta", out var inDelta))
                SourceDelta?.Invoke(inDelta.GetString() ?? "");
            else if (type == "session.created")
                Status?.Invoke("翻譯工作階段已建立。");
            else if (type == "session.closed")
            {
                Status?.Invoke("翻譯工作階段已結束。");
                closedTcs.TrySetResult(true);
            }
            else if (type == "error")
                Error?.Invoke(ExtractError(root));
        }
        catch (Exception ex)
        {
            Error?.Invoke($"收到無法解析的伺服器訊息：{ex.Message}");
        }
    }

    private static string ExtractError(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("error", out var e))
            {
                if (e.ValueKind == JsonValueKind.Object &&
                    e.TryGetProperty("message", out var m))
                    return "OpenAI：" + (m.GetString() ?? "未知錯誤");
                return "OpenAI：" + e;
            }
        }
        catch { }
        return "OpenAI 回傳未知錯誤。";
    }

    public async Task CloseAsync()
    {
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                await SendJsonAsync(new { type = "session.close" }, CancellationToken.None);
                await Task.WhenAny(closedTcs.Task, Task.Delay(2500));
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
        }
        catch { }

        try { receiveCts?.Cancel(); } catch { }
        try { if (receiveTask != null) await receiveTask; } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        ws.Dispose();
        sendLock.Dispose();
        receiveCts?.Dispose();
    }
}