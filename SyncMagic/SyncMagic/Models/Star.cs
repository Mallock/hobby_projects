using System;
using System.Drawing;

namespace SyncMagic
{
    public class Star
    {
        public StarType Type { get; }
        public Color Color { get; }
        public int Radius { get; }

        private Star(StarType type, Color color, int radius)
        {
            Type = type;
            Color = color;
            Radius = radius;
        }

        public static Star CreateRandom(Random rng)
        {
            var type = (StarType)rng.Next(Enum.GetNames(typeof(StarType)).Length);
            var (color, radius) = GetStarVisuals(type, rng);
            return new Star(type, color, radius);
        }

        public static (Color color, int radius) GetStarVisuals(StarType type, Random rng)
        {
            return type switch
            {
                StarType.RedDwarf => (Color.FromArgb(255, 110, 70), rng.Next(10, 15)),
                StarType.YellowDwarf => (Color.FromArgb(255, 210, 80), rng.Next(13, 18)),
                StarType.BlueGiant => (Color.FromArgb(150, 200, 255), rng.Next(16, 22)),
                StarType.WhiteDwarf => (Color.FromArgb(255, 245, 235), rng.Next(9, 12)),
                _ => (Color.FromArgb(255, 170, 80), rng.Next(12, 18)),
            };
        }
    }
}
