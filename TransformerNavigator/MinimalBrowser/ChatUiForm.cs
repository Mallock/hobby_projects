using Microsoft.Web.WebView2.WinForms;
using MinimalBrowser.Util;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TransformerNavigator;
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
        private readonly Button _captureAreaBtn;
        private readonly Button _imgBtn;

        private readonly ProgressBar _imgProgress;
        private CancellationTokenSource _imgCts;

        private readonly Label _status;
        private readonly FlowLayoutPanel _followupPanel;

        private readonly FlowLayoutPanel _buttonBar;

        private readonly Controllers.ChatController _controller;
        private readonly Services.WebView2ChatRenderer _renderer;
        private readonly Services.ScreenCaptureService _screenCapture;
        private readonly Services.OcrService _ocr;

        private readonly Services.IImageGenerator _imageGen;

        public ChatUiForm(string model = null, string baseUrl = null, string apiKey = null, Services.IImageGenerator imageGen = null)
        {
            Text = "Chat";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 980;
            Height = 720;

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

            _imageGen = imageGen ?? new ProcessImageGenerator(
                toolPath: @"C:\hobby_work\stable-diffusion.cpp\build\bin\Release\sd.exe",
                argumentsTemplate: "-m stable-diffusion-xl-base-1.0-FP16.gguf -p {prompt}  -n \"low quality, worst quality, blurry, jpeg artifacts, watermark, text, logo\" -o {out} -W 512 -H 512 --steps 24 --cfg-scale 6.5 --sampling-method dpm++2m --scheduler karras -s 1 -t 8 --vae-tiling -v",
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
                FallbackEstimate = TimeSpan.FromMinutes(4)
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            Controls.Add(root);

            var webHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(0x0f, 0x12, 0x20)
            };
            root.Controls.Add(webHost, 0, 0);
            webHost.Controls.Add(_chatWeb);

            var composerGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(0x0f, 0x12, 0x20),
                Padding = new Padding(8),
                ColumnCount = 1,
                RowCount = 2
            };
            composerGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            composerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(composerGrid, 0, 1);

            _input = new TextBox
            {
                Multiline = true,
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(0xdb, 0xe8, 0xff),
                BackColor = Color.FromArgb(0x0b, 0x0f, 0x1a),
                Dock = DockStyle.Fill
            };
            composerGrid.Controls.Add(_input, 0, 0);

            // Bottom bar with 2 columns: [status | buttons]
            var bottomBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(0x0f, 0x12, 0x20)
            };
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // status
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // buttons fill remaining width
            composerGrid.Controls.Add(bottomBar, 0, 1);

            _status = new Label
            {
                Text = "Ready",
                AutoSize = true,
                ForeColor = Color.FromArgb(0x9f, 0xbc, 0xe8),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 12, 0)
            };
            bottomBar.Controls.Add(_status, 0, 0);

            _buttonBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,                // important: let it shrink/grow with the column
                WrapContents = true,
                FlowDirection = FlowDirection.RightToLeft, // right align
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            bottomBar.Controls.Add(_buttonBar, 1, 0);

            _sendBtn = MakeButton("Send", (s, e) => _ = SendFromInputAsync());
            _stopBtn = MakeButton("Stop", (s, e) => _controller.Stop(), visible: false);
            _clearBtn = MakeButton("Clear", (s, e) => _ = ClearChatAsync());
            _captureBtn = MakeButton("Capture (OCR)", async (s, e) => await CaptureAndSendAsync());
            _captureAreaBtn = MakeButton("Capture Area (Image)", async (s, e) => await CaptureAreaAndSendImageAsync());
            _imgBtn = MakeButton("Generate Image", async (s, e) => await GenerateImageAsync());

            _imgProgress = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Size = new Size(140, 14),
                Visible = false,
                Margin = new Padding(8, 10, 0, 0)
            };

            // Add in natural order; RightToLeft will keep them aligned to the right
            _buttonBar.Controls.Add(_imgProgress);
            _buttonBar.Controls.Add(_stopBtn);
            _buttonBar.Controls.Add(_imgBtn);
            _buttonBar.Controls.Add(_captureAreaBtn);
            _buttonBar.Controls.Add(_captureBtn);
            _buttonBar.Controls.Add(_clearBtn);
            _buttonBar.Controls.Add(_sendBtn);

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

            FormClosed += (_, __) => _controller.Stop();
        }

        private Button MakeButton(string text, EventHandler handler, bool visible = true)
        {
            var btn = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(0xdb, 0xe8, 0xff),
                BackColor = Color.FromArgb(0x18, 0x20, 0x3a),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(150, 32),
                Visible = visible,
                Margin = new Padding(6, 4, 0, 4)
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

        private async Task CaptureAreaAndSendImageAsync()
        {
            try
            {
                var caption = (_input.Text ?? string.Empty).Trim();

                Hide();
                await Task.Delay(150);

                Rectangle? rect = null;
                using (var selector = new ScreenRegionSelector())
                {
                    var dr = selector.ShowDialog(this);
                    if (dr == DialogResult.OK && selector.SelectedRegion.HasValue)
                        rect = selector.SelectedRegion.Value;
                }

                Show();
                Activate();

                if (rect == null) return;

                using var bmp = new Bitmap(rect.Value.Width, rect.Value.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rect.Value.Location, Point.Empty, rect.Value.Size, CopyPixelOperation.SourceCopy);
                }

                using var resized = ResizeForUploadIfNeeded(bmp, maxLongSide: 1280);
                var dataUrl = ToDataUrl(resized, preferJpeg: true);

                await _controller.SendImageAsync(caption, dataUrl);
                _input.Clear();
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
                        _imgProgress.Value = val == 0 ? 1 : val;
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

        // Helpers
        private static string ToDataUrl(Bitmap bmp, bool preferJpeg = false)
        {
            using var ms = new System.IO.MemoryStream();
            if (preferJpeg)
            {
                var enc = GetImageEncoder(ImageFormat.Jpeg);
                if (enc != null)
                {
                    var p = new EncoderParameters(1);
                    p.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
                    bmp.Save(ms, enc, p);
                }
                else
                {
                    bmp.Save(ms, ImageFormat.Jpeg);
                }
                return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
            }
            else
            {
                bmp.Save(ms, ImageFormat.Png);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }

        private static ImageCodecInfo GetImageEncoder(ImageFormat format)
        {
            return ImageCodecInfo.GetImageDecoders().FirstOrDefault(c => c.FormatID == format.Guid);
        }

        private static Bitmap ResizeForUploadIfNeeded(Bitmap src, int maxLongSide)
        {
            int w = src.Width, h = src.Height;
            int longSide = Math.Max(w, h);
            if (longSide <= maxLongSide) return (Bitmap)src.Clone();

            double scale = maxLongSide / (double)longSide;
            int nw = Math.Max(1, (int)Math.Round(w * scale));
            int nh = Math.Max(1, (int)Math.Round(h * scale));

            var dst = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, nw, nh));
            }
            return dst;
        }
    }
}