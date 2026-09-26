using System.Security.Cryptography;
using System.Text;

namespace LiveTranslator;

public sealed class MainForm : Form
{
    private readonly TextBox apiKey = new();
    private readonly ComboBox targetLanguage = new();
    private readonly Button start = new();
    private readonly Button stop = new();
    private readonly Button toggleOverlay = new();
    private readonly Button translateFile = new();
    private readonly Label status = new();
    private readonly TextBox sourceText = new();
    private readonly TextBox translatedText = new();
    private readonly CheckBox rememberKey = new();
    private readonly NumericUpDown fontSize = new();
    private readonly NumericUpDown opacity = new();

    private readonly SubtitleOverlay overlay = new();
    private SystemAudioCapture? capture;
    private OpenAIRealtimeTranslationClient? client;
    private CancellationTokenSource? cts;

    private string sourceBuffer = "";
    private string translationBuffer = "";

    private static readonly Dictionary<string, string> Languages = new()
    {
        ["繁體中文"] = "zh-TW",
        ["簡體中文"] = "zh-CN",
        ["English"] = "en",
        ["日本語"] = "ja",
        ["한국어"] = "ko",
        ["Français"] = "fr",
        ["Deutsch"] = "de",
        ["Español"] = "es",
        ["Português"] = "pt",
        ["Italiano"] = "it",
        ["Русский"] = "ru",
        ["ไทย"] = "th",
        ["Tiếng Việt"] = "vi",
        ["Bahasa Indonesia"] = "id"
    };

    public MainForm()
    {
        Text = "LiveTranslator - OpenAI 即時翻譯";
        Width = 900;
        Height = 720;
        MinimumSize = new Size(780, 640);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        LoadRememberedKey();
        FormClosing += async (_, __) => await StopAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 7,
            ColumnCount = 1,
            Padding = new Padding(18),
            AutoScroll = true
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "LiveTranslator",
            Font = new Font("Microsoft JhengHei UI", 22, FontStyle.Bold),
            AutoSize = true
        });

        var settings = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 4,
            Padding = new Padding(0, 10, 0, 6)
        };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        settings.Controls.Add(new Label
        {
            Text = "OpenAI API Key：",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);

        apiKey.UseSystemPasswordChar = true;
        apiKey.Dock = DockStyle.Fill;
        settings.Controls.Add(apiKey, 1, 0);

        rememberKey.Text = "記住金鑰";
        rememberKey.AutoSize = true;
        settings.Controls.Add(rememberKey, 2, 0);

        var showKey = new CheckBox { Text = "顯示", AutoSize = true };
        showKey.CheckedChanged += (_, __) => apiKey.UseSystemPasswordChar = !showKey.Checked;
        settings.Controls.Add(showKey, 3, 0);

        settings.Controls.Add(new Label
        {
            Text = "翻譯成：",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 1);

        targetLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
        targetLanguage.DataSource = Languages.Keys.ToList();
        targetLanguage.SelectedItem = "繁體中文";
        settings.Controls.Add(targetLanguage, 1, 1);

        var hint = new Label
        {
            Text = "即時模式會把 Windows 系統播放聲音送到 OpenAI Realtime Translation。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Anchor = AnchorStyles.Left
        };
        settings.Controls.Add(hint, 2, 1);
        settings.SetColumnSpan(hint, 2);
        root.Controls.Add(settings);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 4, 0, 4)
        };

        start.Text = "開始即時翻譯";
        stop.Text = "停止";
        toggleOverlay.Text = "顯示 / 隱藏字幕";
        translateFile.Text = "翻譯 SRT / VTT / TXT";
        stop.Enabled = false;

        start.Click += async (_, __) => await StartAsync();
        stop.Click += async (_, __) => await StopAsync();
        toggleOverlay.Click += (_, __) =>
        {
            if (overlay.Visible) overlay.Hide(); else overlay.Show();
        };
        translateFile.Click += async (_, __) => await TranslateFileAsync();

        buttons.Controls.AddRange(new Control[] { start, stop, toggleOverlay, translateFile });
        root.Controls.Add(buttons);

        var sourceGroup = new GroupBox { Text = "原文", Dock = DockStyle.Fill };
        sourceText.Multiline = true;
        sourceText.ReadOnly = true;
        sourceText.ScrollBars = ScrollBars.Vertical;
        sourceText.Dock = DockStyle.Fill;
        sourceText.Font = new Font("Microsoft JhengHei UI", 12);
        sourceGroup.Controls.Add(sourceText);
        root.Controls.Add(sourceGroup);

        var transGroup = new GroupBox { Text = "翻譯", Dock = DockStyle.Fill };
        translatedText.Multiline = true;
        translatedText.ReadOnly = true;
        translatedText.ScrollBars = ScrollBars.Vertical;
        translatedText.Dock = DockStyle.Fill;
        translatedText.Font = new Font("Microsoft JhengHei UI", 14, FontStyle.Bold);
        transGroup.Controls.Add(translatedText);
        root.Controls.Add(transGroup);

        var subtitleSettings = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        subtitleSettings.Controls.Add(new Label
        {
            Text = "字幕大小",
            AutoSize = true,
            Margin = new Padding(0, 6, 5, 0)
        });

        fontSize.Minimum = 16;
        fontSize.Maximum = 52;
        fontSize.Value = 28;
        fontSize.ValueChanged += (_, __) => overlay.SetFontSize((float)fontSize.Value);
        subtitleSettings.Controls.Add(fontSize);

        subtitleSettings.Controls.Add(new Label
        {
            Text = "透明度",
            AutoSize = true,
            Margin = new Padding(16, 6, 5, 0)
        });

        opacity.Minimum = 30;
        opacity.Maximum = 100;
        opacity.Value = 82;
        opacity.ValueChanged += (_, __) => overlay.SetOpacityPercent((int)opacity.Value);
        subtitleSettings.Controls.Add(opacity);
        root.Controls.Add(subtitleSettings);

        status.Text = "狀態：待命";
        status.AutoSize = true;
        status.Padding = new Padding(0, 8, 0, 0);
        root.Controls.Add(status);
    }

    private async Task StartAsync()
    {
        try
        {
            string key = apiKey.Text.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show("請先輸入 OpenAI API Key。", "需要 API Key",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (rememberKey.Checked) SaveKey(key); else DeleteSavedKey();

            start.Enabled = false;
            stop.Enabled = true;
            sourceBuffer = "";
            translationBuffer = "";
            sourceText.Clear();
            translatedText.Clear();

            cts = new CancellationTokenSource();

            client = new OpenAIRealtimeTranslationClient();
            client.Status += SetStatus;
            client.Error += ShowError;
            client.SourceDelta += AppendSource;
            client.TranslationDelta += AppendTranslation;

            string label = targetLanguage.SelectedItem?.ToString() ?? "繁體中文";
            string lang = Languages[label];

            await client.ConnectAsync(key, lang, cts.Token);

            capture = new SystemAudioCapture();
            capture.Status += SetStatus;
            capture.Error += ShowError;
            capture.Pcm24kMono16 += async pcm =>
            {
                try
                {
                    if (client != null && cts != null && !cts.IsCancellationRequested)
                        await client.SendPcm16Async(pcm, cts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { ShowError(ex.Message); }
            };
            capture.Start();

            overlay.Show();
            SetStatus("翻譯中：正在收取電腦播放的聲音。");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            await StopAsync();
        }
    }

    private async Task StopAsync()
    {
        stop.Enabled = false;

        try { capture?.Stop(); } catch { }
        capture?.Dispose();
        capture = null;

        if (client != null)
        {
            try { await client.DisposeAsync(); } catch { }
            client = null;
        }

        try { cts?.Cancel(); } catch { }
        cts?.Dispose();
        cts = null;

        start.Enabled = true;
        SetStatus("已停止。");
    }

    private void AppendSource(string delta)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendSource(delta)); return; }
        sourceBuffer += delta;
        if (sourceBuffer.Length > 6000) sourceBuffer = sourceBuffer[^6000..];
        sourceText.Text = sourceBuffer;
        sourceText.SelectionStart = sourceText.TextLength;
        sourceText.ScrollToCaret();
    }

    private void AppendTranslation(string delta)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendTranslation(delta)); return; }
        translationBuffer += delta;
        if (translationBuffer.Length > 6000) translationBuffer = translationBuffer[^6000..];
        translatedText.Text = translationBuffer;
        translatedText.SelectionStart = translatedText.TextLength;
        translatedText.ScrollToCaret();
        overlay.SetSubtitle(GetRecentSubtitle(translationBuffer));
    }

    private static string GetRecentSubtitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "…";
        text = text.Trim();

        int lastBreak = Math.Max(text.LastIndexOf('\n'), Math.Max(text.LastIndexOf('。'), text.LastIndexOf('！')));
        if (lastBreak >= 0 && lastBreak < text.Length - 1)
        {
            string tail = text[(lastBreak + 1)..].Trim();
            if (tail.Length >= 8) return tail.Length <= 180 ? tail : tail[^180..];
        }

        return text.Length <= 180 ? text : text[^180..];
    }

    private void SetStatus(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => SetStatus(text)); return; }
        status.Text = "狀態：" + text;
    }

    private void ShowError(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => ShowError(text)); return; }
        status.Text = "狀態：錯誤 - " + text;
    }

    private async Task TranslateFileAsync()
    {
        if (string.IsNullOrWhiteSpace(apiKey.Text))
        {
            MessageBox.Show("請先輸入 OpenAI API Key。");
            return;
        }

        using var dlg = new OpenFileDialog
        {
            Filter = "字幕或文字|*.srt;*.vtt;*.txt|SRT|*.srt|VTT|*.vtt|文字|*.txt"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SetStatus("正在翻譯字幕檔…");
            string input = await File.ReadAllTextAsync(dlg.FileName, Encoding.UTF8);

            string label = targetLanguage.SelectedItem?.ToString() ?? "繁體中文";
            var translator = new SubtitleFileTranslator();
            string output = await translator.TranslateAsync(
                apiKey.Text.Trim(), input, label, CancellationToken.None);

            string dir = Path.GetDirectoryName(dlg.FileName)!;
            string name = Path.GetFileNameWithoutExtension(dlg.FileName);
            string ext = Path.GetExtension(dlg.FileName);
            string outPath = Path.Combine(dir, $"{name}.translated{ext}");

            await File.WriteAllTextAsync(outPath, output, new UTF8Encoding(true));
            SetStatus("字幕翻譯完成：" + outPath);

            MessageBox.Show($"已輸出：\n{outPath}", "完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private string KeyPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveTranslator", "apikey.bin");

    private void SaveKey(string key)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
            byte[] raw = Encoding.UTF8.GetBytes(key);
            byte[] protectedData = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(KeyPath, protectedData);
        }
        catch { }
    }

    private void LoadRememberedKey()
    {
        try
        {
            if (!File.Exists(KeyPath)) return;
            byte[] protectedData = File.ReadAllBytes(KeyPath);
            byte[] raw = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
            apiKey.Text = Encoding.UTF8.GetString(raw);
            rememberKey.Checked = true;
        }
        catch { }
    }

    private void DeleteSavedKey()
    {
        try { if (File.Exists(KeyPath)) File.Delete(KeyPath); } catch { }
    }
}