using Microsoft.Web.WebView2.WinForms;
using MinimalBrowser.Util;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TransformerNavigator.Services;

namespace MinimalBrowser.UI
{
    public sealed class ChatUiForm : Form
    {
        private readonly WebView2 _chatWeb;
        private readonly TextBox _input;
        private readonly Button _sendBtn;
        private readonly Button _stopBtn;
        private readonly Button _clearBtn;
        private readonly Button _captureBtn;

        // NEW: image button + progress
        private readonly Button _imgBtn;
        private readonly ProgressBar _imgProgress;
        private CancellationTokenSource _imgCts;

        private readonly Label _status;
        private readonly FlowLayoutPanel _followupPanel;

        private readonly Controllers.ChatController _controller;
        private readonly Services.WebView2ChatRenderer _renderer;
        private readonly Services.ScreenCaptureService _screenCapture;
        private readonly Services.OcrService _ocr;

        // NEW: image generator service
        private readonly Services.IImageGenerator _imageGen;

        public ChatUiForm(string model = null, string baseUrl = null, string apiKey = null, Services.IImageGenerator imageGen = null)
        {
            Text = "Chat";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 980;
            Height = 720;
            TopMost = true;

            // Services
            var chatService = new Services.LlamaChatService(
                model ?? (Environment.GetEnvironmentVariable("LLAMA_MODEL") ?? "gpt"),
                baseUrl ?? Environment.GetEnvironmentVariable("LLAMA_BASE_URL"),
                apiKey ?? Environment.GetEnvironmentVariable("LLAMA_API_KEY"));

            var followUpService = new Services.FollowUpService(
                model ?? (Environment.GetEnvironmentVariable("LLAMA_MODEL") ?? "gpt"),
                baseUrl ?? Environment.GetEnvironmentVariable("LLAMA_BASE_URL"),
                apiKey ?? Environment.GetEnvironmentVariable("LLAMA_API_KEY"));

            _chatWeb = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(0x0f, 0x12, 0x20)
            };
            _renderer = new Services.WebView2ChatRenderer(_chatWeb);

            _controller = new Controllers.ChatController(chatService, followUpService, _renderer);
            _controller.BusyChanged += OnBusyChanged;
            _controller.FollowUpsReady += OnFollowUpsReady;

            _screenCapture = new Services.ScreenCaptureService();
            _ocr = new Services.OcrService();

            // NEW: default image generator (uses your sd.exe example)
            // Change ToolPath/WorkingDirectory/Arguments if your setup differs.
            _imageGen = imageGen ?? new ProcessImageGenerator(
                toolPath: @"C:\hobby_work\stable-diffusion.cpp\build\bin\Release\sd.exe",
                argumentsTemplate: "-m stable-diffusion-v2-1-Q8_0.gguf -p {prompt} -o {out}",
                outputDirectory: System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "MinimalBrowser",
                    "images"
                ),
                workingDirectory: @"C:\hobby_work\stable-diffusion.cpp\build\bin\Release",
                outputExtension: ".png"
            )
            {
                Timeout = TimeSpan.FromMinutes(10),
                FallbackEstimate = TimeSpan.FromMinutes(2)
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            Controls.Add(root);

            var webHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(0x0f, 0x12, 0x20)
            };
            root.Controls.Add(webHost, 0, 0);
            webHost.Controls.Add(_chatWeb);

            var composer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(0x0f, 0x12, 0x20),
                Padding = new Padding(8)
            };
            root.Controls.Add(composer, 0, 1);

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
                Size = new Size(Width - 420, 80) // slightly narrower to fit more buttons
            };
            composer.Controls.Add(_input);

            _sendBtn = MakeButton("Send", new Point(_input.Right + 8, 8), (s, e) => _ = SendFromInputAsync());
            composer.Controls.Add(_sendBtn);

            _stopBtn = MakeButton("Stop", new Point(_input.Right + 8, 48), (s, e) => _controller.Stop(), visible: false);
            composer.Controls.Add(_stopBtn);

            _clearBtn = MakeButton("Clear", new Point(_sendBtn.Right + 8, 8), (s, e) => _ = ClearChatAsync());
            composer.Controls.Add(_clearBtn);

            _captureBtn = MakeButton("Capture", new Point(_clearBtn.Right + 8, 8), async (s, e) => await CaptureAndSendAsync());
            composer.Controls.Add(_captureBtn);

            // NEW: "Generate Image" button
            _imgBtn = MakeButton("Generate Image", new Point(_captureBtn.Right + 8, 8), async (s, e) => await GenerateImageAsync());
            composer.Controls.Add(_imgBtn);

            // NEW: Progress bar for image generation (starts hidden)
            _imgProgress = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Size = new Size(180, 14),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(_imgBtn.Right + 12, 16),
                Visible = false
            };
            composer.Controls.Add(_imgProgress);

            _status = new Label
            {
                Text = "Ready",
                AutoSize = true,
                ForeColor = Color.FromArgb(0x9f, 0xbc, 0xe8),
                Location = new Point(_input.Left, _input.Bottom + 8),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom
            };
            composer.Controls.Add(_status);

            _followupPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                AutoSize = false,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.FromArgb(0x14, 0x19, 0x36),
                Margin = new Padding(0),
                Padding = new Padding(10, 8, 10, 8),
                Visible = false
            };
            webHost.Controls.Add(_followupPanel);

            composer.Resize += (_, __) =>
            {
                _input.Width = composer.ClientSize.Width - 8 - 8 - 420;
                _sendBtn.Left = _input.Right + 8;
                _stopBtn.Left = _input.Right + 8;
                _clearBtn.Left = _sendBtn.Right + 8;
                _captureBtn.Left = _clearBtn.Right + 8;
                _imgBtn.Left = _captureBtn.Right + 8;

                _imgProgress.Left = _imgBtn.Right + 12;
            };

            _input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    _ = SendFromInputAsync();
                }
            };

            _chatWeb.CoreWebView2InitializationCompleted += async (_, e) =>
            {
                if (e.IsSuccess)
                {
                    _chatWeb.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _chatWeb.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                    await _renderer.LoadAsync();
                    await _controller.InitializeAsync();
                }
            };
            _ = _chatWeb.EnsureCoreWebView2Async();

            FormClosed += (_, __) => _controller.Stop();
        }

        private Button MakeButton(string text, Point location, EventHandler handler, bool visible = true)
        {
            var btn = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(0xdb, 0xe8, 0xff),
                BackColor = Color.FromArgb(0x18, 0x20, 0x3a),
                FlatStyle = FlatStyle.Flat,
                Location = location,
                Size = new Size(110, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Visible = visible
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(0x1f, 0x29, 0x46);
            btn.Click += handler;
            return btn;
        }

        private async Task SendFromInputAsync()
        {
            var text = (_input.Text ?? string.Empty).Trim();
            if (text.Length == 0) return;
            ClearFollowups();
            await _controller.SendAsync(text);
            _input.Clear();
        }

        private async Task ClearChatAsync()
        {
            _controller.Stop();
            ClearFollowups();
            await _controller.ClearAsync();
            _status.Text = "Cleared";
        }

        private async Task CaptureAndSendAsync()
        {
            try
            {
                Hide();
                await Task.Delay(200);

                var region = _screenCapture.SelectRegion(this);
                Show();

                if (region == null) return;

                using var bmp = _screenCapture.CaptureRegion(region.Value);
                var markdown = await _ocr.OcrBitmapToMarkdownAsync(bmp);
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    MessageBox.Show("No text detected.");
                    return;
                }

                await _controller.SendScreenAsync(markdown);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MessageBox.Show($"Capture failed: {ex.Message}");
            }
        }

        private void OnBusyChanged(object sender, Controllers.BusyChangedEventArgs e)
        {
            this.UI(() =>
            {
                _sendBtn.Enabled = !e.IsBusy;
                _stopBtn.Visible = e.IsBusy;
                _status.Text = e.StatusText;
            });
        }

        private void OnFollowUpsReady(object sender, Controllers.FollowUpsReadyEventArgs e)
        {
            this.UI(() =>
            {
                ClearFollowups();
                if (e.Questions == null || e.Questions.Count == 0)
                    return;

                _followupPanel.SuspendLayout();
                try
                {
                    foreach (var q in e.Questions)
                    {
                        var btn = CreateFollowupButton(q);
                        _followupPanel.Controls.Add(btn);
                    }
                    _followupPanel.Visible = true;
                }
                finally
                {
                    _followupPanel.ResumeLayout();
                }
            });
        }

        private void ClearFollowups()
        {
            _followupPanel.SuspendLayout();
            try
            {
                _followupPanel.Controls.Clear();
                _followupPanel.Visible = false;
            }
            finally
            {
                _followupPanel.ResumeLayout();
            }
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
                ForeColor = Color.FromArgb(0xdb, 0xe8, 0xff),
                BackColor = Color.FromArgb(0x18, 0x20, 0x3a),
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(10, 6, 10, 6),
                Margin = new Padding(6, 4, 6, 4),
                UseMnemonic = false,
                TabStop = false
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(0x1f, 0x29, 0x46);
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x2b, 0x44, 0x7a);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(0x17, 0x30, 0x60);
            btn.Click += (s, e) => _ = SendFollowupAsync(text);
            return btn;
        }

        private async Task SendFollowupAsync(string question)
        {
            _input.Text = question;
            await SendFromInputAsync();
        }

        // NEW: Generate Image handler
        private async Task GenerateImageAsync()
        {
            try
            {
                var prompt = (_input.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    MessageBox.Show("Please enter an image prompt in the input box first.");
                    return;
                }

                await _renderer.AppendUserMessageAsync($"[Image request]\n{prompt}");

                _imgBtn.Enabled = false;
                _imgProgress.Visible = true;
                _imgProgress.Style = ProgressBarStyle.Marquee;
                _status.Text = "Generating image...";

                _imgCts?.Dispose();
                _imgCts = new CancellationTokenSource();

                var progress = new Progress<double>(p =>
                {
                    this.UI(() =>
                    {
                        if (_imgProgress.Style != ProgressBarStyle.Continuous)
                        {
                            _imgProgress.Style = ProgressBarStyle.Continuous;
                            _imgProgress.Minimum = 0;
                            _imgProgress.Maximum = 100;
                        }
                        int val = Math.Max(0, Math.Min(100, (int)Math.Round(p * 100)));
                        _imgProgress.Value = val == 0 ? 1 : val; // avoid Value must be >= Minimum
                        _status.Text = $"Generating image... {val}%";
                    });
                });

                var imgBytes = await _imageGen.GenerateAsync(prompt, progress, _imgCts.Token);
                if (imgBytes == null || imgBytes.Length == 0)
                {
                    _status.Text = "Image generation returned no data.";
                    return;
                }

                var dataUrl = "data:image/png;base64," + Convert.ToBase64String(imgBytes);
                await _renderer.AppendAssistantImageAsync(dataUrl, $"_{prompt}_");
                _status.Text = "Image ready.";
            }
            catch (OperationCanceledException)
            {
                _status.Text = "Image generation canceled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Image generation failed: {ex.Message}");
                _status.Text = "Image generation failed.";
            }
            finally
            {
                this.UI(() =>
                {
                    _imgProgress.Visible = false;
                    _imgBtn.Enabled = true;
                    _imgProgress.Style = ProgressBarStyle.Marquee;
                });
            }
        }
    }
}