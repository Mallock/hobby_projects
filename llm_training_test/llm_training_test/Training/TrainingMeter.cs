using System;
using System.Collections.Generic;
using System.Linq;

namespace TinyGptDemo.Training
{
    public sealed class TrainingMeter
    {
        private readonly double targetAvgLoss;
        private readonly int maxHistoryPoints;
        private readonly int durationWindow;
        private readonly List<(int epoch, double avgLoss)> history = new();
        private readonly Queue<double> durationsMs = new();
        private double sumDurationsMs;

        public int MaxEpochsCap { get; }

        public TrainingMeter(float targetAvgLoss, int maxHistoryPoints, int durationWindow, int maxEpochsCap)
        {
            this.targetAvgLoss = targetAvgLoss;
            this.maxHistoryPoints = Math.Max(50, maxHistoryPoints);
            this.durationWindow = Math.Max(20, durationWindow);
            MaxEpochsCap = Math.Max(1, maxEpochsCap);
        }

        public void RecordEpoch(int epoch, float avgLoss, double epochMs)
        {
            history.Add((epoch, avgLoss));
            if (history.Count > maxHistoryPoints)
            {
                history.RemoveAt(0);
            }

            durationsMs.Enqueue(epochMs);
            sumDurationsMs += epochMs;

            while (durationsMs.Count > durationWindow)
            {
                sumDurationsMs -= durationsMs.Dequeue();
            }
        }

        public double AvgEpochMs() => durationsMs.Count == 0 ? 0.0 : sumDurationsMs / durationsMs.Count;

        public int EstimatedEpochsToGoal()
        {
            if (history.Count < 20) return int.MaxValue;

            int window = Math.Min(history.Count, Math.Max(60, maxHistoryPoints / 2));
            var slice = history.Skip(history.Count - window).ToArray();
            int m = slice.Length;

            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int i = 0; i < m; i++)
            {
                sx += slice[i].epoch;
                sy += slice[i].avgLoss;
                sxx += slice[i].epoch * slice[i].epoch;
                sxy += slice[i].epoch * slice[i].avgLoss;
            }

            double denom = m * sxx - sx * sx;
            if (Math.Abs(denom) < 1e-9) return int.MaxValue;

            double slope = (m * sxy - sx * sy) / denom;
            double current = slice[m - 1].avgLoss;

            if (slope >= -1e-6) return int.MaxValue;

            double need = (current - targetAvgLoss) / -slope;
            if (need <= 0) return 0;
            if (need > 1e7) return int.MaxValue;

            return (int)Math.Ceiling(need);
        }

        public TimeSpan EstimatedTimeLeft(int remainingEpochs)
        {
            if (remainingEpochs <= 0) return TimeSpan.Zero;

            double msPerEpoch = AvgEpochMs();
            if (msPerEpoch <= 0) return TimeSpan.Zero;

            remainingEpochs = Math.Max(0, Math.Min(remainingEpochs, MaxEpochsCap));
            double totalMs = msPerEpoch * remainingEpochs;

            if (double.IsNaN(totalMs) || double.IsInfinity(totalMs)) return TimeSpan.Zero;

            return TimeSpan.FromMilliseconds(Math.Min(totalMs, 24.0 * 3600 * 1000));
        }
    }
}