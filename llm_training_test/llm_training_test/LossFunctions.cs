using System;

namespace TinyGptDemo.Utils
{
    public static class LossFunctions
    {
        public static float CrossEntropyWithGrad(float[,,] logits, int[,] targets, float[,,] dLogits)
        {
            int B = logits.GetLength(0);
            int T = logits.GetLength(1);
            int V = logits.GetLength(2);

            float loss = 0f;
            Array.Clear(dLogits, 0, dLogits.Length);

            for (int b = 0; b < B; b++)
            {
                for (int t = 0; t < T; t++)
                {
                    float max = float.NegativeInfinity;
                    for (int v = 0; v < V; v++)
                    {
                        if (logits[b, t, v] > max) max = logits[b, t, v];
                    }

                    float sum = 0f;
                    for (int v = 0; v < V; v++)
                    {
                        float e = MathF.Exp(logits[b, t, v] - max);
                        dLogits[b, t, v] = e;
                        sum += e;
                    }

                    if (sum == 0f) sum = 1f;

                    for (int v = 0; v < V; v++)
                    {
                        dLogits[b, t, v] /= sum;
                    }

                    int y = targets[b, t];
                    float p = MathF.Max(1e-7f, dLogits[b, t, y]);
                    loss += -MathF.Log(p);
                    dLogits[b, t, y] -= 1f;
                }
            }

            return loss / (B * T);
        }
    }

    public static class ProbabilityUtils
    {
        public static float[] Softmax(float[] logits, float temperature = 1f)
        {
            float invTemp = 1f / MathF.Max(temperature, 1e-4f);
            int length = logits.Length;

            float max = float.NegativeInfinity;
            for (int i = 0; i < length; i++)
            {
                float value = logits[i] * invTemp;
                logits[i] = value;
                if (value > max) max = value;
            }

            float sum = 0f;
            var probs = new float[length];

            for (int i = 0; i < length; i++)
            {
                float e = MathF.Exp(logits[i] - max);
                probs[i] = e;
                sum += e;
            }

            if (sum == 0f) sum = 1f;

            for (int i = 0; i < length; i++)
            {
                probs[i] /= sum;
            }

            return probs;
        }

        public static int Sample(float[] probs, Random rng)
        {
            float r = (float)rng.NextDouble();
            float cumulative = 0f;

            for (int i = 0; i < probs.Length; i++)
            {
                cumulative += probs[i];
                if (r <= cumulative)
                {
                    return i;
                }
            }

            return probs.Length - 1;
        }
    }
}
