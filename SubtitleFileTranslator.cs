using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LiveTranslator;

public sealed class SubtitleFileTranslator
{
    private readonly HttpClient http = new();

    public async Task<string> TranslateAsync(
        string apiKey, string inputText, string targetLanguage, CancellationToken ct)
    {
        string prompt =
            $"Translate the subtitle content to {targetLanguage}. " +
            "Preserve every subtitle sequence number, timestamp, line break, WEBVTT header, " +
            "HTML-like tag, speaker label, and formatting. Translate only human-readable dialogue. " +
            "Return only the translated subtitle content.";

        var body = new
        {
            model = "gpt-5-mini",
            input = prompt + "\n\nSUBTITLE:\n" + inputText
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        req.Headers.Add("OpenAI-Safety-Identifier", "live-translator-desktop");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, ct);
        string raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"OpenAI HTTP {(int)resp.StatusCode}: {ExtractApiError(raw)}");

        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("output", out var output))
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content)) continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var t) &&
                        t.GetString() == "output_text" &&
                        part.TryGetProperty("text", out var text))
                        return text.GetString() ?? "";
                }
            }
        }
        throw new Exception("OpenAI 沒有回傳可讀的翻譯文字。");
    }

    private static string ExtractApiError(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var e) &&
                e.TryGetProperty("message", out var m))
                return m.GetString() ?? raw;
        }
        catch { }
        return raw.Length > 500 ? raw[..500] : raw;
    }
}