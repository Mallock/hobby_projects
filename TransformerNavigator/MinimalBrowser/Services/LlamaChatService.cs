using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MinimalBrowser.Controllers;   // <-- IMPORTANT
using TransformerNavigator;

namespace MinimalBrowser.Services
{
    public sealed class LlamaChatService : IChatService
    {
        private readonly LlamaSseClient _client;

        public LlamaChatService(string model, string baseUrl, string apiKey)
        {
            _client = new LlamaSseClient(model, baseUrl, apiKey);
        }

        public Task<string> StreamAsync(
            List<ChatMessage> history,       // <- match interface: IList, not List
            Action<string> onDelta,
            double temperature,
            int? maxTokens,
            int? nPredict,
            CancellationToken ct)
        {
            return _client.StreamChatAsync(
                history,
                onDelta: onDelta,
                temperature: temperature,
                maxTokens: maxTokens,
                nPredict: nPredict,
                ct: ct);
        }
    }
}