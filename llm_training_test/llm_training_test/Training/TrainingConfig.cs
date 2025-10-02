using System;

namespace TinyGptDemo.Training
{
    public record TrainingConfig
    {
        public int BatchSize { get; init; }
        public float LearningRate { get; init; }
        public float WeightDecay { get; init; }
        public int MaxEpochs { get; init; }
        public int LogEvery { get; init; }
        public float TargetAverageLoss { get; init; }
        public int MaxHistoryPoints { get; init; }
        public int DurationWindow { get; init; }

        public static TrainingConfig WithDefaults(int vocabSize) => new TrainingConfig
        {
            BatchSize = 16,
            LearningRate = 3e-3f,
            WeightDecay = 0f,
            MaxEpochs = 1500,
            LogEvery = 50,
            TargetAverageLoss = MathF.Max(0.8f, MathF.Log(vocabSize) * 0.85f),
            MaxHistoryPoints = 600,
            DurationWindow = 200
        };
    }

    public readonly record struct TrainingSummary(int EpochsCompleted, float FinalLoss);
}