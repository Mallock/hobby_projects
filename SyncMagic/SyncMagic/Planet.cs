using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

public enum PlanetFeatureType
{
    None,
    Stripes,
    Spots,
    Ocean
}

public class Planet
{
    public PointF Position { get; set; }
    public PointF Direction { get; set; }
    public float Speed { get; set; }
    public float Radius { get; set; }
    public int CollisionCooldown { get; set; } // Frames remaining before this planet can collide again  
    public bool IsOriginal { get; set; } // Indicates if the planet is one of the original planets  
    public int Lifespan { get; set; } // Lifespan in frames for generated planets  
    public bool IsExploding { get; set; } // Indicates if the planet is currently exploding  
    public int ExplosionFrame { get; set; } // Current frame of the explosion animation  
    public Color PlanetColor { get; set; } // Base color of the planet  
    public PlanetFeatureType Feature { get; set; } // Visual feature of the planet  
    public Random rand { get; set; }

    public Planet(PointF position, PointF direction, float speed, float radius, bool isOriginal = false, int lifespan = 0, Random rand = null)
    {
        this.rand = rand ?? new Random();

        Position = position;
        Direction = direction;
        Speed = speed;
        Radius = radius;
        CollisionCooldown = 0; // No cooldown initially  
        IsOriginal = isOriginal;
        Lifespan = lifespan;
        IsExploding = false;
        ExplosionFrame = 0;

        // Assign random feature  
        Feature = (PlanetFeatureType)this.rand.Next(Enum.GetNames(typeof(PlanetFeatureType)).Length);

        // Set planet base color  
        PlanetColor = GenerateRandomColor();
    }

    public void Move()
    {
        if (!IsExploding)
        {
            Position = new PointF(Position.X + Direction.X * Speed, Position.Y + Direction.Y * Speed);

            // Decrease the collision cooldown after each move  
            if (CollisionCooldown > 0)
            {
                CollisionCooldown--;
            }

            // Decrease lifespan for generated planets  
            if (!IsOriginal && Lifespan > 0)
            {
                Lifespan--;
                if (Lifespan == 0)
                {
                    IsExploding = true;
                }
            }
        }
        else
        {
            // Handle explosion animation (increase ExplosionFrame)  
            ExplosionFrame++;
        }
    }

    public void BounceX()
    {
        Direction = new PointF(-Direction.X, Direction.Y);
    }

    public void BounceY()
    {
        Direction = new PointF(Direction.X, -Direction.Y);
    }

    private Color GenerateRandomColor()
    {
        return Color.FromArgb(255, rand.Next(256), rand.Next(256), rand.Next(256));
    }
}

public class Star
{
    public PointF Position { get; set; }
    public float Brightness { get; set; }
    public float BlinkSpeed { get; set; }
    public float MaxBrightness { get; set; }
    public float MinBrightness { get; set; }
    public bool BrightnessIncreasing { get; set; }
}

public class PlanetSimulation
{
    private List<Planet> planets;
    private List<Star> stars; // List of stars for the starfield  
    private int width;
    private int height;
    private Random rand;
    private bool transparentBackground; // Option to have transparent background instead of starfield  

    /// <summary>  
    /// Initializes the planet simulation with two planets and a starfield background.  
    /// </summary>  
    public PlanetSimulation(bool transparentBackground = false)
    {
        // Canvas size remains at 240x240 pixels  
        width = 240;
        height = 240;

        planets = new List<Planet>();
        stars = new List<Star>(); // Initialize the starfield  
        rand = new Random();

        this.transparentBackground = transparentBackground;

        // Initialize two original planets with random positions and directions  
        for (int i = 0; i < 2; i++)
        {
            float x = (float)(rand.NextDouble() * width);
            float y = (float)(rand.NextDouble() * height);

            // Adjusted the minimum margin  
            float margin = 20.0f;

            // Ensure the planets are within the canvas boundaries  
            x = Math.Max(margin, Math.Min(width - margin, x));
            y = Math.Max(margin, Math.Min(height - margin, y));

            double angle = rand.NextDouble() * 2 * Math.PI;
            float dx = (float)Math.Cos(angle);
            float dy = (float)Math.Sin(angle);

            float speed = 9.0f; // Adjusted speed value  

            // Random radius between 10.0f and 30.0f  
            float radius = 5.0f + (float)(rand.NextDouble() * 15.0f);

            // Set IsOriginal = true for original planets  
            var planet = new Planet(new PointF(x, y), new PointF(dx, dy), speed, radius, isOriginal: true, lifespan: 0, rand: rand);
            planets.Add(planet);
        }

        // Initialize stars for the starfield background  
        InitializeStars();
    }

    /// <summary>  
    /// Initializes the stars for the starfield background.  
    /// </summary>  
    private void InitializeStars()
    {
        int numberOfStars = 100; // Adjust the number of stars as needed  

        for (int i = 0; i < numberOfStars; i++)
        {
            float x = (float)(rand.NextDouble() * width);
            float y = (float)(rand.NextDouble() * height);

            Star star = new Star()
            {
                Position = new PointF(x, y),
                Brightness = (float)(rand.NextDouble() * 0.5 + 0.5), // Initial brightness between 0.5 and 1.0  
                BlinkSpeed = (float)(rand.NextDouble() * 0.02 + 0.01), // Blink speed between 0.01 and 0.03  
                MaxBrightness = 1.0f,
                MinBrightness = 0.2f,
                BrightnessIncreasing = rand.Next(2) == 0
            };

            stars.Add(star);
        }
    }

    /// <summary>  
    /// Updates the planet positions, checks for collisions, and returns the bitmap with planets and starfield.  
    /// </summary>  
    /// <returns>A Bitmap image showing all current planet positions within the canvas.</returns>  
    public Bitmap GetPlanetPositions()
    {
        // Update positions of planets  
        UpdatePlanetPositions();

        // Update stars (for blinking effect)  
        UpdateStars();

        // Check for collisions and handle planet creation  
        HandleCollisions();

        // Remove expired planets  
        RemoveExpiredPlanets();

        // Create bitmap and draw planets and stars  
        Bitmap bitmap = new Bitmap(width, height);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            // Draw background  
            if (transparentBackground)
            {
                g.Clear(Color.Transparent); // Transparent background  
            }
            else
            {
                g.Clear(Color.Black); // Black background for space  
                DrawStars(g);         // Draw stars over the background  
            }

            // Draw planets  
            foreach (var planet in planets)
            {
                DrawPlanet(g, planet);
            }
        }

        return bitmap;
    }

    /// <summary>  
    /// Updates the brightness of the stars for the blinking effect.  
    /// </summary>  
    private void UpdateStars()
    {
        foreach (var star in stars)
        {
            if (star.BrightnessIncreasing)
            {
                star.Brightness += star.BlinkSpeed;
                if (star.Brightness >= star.MaxBrightness)
                {
                    star.Brightness = star.MaxBrightness;
                    star.BrightnessIncreasing = false;
                }
            }
            else
            {
                star.Brightness -= star.BlinkSpeed;
                if (star.Brightness <= star.MinBrightness)
                {
                    star.Brightness = star.MinBrightness;
                    star.BrightnessIncreasing = true;
                }
            }
        }
    }

    /// <summary>  
    /// Draws the starfield background with blinking stars.  
    /// </summary>  
    private void DrawStars(Graphics g)
    {
        foreach (var star in stars)
        {
            // Adjust star size based on brightness (optional)  
            float size = 1.0f + star.Brightness * 1.5f; // Size between 1.0 and 2.5  

            int brightnessValue = (int)(star.Brightness * 255);
            Color starColor = Color.FromArgb(brightnessValue, brightnessValue, brightnessValue);

            using (SolidBrush brush = new SolidBrush(starColor))
            {
                g.FillRectangle(brush, star.Position.X, star.Position.Y, size, size);
            }
        }
    }

    /// <summary>  
    /// Draws the planet or its explosion with gradient coloring.  
    /// </summary>  
    private void DrawPlanet(Graphics g, Planet planet)
    {
        // Enable anti-aliasing for smoother graphics  
        var originalSmoothingMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (planet.IsExploding)
        {
            // Draw explosion effect  
            DrawExplosion(g, planet);
        }
        else
        {
            float diameter = planet.Radius * 2;
            float x = planet.Position.X - planet.Radius;
            float y = planet.Position.Y - planet.Radius;

            // Create gradient brush for planet  
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(x, y, diameter, diameter);

                PathGradientBrush pgb = new PathGradientBrush(path);

                // Set the center color and surround colors for the gradient  
                pgb.CenterColor = Color.White;
                pgb.SurroundColors = new Color[] { planet.PlanetColor };

                // Fill the planet with gradient  
                g.FillEllipse(pgb, x, y, diameter, diameter);
            }

            // Draw planet features  
            switch (planet.Feature)
            {
                case PlanetFeatureType.Stripes:
                    DrawStripes(g, planet);
                    break;
                case PlanetFeatureType.Spots:
                    DrawSpots(g, planet);
                    break;
                case PlanetFeatureType.Ocean:
                    DrawOcean(g, planet);
                    break;
                default:
                    // No additional features  
                    break;
            }

            // Outline the planet  
            g.DrawEllipse(Pens.White, x, y, diameter, diameter);
        }

        // Restore the original smoothing mode  
        g.SmoothingMode = originalSmoothingMode;
    }

    private void DrawExplosion(Graphics g, Planet planet)
    {
        // Simple pixel explosion effect  
        int totalExplosionFrames = 15; // Number of frames for the explosion  
        if (planet.ExplosionFrame <= totalExplosionFrames)
        {
            float progress = (float)planet.ExplosionFrame / totalExplosionFrames;
            int particles = 50; // Number of particles  
            for (int i = 0; i < particles; i++)
            {
                double angle = rand.NextDouble() * 2 * Math.PI;
                float distance = planet.Radius * 2 * progress * (float)rand.NextDouble();
                float x = planet.Position.X + (float)Math.Cos(angle) * distance;
                float y = planet.Position.Y + (float)Math.Sin(angle) * distance;

                // Random color for explosion particles  
                Color explosionColor = Color.FromArgb(255, rand.Next(256), rand.Next(256), 0);
                using (SolidBrush brush = new SolidBrush(explosionColor))
                {
                    g.FillRectangle(brush, x, y, 2, 2);
                }
            }
        }
    }

    /// <summary>  
    /// Updates the positions of all planets and handles wall collisions.  
    /// </summary>  
    private void UpdatePlanetPositions()
    {
        foreach (var planet in planets)
        {
            planet.Move();

            if (planet.IsExploding)
                continue; // Skip collision detection for exploding planets  

            // Check for wall collisions and adjust direction  

            // Left or right walls  
            if (planet.Position.X - planet.Radius < 0)
            {
                planet.Position = new PointF(planet.Radius, planet.Position.Y);
                planet.BounceX();
            }
            else if (planet.Position.X + planet.Radius > width)
            {
                planet.Position = new PointF(width - planet.Radius, planet.Position.Y);
                planet.BounceX();
            }

            // Top or bottom walls  
            if (planet.Position.Y - planet.Radius < 0)
            {
                planet.Position = new PointF(planet.Position.X, planet.Radius);
                planet.BounceY();
            }
            else if (planet.Position.Y + planet.Radius > height)
            {
                planet.Position = new PointF(planet.Position.X, height - planet.Radius);
                planet.BounceY();
            }
        }
    }

    /// <summary>  
    /// Checks for collisions between planets and handles planet creation.  
    /// </summary>  
    private void HandleCollisions()
    {
        List<Planet> newPlanets = new List<Planet>();

        for (int i = 0; i < planets.Count; i++)
        {
            Planet planet1 = planets[i];

            // Skip if this planet is on collision cooldown or exploding  
            if (planet1.CollisionCooldown > 0 || planet1.IsExploding)
                continue;

            for (int j = i + 1; j < planets.Count; j++)
            {
                Planet planet2 = planets[j];

                // Skip if the other planet is on collision cooldown or exploding  
                if (planet2.CollisionCooldown > 0 || planet2.IsExploding)
                    continue;

                float dx = planet1.Position.X - planet2.Position.X;
                float dy = planet1.Position.Y - planet2.Position.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);

                if (distance <= planet1.Radius + planet2.Radius)
                {
                    // Collision detected  

                    // Only create a new planet if both are original  
                    if (planet1.IsOriginal && planet2.IsOriginal)
                    {
                        // Create a new random planet  
                        float newX = (float)(rand.NextDouble() * width);
                        float newY = (float)(rand.NextDouble() * height);

                        // Ensure the planet is within the canvas boundaries  
                        float margin = 10.0f;
                        newX = Math.Max(margin, Math.Min(width - margin, newX));
                        newY = Math.Max(margin, Math.Min(height - margin, newY));

                        // Random direction  
                        double angle = rand.NextDouble() * 2 * Math.PI;
                        float newDx = (float)Math.Cos(angle);
                        float newDy = (float)Math.Sin(angle);

                        float newSpeed = 2.0f + (float)(rand.NextDouble() * 3.0f); // Speed between 2.0 and 5.0  

                        // Random radius between 10.0f and 30.0f  
                        float newRadius = 10.0f + (float)(rand.NextDouble() * 20.0f);

                        // Lifespan between 200 and 400 frames  
                        int lifespan = 200 + rand.Next(200);

                        // Create new planet (IsOriginal = false)  
                        var newPlanet = new Planet(new PointF(newX, newY), new PointF(newDx, newDy), newSpeed, newRadius, isOriginal: false, lifespan: lifespan, rand: rand);
                        newPlanets.Add(newPlanet);

                        // Set collision cooldown to prevent immediate re-collision  
                        int cooldownFrames = 30;
                        planet1.CollisionCooldown = cooldownFrames;
                        planet2.CollisionCooldown = cooldownFrames;
                    }

                    // Bounce the planets off each other  
                    BouncePlanets(planet1, planet2);
                }
            }
        }

        // Add all new planets to the list  
        planets.AddRange(newPlanets);
    }

    /// <summary>  
    /// Removes planets whose explosion animation is complete.  
    /// </summary>  
    private void RemoveExpiredPlanets()
    {
        planets.RemoveAll(p => p.IsExploding && p.ExplosionFrame > 15);
    }

    /// <summary>  
    /// Simulates a simple elastic collision between two planets by exchanging their directions.  
    /// </summary>  
    private void BouncePlanets(Planet planet1, Planet planet2)
    {
        // Simple elastic collision by swapping velocities  
        var tempDirection = planet1.Direction;
        planet1.Direction = planet2.Direction;
        planet2.Direction = tempDirection;
    }

    // Methods to draw planet features with gradient coloring  

    private void DrawStripes(Graphics g, Planet planet)
    {
        Random rand = planet.rand;
        float diameter = planet.Radius * 2;
        float x = planet.Position.X - planet.Radius;
        float y = planet.Position.Y - planet.Radius;

        int numberOfStripes = 5 + rand.Next(5); // Between 5 and 10 stripes  
        float stripeWidth = diameter / numberOfStripes;

        using (GraphicsPath clipPath = new GraphicsPath())
        {
            clipPath.AddEllipse(x, y, diameter, diameter);
            g.SetClip(clipPath);

            for (int i = 0; i < numberOfStripes; i++)
            {
                // Gradient for each stripe  
                RectangleF stripeRect = new RectangleF(x + i * stripeWidth, y, stripeWidth, diameter);
                using (LinearGradientBrush brush = new LinearGradientBrush(stripeRect, planet.PlanetColor, Color.White, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, stripeRect);
                }
            }

            g.ResetClip();
        }
    }

    private void DrawSpots(Graphics g, Planet planet)
    {
        Random rand = planet.rand;
        float diameter = planet.Radius * 2;
        float x = planet.Position.X - planet.Radius;
        float y = planet.Position.Y - planet.Radius;

        int numberOfSpots = 5 + rand.Next(10); // Between 5 and 15 spots  

        using (GraphicsPath clipPath = new GraphicsPath())
        {
            clipPath.AddEllipse(x, y, diameter, diameter);
            g.SetClip(clipPath);

            for (int i = 0; i < numberOfSpots; i++)
            {
                float spotSize = diameter * (0.05f + (float)rand.NextDouble() * 0.1f); // Spot size relative to planet size  
                float spotX = x + (float)rand.NextDouble() * (diameter - spotSize);
                float spotY = y + (float)rand.NextDouble() * (diameter - spotSize);

                // Gradient for each spot  
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(spotX, spotY, spotSize, spotSize);

                    PathGradientBrush pgb = new PathGradientBrush(path);
                    pgb.CenterColor = Color.White;
                    pgb.SurroundColors = new Color[] { Color.FromArgb(200, planet.PlanetColor) };

                    g.FillEllipse(pgb, spotX, spotY, spotSize, spotSize);
                }
            }

            g.ResetClip();
        }
    }

    private void DrawOcean(Graphics g, Planet planet)
    {
        Random rand = planet.rand;
        float diameter = planet.Radius * 2;
        float x = planet.Position.X - planet.Radius;
        float y = planet.Position.Y - planet.Radius;

        // Fill the planet with ocean gradient color  
        using (GraphicsPath path = new GraphicsPath())
        {
            path.AddEllipse(x, y, diameter, diameter);

            PathGradientBrush pgb = new PathGradientBrush(path);
            pgb.CenterColor = Color.LightBlue;
            pgb.SurroundColors = new Color[] { Color.Blue };

            g.FillEllipse(pgb, x, y, diameter, diameter);
        }

        // Draw land masses with gradient  
        int numberOfContinents = 2 + rand.Next(3); // Between 2 and 4 continents  
        for (int i = 0; i < numberOfContinents; i++)
        {
            float landWidth = diameter * (0.2f + (float)rand.NextDouble() * 0.3f);
            float landHeight = diameter * (0.1f + (float)rand.NextDouble() * 0.2f);

            float landX = x + (float)rand.NextDouble() * (diameter - landWidth);
            float landY = y + (float)rand.NextDouble() * (diameter - landHeight);

            using (GraphicsPath landPath = new GraphicsPath())
            {
                landPath.AddEllipse(landX, landY, landWidth, landHeight);

                PathGradientBrush pgb = new PathGradientBrush(landPath);
                pgb.CenterColor = Color.LightGreen;
                pgb.SurroundColors = new Color[] { Color.Green };

                g.FillPath(pgb, landPath);
            }
        }
    }
}