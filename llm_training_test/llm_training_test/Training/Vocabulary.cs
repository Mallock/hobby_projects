using System.Collections.Generic;

namespace TinyGptDemo.Training
{
    public sealed class Vocabulary
    {
        private readonly Dictionary<string, int> tokenToId = new Dictionary<string, int>(System.StringComparer.Ordinal);
        private readonly List<string> idToToken = new List<string>();

        private Vocabulary()
        {
        }

        public static Vocabulary FromTokens(IEnumerable<string> tokens)
        {
            var vocab = new Vocabulary();
            foreach (var token in tokens)
            {
                vocab.Add(token);
            }

            return vocab;
        }

        public int Add(string token)
        {
            if (tokenToId.TryGetValue(token, out int existing))
            {
                return existing;
            }

            int id = idToToken.Count;
            tokenToId[token] = id;
            idToToken.Add(token);
            return id;
        }

        public int GetId(string token) => tokenToId[token];

        public bool TryGetId(string token, out int id) => tokenToId.TryGetValue(token, out id);

        public string this[int id] => idToToken[id];

        public int Size => idToToken.Count;

        public IReadOnlyList<string> Tokens => idToToken;
    }
}