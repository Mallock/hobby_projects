using System;
using System.Drawing;

namespace SyncMagic
{
    public class Moon
    {
        public double Angle;
        public double AngularVelocity;
        public int OrbitRadius;
        public int Radius;
        public Color Color;

        public static Moon CreateRandom(Random rng, int orbitRadius, int planetRadius)
        {
            int r = rng.Next(
                Math.Max(2, planetRadius / 3),
                Math.Max(3, (int)(planetRadius * 0.75))
            );

            return new Moon
            {
                Radius = r,
                OrbitRadius = orbitRadius,
                Color = WarmMuted(rng, 185, 185, 185),
                Angle = rng.NextDouble() * Math.PI * 2,
                AngularVelocity = (0.13 + rng.NextDouble() * 0.08) / Math.Sqrt(Math.Max(orbitRadius, 1))
            };
        }

        private static Color WarmMuted(Random rng, int r, int g, int b)
        {
            return Color.FromArgb(
                Math.Clamp(r + rng.Next(-30, 31), 30, 240),
                Math.Clamp(g + rng.Next(-30, 31), 30, 240),
                Math.Clamp(b + rng.Next(-30, 31), 30, 240)
            );
        }
    }
}
