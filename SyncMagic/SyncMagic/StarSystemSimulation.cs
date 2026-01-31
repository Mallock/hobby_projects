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

    private readonly Random rng;

    private StarType starType;
    private Color starColor;
    private int starRadius;

    private class OrbitingBody
    {
        public double Angle;              // current angle (radians)
        public double AngularVelocity;    // radians per frame
        public int OrbitRadius;           // pixels from center
        public int Radius;                // body radius in pixels
        public Color Color;               // body color
        public WorldType Type;            // classification
        public bool HasRings;             // draw simple rings
    }

    private List<OrbitingBody> planets = new List<OrbitingBody>();

    public StarSystemSimulation(int? seed = null)
    {
        rng = seed.HasValue ? new Random(seed.Value) : new Random();
        GenerateSystem();
    }

    private void GenerateSystem()
    {
        // Star type
        starType = (StarType)rng.Next(Enum.GetNames(typeof(StarType)).Length);
        (starColor, starRadius) = GetStarVisuals(starType);

        planets.Clear();

        // Number of planets
        int planetCount = rng.Next(4, 11); // 4..10

        // Build non-overlapping orbits with some spacing
        int minOrbit = Math.Max(28, starRadius + 12);
        int orbit = minOrbit;
        for (int i = 0; i < planetCount; i++)
        {
            // Randomize gap between orbits
            orbit += rng.Next(10, 18);

            var type = (WorldType)rng.Next(Enum.GetNames(typeof(WorldType)).Length);
            // Size and color based on type
            int bodyRadius;
            Color bodyColor;
            switch (type)
            {
                case WorldType.GasGiant:
                    bodyRadius = rng.Next(6, 10);
                    bodyColor = Muted(rng, 80, 160, 200);
                    break;
                case WorldType.Ice:
                    bodyRadius = rng.Next(3, 6);
                    bodyColor = Color.FromArgb(200, 230, 255);
                    break;
                case WorldType.Ocean:
                    bodyRadius = rng.Next(4, 7);
                    bodyColor = Color.CornflowerBlue;
                    break;
                case WorldType.Desert:
                    bodyRadius = rng.Next(3, 6);
                    bodyColor = Color.SandyBrown;
                    break;
                case WorldType.Ringed:
                    bodyRadius = rng.Next(5, 8);
                    bodyColor = Muted(rng, 150, 150, 130);
                    break;
                default: // Rocky
                    bodyRadius = rng.Next(2, 5);
                    bodyColor = Muted(rng, 160, 120, 100);
                    break;
            }

            // orbital speed inversely related to radius (simple approx): w ~ 1/sqrt(r)
            double angularVel = (0.06 + rng.NextDouble() * 0.06) / Math.Sqrt(Math.Max(orbit, 1));
            // random direction (CW/CCW)
            if (rng.Next(2) == 0) angularVel = -angularVel;

            planets.Add(new OrbitingBody
            {
                Angle = rng.NextDouble() * Math.PI * 2,
                AngularVelocity = angularVel,
                OrbitRadius = orbit,
                Radius = bodyRadius,
                Color = bodyColor,
                Type = type,
                HasRings = type == WorldType.Ringed && rng.Next(100) < 85
            });
        }
    }

    private (Color color, int radius) GetStarVisuals(StarType type)
    {
        switch (type)
        {
            case StarType.RedDwarf:
                return (Color.OrangeRed, rng.Next(10, 16));
            case StarType.YellowDwarf:
                return (Color.Gold, rng.Next(12, 18));
            case StarType.BlueGiant:
                return (Color.DeepSkyBlue, rng.Next(16, 24));
            case StarType.WhiteDwarf:
                return (Color.WhiteSmoke, rng.Next(8, 12));
            case StarType.OrangeK:
            default:
                return (Color.Orange, rng.Next(12, 18));
        }
    }

    private static Color Muted(Random r, int baseR, int baseG, int baseB)
    {
        int dr = r.Next(-40, 41);
        int dg = r.Next(-40, 41);
        int db = r.Next(-40, 41);
        return Color.FromArgb(
            Math.Clamp(baseR + dr, 30, 235),
            Math.Clamp(baseG + dg, 30, 235),
            Math.Clamp(baseB + db, 30, 235));
    }

    public Bitmap GetFrame()
    {
        // Advance simulation angles
        foreach (var p in planets)
        {
            p.Angle += p.AngularVelocity;
        }

        Bitmap bmp = new Bitmap(width, height);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Black);

            var cx = width / 2f;
            var cy = height / 2f;

            // Draw orbital tracks first
            using (Pen orbitPen = new Pen(Color.White, 1f) { Alignment = PenAlignment.Center })
            {
                foreach (var p in planets)
                {
                    float d = p.OrbitRadius * 2f;
                    g.DrawEllipse(orbitPen, cx - p.OrbitRadius, cy - p.OrbitRadius, d, d);
                }
            }

            // Draw star with a simple radial gradient glow
            DrawStar(g, cx, cy);

            // Draw planets on top
            foreach (var p in planets)
            {
                float px = (float)(cx + Math.Cos(p.Angle) * p.OrbitRadius);
                float py = (float)(cy + Math.Sin(p.Angle) * p.OrbitRadius);

                // Simple shadow to give depth
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                {
                    g.FillEllipse(shadow, px - p.Radius + 1, py - p.Radius + 1, p.Radius * 2, p.Radius * 2);
                }

                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(px - p.Radius, py - p.Radius, p.Radius * 2, p.Radius * 2);
                    using (PathGradientBrush pgb = new PathGradientBrush(path))
                    {
                        pgb.CenterColor = Color.White;
                        pgb.SurroundColors = new[] { p.Color };
                        g.FillEllipse(pgb, px - p.Radius, py - p.Radius, p.Radius * 2, p.Radius * 2);
                    }
                }

                if (p.HasRings)
                {
                    DrawRings(g, px, py, p.Radius);
                }
            }
        }

        return bmp;
    }

    private void DrawStar(Graphics g, float cx, float cy)
    {
        int glow = Math.Max(14, starRadius + 6);
        using (GraphicsPath path = new GraphicsPath())
        {
            path.AddEllipse(cx - glow, cy - glow, glow * 2, glow * 2);
            using (PathGradientBrush pgb = new PathGradientBrush(path))
            {
                pgb.CenterColor = Color.FromArgb(220, starColor);
                pgb.SurroundColors = new[] { Color.FromArgb(0, starColor) };
                g.FillEllipse(pgb, cx - glow, cy - glow, glow * 2, glow * 2);
            }
        }

        using (SolidBrush core = new SolidBrush(starColor))
        {
            g.FillEllipse(core, cx - starRadius, cy - starRadius, starRadius * 2, starRadius * 2);
        }
        using (Pen rim = new Pen(Color.White, 1f))
        {
            g.DrawEllipse(rim, cx - starRadius, cy - starRadius, starRadius * 2, starRadius * 2);
        }
    }

    private void DrawRings(Graphics g, float px, float py, int planetRadius)
    {
        using (Pen ringPen = new Pen(Color.FromArgb(180, Color.LightGray), 1f))
        {
            // draw two thin rings as flattened ellipses
            float rx1 = planetRadius + 2;
            float ry1 = planetRadius / 2f + 1;
            g.DrawEllipse(ringPen, px - rx1, py - ry1, rx1 * 2, ry1 * 2);

            float rx2 = planetRadius + 4;
            float ry2 = planetRadius / 2f + 2;
            g.DrawEllipse(ringPen, px - rx2, py - ry2, rx2 * 2, ry2 * 2);
        }
    }
}
