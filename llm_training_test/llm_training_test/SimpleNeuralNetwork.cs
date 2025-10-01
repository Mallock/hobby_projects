namespace llm_training_test
{
    public class StableRnn
    {
        // dimensions
        int vocabSize;
        int hiddenSize;

        // parameters
        double[,] Wxh; // [V x H]
        double[,] Whh; // [H x H]
        double[,] Why; // [H x V]
        double[] bh;   // [H]
        double[] by;   // [V]

        // RNG
        private readonly Random _rng;
        public Random Rng => _rng;

        // toggles
        bool parallelForward = true;  // parallelize within-step forward
        bool parallelBackward = true; // parallelize within-step backward
        int parallelThreshold = 64;   // minimal size to benefit from parallel loops

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

        // initialize weights
        void InitWeights()
        {
            double scaleIH = 0.05;
            double scaleHH = 0.05 / Math.Sqrt(hiddenSize);
            double scaleHO = 0.05;

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

        // zero hidden state
        public double[] ZeroHidden()
        {
            return new double[hiddenSize];
        }

        // forward single step with per-dimension parallelism; avoids capturing out parameter in lambda
        public double[] StepProbs(int xIndex, double[] hPrev, out double[] hNext)
        {
            var hLocal = new double[hiddenSize]; // local buffer for hidden state

            // hidden activation: h = tanh(Wxh[x,:] + hPrev * Whh + bh)
            if (parallelForward && hiddenSize >= parallelThreshold)
            {
                Parallel.For(0, hiddenSize, j =>
                {
                    double sum = bh[j] + Wxh[xIndex, j];
                    for (int u = 0; u < hiddenSize; u++)
                        sum += hPrev[u] * Whh[u, j];
                    hLocal[j] = Math.Tanh(sum);
                });
            }
            else
            {
                for (int j = 0; j < hiddenSize; j++)
                {
                    double sum = bh[j] + Wxh[xIndex, j];
                    for (int u = 0; u < hiddenSize; u++)
                        sum += hPrev[u] * Whh[u, j];
                    hLocal[j] = Math.Tanh(sum);
                }
            }

            // logits = h * Why + by
            double[] logits = new double[vocabSize];
            if (parallelForward && vocabSize >= parallelThreshold)
            {
                Parallel.For(0, vocabSize, k =>
                {
                    double s = by[k];
                    for (int j = 0; j < hiddenSize; j++)
                        s += hLocal[j] * Why[j, k];
                    logits[k] = s;
                });
            }
            else
            {
                for (int k = 0; k < vocabSize; k++)
                {
                    double s = by[k];
                    for (int j = 0; j < hiddenSize; j++)
                        s += hLocal[j] * Why[j, k];
                    logits[k] = s;
                }
            }

            hNext = hLocal; // assign out parameter after parallel sections

            // softmax
            return Softmax(logits);
        }

        // train full sequence with within-step parallelism; time loop remains sequential
        public double TrainSequence(int[] xIdxs, int[] yIdxs, double learningRate)
        {
            int T = xIdxs.Length;

            // forward cache
            double[,] hs = new double[T, hiddenSize];
            double[,] ps = new double[T, vocabSize];

            double[] hPrev = ZeroHidden();
            double loss = 0.0;

            // forward through time (sequential)
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

            // gradients
            double[,] dWxh = new double[vocabSize, hiddenSize];
            double[,] dWhh = new double[hiddenSize, hiddenSize];
            double[,] dWhy = new double[hiddenSize, vocabSize];
            double[] dbh = new double[hiddenSize];
            double[] dby = new double[vocabSize];

            // backprop through time (sequential over t)
            double[] dhNext = new double[hiddenSize];
            double[] dh = new double[hiddenSize];
            double[] dhraw = new double[hiddenSize];

            for (int t = T - 1; t >= 0; t--)
            {
                // dy = y_hat - y
                double[] dy = new double[vocabSize];
                for (int k = 0; k < vocabSize; k++)
                    dy[k] = ps[t, k];
                dy[yIdxs[t]] -= 1.0;

                // dWhy += h_t^T * dy (parallel over hidden rows)
                if (hiddenSize >= parallelThreshold)
                {
                    Parallel.For(0, hiddenSize, j =>
                    {
                        double h_tj = hs[t, j];
                        for (int k = 0; k < vocabSize; k++)
                            dWhy[j, k] += h_tj * dy[k];
                    });
                }
                else
                {
                    for (int j = 0; j < hiddenSize; j++)
                    {
                        double h_tj = hs[t, j];
                        for (int k = 0; k < vocabSize; k++)
                            dWhy[j, k] += h_tj * dy[k];
                    }
                }

                // dby += dy
                for (int k = 0; k < vocabSize; k++)
                    dby[k] += dy[k];

                // dh = Why * dy + dhNext (parallel over hidden)
                if (hiddenSize >= parallelThreshold)
                {
                    Parallel.For(0, hiddenSize, j =>
                    {
                        double sum = 0.0;
                        for (int k = 0; k < vocabSize; k++)
                            sum += dy[k] * Why[j, k];
                        dh[j] = sum + dhNext[j];
                    });
                }
                else
                {
                    for (int j = 0; j < hiddenSize; j++)
                    {
                        double sum = 0.0;
                        for (int k = 0; k < vocabSize; k++)
                            sum += dy[k] * Why[j, k];
                        dh[j] = sum + dhNext[j];
                    }
                }

                // dhraw = (1 - h_t^2) * dh (parallel over hidden)
                if (hiddenSize >= parallelThreshold)
                {
                    Parallel.For(0, hiddenSize, j =>
                    {
                        double h_tj = hs[t, j];
                        dhraw[j] = (1.0 - h_tj * h_tj) * dh[j];
                    });
                }
                else
                {
                    for (int j = 0; j < hiddenSize; j++)
                    {
                        double h_tj = hs[t, j];
                        dhraw[j] = (1.0 - h_tj * h_tj) * dh[j];
                    }
                }

                // dbh += dhraw; dWxh[xIndex,:] += dhraw (parallel over hidden)
                int xIndex = xIdxs[t];
                if (hiddenSize >= parallelThreshold)
                {
                    Parallel.For(0, hiddenSize, j =>
                    {
                        dbh[j] += dhraw[j];
                        dWxh[xIndex, j] += dhraw[j];
                    });
                }
                else
                {
                    for (int j = 0; j < hiddenSize; j++)
                    {
                        dbh[j] += dhraw[j];
                        dWxh[xIndex, j] += dhraw[j];
                    }
                }

                // dWhh += h_{t-1}^T * dhraw (parallel over hidden column j)
                if (hiddenSize >= parallelThreshold)
                {
                    Parallel.For(0, hiddenSize, j =>
                    {
                        for (int u = 0; u < hiddenSize; u++)
                        {
                            double h_prev_u = t > 0 ? hs[t - 1, u] : 0.0;
                            dWhh[u, j] += h_prev_u * dhraw[j];
                        }
                    });
                }
                else
                {
                    for (int j = 0; j < hiddenSize; j++)
                    {
                        for (int u = 0; u < hiddenSize; u++)
                        {
                            double h_prev_u = t > 0 ? hs[t - 1, u] : 0.0;
                            dWhh[u, j] += h_prev_u * dhraw[j];
                        }
                    }
                }

                // dhNext = Whh * dhraw (parallel over u)
                if (hiddenSize >= parallelThreshold)
                {
                    Parallel.For(0, hiddenSize, u =>
                    {
                        double s = 0.0;
                        for (int j = 0; j < hiddenSize; j++)
                            s += dhraw[j] * Whh[u, j];
                        dhNext[u] = s;
                    });
                }
                else
                {
                    for (int u = 0; u < hiddenSize; u++)
                    {
                        double s = 0.0;
                        for (int j = 0; j < hiddenSize; j++)
                            s += dhraw[j] * Whh[u, j];
                        dhNext[u] = s;
                    }
                }
            }

            // clip gradients (parallel)
            ClipInPlace(dWxh, 5.0);
            ClipInPlace(dWhh, 5.0);
            ClipInPlace(dWhy, 5.0);
            ClipInPlace(dbh, 5.0);
            ClipInPlace(dby, 5.0);

            // SGD update (parallel over dimensions)
            Parallel.For(0, vocabSize, i =>
            {
                for (int j = 0; j < hiddenSize; j++)
                    Wxh[i, j] -= learningRate * dWxh[i, j];
            });

            Parallel.For(0, hiddenSize, u =>
            {
                for (int j = 0; j < hiddenSize; j++)
                    Whh[u, j] -= learningRate * dWhh[u, j];
            });

            Parallel.For(0, hiddenSize, j =>
            {
                for (int k = 0; k < vocabSize; k++)
                    Why[j, k] -= learningRate * dWhy[j, k];
            });

            Parallel.For(0, hiddenSize, j =>
            {
                bh[j] -= learningRate * dbh[j];
            });

            Parallel.For(0, vocabSize, k =>
            {
                by[k] -= learningRate * dby[k];
            });

            return loss;
        }

        // softmax with numerical stability
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

        // clip matrix in place (parallel)
        static void ClipInPlace(double[,] m, double clip)
        {
            int r = m.GetLength(0);
            int c = m.GetLength(1);
            Parallel.For(0, r, i =>
            {
                for (int j = 0; j < c; j++)
                {
                    if (m[i, j] > clip) m[i, j] = clip;
                    else if (m[i, j] < -clip) m[i, j] = -clip;
                }
            });
        }

        // clip vector in place (parallel)
        static void ClipInPlace(double[] v, double clip)
        {
            Parallel.For(0, v.Length, i =>
            {
                if (v[i] > clip) v[i] = clip;
                else if (v[i] < -clip) v[i] = -clip;
            });
        }
    }
}