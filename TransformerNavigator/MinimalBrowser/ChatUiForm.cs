using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Tesseract;
using TransformerNavigator;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
namespace MinimalBrowser
{
    public sealed class ChatUiForm : Form
    {
        private readonly WebView2 _chatWeb;
        private readonly TextBox _input;
        private readonly Button _sendBtn;
        private readonly Button _stopBtn;
        private readonly Button _clearBtn;
        private readonly Button _captureBtn; // new capture button
        private readonly Label _status;

        private readonly List<ChatMessage> _history = new();
        private readonly LlamaSseClient _client;
        private CancellationTokenSource _cts;

        private readonly StringBuilder _assistantBuf = new StringBuilder();
        private readonly FollowUpAssistant _followUpAssistant;
        private readonly FlowLayoutPanel followupPanel;
        public ChatUiForm(string model = null, string baseUrl = null, string apiKey = null)
        {
            Text = "Chat";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 980;
            Height = 720;
            TopMost = true;
            _followUpAssistant = new FollowUpAssistant(
                model ?? (Environment.GetEnvironmentVariable("LLAMA_MODEL") ?? "gpt"),
                baseUrl ?? Environment.GetEnvironmentVariable("LLAMA_BASE_URL"),
                apiKey ?? Environment.GetEnvironmentVariable("LLAMA_API_KEY"));
            _client = new LlamaSseClient(
                model ?? (Environment.GetEnvironmentVariable("LLAMA_MODEL") ?? "gpt"),
                baseUrl ?? Environment.GetEnvironmentVariable("LLAMA_BASE_URL"),
                apiKey ?? Environment.GetEnvironmentVariable("LLAMA_API_KEY"));

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            Controls.Add(root);

            // chat webview
            _chatWeb = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(0x0f, 0x12, 0x20)
            };
            root.Controls.Add(_chatWeb, 0, 0);

            _chatWeb.CoreWebView2InitializationCompleted += (_, e) =>
            {
                if (e.IsSuccess)
                {
                    _chatWeb.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _chatWeb.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                    _chatWeb.NavigateToString(BuildChatPageHtml());
                }
            };
            _ = _chatWeb.EnsureCoreWebView2Async();

            // composer panel
            var composer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(0x0f, 0x12, 0x20),
                Padding = new Padding(8)
            };
            root.Controls.Add(composer, 0, 1);

            // input
            _input = new TextBox
            {
                Multiline = true,
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(0xdb, 0xe8, 0xff),
                BackColor = Color.FromArgb(0x0b, 0x0f, 0x1a),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom,
                Location = new Point(8, 8),
                Size = new Size(Width - 330, 80)
            };
            composer.Controls.Add(_input);

            // send button
            _sendBtn = new Button
            {
                Text = "Send",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(0xdb, 0xe8, 0xff),
                BackColor = Color.FromArgb(0x18, 0x20, 0x3a),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(_input.Right + 8, 8),
                Size = new Size(88, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _sendBtn.FlatAppearance.BorderColor = Color.FromArgb(0x1f, 0x29, 0x46);
            _sendBtn.Click += (s, e) => _ = HandleSendAsync(_input.Text);
            composer.Controls.Add(_sendBtn);

            // stop button
            _stopBtn = new Button
            {
                Text = "Stop",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(0xdb, 0xe8, 0xff),
                BackColor = Color.FromArgb(0x18, 0x20, 0x3a),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(_input.Right + 8, 48),
                Size = new Size(88, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Visible = false
            };
            _stopBtn.FlatAppearance.BorderColor = Color.FromArgb(0x1f, 0x29, 0x46);
            _stopBtn.Click += (s, e) => StopStreaming();
            composer.Controls.Add(_stopBtn);

            // clear button
            _clearBtn = new Button
            {
                Text = "Clear",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(0xdb, 0xe8, 0xff),
                BackColor = Color.FromArgb(0x18, 0x20, 0x3a),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(_sendBtn.Right + 8, 8),
                Size = new Size(88, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _clearBtn.FlatAppearance.BorderColor = Color.FromArgb(0x1f, 0x29, 0x46);
            _clearBtn.Click += (s, e) => ClearChat();
            composer.Controls.Add(_clearBtn);

            // capture button
            _captureBtn = new Button
            {
                Text = "Capture",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(0xdb, 0xe8, 0xff),
                BackColor = Color.FromArgb(0x18, 0x20, 0x3a),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(_clearBtn.Right + 8, 8),
                Size = new Size(88, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _captureBtn.FlatAppearance.BorderColor = Color.FromArgb(0x1f, 0x29, 0x46);
            _captureBtn.Click += async (s, e) => await CaptureAndSendAsync();
            composer.Controls.Add(_captureBtn);

            // status label
            _status = new Label
            {
                Text = "Ready",
                AutoSize = true,
                ForeColor = Color.FromArgb(0x9f, 0xbc, 0xe8),
                Location = new Point(_input.Left, _input.Bottom + 8),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom
            };
            composer.Controls.Add(_status);
            followupPanel = new FlowLayoutPanel
            {
                AutoSize = false,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Location = new Point(_input.Left, _input.Bottom + 8), // temporary, we'll position again below
                Size = new Size(Width - 330, 40),                     // enough for a couple rows
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Visible = false
            };
            composer.Controls.Add(followupPanel);
            // resize logic
            composer.Resize += (_, __) =>
            {
                _input.Width = composer.ClientSize.Width - 8 - 8 - 300;
                _sendBtn.Left = _input.Right + 8;
                _stopBtn.Left = _input.Right + 8;
                _clearBtn.Left = _sendBtn.Right + 8;
                _captureBtn.Left = _clearBtn.Right + 8;

                // Place followups above the status label, spanning the width of the input
                followupPanel.Left = _input.Left;
                followupPanel.Width = _input.Width;
                // keep a fixed height; adjust if needed
                followupPanel.Height = 40;
                followupPanel.Top = _status.Top - followupPanel.Height - 6;
            };

            // enter key send
            _input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    _ = HandleSendAsync(_input.Text);
                }
            };
            _history.Add(new ChatMessage
            {
                role = "system",
                content = "You are a helpful assistant that explains things clearly and concisely."
            });
            FormClosed += (_, __) => StopStreaming();
        }
        private void ClearFollowups()
        {
            UI(() =>
            {
                followupPanel.SuspendLayout();
                try
                {
                    followupPanel.Controls.Clear();
                    followupPanel.Visible = false;
                }
                finally
                {
                    followupPanel.ResumeLayout();
                }
            });
        }
        private Button CreateFollowupButton(string text)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                AutoEllipsis = true,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(0xdb, 0xe8, 0xff),           // same as other buttons
                BackColor = Color.FromArgb(0x18, 0x20, 0x3a),           // same as other buttons
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(10, 6, 10, 6),
                Margin = new Padding(6, 4, 6, 4),
                UseMnemonic = false,
                TabStop = false
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(0x1f, 0x29, 0x46);   // border like others
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x2b, 0x44, 0x7a);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(0x17, 0x30, 0x60);

            btn.Click += (s, e) => SendFollowup(text);
            return btn;
        }
        private async Task ShowFollowUpsAsync(CancellationToken ct = default)
        {
            // short timeout to avoid hanging UI
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

            try
            {
                var followup = await _followUpAssistant
                    .GenerateSuggestionsAsync(_history, timeoutCts.Token)
                    .ConfigureAwait(false);

                UI(() =>
                {
                    followupPanel.SuspendLayout();
                    try
                    {
                        followupPanel.Controls.Clear();

                        var questions = followup?.Questions ?? new List<string>();
                        if (questions.Count == 0)
                        {
                            followupPanel.Visible = false;
                            return;
                        }

                        var lbl = new Label
                        {
                            Text = "Suggested follow-ups:",
                            ForeColor = Color.FromArgb(0x9f, 0xbc, 0xe8),
                            AutoSize = true,
                            Margin = new Padding(6, 10, 10, 6)
                        };
                        followupPanel.Controls.Add(lbl);

                        foreach (var q in questions)
                            followupPanel.Controls.Add(CreateFollowupButton(q));

                        followupPanel.Visible = true;
                    }
                    finally
                    {
                        followupPanel.ResumeLayout();
                    }
                });
            }
            catch
            {
                // Never let follow-ups break the main flow
                UI(() =>
                {
                    followupPanel.Controls.Clear();
                    followupPanel.Visible = false;
                });
            }
        }

        private void SendFollowup(string question)
        {
            _input.Text = question;
            SendMessageToMainAssistant();
        }
        private void SendMessageToMainAssistant()
        {
            _ = HandleSendAsync(_input.Text);
        }
        /// <summary>
        /// Captures the entire primary screen, OCRs it, and sends the text.
        /// </summary>
        private async Task CaptureAndSendAsync()
        {
            try
            {
                Hide();
                await Task.Delay(200);

                Rectangle? region = null;
                using (var selector = new ScreenRegionSelector())
                {
                    selector.Opacity = 0.25f;
                    if (selector.ShowDialog() == DialogResult.OK)
                        region = selector.SelectedRegion;
                }

                Show();

                if (region == null)
                    return; // user cancelled

                using var bmp = new Bitmap(region.Value.Width, region.Value.Height);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(region.Value.Location, Point.Empty, region.Value.Size);
                }

                string xml = await OCRBitmapToMarkdownAsync(bmp);

                if (!string.IsNullOrWhiteSpace(xml))
                {
                    await HandleScreenSendAsync(xml);
                }
                else
                {
                    MessageBox.Show("No text detected.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Capture failed: {ex.Message}");
            }
        }

        private sealed class LineInfo
        {
            public int BlockIndex { get; set; }
            public int ParaIndex { get; set; }
            public int LineIndex { get; set; }
            public string Text { get; set; } = "";
            public int X { get; set; }
            public int Y { get; set; }
            public int W { get; set; }
            public int H { get; set; }
            public float Confidence { get; set; }
        }

        private sealed class MergedElement
        {
            public string Type { get; set; } = "paragraph"; // "heading" or "paragraph"
            public string Text { get; set; } = "";
            public int X { get; set; }
            public int Y { get; set; }
            public int W { get; set; }
            public int H { get; set; }
            public float Confidence { get; set; } // average confidence of merged lines
            public List<LineInfo> Lines { get; } = new();
        }
        private static Bitmap PreprocessForOCR(Bitmap bmp)
        {
            // Convert to grayscale
            var grayBmp = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(grayBmp))
            {
                var colorMatrix = new ColorMatrix(new float[][]
                {
            new float[] {0.299f, 0.299f, 0.299f, 0, 0},
            new float[] {0.587f, 0.587f, 0.587f, 0, 0},
            new float[] {0.114f, 0.114f, 0.114f, 0, 0},
            new float[] {0,      0,      0,      1, 0},
            new float[] {0,      0,      0,      0, 1}
                });
                var attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);
                g.DrawImage(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height),
                    0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attributes);
            }

            // Optional: simple threshold
            for (int y = 0; y < grayBmp.Height; y++)
            {
                for (int x = 0; x < grayBmp.Width; x++)
                {
                    Color c = grayBmp.GetPixel(x, y);
                    byte v = c.R; // R=G=B in grayscale
                    grayBmp.SetPixel(x, y, v > 160 ? Color.White : Color.Black);
                }
            }
            return grayBmp;
        }
        private async Task<string> OCRBitmapToMarkdownAsync(Bitmap bmp)
        {
            return await Task.Run(() =>
            {
                using var preprocessed = PreprocessForOCR(bmp);
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);

                using var engine = new TesseractEngine(@"./tessdata-main", "eng+fin", EngineMode.LstmOnly);
                using var img = Pix.LoadFromMemory(ms.ToArray());
                using var page = engine.Process(img, PageSegMode.SingleBlock);

                var pageConf = page.GetMeanConfidence();

                var lines = new List<LineInfo>();
                using (var iter = page.GetIterator())
                {
                    iter.Begin();
                    int blockIdx = -1, paraIdx = -1, lineIdx = -1;
                    do
                    {
                        blockIdx++;
                        paraIdx = -1;
                        do
                        {
                            paraIdx++;
                            lineIdx = -1;
                            do
                            {
                                lineIdx++;
                                var text = (iter.GetText(PageIteratorLevel.TextLine) ?? "").Trim();
                                if (string.IsNullOrEmpty(text))
                                    continue;
                                if (iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out var rect))
                                {
                                    var conf = iter.GetConfidence(PageIteratorLevel.TextLine);
                                    lines.Add(new LineInfo
                                    {
                                        BlockIndex = blockIdx,
                                        ParaIndex = paraIdx,
                                        LineIndex = lineIdx,
                                        Text = text,
                                        X = rect.X1,
                                        Y = rect.Y1,
                                        W = rect.Width,
                                        H = rect.Height,
                                        Confidence = conf
                                    });
                                }
                            }
                            while (iter.Next(PageIteratorLevel.TextLine));
                        }
                        while (iter.Next(PageIteratorLevel.Para));
                    }
                    while (iter.Next(PageIteratorLevel.Block));
                }

                if (lines.Count == 0)
                    return string.Empty;

                var elements = MergeLinesIntoElements(lines);

                var sb = new StringBuilder();
                sb.AppendLine($"<!-- ocr-screen w={bmp.Width} h={bmp.Height} c={pageConf:0.##} -->");

                foreach (var el in elements)
                {
                    // Optionally add inline geometry/confidence as HTML comment (remove/comment this if not needed)
                    string meta = $"<!-- x={el.X} y={el.Y} w={el.W} h={el.H} c={el.Confidence:0.##} -->";
                    string text = EscapeMarkdown(el.Text);

                    if (el.Type == "heading")
                    {
                        sb.AppendLine($"# {text} {meta}");
                    }
                    else
                    {
                        sb.AppendLine($"{text} {meta}");
                    }
                    sb.AppendLine();
                }
                return sb.ToString();
            });
        }

        // Minimal escaping for Markdown special characters
        private static string EscapeMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text
                .Replace("\\", "\\\\")
                .Replace("`", "\\`")
                .Replace("*", "\\*")
                .Replace("_", "\\_")
                .Replace("{", "\\{")
                .Replace("}", "\\}")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace("(", "\\(")
                .Replace(")", "\\)")
                .Replace("#", "\\#") // Only if not at line start
                .Replace("+", "\\+")
                .Replace("-", "\\-")
                .Replace(".", "\\.")
                .Replace("!", "\\!");
        }

        // Simple heuristic to merge lines into larger, readable elements:
        // - "heading": lines significantly taller than average (suggesting larger text)
        // - "paragraph": consecutive lines with small vertical gaps and aligned horizontally
        private List<MergedElement> MergeLinesIntoElements(List<LineInfo> lines)
        {
            var sorted = lines
                .OrderBy(l => l.Y)
                .ThenBy(l => l.X)
                .ToList();

            double avgHeight = sorted.Average(l => l.H);
            double headingThreshold = avgHeight * 1.35;

            var result = new List<MergedElement>();

            bool OverlapsHorizontally(LineInfo a, LineInfo b, double minOverlapRatio)
            {
                int left = Math.Max(a.X, b.X);
                int right = Math.Min(a.X + a.W, b.X + b.W);
                int overlap = Math.Max(0, right - left);
                int minWidth = Math.Min(a.W, b.W);
                return minWidth > 0 && ((double)overlap / minWidth) >= minOverlapRatio;
            }

            bool IsCloseVertically(LineInfo a, LineInfo b, double maxGap)
            {
                int gap = b.Y - (a.Y + a.H);
                return gap >= 0 && gap <= maxGap;
            }

            foreach (var line in sorted)
            {
                bool isHeading = line.H >= headingThreshold;

                if (result.Count == 0)
                {
                    result.Add(NewElementFromLine(line, isHeading ? "heading" : "paragraph"));
                    continue;
                }

                var last = result[^1];

                if (last.Type == "heading" && isHeading &&
                    IsCloseVertically(last.Lines[^1], line, Math.Max(10, avgHeight * 0.6)) &&
                    OverlapsHorizontally(last.Lines[^1], line, 0.4))
                {
                    AppendLine(last, line, " ");
                    continue;
                }

                if (last.Type == "paragraph" && !isHeading &&
                    IsCloseVertically(last.Lines[^1], line, Math.Max(10, avgHeight * 0.8)) &&
                    OverlapsHorizontally(last.Lines[^1], line, 0.3))
                {
                    AppendLine(last, line, " ");
                    continue;
                }

                result.Add(NewElementFromLine(line, isHeading ? "heading" : "paragraph"));
            }

            return result;

            static MergedElement NewElementFromLine(LineInfo l, string type)
            {
                var el = new MergedElement
                {
                    Type = type,
                    Text = l.Text,
                    X = l.X,
                    Y = l.Y,
                    W = l.W,
                    H = l.H,
                    Confidence = l.Confidence
                };
                el.Lines.Add(l);
                return el;
            }

            static void AppendLine(MergedElement el, LineInfo l, string joinWith)
            {
                el.Text = string.IsNullOrWhiteSpace(el.Text) ? l.Text : $"{el.Text}{joinWith}{l.Text}";
                int x1 = Math.Min(el.X, l.X);
                int y1 = Math.Min(el.Y, l.Y);
                int x2 = Math.Max(el.X + el.W, l.X + l.W);
                int y2 = Math.Max(el.Y + el.H, l.Y + l.H);
                el.X = x1; el.Y = y1; el.W = x2 - x1; el.H = y2 - y1;
                el.Lines.Add(l);
                el.Confidence = (float)(el.Lines.Average(li => li.Confidence));
            }
        }

        

        private async Task HandleSendAsync(string userText)
        {
            var text = (userText ?? "").Trim();
            if (text.Length == 0) return;
            ClearFollowups();
            StopStreaming();

            await AddUserMessage(text);

            _history.Add(new ChatMessage { role = "user", content = text });
            _input.Clear();

            SetBusy(true);
            _assistantBuf.Clear();
            _cts = new CancellationTokenSource();

            try
            {
                var full = await Task.Run(() =>
                    _client.StreamChatAsync(
                        _history,
                        onDelta: delta => UI(() => AppendAssistantDelta(delta)),
                        temperature: 0.7,
                        maxTokens: null,
                        nPredict: null,
                        ct: _cts.Token)
                ).ConfigureAwait(false);

                if (string.IsNullOrEmpty(full))
                    UI(() => AppendAssistantDelta("[no content]"));

                _history.Add(new ChatMessage { role = "assistant", content = _assistantBuf.ToString() });

                // 5. Generate follow-ups
                _ = ShowFollowUpsAsync(CancellationToken.None);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UI(() => AppendAssistantDelta("\n[error] " + ex.Message));
            }
            finally
            {
                UI(async () =>
                {
                    SetBusy(false);
                    // finalize assistant bubble so the next one doesn't overwrite
                    await _chatWeb.CoreWebView2.ExecuteScriptAsync("finishAssistantMessage();");
                });
            }
        }
        // handle sending user text
        private async Task HandleScreenSendAsync(string userText)
        {
            var text = (userText ?? string.Empty).Trim();
            if (text.Length == 0) return;


            string BuildUserMessage(string t) =>
            $@"Below is my computer screen capture text:

            ---
            {t}
            ---";


            StopStreaming();

            var userMessage = BuildUserMessage(text);

            await AddUserMessage(userMessage);
            _history.Add(new ChatMessage { role = "user", content = userMessage });

            _input.Clear();
            SetBusy(true);
            _assistantBuf.Clear();
            _cts = new CancellationTokenSource();

            try
            {
                var full = await Task.Run(() =>
                    _client.StreamChatAsync(
                        _history,
                        onDelta: delta => UI(() => AppendAssistantDelta(delta)),
                        temperature: 0.2,
                        maxTokens: null,
                        nPredict: null,
                        ct: _cts.Token)
                ).ConfigureAwait(false);

                if (string.IsNullOrEmpty(full))
                    UI(() => AppendAssistantDelta("[no content]"));

                _history.Add(new ChatMessage { role = "assistant", content = full });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UI(() => AppendAssistantDelta("\n[error] " + ex.Message));
            }
            finally
            {
                UI(async () =>
                {
                    SetBusy(false);
                    await _chatWeb.CoreWebView2.ExecuteScriptAsync("finishAssistantMessage();");
                });
            }
        }

        // add user message
        private async Task AddUserMessage(string text)
        {
            string safe = EscapeJs(text);
            await _chatWeb.CoreWebView2.ExecuteScriptAsync($"appendMessage('user', `{safe}`);");
        }

        // append assistant stream delta
        private async void AppendAssistantDelta(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            _assistantBuf.Append(delta);
            string safe = EscapeJs(_assistantBuf.ToString());
            await _chatWeb.CoreWebView2.ExecuteScriptAsync($"updateAssistantMessage(`{safe}`);");
        }

        // set busy state
        private void SetBusy(bool busy)
        {
            _sendBtn.Enabled = !busy;
            _stopBtn.Visible = busy;
            _status.Text = busy ? "Generating…" : "Ready";
        }

        // clear chat
        private void ClearChat()
        {
            StopStreaming();
            ClearFollowups();
            _history.Clear();
            _assistantBuf.Clear();
            _status.Text = "Cleared";
            _chatWeb.CoreWebView2?.NavigateToString(BuildChatPageHtml());
            _history.Add(new ChatMessage
            {
                role = "system",
                content = "You are a helpful assistant that explains things clearly and concisely."
            });
        }

        // stop streaming
        private void StopStreaming()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            SetBusy(false);
        }

        // UI thread invoke
        private void UI(Action action)
        {
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }

        // escape js string
        private static string EscapeJs(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("`", "\\`").Replace("${", "\\${");
        }

        // html page with markdown and highlight.js
        private string BuildChatPageHtml()
        {
            return @"<!doctype html>
<html>
<head>
<meta charset='utf-8'>
<title>Chat Assistant</title>
<style>
body {
    margin:0; padding:0; font-family:'Segoe UI',sans-serif;
    background:#12172e; color:#e3eafc;
}
#topbar {
    position:sticky; top:0; left:0; right:0;
    background:rgba(16,20,38,0.97); z-index:10;
    display:flex; align-items:center; justify-content:space-between;
    padding:0.6em 2em; border-bottom:1px solid #29304b;
    box-shadow:0 2px 8px #0002;
}
#topbar h1 {
    font-size:1.3em; letter-spacing:0.06em; margin:0; color:#9ec8fa; font-weight:500;
}
#topbar button {
    background:#223469; color:#e3eafc;
    border:0; border-radius:6px; padding:0.45em 1.1em;
    font-size:1em; cursor:pointer; transition:background .18s;
}
#topbar button:hover { background:#2b447a; }
#chat {
    width:90%;              
    max-width:none;           
    margin:3em;       
    padding:0;                
}
.message {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    width: 100%;
    max-width: 100%;
    margin: 1.2em 0;
    padding: 1.2em 4vw;
    border-radius: 0;
    background: linear-gradient(110deg, #192245 90%, #232950 100%);
    box-shadow: 0 2px 8px #1115;
    font-size: 1em;
    line-height: 1.0;
    position: relative;
    white-space: pre-wrap;
    word-break: break-word;
    overflow-wrap: break-word;
    overflow: hidden;
    box-sizing: border-box;
}
.message.user {
    background:linear-gradient(110deg,#26337d 80%,#334592 100%);
    color:#d3e5ff;
}
.message.assistant { }
.message .meta {
    font-size:0.80em; color:#8ea2c8; margin-bottom:0.5em; user-select:none;
}
code {
    font-family:'Cascadia Code',Consolas,monospace;
    background:#181f3a; border:1px solid #2b3c60; border-radius:6px;
    padding:0 0.25em; font-size:1em;
}
pre {
    background:#181f3a; border:1px solid #2b3c60; border-radius:8px;
    padding:1.15em 1.5em 1.15em 1.5em; overflow:auto;
    margin-bottom:0.4em; position:relative;
}
pre code {
    background:transparent; border:0; padding:0;
    font-size:1.08em;
}
.code-copy-btn {
    position:absolute; top:10px; right:16px;
    background:#26337d; color:#cfe7ff;
    border:0; border-radius:5px; font-size:0.95em;
    padding:3px 13px; cursor:pointer; opacity:0.7;
    transition:background 0.13s,opacity 0.15s;
    z-index:2;
    display:none;
}
pre:hover .code-copy-btn { display:inline-block; opacity:1; }
.code-copy-btn:active { background:#173060; }
@media (max-width:700px) {
    #chat { width:100vw; padding:0; }
    .message { font-size:1em; padding:0.7em 2vw 0.7em 2vw; }
}
::-webkit-scrollbar { width:8px; background:#191f37; }
::-webkit-scrollbar-thumb { background:#223469; border-radius:5px; }
</style>
<link rel='stylesheet' href='https://cdn.jsdelivr.net/npm/highlight.js@11/styles/github-dark.min.css'>
</head>
<body>
<div id='topbar'>
    <h1>💬 Chat Assistant</h1>
    <button onclick='printToPDF()' title='Print or Save as PDF'>🖨️ Print PDF</button>
</div>
<div id='chat'></div>
<script src='https://cdn.jsdelivr.net/npm/marked/marked.min.js'></script>
<script src='https://cdn.jsdelivr.net/npm/dompurify@3/dist/purify.min.js'></script>
<script src='https://cdn.jsdelivr.net/npm/highlight.js@11/lib/common.min.js'></script>
<script>
let currentAssistant = null;
function nowTime() {
    const d = new Date();
    return d.toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'});
}
function appendMessage(role, md) {
    const msg = document.createElement('div');
    msg.className = 'message ' + role;
    msg.innerHTML = `<div class='meta'>${role==='user'?'You':'Assistant'} · <span>${nowTime()}</span></div>`;
    const content = document.createElement('div');
    renderMarkdown(content, md);
    msg.appendChild(content);
    document.getElementById('chat').appendChild(msg);
    if(role==='assistant'){ currentAssistant = content; }
    window.scrollTo(0, document.body.scrollHeight);
}
function updateAssistantMessage(md) {
    if(!currentAssistant){ appendMessage('assistant', md); return; }
    renderMarkdown(currentAssistant, md);
}
function finishAssistantMessage() {
    currentAssistant = null;
    window.scrollTo(0, document.body.scrollHeight);
}
function renderMarkdown(el, md) {
    try {
        marked.setOptions({ gfm:true, breaks:true, headerIds:false, mangle:false });
        let html = marked.parse(md ?? '');
        const temp = document.createElement('div');
        temp.innerHTML = DOMPurify.sanitize(html, { USE_PROFILES:{ html:true } });
        temp.querySelectorAll('pre').forEach(pre => {
            const btn = document.createElement('button');
            btn.className = 'code-copy-btn';
            btn.innerText = 'Copy';
            btn.title = 'Copy code to clipboard';
            btn.onclick = function() {
                const code = pre.querySelector('code');
                if(code){
                    navigator.clipboard.writeText(code.innerText).then(()=>{
                        btn.innerText = 'Copied!';
                        setTimeout(()=>{btn.innerText='Copy';}, 1700);
                    });
                }
            };
            pre.insertBefore(btn, pre.firstChild);
        });
        el.innerHTML = temp.innerHTML;
        el.querySelectorAll('pre code').forEach(e=>{ try{ hljs.highlightElement(e); }catch{} });
    } catch { el.textContent = md ?? ''; }
}
function printToPDF() {
    document.querySelectorAll('#topbar button').forEach(btn=>btn.style.visibility='hidden');
    window.print();
    setTimeout(()=>{document.querySelectorAll('#topbar button').forEach(btn=>btn.style.visibility='visible');}, 1000);
}
</script>
</body>
</html>";
        }
    }
}
