using System;
using System.Collections.Generic;
using System.Drawing;

public class Ball
{
    public PointF Position { get; set; }
    public PointF Direction { get; set; }
    public float Speed { get; set; }
    public float Radius { get; set; }
    public int CollisionCooldown { get; set; } // Frames remaining before this ball can collide again    
    public bool IsOriginal { get; set; } // Indicates if the ball is one of the original balls    

    // NEW: Added BallColor property    
    public Color BallColor { get; set; } // Color of the ball (Yellow for original, Red for duplicated)    

    public Ball(PointF position, PointF direction, float speed, float radius, bool isOriginal = false)
    {
        Position = position;
        Direction = direction;
        Speed = speed;
        Radius = radius;
        CollisionCooldown = 0; // No cooldown initially    
        IsOriginal = isOriginal;

        // NEW: Set BallColor based on whether the ball is original    
        BallColor = IsOriginal ? Color.Yellow : Color.Red;
    }

    public void Move()
    {
        Position = new PointF(Position.X + Direction.X * Speed, Position.Y + Direction.Y * Speed);

        // Decrease the collision cooldown after each move    
        if (CollisionCooldown > 0)
        {
            CollisionCooldown--;
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
}

public class BallSimulation
{
    private List<Ball> balls;
    private int width;
    private int height;
    private Random rand;

    /// <summary>    
    /// Initializes the ball simulation with two balls.    
    /// </summary>    
    public BallSimulation()
    {
        // Canvas size remains at 240x240 pixels    
        width = 240;
        height = 240;

        balls = new List<Ball>();
        rand = new Random();

        // Initialize two balls with random positions and directions    
        for (int i = 0; i < 2; i++)
        {
            float x = (float)(rand.NextDouble() * width);
            float y = (float)(rand.NextDouble() * height);

            // Adjusted the minimum margin for the smaller balls    
            float margin = 10.0f;

            // Ensure the balls are within the canvas boundaries    
            x = Math.Max(margin, Math.Min(width - margin, x));
            y = Math.Max(margin, Math.Min(height - margin, y));

            double angle = rand.NextDouble() * 2 * Math.PI;
            float dx = (float)Math.Cos(angle);
            float dy = (float)Math.Sin(angle);

            // Increased the ball speed slightly  
            float speed = 8.0f;   // Increased speed value    

            // Decreased the ball size for better spacing within the 240x240 canvas    
            float radius = 10.0f; // Changed from 15.0f to 10.0f    

            // Set IsOriginal = true for original balls    
            var ball = new Ball(new PointF(x, y), new PointF(dx, dy), speed, radius, isOriginal: true);
            balls.Add(ball);
        }
    }

    /// <summary>    
    /// Updates the ball positions, checks for collisions, and returns the bitmap with smiley balls.    
    /// </summary>    
    /// <returns>A Bitmap image showing all current ball positions within the canvas.</returns>    
    public Bitmap GetBallPositions()
    {
        // Update positions of balls    
        UpdateBallPositions();

        // Check for collisions and handle duplications    
        HandleCollisions();

        // Create bitmap and draw balls    
        Bitmap bitmap = new Bitmap(width, height);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent); // Clear the background to transparent    

            foreach (var ball in balls)
            {
                // Draw each ball as a smiley face    
                DrawSmiley(g, ball);
            }
        }

        return bitmap;
    }

    /// <summary>    
    /// Draws a smiley face representing the ball.    
    /// </summary>    
    private void DrawSmiley(Graphics g, Ball ball)
    {
        // Enable anti-aliasing for smoother graphics    
        var originalSmoothingMode = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        float diameter = ball.Radius * 2;
        float x = ball.Position.X - ball.Radius;
        float y = ball.Position.Y - ball.Radius;

        // Draw the face (use BallColor)    
        using (SolidBrush faceBrush = new SolidBrush(ball.BallColor))
        {
            g.FillEllipse(faceBrush, x, y, diameter, diameter);
        }
        g.DrawEllipse(Pens.Black, x, y, diameter, diameter);

        if (ball.IsOriginal)
        {
            // Original smiley face    
            // Calculate positions for eyes and mouth    
            float eyeRadius = diameter * 0.1f;
            float eyeOffsetX = diameter * 0.25f;
            float eyeOffsetY = diameter * 0.3f;

            // Left eye    
            g.FillEllipse(Brushes.Black,
                x + eyeOffsetX - eyeRadius / 2,
                y + eyeOffsetY - eyeRadius / 2,
                eyeRadius,
                eyeRadius);

            // Right eye    
            g.FillEllipse(Brushes.Black,
                x + diameter - eyeOffsetX - eyeRadius / 2,
                y + eyeOffsetY - eyeRadius / 2,
                eyeRadius,
                eyeRadius);

            // Mouth (arc)    
            float mouthWidth = diameter * 0.5f;
            float mouthHeight = diameter * 0.3f;
            float mouthX = x + (diameter - mouthWidth) / 2;
            float mouthY = y + diameter * 0.5f;

            g.DrawArc(
                new Pen(Color.Black, 2),
                mouthX,
                mouthY,
                mouthWidth,
                mouthHeight,
                20,
                140);
        }
        else
        {
            // Duplicated ball: Draw a random smiley face    
            // Randomize eye positions, sizes, and mouth    

            // Random eye sizes    
            float eyeRadius = diameter * (0.05f + 0.1f * (float)rand.NextDouble()); // Between 5% and 15% of diameter    

            // Random eye offsets    
            float eyeOffsetX1 = diameter * (0.15f + 0.2f * (float)rand.NextDouble());
            float eyeOffsetY1 = diameter * (0.2f + 0.3f * (float)rand.NextDouble());
            float eyeOffsetX2 = diameter * (0.65f + 0.2f * (float)rand.NextDouble());
            float eyeOffsetY2 = diameter * (0.2f + 0.3f * (float)rand.NextDouble());

            // Left eye    
            g.FillEllipse(Brushes.Black,
                x + eyeOffsetX1 - eyeRadius / 2,
                y + eyeOffsetY1 - eyeRadius / 2,
                eyeRadius,
                eyeRadius);

            // Right eye    
            g.FillEllipse(Brushes.Black,
                x + eyeOffsetX2 - eyeRadius / 2,
                y + eyeOffsetY2 - eyeRadius / 2,
                eyeRadius,
                eyeRadius);

            // Random mouth    
            float mouthWidth = diameter * (0.3f + 0.4f * (float)rand.NextDouble()); // Between 30% and 70% of diameter    
            float mouthHeight = diameter * (0.1f + 0.2f * (float)rand.NextDouble()); // Between 10% and 30% of diameter    
            float mouthX = x + (diameter - mouthWidth) / 2;
            float mouthY = y + diameter * (0.5f + 0.1f * (float)rand.NextDouble()); // Mouth starting between 50% and 60% down    

            float startAngle = 10f + 160f * (float)rand.NextDouble(); // Random start angle between 10° and 170°    
            float sweepAngle = 20f + 140f * (float)rand.NextDouble(); // Random sweep angle between 20° and 160°    

            g.DrawArc(
                new Pen(Color.Black, 2),
                mouthX,
                mouthY,
                mouthWidth,
                mouthHeight,
                startAngle,
                sweepAngle);
        }

        // Restore the original smoothing mode    
        g.SmoothingMode = originalSmoothingMode;
    }

    /// <summary>    
    /// Updates the positions of all balls and handles wall collisions.    
    /// </summary>    
    private void UpdateBallPositions()
    {
        foreach (var ball in balls)
        {
            ball.Move();

            // Check for wall collisions and adjust direction    

            // Left or right walls    
            if (ball.Position.X - ball.Radius < 0)
            {
                ball.Position = new PointF(ball.Radius, ball.Position.Y);
                ball.BounceX();
            }
            else if (ball.Position.X + ball.Radius > width)
            {
                ball.Position = new PointF(width - ball.Radius, ball.Position.Y);
                ball.BounceX();
            }

            // Top or bottom walls    
            if (ball.Position.Y - ball.Radius < 0)
            {
                ball.Position = new PointF(ball.Position.X, ball.Radius);
                ball.BounceY();
            }
            else if (ball.Position.Y + ball.Radius > height)
            {
                ball.Position = new PointF(ball.Position.X, height - ball.Radius);
                ball.BounceY();
            }
        }
    }

    /// <summary>    
    /// Checks for collisions between balls and duplicates colliding balls.    
    /// </summary>    
    private void HandleCollisions()
    {
        List<Ball> newBalls = new List<Ball>();

        for (int i = 0; i < balls.Count; i++)
        {
            Ball ball1 = balls[i];

            // Skip if this ball is on collision cooldown    
            if (ball1.CollisionCooldown > 0)
                continue;

            for (int j = i + 1; j < balls.Count; j++)
            {
                Ball ball2 = balls[j];

                // Skip if the other ball is on collision cooldown    
                if (ball2.CollisionCooldown > 0)
                    continue;

                float dx = ball1.Position.X - ball2.Position.X;
                float dy = ball1.Position.Y - ball2.Position.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);

                if (distance <= ball1.Radius + ball2.Radius)
                {
                    // Collision detected    

                    // Check if either ball is original    
                    if (ball1.IsOriginal || ball2.IsOriginal)
                    {
                        // Duplicate the colliding original ball    
                        // Create a new ball with random properties    

                        // Random position within the canvas    
                        float newX = (float)(rand.NextDouble() * width);
                        float newY = (float)(rand.NextDouble() * height);

                        // Ensure the ball is within the canvas boundaries    
                        float margin = 10.0f;
                        newX = Math.Max(margin, Math.Min(width - margin, newX));
                        newY = Math.Max(margin, Math.Min(height - margin, newY));

                        // Random direction    
                        double angle = rand.NextDouble() * 2 * Math.PI;
                        float newDx = (float)Math.Cos(angle);
                        float newDy = (float)Math.Sin(angle);

                        // Increased the minimum speed  
                        float newSpeed = 2.0f + (float)(rand.NextDouble() * 3.0f); // Speed between 2.0 and 5.0    

                        // Random radius    
                        float newRadius = 5.0f + (float)(rand.NextDouble() * 10.0f); // Radius between 5.0f and 15.0f    

                        // Create new ball (IsOriginal = false, and BallColor will be set to Red)    
                        var newBall = new Ball(new PointF(newX, newY), new PointF(newDx, newDy), newSpeed, newRadius, isOriginal: false);
                        newBalls.Add(newBall);

                        // Set collision cooldown to prevent immediate re-collision    
                        int cooldownFrames = 30; // Increased cooldown from 10 to 30 frames    
                        ball1.CollisionCooldown = cooldownFrames;
                        ball2.CollisionCooldown = cooldownFrames;
                    }

                    // Bounce the balls off each other (simple elastic collision response)    
                    BounceBalls(ball1, ball2);

                    // No need to check other collisions for this pair in this update    
                    // Continue to next pair    
                }
            }
        }

        // Add all new balls to the list    
        balls.AddRange(newBalls);
    }

    /// <summary>    
    /// Simulates a simple elastic collision between two balls by swapping their velocities.    
    /// </summary>    
    private void BounceBalls(Ball ball1, Ball ball2)
    {
        // Simple elastic collision by swapping velocities    
        // This is a simplification and may not be physically accurate    
        var tempDirection = ball1.Direction;
        ball1.Direction = ball2.Direction;
        ball2.Direction = tempDirection;
    }
}