using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransformerNavigator
{
    public interface IChatClient
    {
        void SetTemperature(double? temperature);
        void AddUserMessage(string message);
        void AddAssistantMessage(string message);
        void SetFinalInstructionMessage(string instruction);
        Task<string> GetChatCompletionAsync(CancellationToken ct = default);
    }
}
