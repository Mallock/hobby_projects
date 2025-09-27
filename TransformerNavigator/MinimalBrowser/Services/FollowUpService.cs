using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TransformerNavigator;

namespace MinimalBrowser.Services
{
    public sealed class FollowUpService
    {
        private readonly FollowUpAssistant _assistant;

        public FollowUpService(string model, string baseUrl, string apiKey)
        {
            _assistant = new FollowUpAssistant(model, baseUrl, apiKey);
        }

        public async Task<MinimalBrowser.Controllers.FollowUpResult> GenerateSuggestionsAsync(List<ChatMessage> history, CancellationToken ct)
        {
            var res = await _assistant.GenerateSuggestionsAsync(history, ct).ConfigureAwait(false);
            return new MinimalBrowser.Controllers.FollowUpResult
            {
                Questions = res?.Questions ?? new List<string>()
            };
        }
    }
}