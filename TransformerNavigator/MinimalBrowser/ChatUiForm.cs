using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MinimalBrowser
{
    public sealed class ChatUiForm : Form
    {
        private readonly DoubleBufferedFlow _messagesPanel;
        private readonly TextBox _input;
        private readonly Button _sendBtn;
        private readonly Button _stopBtn;
        private readonly Button _clearBtn;
        private readonly Label _status;

        private readonly List<ChatMessage> _history = new();
        private readonly LlamaSseClient _client;
        private CancellationTokenSource _cts;

        private MessageBubble _assistantBubble;
        private readonly StringBuilder _assistantBuf = new StringBuilder();

        public ChatUiForm(string model = null, string baseUrl = null, string apiKey = null)
        {
            Text = "Chat";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 980;
            Height = 720;

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

            _messagesPanel = new DoubleBufferedFlow
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = Color.FromArgb(0x0f, 0x12, 0x20),
                Padding = new Padding(10)
            };
            root.Controls.Add(_messagesPanel, 0, 0);

            _messagesPanel.Resize += (_, __) =>
            {
                int w = GetBubbleWidth();
                foreach (Control c in _messagesPanel.Controls)
                {
                    if (c is MessageBubble mb)
                        mb.Width = w;
                }
            };

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
                Size = new Size(Width - 240, 80)
            };
            composer.Controls.Add(_input);

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

            _status = new Label
            {
                Text = "Ready",
                AutoSize = true,
                ForeColor = Color.FromArgb(0x9f, 0xbc, 0xe8),
                Location = new Point(_input.Left, _input.Bottom + 8),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom
            };
            composer.Controls.Add(_status);

            composer.Resize += (_, __) =>
            {
                _input.Width = composer.ClientSize.Width - 8 - 8 - 200;
                _sendBtn.Left = _input.Right + 8;
                _stopBtn.Left = _input.Right + 8;
                _clearBtn.Left = _sendBtn.Right + 8;
            };

            _input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    _ = HandleSendAsync(_input.Text);
                }
            };

            FormClosed += (_, __) => StopStreaming();
        }

        private async Task HandleSendAsync(string userText)
        {
            var text = (userText ?? "").Trim();
            if (text.Length == 0) return;

            StopStreaming();

            AddUserBubble(text);

            _history.Add(new ChatMessage { role = "user", content = text });
            _input.Clear();

            SetBusy(true);
            _assistantBuf.Clear();
            StartAssistantBubble();

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
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                UI(() => AppendAssistantDelta("\n[error] " + ex.Message));
            }
            finally
            {
                UI(() =>
                {
                    FinishAssistantBubble();
                    SetBusy(false);
                });
            }
        }

        private int GetBubbleWidth() => Math.Max(300, _messagesPanel.ClientSize.Width - 80);

        private void AddUserBubble(string text)
        {
            var bubble = new MessageBubble(isUser: true)
            {
                Width = GetBubbleWidth()
            };
            _messagesPanel.Controls.Add(bubble);
            _ = bubble.SetMarkdownAsync(text);
            _messagesPanel.ScrollControlIntoView(bubble);
        }

        private void StartAssistantBubble()
        {
            _assistantBubble = new MessageBubble(isUser: false)
            {
                Width = GetBubbleWidth()
            };
            _messagesPanel.Controls.Add(_assistantBubble);
            _messagesPanel.ScrollControlIntoView(_assistantBubble);
        }

        private void AppendAssistantDelta(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            if (_assistantBubble == null) StartAssistantBubble();

            _assistantBuf.Append(delta);
            _ = _assistantBubble.SetMarkdownAsync(_assistantBuf.ToString());

            if (Control.MouseButtons != MouseButtons.Left)
                _messagesPanel.ScrollControlIntoView(_assistantBubble);
        }

        private void FinishAssistantBubble()
        {
            _assistantBubble = null;
        }

        private void SetBusy(bool busy)
        {
            _sendBtn.Enabled = !busy;
            _stopBtn.Visible = busy;
            _status.Text = busy ? "Generating…" : "Ready";
        }

        private void ClearChat()
        {
            StopStreaming();
            _history.Clear();
            _messagesPanel.Controls.Clear();
            _assistantBuf.Clear();
            _assistantBubble = null;
            _status.Text = "Cleared";
        }

        private void StopStreaming()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            SetBusy(false);
        }

        private void UI(Action action)
        {
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }

        private sealed class DoubleBufferedFlow : FlowLayoutPanel
        {
            public DoubleBufferedFlow()
            {
                DoubleBuffered = true;
            }
        }
    }
}