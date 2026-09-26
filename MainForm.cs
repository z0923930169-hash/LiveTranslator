using System.Diagnostics;
using System.Text;

namespace LiveTranslator;

public sealed class MainForm : Form
{
    private readonly ComboBox targetLanguage = new();
    private readonly Button start = new();
    private readonly Button stop = new();
    private readonly Button toggleOverlay = new();
    private readonly Button translateFile = new();
    private readonly Button openModels = new();
    private readonly Label status = new();
    private readonly Label modelInfo = new();
    private readonly ProgressBar progressBar = new();
    private readonly TextBox sourceText = new();
    private readonly TextBox translatedText = new();
    private readonly NumericUpDown fontSize = new();
    private readonly NumericUpDown opacity = new();

    private readonly SubtitleOverlay overlay = new();
    private readonly ModelManager models = new();

    private LocalAiEngine? engine;
    private SystemAudioCapture? capture;
    private CancellationTokenSource operationCts = new();
    private bool isRunning;

    private static readonly Dictionary<string, string> TargetDescriptions = new()
    {
        ["繁體中文"] = "Traditional Chinese (Taiwan)",
        ["簡體中文"] = "Simplified Chinese",
        ["English"] = "English",
        ["日本語"] = "Japanese",
        ["한국어"] = "Korean",
        ["Français"] = "French",
        ["Deutsch"] = "German",
        ["Español"] = "Spanish",
        ["Português"] = "Portuguese",
        ["Italiano"] = "Italian",
        ["Русский"] = "Russian",
        ["ไทย"] = "Thai",
        ["Tiếng Việt"] = "Vietnamese",
        ["Bahasa Indonesia"] = "Indonesian"
    };

    public MainForm()
    {
        Text = "LiveTranslator - 免費本機 AI 即時翻譯";
        Width = 920;
        Height = 760;
        MinimumSize = new Size(800, 650);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        UpdateModelInfo();

        FormClosing += async (_, __) =>
        {
            try { operationCts.Cancel(); } catch { }
            try { capture?.Stop(); } catch { }
            capture?.Dispose();

            if (engine != null)
            {
                try { await engine.DisposeAsync(); } catch { }
            }

            operationCts.Dispose();
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 8,
            ColumnCount = 1,
            Padding = new Padding(18),
            AutoScroll = true
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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

        var subtitle = new Label
        {
            Text = "免費本機 AI：電腦聲音 → Whisper 語音辨識 → Qwen3 翻譯 → 即時字幕",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Padding = new Padding(0, 4, 0, 8)
        };
        root.Controls.Add(subtitle);

        var settings = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 5, 0, 5)
        };

        settings.Controls.Add(new Label
        {
            Text = "翻譯成：",
            AutoSize = true,
            Margin = new Padding(0, 7, 5, 0)
        });

        targetLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
        targetLanguage.Width = 180;
        targetLanguage.DataSource = TargetDescriptions.Keys.ToList();
        targetLanguage.SelectedItem = "繁體中文";
        settings.Controls.Add(targetLanguage);

        modelInfo.AutoSize = true;
        modelInfo.Margin = new Padding(20, 7, 10, 0);
        settings.Controls.Add(modelInfo);

        openModels.Text = "開啟模型資料夾";
        openModels.AutoSize = true;
        openModels.Click += (_, __) =>
        {
            Directory.CreateDirectory(models.ModelDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", models.ModelDirectory)
            {
                UseShellExecute = true
            });
        };
        settings.Controls.Add(openModels);

        root.Controls.Add(settings);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 4)
        };

        start.Text = "開始即時翻譯";
        stop.Text = "停止";
        toggleOverlay.Text = "顯示 / 隱藏字幕";
        translateFile.Text = "翻譯 SRT / VTT / TXT";
        stop.Enabled = false;

        start.Click += async (_, __) => await StartAsync();
        stop.Click += (_, __) => StopLive();
        toggleOverlay.Click += (_, __) =>
        {
            if (overlay.Visible) overlay.Hide(); else overlay.Show();
        };
        translateFile.Click += async (_, __) => await TranslateFileAsync();

        buttons.Controls.AddRange(new Control[]
        {
            start, stop, toggleOverlay, translateFile
        });

        root.Controls.Add(buttons);

        var progressPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1
        };
        progressBar.Dock = DockStyle.Top;
        progressBar.Height = 18;
        progressBar.Minimum = 0;
        progressBar.Maximum = 100;
        progressPanel.Controls.Add(progressBar);

        status.Text = "狀態：待命";
        status.AutoSize = true;
        status.Padding = new Padding(0, 5, 0, 4);
        progressPanel.Controls.Add(status);

        root.Controls.Add(progressPanel);

        var sourceGroup = new GroupBox
        {
            Text = "語音辨識原文",
            Dock = DockStyle.Fill
        };

        sourceText.Multiline = true;
        sourceText.ReadOnly = true;
        sourceText.ScrollBars = ScrollBars.Vertical;
        sourceText.Dock = DockStyle.Fill;
        sourceText.Font = new Font("Microsoft JhengHei UI", 12);
        sourceGroup.Controls.Add(sourceText);
        root.Controls.Add(sourceGroup);

        var transGroup = new GroupBox
        {
            Text = "翻譯",
            Dock = DockStyle.Fill
        };

        translatedText.Multiline = true;
        translatedText.ReadOnly = true;
        translatedText.ScrollBars = ScrollBars.Vertical;
        translatedText.Dock = DockStyle.Fill;
        translatedText.Font = new Font("Microsoft JhengHei UI", 14, FontStyle.Bold);
        transGroup.Controls.Add(translatedText);
        root.Controls.Add(transGroup);

        var subtitleSettings = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true
        };

        subtitleSettings.Controls.Add(new Label
        {
            Text = "字幕大小",
            AutoSize = true,
            Margin = new Padding(0, 6, 5, 0)
        });

        fontSize.Minimum = 16;
        fontSize.Maximum = 52;
        fontSize.Value = 28;
        fontSize.ValueChanged += (_, __) =>
            overlay.SetFontSize((float)fontSize.Value);
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
        opacity.ValueChanged += (_, __) =>
            overlay.SetOpacityPercent((int)opacity.Value);
        subtitleSettings.Controls.Add(opacity);

        root.Controls.Add(subtitleSettings);

        root.Controls.Add(new Label
        {
            Text =
                "第一次使用會下載約 1.4 GB 的本機模型。下載完成後，不需 API Key，" +
                "翻譯時也不會把你的遊戲聲音傳給 Google、OpenAI 或其他翻譯網站。",
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            ForeColor = Color.DimGray,
            Padding = new Padding(0, 5, 0, 0)
        });
    }

    private async Task StartAsync()
    {
        if (isRunning) return;

        try
        {
            start.Enabled = false;
            translateFile.Enabled = false;
            targetLanguage.Enabled = false;

            await EnsureEngineReadyAsync(operationCts.Token);

            string targetLabel =
                targetLanguage.SelectedItem?.ToString() ?? "繁體中文";
            string target = TargetDescriptions[targetLabel];

            sourceText.Clear();
            translatedText.Clear();

            capture?.Dispose();
            capture = new SystemAudioCapture();
            capture.Status += SetStatus;
            capture.Error += ShowError;
            capture.Pcm16Chunk += pcm =>
            {
                if (engine == null) return;

                bool queued = engine.QueueAudio(pcm, target);
                if (!queued)
                    SetStatus("本機 AI 尚未準備完成。");
            };

            capture.Start();

            isRunning = true;
            stop.Enabled = true;
            overlay.Show();
            SetStatus("翻譯中：每約 4 秒處理一段電腦聲音。");
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消。");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            targetLanguage.Enabled = true;
        }
        finally
        {
            if (!isRunning)
            {
                start.Enabled = true;
                translateFile.Enabled = true;
            }
        }
    }

    private void StopLive()
    {
        try { capture?.Stop(); } catch { }
        capture?.Dispose();
        capture = null;

        isRunning = false;
        start.Enabled = true;
        stop.Enabled = false;
        translateFile.Enabled = true;
        targetLanguage.Enabled = true;
        SetStatus("已停止收音。本機模型仍保留在記憶體中，可快速再次開始。");
    }

    private async Task EnsureEngineReadyAsync(CancellationToken ct)
    {
        if (engine != null) return;

        progressBar.Value = 0;

        var downloadProgress = new Progress<ModelDownloadProgress>(p =>
        {
            progressBar.Value = p.Percent;

            double mb = p.Received / 1024d / 1024d;
            string totalText = p.Total is > 0
                ? $" / {p.Total.Value / 1024d / 1024d:0} MB"
                : "";

            SetStatus($"下載 {p.Name}：{mb:0} MB{totalText} ({p.Percent}%)");
        });

        if (!models.ModelsReady)
            SetStatus("第一次使用：正在下載本機 AI 模型…");

        await models.EnsureModelsAsync(downloadProgress, ct);
        progressBar.Value = 100;
        UpdateModelInfo();

        engine = new LocalAiEngine(models);
        engine.Status += SetStatus;
        engine.Error += ShowError;
        engine.Recognized += AppendSource;
        engine.TranslationReady += AppendTranslation;

        await engine.InitializeAsync(ct);
        progressBar.Value = 0;
    }

    private void AppendSource(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendSource(text));
            return;
        }

        AppendLine(sourceText, text);
    }

    private void AppendTranslation(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendTranslation(text));
            return;
        }

        AppendLine(translatedText, text);
        overlay.SetSubtitle(text);
    }

    private static void AppendLine(TextBox box, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        if (box.TextLength > 12000)
            box.Text = box.Text[^8000..];

        if (box.TextLength > 0)
            box.AppendText(Environment.NewLine);

        box.AppendText(text.Trim());
        box.SelectionStart = box.TextLength;
        box.ScrollToCaret();
    }

    private async Task TranslateFileAsync()
    {
        try
        {
            if (isRunning)
                StopLive();

            start.Enabled = false;
            translateFile.Enabled = false;
            targetLanguage.Enabled = false;

            await EnsureEngineReadyAsync(operationCts.Token);
            if (engine == null) return;

            using var dlg = new OpenFileDialog
            {
                Filter =
                    "字幕或文字|*.srt;*.vtt;*.txt|" +
                    "SRT|*.srt|VTT|*.vtt|文字|*.txt"
            };

            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            string targetLabel =
                targetLanguage.SelectedItem?.ToString() ?? "繁體中文";
            string target = TargetDescriptions[targetLabel];

            string input = await File.ReadAllTextAsync(
                dlg.FileName, Encoding.UTF8, operationCts.Token);

            var translator = new LocalSubtitleTranslator(engine);

            var fileProgress = new Progress<(int Done, int Total)>(p =>
            {
                int percent = p.Total > 0 ? p.Done * 100 / p.Total : 0;
                progressBar.Value = Math.Clamp(percent, 0, 100);
                SetStatus($"本機翻譯字幕：{p.Done}/{p.Total} ({percent}%)");
            });

            string output = await translator.TranslateAsync(
                input, target, fileProgress, operationCts.Token);

            string dir = Path.GetDirectoryName(dlg.FileName)!;
            string name = Path.GetFileNameWithoutExtension(dlg.FileName);
            string ext = Path.GetExtension(dlg.FileName);
            string outPath = Path.Combine(
                dir, $"{name}.translated{ext}");

            await File.WriteAllTextAsync(
                outPath, output, new UTF8Encoding(true), operationCts.Token);

            progressBar.Value = 100;
            SetStatus("字幕翻譯完成：" + outPath);

            MessageBox.Show(
                $"已輸出：\n{outPath}",
                "翻譯完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消。");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            start.Enabled = true;
            translateFile.Enabled = true;
            targetLanguage.Enabled = true;
        }
    }

    private void UpdateModelInfo()
    {
        modelInfo.Text = models.ModelsReady
            ? "本機模型：已下載"
            : "本機模型：第一次使用時下載約 1.4 GB";
    }

    private void SetStatus(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(text));
            return;
        }

        status.Text = "狀態：" + text;
    }

    private void ShowError(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowError(text));
            return;
        }

        status.Text = "狀態：錯誤 - " + text;
    }
}