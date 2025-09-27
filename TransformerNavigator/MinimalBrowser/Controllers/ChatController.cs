using MinimalBrowser.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TransformerNavigator;

namespace MinimalBrowser.Controllers
{
    public sealed class BusyChangedEventArgs : EventArgs
    {
        public bool IsBusy { get; }
        public string StatusText { get; }
        public BusyChangedEventArgs(bool isBusy, string statusText)
        {
            IsBusy = isBusy;
            StatusText = statusText;
        }
    }

    public sealed class FollowUpsReadyEventArgs : EventArgs
    {
        public IReadOnlyList<string> Questions { get; }
        public FollowUpsReadyEventArgs(IReadOnlyList<string> questions) => Questions = questions ?? Array.Empty<string>();
    }

    public interface IChatService
    {
        Task<string> StreamAsync(
            List<ChatMessage> history,
            Action<string> onDelta,
            double temperature,
            int? maxTokens,
            int? nPredict,
            CancellationToken ct);
    }

    public interface IFollowUpService
    {
        Task<FollowUpResult> GenerateSuggestionsAsync(IList<ChatMessage> history, CancellationToken ct);
    }

    public interface IChatRenderer
    {
        Task LoadAsync();
        Task AppendUserMessageAsync(string text);
        Task UpdateAssistantMessageAsync(string fullAssistantText);
        Task FinishAssistantMessageAsync();
    }

    public sealed class FollowUpResult
    {
        public List<string> Questions { get; set; } = new();
    }

    public sealed class ChatController : IDisposable
    {
        private readonly IChatService _chat;
        private readonly FollowUpService _followUps;
        private readonly IChatRenderer _renderer;

        private readonly List<ChatMessage> _history = new();
        private readonly StringBuilder _assistantBuf = new();

        private CancellationTokenSource _cts;

        public event EventHandler<BusyChangedEventArgs> BusyChanged;
        public event EventHandler<FollowUpsReadyEventArgs> FollowUpsReady;

        public ChatController(IChatService chat, FollowUpService followUps, IChatRenderer renderer)
        {
            _chat = chat;
            _followUps = followUps;
            _renderer = renderer;
        }

        public async Task InitializeAsync()
        {
            _history.Clear();
            _history.Add(new ChatMessage
            {
                role = "system",
                content = "You are a helpful assistant that explains things clearly and concisely."
            });
            // no rendering needed here; first messages will display as user/assistant
        }

        public async Task ClearAsync()
        {
            Stop();
            _history.Clear();
            _assistantBuf.Clear();
            await _renderer.LoadAsync();
            await InitializeAsync();
            OnBusyChanged(false, "Ready");
        }

        public async Task SendAsync(string userText)
        {
            var text = (userText ?? string.Empty).Trim();
            if (text.Length == 0) return;

            Stop();
            _assistantBuf.Clear();

            await _renderer.AppendUserMessageAsync(text);
            _history.Add(new ChatMessage { role = "user", content = text });

            OnBusyChanged(true, "Generating…");
            _cts = new CancellationTokenSource();

            try
            {
                var full = await _chat.StreamAsync(
                    _history,
                    onDelta: delta => AppendAssistantDelta(delta),
                    temperature: 0.8,
                    maxTokens: null,
                    nPredict: null,
                    ct: _cts.Token);

                if (string.IsNullOrEmpty(full))
                    AppendAssistantDelta("[no content]");

                _history.Add(new ChatMessage { role = "assistant", content = _assistantBuf.ToString() });

                _ = TryShowFollowUpsAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppendAssistantDelta("\n[error] " + ex.Message);
            }
            finally
            {
                await _renderer.FinishAssistantMessageAsync();
                OnBusyChanged(false, "Ready");
            }
        }

        public async Task SendScreenAsync(string ocrMarkdown)
        {
            var text = (ocrMarkdown ?? string.Empty).Trim();
            if (text.Length == 0) return;

            Stop();
            _assistantBuf.Clear();

            string userMessage =
            $@"Below is my computer screen capture text:

            ---
            {text}
            ---";

            await _renderer.AppendUserMessageAsync(userMessage);
            _history.Add(new ChatMessage { role = "user", content = userMessage });

            OnBusyChanged(true, "Generating…");
            _cts = new CancellationTokenSource();

            try
            {
                var full = await _chat.StreamAsync(
                    _history,
                    onDelta: delta => AppendAssistantDelta(delta),
                    temperature: 0.8,
                    maxTokens: null,
                    nPredict: null,
                    ct: _cts.Token);

                if (string.IsNullOrEmpty(full))
                    AppendAssistantDelta("[no content]");

                _history.Add(new ChatMessage { role = "assistant", content = _assistantBuf.ToString() });

                _ = TryShowFollowUpsAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppendAssistantDelta("\n[error] " + ex.Message);
            }
            finally
            {
                await _renderer.FinishAssistantMessageAsync();
                OnBusyChanged(false, "Ready");
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            OnBusyChanged(false, "Ready");
        }

        private async Task TryShowFollowUpsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                var followup = await _followUps.GenerateSuggestionsAsync(_history, timeout.Token);
                FollowUpsReady?.Invoke(this, new FollowUpsReadyEventArgs(followup?.Questions ?? new List<string>()));
            }
            catch
            {
                FollowUpsReady?.Invoke(this, new FollowUpsReadyEventArgs(Array.Empty<string>()));
            }
        }

        private void AppendAssistantDelta(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            _assistantBuf.Append(delta);
            _ = _renderer.UpdateAssistantMessageAsync(_assistantBuf.ToString());
        }

        private void OnBusyChanged(bool busy, string text) => BusyChanged?.Invoke(this, new BusyChangedEventArgs(busy, text));

        public void Dispose()
        {
            Stop();
        }
    }
}