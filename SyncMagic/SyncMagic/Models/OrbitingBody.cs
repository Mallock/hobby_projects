using System;
using System.Collections.Generic;
using System.Drawing;

namespace SyncMagic
{
    public class OrbitingBody
    {
        public double Angle;
        public double AngularVelocity;
        public int OrbitRadius;
        public int Radius;
        public Color Color;
        public WorldType Type;
        public bool HasRings;

        public int MoonSystemMaxOrbit;
        public List<Moon> Moons = new();

        // Factory: create a planet/body with visuals determined by RNG
        public static OrbitingBody CreateRandom(Random rng)
        {
            var type = (WorldType)rng.Next(Enum.GetNames(typeof(WorldType)).Length);

            int bodyRadius;
            Color bodyColor;

            switch (type)
            {
                case WorldType.GasGiant:
                    bodyRadius = rng.Next(7, 11);
                    bodyColor = WarmMuted(rng, 180, 140, 90);
                    break;

                case WorldType.Ice:
                    bodyRadius = rng.Next(3, 6);
                    bodyColor = WarmMuted(rng, 210, 220, 230);
                    break;

                case WorldType.Ocean:
                    bodyRadius = rng.Next(4, 7);
                    bodyColor = WarmMuted(rng, 70, 120, 170);
                    break;

                case WorldType.Desert:
                    bodyRadius = rng.Next(3, 6);
                    bodyColor = WarmMuted(rng, 200, 160, 80);
                    break;

                case WorldType.Ringed:
                    bodyRadius = rng.Next(6, 9);
                    bodyColor = WarmMuted(rng, 170, 150, 120);
                    break;

                default:
                    bodyRadius = rng.Next(2, 5);
                    bodyColor = WarmMuted(rng, 150, 120, 100);
                    break;
            }

            return new OrbitingBody
            {
                Radius = bodyRadius,
                Color = bodyColor,
                Type = type,
                HasRings = type == WorldType.Ringed && rng.Next(100) < 80
            };
        }

        // Generate moons for this body using RNG
        public void GenerateMoons(Random rng)
        {
            Moons.Clear();

            int moonCount = rng.Next(100) switch
            {
                < 50 => 0,
                < 80 => 1,
                < 95 => 2,
                _ => 3
            };

            int orbit = Radius + 5;
            int maxMoonOrbit = 0;

            for (int i = 0; i < moonCount; i++)
            {
                orbit += rng.Next(4, 7);

                var moon = Moon.CreateRandom(rng, orbit, Radius);

                maxMoonOrbit = Math.Max(maxMoonOrbit, orbit + moon.Radius + 2);
                Moons.Add(moon);
            }

            MoonSystemMaxOrbit = maxMoonOrbit;
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

