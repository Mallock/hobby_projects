using MinimalBrowser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TransformerNavigator
{
    public sealed class FollowUpAssistant
    {
        private readonly LlamaSseClient _client;

        public FollowUpAssistant(string model, string baseUrl = null, string apiKey = null)
        {
            _client = new LlamaSseClient(model, baseUrl, apiKey);
        }

        public async Task<FollowUpSuggestions> GenerateSuggestionsAsync(
            List<ChatMessage> history,
            CancellationToken ct = default)
        {
            try
            {
                // Use last few non-system turns
                var recent = history.Where(m => m.role == "user" || m.role == "assistant").ToList();
                if (recent.Count == 0)
                    return new FollowUpSuggestions();

                var take = Math.Min(2, recent.Count);
                var lastTurns = recent.Skip(Math.Max(0, recent.Count - take)).ToList();

                var sysPrompt = new ChatMessage
                {
                    role = "system",
                    content =
                        "You are a follow-up question generator. " +
                        "Given the latest exchange(s) between the user and assistant, suggest exactly 4 short follow-up questions for the user to get best responses from the assistant. " +
                        "Rules: " +
                        "1) Return ONLY a JSON array of 4 strings. " +
                        "2) No explanations, no prose, no code fences. " +
                        "3) Keep each question under 120 characters. " +
                        "Example: [\"Can you explain X to me?\",\"What about Y?\",\"How do I Z?\",\"Could you compare A and B?\",\"How about X?\"]"
                };

                var reqHistory = new List<ChatMessage> { sysPrompt };
                reqHistory.AddRange(lastTurns);
                reqHistory.Add(new ChatMessage
                {
                    role = "user",
                    content = "Now tell me what questions I should ask next from the assistant."
                });
                // short timeout to avoid blocking main flow

                var sb = new StringBuilder();
                await _client.StreamChatAsync(
                    reqHistory,
                    delta => sb.Append(delta),
                    temperature: 0.8,
                    maxTokens: 1000,
                    ct: default);

                var raw = sb.ToString();
                var questions = TryExtractStringArray(raw);
                return new FollowUpSuggestions { Questions = questions };
            }
            catch(Exception ex)
            {
                Console.WriteLine("Follow-up suggestion generation failed: " + ex.Message);
                // Never throw to caller
                return new FollowUpSuggestions();
            }
        }

        private static List<string> TryExtractStringArray(string raw)
        {
            var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            // 1) Try direct parse
            if (TryDeserialize(raw, opts, out var arr))
                return Normalize(arr);

            // 2) Try code fence extraction
            var fence = Regex.Match(raw, "```(?:json|JSON)?\\s*(\\[[\\s\\S]*?\\])\\s*```");
            if (fence.Success)
            {
                var inside = fence.Groups[1].Value;
                if (TryDeserialize(inside, opts, out arr))
                    return Normalize(arr);
            }

            // 3) Try to find first JSON array by bracket scanning
            var bracketed = ExtractFirstJsonArray(raw);
            if (bracketed != null && TryDeserialize(bracketed, opts, out arr))
                return Normalize(arr);

            // 4) Fallback: bullets
            var bullets = Regex.Matches(raw, @"^\s*(?:[-•*]\s+)(.+)$", RegexOptions.Multiline)
                               .Cast<Match>()
                               .Select(m => m.Groups[1].Value.Trim())
                               .Where(s => !string.IsNullOrWhiteSpace(s))
                               .ToList();
            if (bullets.Count > 0)
                return Normalize(bullets).Take(4).ToList();

            // 5) Fallback: lines ending with '?'
            var qmarks = raw.Split('\n')
                            .Select(s => s.Trim())
                            .Where(s => s.EndsWith("?"))
                            .ToList();
            if (qmarks.Count > 0)
                return Normalize(qmarks).Take(4).ToList();

            return new List<string>();
        }

        private static bool TryDeserialize(string s, JsonSerializerOptions opts, out List<string> result)
        {
            try
            {
                result = JsonSerializer.Deserialize<List<string>>(s, opts) ?? new List<string>();
                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }

        private static string ExtractFirstJsonArray(string s)
        {
            int start = s.IndexOf('[');
            if (start < 0) return null;

            int depth = 0;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return s.Substring(start, i - start + 1);
                }
            }
            return null;
        }

        private static List<string> Normalize(IEnumerable<string> items)
        {
            return items
                .Select(x => x?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim('“', '”', '"', '’', '‘'))
                .Select(x => x.Length <= 120 ? x : x.Substring(0, 120).TrimEnd() + "…")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();
        }
    }
}