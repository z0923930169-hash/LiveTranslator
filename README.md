# LiveTranslator

Windows 桌面即時翻譯工具。這個版本是 **完全本機 AI 版**，不需要 OpenAI API Key，也不使用 Google、Lingva 或其他線上翻譯服務。

## 功能

- 擷取 Windows 系統播放聲音（遊戲、YouTube、影片）
- Whisper 本機多語言語音辨識
- Qwen2.5 1.7B 本機 AI 翻譯
- 可翻成繁體中文、簡體中文、英文、日文、韓文、法文、德文、西班牙文、葡萄牙文、義大利文、俄文、泰文、越南文、印尼文
- 顯示原文與翻譯
- 置頂字幕視窗
- 字幕大小與透明度可調
- 翻譯 SRT / VTT / TXT 字幕檔
- 不需要 Python
- 不需要 API Key

## 第一次使用

第一次按「開始即時翻譯」或翻譯字幕檔時，程式會下載兩個模型到：

`%LOCALAPPDATA%\LiveTranslator\models`

目前預設：

- Whisper `ggml-base.bin`：多語言語音辨識
- Qwen2.5 1.5B Instruct Q4_K_M：本機翻譯

總下載量約 1.27 GB。

模型下載完成後，之後翻譯不需要把遊戲聲音或文字送到 OpenAI、Google 或其他翻譯網站。

## 使用方式

1. 開啟 `LiveTranslator.exe`
2. 選擇「翻譯成」的語言
3. 按「開始即時翻譯」
4. 第一次等待模型下載與載入
5. 播放遊戲、YouTube 或影片
6. 每約 4 秒處理一段聲音並顯示翻譯字幕

## 效能

目前為相容性優先版本：

- Whisper 使用 CPU 版 runtime
- Qwen2.5 使用 LLamaSharp CPU backend
- 不需要 NVIDIA 顯示卡

因此速度會依 CPU 而不同。之後可再加入 CUDA / Vulkan GPU 加速模式。

## 建置

GitHub Actions 會自動建立 Windows x64 self-contained EXE。

專案使用：

- .NET 8 WinForms
- NAudio
- Whisper.net 1.9.1
- LLamaSharp 0.27.0
