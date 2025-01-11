using System;
using System.Collections.Generic;
using System.Drawing;

public class ArkanoidGame
{
    private int canvasWidth = 240;
    private int canvasHeight = 240;

    // Paddle properties    
    private float paddleWidth = 50f;
    private float paddleHeight = 10f;
    private float paddleX;
    private float paddleY;

    // Ball properties    
    private float ballX;
    private float ballY;
    private float ballRadius = 5f;
    private float ballSpeed;
    private float ballVelocityX;
    private float ballVelocityY;

    // Bricks    
    private List<RectangleF> bricks;
    private int bricksPerRow;
    private int brickRows;
    private float brickWidth;
    private float brickHeight = 15f;

    private Random rand;

    private int level;

    // Configurable property for the number of frames to update per render  
    private int framesPerRender = 3;

    /// <summary>  
    /// Gets or sets the number of frames to update each time GetFrame is called.  
    /// </summary>  
    public int FramesPerRender
    {
        get { return framesPerRender; }
        set { framesPerRender = Math.Max(1, value); } // Ensure at least one frame is updated  
    }

    public ArkanoidGame()
    {
        rand = new Random();
        level = 1;
        InitializeGame();
    }

    /// <summary>    
    /// Initializes or resets the game state for a new level.    
    /// </summary>    
    private void InitializeGame()
    {
        // Initialize paddle position at the bottom center    
        paddleX = (canvasWidth - paddleWidth) / 2;
        paddleY = canvasHeight - paddleHeight - 10;

        // Initialize ball position above the paddle    
        ballX = paddleX + paddleWidth / 2;
        ballY = paddleY - ballRadius * 2;

        // Initialize ball velocity    
        ballSpeed = Math.Min(8f, 3f + level * 0.5f); // Increase speed with level up to a max    
        double angle = rand.NextDouble() * Math.PI / 2 + Math.PI / 4; // Launch angle between 45 and 135 degrees    
        ballVelocityX = ballSpeed * (float)Math.Cos(angle);
        ballVelocityY = -ballSpeed * (float)Math.Sin(angle);

        // Initialize bricks with random configuration    
        GenerateBricks();
    }

    /// <summary>    
    /// Generates a random brick layout for the current level.    
    /// </summary>    
    private void GenerateBricks()
    {
        bricks = new List<RectangleF>();

        // Randomly determine number of rows and bricks per row, increasing with level    
        brickRows = rand.Next(3, 6 + level); // Increase rows with level    
        bricksPerRow = rand.Next(5, 9); // Variable bricks per row    

        brickWidth = canvasWidth / bricksPerRow;

        for (int row = 0; row < brickRows; row++)
        {
            for (int col = 0; col < bricksPerRow; col++)
            {
                // Randomly decide whether to place a brick at this position    
                if (rand.NextDouble() < 0.7) // 70% chance to place a brick    
                {
                    float x = col * brickWidth;
                    float y = row * brickHeight + 30; // Start bricks a bit down from top    
                    RectangleF brick = new RectangleF(x, y, brickWidth - 2, brickHeight - 2); // Small gap between bricks    
                    bricks.Add(brick);
                }
            }
        }
    }

    /// <summary>    
    /// Updates the game state, including positions of the paddle, ball, and detects collisions.    
    /// </summary>    
    private void UpdateGame()
    {
        // Move paddle to track the ball's x-position    
        float paddleSpeed = 5f + level * 0.2f; // Increase paddle speed slightly with level    
        float targetX = ballX - paddleWidth / 2;
        float delta = targetX - paddleX;
        if (Math.Abs(delta) > paddleSpeed)
        {
            paddleX += Math.Sign(delta) * paddleSpeed;
        }
        else
        {
            paddleX = targetX;
        }

        // Keep paddle within screen bounds    
        paddleX = Math.Max(0, Math.Min(canvasWidth - paddleWidth, paddleX));

        // Update ball position    
        ballX += ballVelocityX;
        ballY += ballVelocityY;

        // Collision with walls    
        if (ballX - ballRadius < 0)
        {
            ballX = ballRadius;
            ballVelocityX *= -1;
        }
        else if (ballX + ballRadius > canvasWidth)
        {
            ballX = canvasWidth - ballRadius;
            ballVelocityX *= -1;
        }

        if (ballY - ballRadius < 0)
        {
            ballY = ballRadius;
            ballVelocityY *= -1;
        }

        // Collision with paddle    
        RectangleF paddleRect = new RectangleF(paddleX, paddleY, paddleWidth, paddleHeight);
        if (ballY + ballRadius >= paddleY && ballY + ballRadius <= paddleY + paddleHeight)
        {
            if (ballX >= paddleX && ballX <= paddleX + paddleWidth)
            {
                ballY = paddleY - ballRadius;
                ballVelocityY *= -1;

                // Add some "spin" to the ball based on where it hits the paddle    
                float hitPos = (ballX - paddleX) / paddleWidth - 0.5f;
                ballVelocityX += hitPos * 2f; // Adjust spin effect as needed    
            }
        }

        // Collision with bricks    
        for (int i = 0; i < bricks.Count; i++)
        {
            RectangleF brick = bricks[i];
            if (brick.Contains(ballX, ballY))
            {
                bricks.RemoveAt(i);
                ballVelocityY *= -1; // Simple collision response    
                break;
            }
        }

        // If all bricks are destroyed, generate a new level    
        if (bricks.Count == 0)
        {
            level++;
            InitializeGame();
        }

        // Ball falling below the paddle: Reset the game at current level    
        if (ballY - ballRadius > canvasHeight)
        {
            InitializeGame();
        }
    }

    /// <summary>    
    /// Updates the game state and renders the current frame onto a bitmap.    
    /// </summary>    
    /// <returns>A bitmap representing the current game frame.</returns>    
    public Bitmap GetFrame()
    {
        // Update game state multiple times based on FramesPerRender  
        for (int i = 0; i < framesPerRender; i++)
        {
            UpdateGame();
        }

        // Create a bitmap to draw on    
        Bitmap bmp = new Bitmap(canvasWidth, canvasHeight);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);

            // Enable smoother rendering    
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Draw paddle    
            Brush paddleBrush = Brushes.White;
            g.FillRectangle(paddleBrush, paddleX, paddleY, paddleWidth, paddleHeight);

            // Draw ball    
            Brush ballBrush = Brushes.Yellow;
            g.FillEllipse(ballBrush, ballX - ballRadius, ballY - ballRadius, ballRadius * 2, ballRadius * 2);

            // Draw bricks with varied colors    
            foreach (RectangleF brick in bricks)
            {
                // Generate a random color for the brick    
                Color brickColor = Color.FromArgb(rand.Next(100, 256), rand.Next(100, 256), rand.Next(100, 256));
                Brush brickBrush = new SolidBrush(brickColor);
                g.FillRectangle(brickBrush, brick);
                brickBrush.Dispose();
            }
        }

        return bmp;
    }
}