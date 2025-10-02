namespace TinyGptDemo.Model
{
    public record ModelConfig
    {
        public int ContextLength { get; init; }
        public int EmbeddingDim { get; init; }
        public int Layers { get; init; }
        public int Heads { get; init; }
        public int HiddenDim { get; init; }

        public static ModelConfig Default() => new ModelConfig
        {
            ContextLength = 32,
            EmbeddingDim = 64,
            Layers = 2,
            Heads = 1,
            HiddenDim = 256
        };
    }
}