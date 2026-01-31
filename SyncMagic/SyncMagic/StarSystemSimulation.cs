using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

public enum StarType
{
    RedDwarf,
    YellowDwarf,
    BlueGiant,
    WhiteDwarf,
    OrangeK
}

public enum WorldType
{
    Rocky,
    GasGiant,
    Ice,
    Desert,
    Ocean,
    Ringed
}

public class StarSystemSimulation
{
    private readonly int width = 240;
    private readonly int height = 240;
    private readonly int safeMargin = 8;

    private readonly Random rng;

    private StarType starType;
    private Color starColor;
    private int starRadius;

    // ------------------------------------------------------

    private class Moon
    {
        public double Angle;
        public double AngularVelocity;
        public int OrbitRadius;
        public int Radius;
        public Color Color;
    }

    private class OrbitingBody
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
    }

    private List<OrbitingBody> planets = new();

    public StarSystemSimulation(int? seed = null)
    {
        rng = seed.HasValue ? new Random(seed.Value) : new Random();
        GenerateSystem();
    }

    // ------------------------------------------------------
    // SYSTEM GENERATION
    // ------------------------------------------------------

    private void GenerateSystem()
    {
        starType = (StarType)rng.Next(Enum.GetNames(typeof(StarType)).Length);
        (starColor, starRadius) = GetStarVisuals(starType);

        planets.Clear();

        int maxOrbit = (Math.Min(width, height) / 2) - safeMargin;

        int orbit = Math.Max(starRadius + 18, 34);
        int planetCount = rng.Next(4, 11);

        for (int i = 0; i < planetCount; i++)
        {
            var planet = CreatePlanet();

            // Generate moons BEFORE calculating spacing
            GenerateMoons(planet);

            // ---- Dynamic spacing calculation ----
            int requiredSpacing =
                10 +                         // baseline spacing
                planet.Radius +
                planet.MoonSystemMaxOrbit +
                (planet.HasRings ? 4 : 0);

            orbit += requiredSpacing;

            if (orbit >= maxOrbit - 4)
                break;

            planet.OrbitRadius = orbit;

            double angularVel =
                (0.07 + rng.NextDouble() * 0.05) /
                Math.Sqrt(Math.Max(orbit, 1));

            if (rng.Next(2) == 0)
                angularVel = -angularVel;

            planet.AngularVelocity = angularVel;
            planet.Angle = rng.NextDouble() * Math.PI * 2;

            planets.Add(planet);
        }
    }

    // ------------------------------------------------------

    private OrbitingBody CreatePlanet()
    {
        var type = (WorldType)rng.Next(Enum.GetNames(typeof(WorldType)).Length);

        int bodyRadius;
        Color bodyColor;

        switch (type)
        {
            case WorldType.GasGiant:
                bodyRadius = rng.Next(7, 11);
                bodyColor = WarmMuted(180, 140, 90);
                break;

            case WorldType.Ice:
                bodyRadius = rng.Next(3, 6);
                bodyColor = WarmMuted(210, 220, 230);
                break;

            case WorldType.Ocean:
                bodyRadius = rng.Next(4, 7);
                bodyColor = WarmMuted(70, 120, 170);
                break;

            case WorldType.Desert:
                bodyRadius = rng.Next(3, 6);
                bodyColor = WarmMuted(200, 160, 80);
                break;

            case WorldType.Ringed:
                bodyRadius = rng.Next(6, 9);
                bodyColor = WarmMuted(170, 150, 120);
                break;

            default:
                bodyRadius = rng.Next(2, 5);
                bodyColor = WarmMuted(150, 120, 100);
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

    // ------------------------------------------------------
    // MOON GENERATION WITH SYSTEM SIZE TRACKING
    // ------------------------------------------------------

    private void GenerateMoons(OrbitingBody planet)
    {
        int moonCount = rng.Next(100) switch
        {
            < 50 => 0,
            < 80 => 1,
            < 95 => 2,
            _ => 3
        };

        int orbit = planet.Radius + 5;
        int maxMoonOrbit = 0;

        for (int i = 0; i < moonCount; i++)
        {
            orbit += rng.Next(4, 7);

            int radius = rng.Next(1, Math.Max(2, planet.Radius / 2));

            var moon = new Moon
            {
                Radius = radius,
                OrbitRadius = orbit,
                Color = WarmMuted(185, 185, 185),
                Angle = rng.NextDouble() * Math.PI * 2,
                AngularVelocity =
                    (0.13 + rng.NextDouble() * 0.08) /
                    Math.Sqrt(orbit)
            };

            maxMoonOrbit = Math.Max(maxMoonOrbit, orbit + radius + 2);

            planet.Moons.Add(moon);
        }

        planet.MoonSystemMaxOrbit = maxMoonOrbit;
    }

    // ------------------------------------------------------

    private (Color color, int radius) GetStarVisuals(StarType type)
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

    private Color WarmMuted(int r, int g, int b)
    {
        return Color.FromArgb(
            Math.Clamp(r + rng.Next(-30, 31), 30, 240),
            Math.Clamp(g + rng.Next(-30, 31), 30, 240),
            Math.Clamp(b + rng.Next(-30, 31), 30, 240)
        );
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
        }

        return bmp;
    }

    // ------------------------------------------------------

    private void DrawPlanetOrbits(Graphics g, float cx, float cy)
    {
        using Pen pen = new(Color.FromArgb(150, 230, 230, 230), 1f);

        foreach (var p in planets)
        {
            float d = p.OrbitRadius * 2f;

            g.DrawEllipse(pen,
                cx - p.OrbitRadius,
                cy - p.OrbitRadius,
                d, d);
        }
    }

    private void DrawMoons(Graphics g, OrbitingBody planet, float px, float py)
    {
        // Brighter moon orbit lines
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
        int glow = starRadius + 10;

        using GraphicsPath path = new();
        path.AddEllipse(cx - glow, cy - glow, glow * 2, glow * 2);

        using PathGradientBrush pgb = new(path)
        {
            CenterColor = Color.FromArgb(220, starColor),
            SurroundColors = new[] { Color.FromArgb(0, starColor) }
        };

        g.FillEllipse(pgb, cx - glow, cy - glow, glow * 2, glow * 2);

        using SolidBrush core = new(starColor);
        g.FillEllipse(core,
            cx - starRadius,
            cy - starRadius,
            starRadius * 2,
            starRadius * 2);
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
