using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransformerNavigator
{
    public sealed class OpenAIChatClientAdapter : IChatClient
    {
        private readonly OpenAIChatClient _inner;
        public OpenAIChatClientAdapter(OpenAIChatClient inner) => _inner = inner;

        public void SetTemperature(double? temperature) => _inner.SetTemperature(temperature);
        public void AddUserMessage(string message) => _inner.AddUserMessage(message);
        public void AddAssistantMessage(string message) => _inner.AddAssistantMessage(message);
        public void SetFinalInstructionMessage(string instruction) => _inner.SetFinalInstructionMessage(instruction);
        public Task<string> GetChatCompletionAsync(CancellationToken ct = default) => _inner.GetChatCompletionAsync();
    }
}
