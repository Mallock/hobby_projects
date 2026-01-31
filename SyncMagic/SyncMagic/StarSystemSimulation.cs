using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SyncMagic
{
    public class StarSystemSimulation
    {
        private readonly int width = 240;
        private readonly int height = 240;
        private readonly int safeMargin = 8;

        private readonly Random rng;

        private Star star;

        // ------------------------------------------------------
        // SHIPS
        // ------------------------------------------------------

        private class StarShip
        {
            public PointF Position;
            public PointF Velocity;

            public Func<PointF> Origin;
            public Func<PointF> Destination;

            public float Progress;
            public float Speed;

            public Queue<PointF> Trail = new();
            public int TrailMax = 14;
        }

        private List<StarShip> ships = new();

        // ------------------------------------------------------

        private List<OrbitingBody> planets = new();

        public StarSystemSimulation(int? seed = null)
        {
            rng = seed.HasValue ? new Random(seed.Value) : new Random();
            GenerateSystem();
            GenerateShips();
        }

        // ------------------------------------------------------
        // SHIP GENERATION
        // ------------------------------------------------------

        private void GenerateShips()
        {
            ships.Clear();

            int shipCount = rng.Next(2, 6);

            for (int i = 0; i < shipCount; i++)
            {
                var ship = new StarShip();
                AssignNewRoute(ship);
                ships.Add(ship);
            }
        }

        private void AssignNewRoute(StarShip ship)
        {
            Func<PointF> GetStar = () =>
            {
                return new PointF(width / 2f, height / 2f);
            };

            Func<PointF> GetPlanet(OrbitingBody p)
            {
                return () =>
                {
                    float cx = width / 2f;
                    float cy = height / 2f;

                    return new PointF(
                        (float)(cx + Math.Cos(p.Angle) * p.OrbitRadius),
                        (float)(cy + Math.Sin(p.Angle) * p.OrbitRadius)
                    );
                };
            }

            bool starRoute = rng.Next(100) < 35;

            OrbitingBody p1 = planets[rng.Next(planets.Count)];
            OrbitingBody p2 = planets[rng.Next(planets.Count)];

            if (starRoute)
            {
                ship.Origin = GetStar;
                ship.Destination = GetPlanet(p1);
            }
            else
            {
                ship.Origin = GetPlanet(p1);
                ship.Destination = GetPlanet(p2);
            }

            ship.Progress = 0f;
            ship.Speed = 0.002f + (float)rng.NextDouble() * 0.0035f;

            ship.Position = ship.Origin();
            ship.Trail.Clear();
        }

        // ------------------------------------------------------
        // SYSTEM GENERATION
        // ------------------------------------------------------

        private void GenerateSystem()
        {
            star = Star.CreateRandom(rng);

            planets.Clear();

            int maxOrbit = (Math.Min(width, height) / 2) - safeMargin;
            int orbit = Math.Max(star.Radius + 18, 34);

            int planetCount = rng.Next(4, 11);

            for (int i = 0; i < planetCount; i++)
            {
                var planet = OrbitingBody.CreateRandom(rng);
                planet.GenerateMoons(rng);

                int spacing =
                    10 +
                    planet.Radius +
                    planet.MoonSystemMaxOrbit +
                    (planet.HasRings ? 4 : 0);

                orbit += spacing;

                if (orbit >= maxOrbit - 4)
                    break;

                planet.OrbitRadius = orbit;

                double vel =
                    (0.07 + rng.NextDouble() * 0.05) /
                    Math.Sqrt(Math.Max(orbit, 1));

                if (rng.Next(2) == 0)
                    vel = -vel;

                planet.AngularVelocity = vel;
                planet.Angle = rng.NextDouble() * Math.PI * 2;

                planets.Add(planet);
            }
        }

    // ------------------------------------------------------

    public Bitmap GetFrame()
    {
        foreach (var p in planets)
        {
            p.Angle += p.AngularVelocity;

            foreach (var m in p.Moons)
                m.Angle += m.AngularVelocity;
        }

        UpdateShips();

        Bitmap bmp = new(width, height);

        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Black);

            float cx = width / 2f;
            float cy = height / 2f;

            DrawPlanetOrbits(g, cx, cy);
            DrawStar(g, cx, cy);

            foreach (var p in planets)
            {
                float px = (float)(cx + Math.Cos(p.Angle) * p.OrbitRadius);
                float py = (float)(cy + Math.Sin(p.Angle) * p.OrbitRadius);

                DrawPlanet(g, p, px, py);
                DrawMoons(g, p, px, py);
            }

            DrawShips(g);
        }

        return bmp;
    }

    // ------------------------------------------------------
    // SHIP UPDATE + DRAW
    // ------------------------------------------------------

    private void UpdateShips()
    {
        foreach (var s in ships)
        {
            s.Progress += s.Speed;

            if (s.Progress >= 1f)
            {
                AssignNewRoute(s);
                continue;
            }

            PointF a = s.Origin();
            PointF b = s.Destination();

            float t = SmoothStep(s.Progress);

            s.Position = new PointF(
                Lerp(a.X, b.X, t),
                Lerp(a.Y, b.Y, t)
            );

            s.Trail.Enqueue(s.Position);
            while (s.Trail.Count > s.TrailMax)
                s.Trail.Dequeue();
        }
    }

    private void DrawShips(Graphics g)
    {
        foreach (var s in ships)
        {
            // trail
            int i = 0;
            foreach (var p in s.Trail)
            {
                float alpha = (float)i / s.TrailMax;
                using SolidBrush br =
                    new(Color.FromArgb((int)(alpha * 140), 200, 230, 255));

                g.FillEllipse(br, p.X - 1.2f, p.Y - 1.2f, 2.4f, 2.4f);
                i++;
            }

            using SolidBrush ship = new(Color.White);
            g.FillEllipse(ship, s.Position.X - 1.5f, s.Position.Y - 1.5f, 3, 3);
        }
    }

    // ------------------------------------------------------

    private float Lerp(float a, float b, float t) => a + (b - a) * t;

    private float SmoothStep(float t) => t * t * (3f - 2f * t);

    // ------------------------------------------------------
    // DRAWING
    // ------------------------------------------------------

    private void DrawPlanetOrbits(Graphics g, float cx, float cy)
    {
        using Pen pen = new(Color.FromArgb(150, 230, 230, 230), 1f);

        foreach (var p in planets)
        {
            float d = p.OrbitRadius * 2f;
            g.DrawEllipse(pen, cx - p.OrbitRadius, cy - p.OrbitRadius, d, d);
        }
    }

    private void DrawMoons(Graphics g, OrbitingBody planet, float px, float py)
    {
        using Pen orbitPen = new(Color.FromArgb(190, 240, 240, 240), 1f);

        foreach (var m in planet.Moons)
        {
            float d = m.OrbitRadius * 2f;

            g.DrawEllipse(orbitPen,
                px - m.OrbitRadius,
                py - m.OrbitRadius,
                d, d);

            float mx = (float)(px + Math.Cos(m.Angle) * m.OrbitRadius);
            float my = (float)(py + Math.Sin(m.Angle) * m.OrbitRadius);

            using SolidBrush br = new(m.Color);

            g.FillEllipse(br,
                mx - m.Radius,
                my - m.Radius,
                m.Radius * 2,
                m.Radius * 2);
        }
    }

    private void DrawPlanet(Graphics g, OrbitingBody p, float px, float py)
    {
        using GraphicsPath path = new();
        path.AddEllipse(px - p.Radius, py - p.Radius, p.Radius * 2, p.Radius * 2);

        using PathGradientBrush pgb = new(path)
        {
            CenterColor = Lighten(p.Color, 0.35f),
            SurroundColors = new[] { p.Color }
        };

        g.FillEllipse(pgb,
            px - p.Radius,
            py - p.Radius,
            p.Radius * 2,
            p.Radius * 2);

        if (p.HasRings)
            DrawRings(g, px, py, p.Radius);
    }

    private void DrawStar(Graphics g, float cx, float cy)
    {
        int glow = star.Radius + 10;

        using GraphicsPath path = new();
        path.AddEllipse(cx - glow, cy - glow, glow * 2, glow * 2);

        using PathGradientBrush pgb = new(path)
        {
            CenterColor = Color.FromArgb(220, star.Color),
            SurroundColors = new[] { Color.FromArgb(0, star.Color) }
        };

        g.FillEllipse(pgb, cx - glow, cy - glow, glow * 2, glow * 2);

        using SolidBrush core = new(star.Color);
        g.FillEllipse(core,
            cx - star.Radius,
            cy - star.Radius,
            star.Radius * 2,
            star.Radius * 2);
    }

    private void DrawRings(Graphics g, float px, float py, int radius)
    {
        using Pen pen = new(Color.FromArgb(180, 210, 210, 210), 1f);

        float rx1 = radius + 2;
        float ry1 = radius / 2f + 1;

        g.DrawEllipse(pen, px - rx1, py - ry1, rx1 * 2, ry1 * 2);

        float rx2 = radius + 4;
        float ry2 = radius / 2f + 2;

        g.DrawEllipse(pen, px - rx2, py - ry2, rx2 * 2, ry2 * 2);
    }

    

    private Color WarmMuted(int r, int g, int b)
    {
        return Color.FromArgb(
            Math.Clamp(r + rng.Next(-30, 31), 30, 240),
            Math.Clamp(g + rng.Next(-30, 31), 30, 240),
            Math.Clamp(b + rng.Next(-30, 31), 30, 240)
        );
    }

    private Color Lighten(Color c, float amount)
    {
        return Color.FromArgb(
            c.A,
            (int)(c.R + (255 - c.R) * amount),
            (int)(c.G + (255 - c.G) * amount),
            (int)(c.B + (255 - c.B) * amount)
        );
    }
}
}
