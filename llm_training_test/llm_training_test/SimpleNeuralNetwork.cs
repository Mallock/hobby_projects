namespace llm_training_test
{
    public class StableRnn
    {
        int vocabSize;
        int hiddenSize;

        double[,] Wxh; // [V x H]
        double[,] Whh; // [H x H]
        double[,] Why; // [H x V]
        double[] bh;   // [H]
        double[] by;   // [V]

        private readonly Random _rng;
        public Random Rng => _rng;

        public StableRnn(int vocabSize, int hiddenSize, Random rng)
        {
            this.vocabSize = vocabSize;
            this.hiddenSize = hiddenSize;
            _rng = rng;

            Wxh = new double[vocabSize, hiddenSize];
            Whh = new double[hiddenSize, hiddenSize];
            Why = new double[hiddenSize, vocabSize];
            bh = new double[hiddenSize];
            by = new double[vocabSize];

            InitWeights();
        }

        void InitWeights()
        {
            double scaleIH = 0.1;
            double scaleHH = 0.1 / Math.Sqrt(hiddenSize);
            double scaleHO = 0.1;

            for (int i = 0; i < vocabSize; i++)
                for (int j = 0; j < hiddenSize; j++)
                    Wxh[i, j] = (_rng.NextDouble() - 0.5) * scaleIH;

            for (int i = 0; i < hiddenSize; i++)
                for (int j = 0; j < hiddenSize; j++)
                    Whh[i, j] = (_rng.NextDouble() - 0.5) * scaleHH;

            for (int i = 0; i < hiddenSize; i++)
                for (int j = 0; j < vocabSize; j++)
                    Why[i, j] = (_rng.NextDouble() - 0.5) * scaleHO;

            for (int j = 0; j < hiddenSize; j++) bh[j] = 0.0;
            for (int k = 0; k < vocabSize; k++) by[k] = 0.0;
        }

        public double[] ZeroHidden()
        {
            return new double[hiddenSize];
        }

        public double[] StepProbs(int xIndex, double[] hPrev, out double[] hNext)
        {
            hNext = new double[hiddenSize];

            for (int j = 0; j < hiddenSize; j++)
            {
                double sum = bh[j];
                sum += Wxh[xIndex, j];
                for (int u = 0; u < hiddenSize; u++)
                    sum += hPrev[u] * Whh[u, j];
                hNext[j] = Math.Tanh(sum);
            }

            double[] logits = new double[vocabSize];
            for (int k = 0; k < vocabSize; k++)
            {
                double sum = by[k];
                for (int j = 0; j < hiddenSize; j++)
                    sum += hNext[j] * Why[j, k];
                logits[k] = sum;
            }

            return Softmax(logits);
        }

        public double TrainSequence(int[] xIdxs, int[] yIdxs, double learningRate)
        {
            int T = xIdxs.Length;

            double[,] hs = new double[T, hiddenSize];
            double[,] ps = new double[T, vocabSize];

            double[] hPrev = ZeroHidden();
            double loss = 0.0;

            for (int t = 0; t < T; t++)
            {
                int x = xIdxs[t];
                var probs = StepProbs(x, hPrev, out double[] hNext);

                for (int j = 0; j < hiddenSize; j++)
                    hs[t, j] = hNext[j];
                for (int k = 0; k < vocabSize; k++)
                    ps[t, k] = probs[k];

                loss += -Math.Log(Math.Max(probs[yIdxs[t]], 1e-12));
                hPrev = hNext;
            }

            double[,] dWxh = new double[vocabSize, hiddenSize];
            double[,] dWhh = new double[hiddenSize, hiddenSize];
            double[,] dWhy = new double[hiddenSize, vocabSize];
            double[] dbh = new double[hiddenSize];
            double[] dby = new double[vocabSize];

            double[] dhNext = new double[hiddenSize];

            for (int t = T - 1; t >= 0; t--)
            {
                double[] dy = new double[vocabSize];
                for (int k = 0; k < vocabSize; k++)
                    dy[k] = ps[t, k];
                dy[yIdxs[t]] -= 1.0;

                for (int j = 0; j < hiddenSize; j++)
                {
                    double h_tj = hs[t, j];
                    for (int k = 0; k < vocabSize; k++)
                        dWhy[j, k] += h_tj * dy[k];
                }
                for (int k = 0; k < vocabSize; k++)
                    dby[k] += dy[k];

                double[] dh = new double[hiddenSize];
                for (int j = 0; j < hiddenSize; j++)
                {
                    double sum = 0.0;
                    for (int k = 0; k < vocabSize; k++)
                        sum += dy[k] * Why[j, k];
                    dh[j] = sum + dhNext[j];
                }

                double[] dhraw = new double[hiddenSize];
                for (int j = 0; j < hiddenSize; j++)
                {
                    double h_tj = hs[t, j];
                    dhraw[j] = (1.0 - h_tj * h_tj) * dh[j];
                }

                for (int j = 0; j < hiddenSize; j++)
                    dbh[j] += dhraw[j];

                int xIndex = xIdxs[t];
                for (int j = 0; j < hiddenSize; j++)
                    dWxh[xIndex, j] += dhraw[j];

                for (int u = 0; u < hiddenSize; u++)
                {
                    double h_prev_u = t > 0 ? hs[t - 1, u] : 0.0;
                    for (int j = 0; j < hiddenSize; j++)
                        dWhh[u, j] += h_prev_u * dhraw[j];
                }

                for (int u = 0; u < hiddenSize; u++)
                {
                    double sum = 0.0;
                    for (int j = 0; j < hiddenSize; j++)
                        sum += dhraw[j] * Whh[u, j];
                    dhNext[u] = sum;
                }
            }

            ClipInPlace(dWxh, 5.0);
            ClipInPlace(dWhh, 5.0);
            ClipInPlace(dWhy, 5.0);
            ClipInPlace(dbh, 5.0);
            ClipInPlace(dby, 5.0);

            for (int i = 0; i < vocabSize; i++)
                for (int j = 0; j < hiddenSize; j++)
                    Wxh[i, j] -= learningRate * dWxh[i, j];

            for (int u = 0; u < hiddenSize; u++)
                for (int j = 0; j < hiddenSize; j++)
                    Whh[u, j] -= learningRate * dWhh[u, j];

            for (int j = 0; j < hiddenSize; j++)
                for (int k = 0; k < vocabSize; k++)
                    Why[j, k] -= learningRate * dWhy[j, k];

            for (int j = 0; j < hiddenSize; j++)
                bh[j] -= learningRate * dbh[j];

            for (int k = 0; k < vocabSize; k++)
                by[k] -= learningRate * dby[k];

            return loss;
        }

        static double[] Softmax(double[] logits)
        {
            double max = logits[0];
            for (int i = 1; i < logits.Length; i++)
                if (logits[i] > max) max = logits[i];

            double sum = 0.0;
            double[] exp = new double[logits.Length];
            for (int i = 0; i < logits.Length; i++)
            {
                exp[i] = Math.Exp(logits[i] - max);
                sum += exp[i];
            }
            for (int i = 0; i < logits.Length; i++)
                exp[i] /= sum;
            return exp;
        }

        static void ClipInPlace(double[,] m, double clip)
        {
            int r = m.GetLength(0);
            int c = m.GetLength(1);
            for (int i = 0; i < r; i++)
                for (int j = 0; j < c; j++)
                {
                    if (m[i, j] > clip) m[i, j] = clip;
                    else if (m[i, j] < -clip) m[i, j] = -clip;
                }
        }

        static void ClipInPlace(double[] v, double clip)
        {
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] > clip) v[i] = clip;
                else if (v[i] < -clip) v[i] = -clip;
            }
        }
    }
}