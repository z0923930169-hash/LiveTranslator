namespace LiveTranslator;

public sealed class LocalSubtitleTranslator
{
    private readonly LocalAiEngine engine;

    public LocalSubtitleTranslator(LocalAiEngine engine)
    {
        this.engine = engine;
    }

    public async Task<string> TranslateAsync(
        string input,
        string targetLanguage,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken ct)
    {
        string normalized = input.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] blocks = normalized.Split("\n\n");
        var output = new List<string>(blocks.Length);

        for (int i = 0; i < blocks.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            output.Add(await TranslateBlockAsync(blocks[i], targetLanguage, ct));
            progress?.Report((i + 1, blocks.Length));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, output);
    }

    private async Task<string> TranslateBlockAsync(
        string block,
        string targetLanguage,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(block)) return block;

        string[] lines = block.Split('\n');

        if (lines[0].TrimStart().StartsWith("NOTE", StringComparison.OrdinalIgnoreCase) ||
            lines[0].TrimStart().StartsWith("STYLE", StringComparison.OrdinalIgnoreCase) ||
            lines[0].TrimStart().StartsWith("REGION", StringComparison.OrdinalIgnoreCase))
            return string.Join(Environment.NewLine, lines);

        var metadata = new List<string>();
        var dialogue = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            bool isHeader =
                trimmed.Equals("WEBVTT", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("-->") ||
                int.TryParse(trimmed, out _) ||
                trimmed.StartsWith("X-TIMESTAMP-MAP", StringComparison.OrdinalIgnoreCase) ||
                (i == 0 && lines.Length > 1 && lines[1].Contains("-->"));

            if (isHeader && dialogue.Count == 0)
                metadata.Add(line);
            else
                dialogue.Add(line);
        }

        string humanText = string.Join("\n", dialogue).Trim();
        if (string.IsNullOrWhiteSpace(humanText))
            return string.Join(Environment.NewLine, lines);

        string translated = await engine.TranslateTextAsync(humanText, targetLanguage, ct);

        if (metadata.Count == 0)
            return translated;

        return string.Join(Environment.NewLine, metadata) +
               Environment.NewLine +
               translated;
    }
}