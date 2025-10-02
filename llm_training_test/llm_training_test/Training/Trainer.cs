using System;
using System.Diagnostics;
using TinyGptDemo.Model;
using TinyGptDemo.Utils;

namespace TinyGptDemo.Training
{
    public sealed class Trainer
    {
        private readonly TinyGpt model;
        private readonly TextDataset dataset;
        private readonly TrainingConfig config;
        private readonly ModelConfig modelConfig;
        private readonly Random rng;

        public Trainer(TinyGpt model, TextDataset dataset, TrainingConfig config, ModelConfig modelConfig, Random rng)
        {
            this.model = model;
            this.dataset = dataset;
            this.config = config;
            this.modelConfig = modelConfig;
            this.rng = rng;
        }

        public TrainingSummary Train()
        {
            var sampler = dataset.CreateBatchSampler(modelConfig.ContextLength);
            int stepsPerEpoch = sampler.StepsPerEpoch(config.BatchSize);
            var meter = new TrainingMeter(config.TargetAverageLoss, config.MaxHistoryPoints, config.DurationWindow, config.MaxEpochs);

            float lastAvgLoss = float.NaN;
            int epochsCompleted = 0;
            var trainingTimer = Stopwatch.StartNew();
            for (int epoch = 1; epoch <= config.MaxEpochs; epoch++)
            {
                float epochLoss = 0f;
                var sw = Stopwatch.StartNew();

                for (int step = 0; step < stepsPerEpoch; step++)
                {
                    var batch = sampler.CreateBatch(rng, config.BatchSize);

                    var cache = model.Forward(batch.Inputs);
                    float loss = LossFunctions.CrossEntropyWithGrad(cache.Logits, batch.Targets, cache.DLogits);
                    epochLoss += loss;

                    model.Backward(cache);
                    model.AdamStep(config.LearningRate, config.WeightDecay);
                }

                sw.Stop();

                lastAvgLoss = epochLoss / Math.Max(1, stepsPerEpoch);
                epochsCompleted = epoch;

                meter.RecordEpoch(epoch, lastAvgLoss, sw.Elapsed.TotalMilliseconds);

                if (epoch % config.LogEvery == 0)
                {
                    TimeSpan elapsed = trainingTimer.Elapsed;

                    int estToGoal = meter.EstimatedEpochsToGoal();
                    string estEpochsText = estToGoal == int.MaxValue ? "?" : estToGoal.ToString();

                    int epochsForEta = estToGoal == int.MaxValue
                        ? Math.Min(200, config.MaxEpochs - epoch)
                        : Math.Min(estToGoal, config.MaxEpochs - epoch);

                    TimeSpan eta = meter.EstimatedTimeLeft(epochsForEta);
                    TimeSpan estTotal = eta == TimeSpan.Zero ? elapsed : elapsed + eta;

                    Console.WriteLine(
                        $"Epoch {epoch}, Avg Loss: {lastAvgLoss:F4} | " +
                        $"{meter.AvgEpochMs():F1} ms/epoch | " +
                        $"elapsed: {TimeFormatting.Format(elapsed)} | " +
                        $"ETA: {TimeFormatting.Format(eta)} | " +
                        $"est total: {TimeFormatting.Format(estTotal)} | " +
                        $"est epochs to goal: {estEpochsText}");
                }
            }
            trainingTimer.Stop();
            Console.WriteLine($"Total training time: {TimeFormatting.Format(trainingTimer.Elapsed)}");
            return new TrainingSummary(epochsCompleted, lastAvgLoss);
        }
    }
}