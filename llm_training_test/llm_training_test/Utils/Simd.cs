using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TinyGptDemo.Utils
{
    internal static class Simd
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            int len = Vector<float>.Count * (a.Length / Vector<float>.Count);
            Vector<float> acc = Vector<float>.Zero;

            int i = 0;
            for (; i < len; i += Vector<float>.Count)
            {
                acc += new Vector<float>(a[i..]) * new Vector<float>(b[i..]);
            }

            float sum = Vector.Dot(acc, Vector<float>.One);
            for (; i < a.Length; i++)
                sum += a[i] * b[i];

            return sum;
        }
    }
}