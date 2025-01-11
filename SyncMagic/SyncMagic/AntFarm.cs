using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace SyncMagic
{
    public class AntFarm
    {
        private const int FarmSize = 240; // Size in pixels  
        private const int CellSize = 6;   // Size of each cell in pixels  
        private int gridWidth;
        private int gridHeight;

        private int[,] tunnels; // Represents the tunnel grid  
        private double[,] pheromones; // Pheromone levels  
        private List<Ant> ants;  // List of ants  
        private Point basePosition; // Position of the queen's chamber (base)  
        private Point eggStoragePosition;
        private Point foodStoragePosition;

        private Random random;
        private List<Point> foodPositions;
        private List<Point> eggPositions;

        private int elapsedTime; // To track the simulation time  

        public AntFarm()
        {
            gridWidth = FarmSize / CellSize;
            gridHeight = FarmSize / CellSize;
            tunnels = new int[gridWidth, gridHeight]; // Tunnel grid  
            pheromones = new double[gridWidth, gridHeight]; // Pheromone levels  
            ants = new List<Ant>();
            random = new Random();

            foodPositions = new List<Point>();
            eggPositions = new List<Point>();

            elapsedTime = 0;

            InitializeFarm();
        }

        private void InitializeFarm()
        {
            // Adjust margin based on new grid size  
            int margin = gridWidth / 6; // Ensure base is not too close to edges  
            basePosition = new Point(
                random.Next(margin, gridWidth - margin),
                random.Next(margin, gridHeight - margin)
            );
            tunnels[basePosition.X, basePosition.Y] = 1;

            // Set egg storage to the left of base  
            eggStoragePosition = new Point(basePosition.X - 3, basePosition.Y);
            if (IsValidPosition(eggStoragePosition))
                tunnels[eggStoragePosition.X, eggStoragePosition.Y] = 1;

            // Set food storage to the right of base  
            foodStoragePosition = new Point(basePosition.X + 3, basePosition.Y);
            if (IsValidPosition(foodStoragePosition))
                tunnels[foodStoragePosition.X, foodStoragePosition.Y] = 1;

            // Create paths to egg storage and food storage  
            CreatePath(basePosition, eggStoragePosition);
            CreatePath(basePosition, foodStoragePosition);

            // Create initial tunnel network around the base to prevent isolation  
            CreateInitialTunnels();

            // Initialize ants with different roles  
            int workerCount = 5;
            int foragerCount = 5;
            int nurseCount = 2;
            int queenAttendantCount = 2;

            // Create workers  
            for (int i = 0; i < workerCount; i++)
            {
                ants.Add(new Ant(basePosition, AntRole.Worker, random, this));
            }

            // Create foragers  
            for (int i = 0; i < foragerCount; i++)
            {
                ants.Add(new Ant(basePosition, AntRole.Forager, random, this));
            }

            // Create nurses  
            for (int i = 0; i < nurseCount; i++)
            {
                ants.Add(new Ant(basePosition, AntRole.Nurse, random, this));
            }

            // Create queen attendants  
            for (int i = 0; i < queenAttendantCount; i++)
            {
                ants.Add(new Ant(basePosition, AntRole.QueenAttendant, random, this));
            }

            // Create the queen  
            ants.Add(new Ant(basePosition, AntRole.Queen, random, this));

            // Place initial food and eggs  
            PlaceFoodSources();
            PlaceEggs();
        }

        private bool IsValidPosition(Point position)
        {
            return position.X >= 0 && position.X < gridWidth &&
                   position.Y >= 0 && position.Y < gridHeight;
        }

        private void CreateInitialTunnels()
        {
            // Create a small network of tunnels around the base to prevent ants from getting stuck  
            for (int x = basePosition.X - 2; x <= basePosition.X + 2; x++)
            {
                for (int y = basePosition.Y - 2; y <= basePosition.Y + 2; y++)
                {
                    if (x >= 0 && x < gridWidth && y >= 0 && y < gridHeight)
                    {
                        tunnels[x, y] = 1;
                    }
                }
            }
        }

        private void CreatePath(Point start, Point end)
        {
            // Create a simple path between two points (e.g., straight lines)  
            int x = start.X;
            int y = start.Y;

            while (x != end.X || y != end.Y)
            {
                // Dig tunnel at current position  
                if (IsValidPosition(new Point(x, y)))
                    tunnels[x, y] = 1;

                if (x < end.X) x++;
                else if (x > end.X) x--;

                if (y < end.Y) y++;
                else if (y > end.Y) y--;
            }

            // Mark the end position as well  
            if (IsValidPosition(new Point(x, y)))
                tunnels[x, y] = 1;
        }

        /// <summary>  
        /// Places food sources outside the existing tunnel network to encourage ants to expand.  
        /// </summary>  
        private void PlaceFoodSources()
        {
            // Limit the maximum number of food sources  
            int maxFoodSources = 5;
            if (foodPositions.Count >= maxFoodSources)
                return;

            int attempts = 0;
            int maxAttempts = 100; // Prevent infinite loops  

            while (foodPositions.Count < maxFoodSources && attempts < maxAttempts)
            {
                int x = random.Next(0, gridWidth);
                int y = random.Next(0, gridHeight);

                Point newFoodPosition = new Point(x, y);

                // Check if the position is outside the current tunnel network  
                if (tunnels[x, y] == 0 && !foodPositions.Contains(newFoodPosition))
                {
                    // Check if the position is a certain distance away from the base  
                    int distanceFromBase = Math.Abs(x - basePosition.X) + Math.Abs(y - basePosition.Y);
                    if (distanceFromBase > Math.Min(gridWidth, gridHeight) / 4)
                    {
                        foodPositions.Add(newFoodPosition);
                    }
                }

                attempts++;
            }
        }

        private void PlaceEggs()
        {
            // Assume eggs are at the base (queen lays eggs)  
            eggPositions.Add(basePosition);
        }

        /// <summary>  
        /// Updates the ant farm by moving ants and extending tunnels.  
        /// </summary>  
        private void Update()
        {
            elapsedTime++;

            // Move ants and update their states  
            foreach (Ant ant in ants.ToList()) // Use ToList() to avoid modification during iteration  
            {
                ant.Update();
                // Remove ants that have reached their lifespan  
                if (ant.Age >= ant.Lifespan && ant.Role != AntRole.Queen)
                {
                    ants.Remove(ant);
                }
            }

            // Evaporate pheromones over time  
            EvaporatePheromones();

            // Adjust food spawning frequency based on current food availability  
            int totalFood = foodPositions.Count;
            double foodSpawnChance;

            if (totalFood == 0)
            {
                foodSpawnChance = 0.02; // Increase chance when no food is available  
            }
            else if (totalFood < 3)
            {
                foodSpawnChance = 0.005; // Moderate chance  
            }
            else
            {
                foodSpawnChance = 0.001; // Low chance  
            }

            // Occasionally spawn new food  
            if (random.NextDouble() < foodSpawnChance)
            {
                PlaceFoodSources();
            }

            // Queen lays new eggs at a regular interval  
            if (elapsedTime % 50 == 0) // Every 50 time units  
            {
                eggPositions.Add(basePosition);
            }

            // Eggs hatch into new ants after some time  
            HatchEggs();
        }

        /// <summary>  
        /// Hatches eggs into new ants after a certain incubation period.  
        /// </summary>  
        private void HatchEggs()
        {
            // Assuming eggs take 100 time units to hatch  
            if (elapsedTime % 100 == 0 && eggPositions.Count > 0)
            {
                // For simplicity, hatch all eggs  
                int newAntsCount = eggPositions.Count;
                eggPositions.Clear();

                // Distribute roles among new ants  
                for (int i = 0; i < newAntsCount; i++)
                {
                    AntRole newRole = AssignAntRole();
                    ants.Add(new Ant(basePosition, newRole, random, this));
                }
            }
        }

        /// <summary>  
        /// Assigns roles to new ants based on colony needs.  
        /// </summary>  
        private AntRole AssignAntRole()
        {
            int workerCount = ants.Count(a => a.Role == AntRole.Worker);
            int foragerCount = ants.Count(a => a.Role == AntRole.Forager);
            int nurseCount = ants.Count(a => a.Role == AntRole.Nurse);
            int queenAttendantCount = ants.Count(a => a.Role == AntRole.QueenAttendant);

            // Simple logic to balance roles  
            if (workerCount < 10)
                return AntRole.Worker;
            else if (foragerCount < 15)
                return AntRole.Forager;
            else if (nurseCount < 5)
                return AntRole.Nurse;
            else
                return AntRole.Forager; // Default to forager if roles are balanced  
        }

        /// <summary>  
        /// Evaporates pheromones over time.  
        /// </summary>  
        private void EvaporatePheromones()
        {
            double evaporationRate = 0.01;
            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    pheromones[x, y] *= (1 - evaporationRate);
                }
            }
        }

        /// <summary>  
        /// Generates and returns the current image of the ant farm.  
        /// </summary>  
        /// <returns>A Bitmap image of the ant farm.</returns>  
        public Bitmap GetFarmImage()
        {
            Update(); // Move ants and create tunnels before drawing  

            Bitmap bitmap = new Bitmap(FarmSize, FarmSize);

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                // Draw the background (soil)  
                g.Clear(Color.SaddleBrown);

                // Draw tunnels  
                for (int x = 0; x < gridWidth; x++)
                {
                    for (int y = 0; y < gridHeight; y++)
                    {
                        if (tunnels[x, y] > 0)
                        {
                            // Brighten color if there are pheromones  
                            double pheromoneLevel = pheromones[x, y];
                            double brightnessFactor = 1 + pheromoneLevel;
                            brightnessFactor = Math.Min(brightnessFactor, 2); // Max brightness  

                            Color baseColor = Color.BurlyWood;
                            int r = (int)(baseColor.R * brightnessFactor);
                            int gColor = (int)(baseColor.G * brightnessFactor);
                            int b = (int)(baseColor.B * brightnessFactor);

                            r = Math.Min(255, r);
                            gColor = Math.Min(255, gColor);
                            b = Math.Min(255, b);

                            Color tunnelColor = Color.FromArgb(r, gColor, b);
                            Brush tunnelBrush = new SolidBrush(tunnelColor);
                            g.FillRectangle(tunnelBrush, x * CellSize, y * CellSize, CellSize, CellSize);
                        }
                    }
                }

                // Draw food positions  
                foreach (var food in foodPositions)
                {
                    g.FillRectangle(Brushes.LightGreen, food.X * CellSize, food.Y * CellSize, CellSize, CellSize);
                }

                // Draw egg positions  
                foreach (var egg in eggPositions)
                {
                    g.FillRectangle(Brushes.Yellow, egg.X * CellSize, egg.Y * CellSize, CellSize, CellSize);
                }

                // Draw the ants' base (queen's chamber)  
                g.FillRectangle(Brushes.Gold, basePosition.X * CellSize, basePosition.Y * CellSize, CellSize, CellSize);

                // Draw egg storage  
                g.FillRectangle(Brushes.Orange, eggStoragePosition.X * CellSize, eggStoragePosition.Y * CellSize, CellSize, CellSize);

                // Draw food storage  
                g.FillRectangle(Brushes.Cyan, foodStoragePosition.X * CellSize, foodStoragePosition.Y * CellSize, CellSize, CellSize);

                // Draw ants  
                foreach (Ant ant in ants)
                {
                    Brush antBrush = Brushes.Red; // Default color for ants  

                    switch (ant.Role)
                    {
                        case AntRole.Nurse:
                            antBrush = Brushes.LimeGreen; // Bright green for nurses  
                            break;
                        case AntRole.Forager:
                            antBrush = Brushes.OrangeRed; // Vibrant orange-red for foragers  
                            break;
                        case AntRole.QueenAttendant:
                            antBrush = Brushes.Magenta; // Bright magenta for queen attendants  
                            break;
                        case AntRole.Queen:
                            antBrush = Brushes.DeepPink; // Stand-out pink for the queen  
                            break;
                    }

                    if (ant.Role == AntRole.Queen)
                    {
                        // Draw the queen slightly taller  
                        g.FillRectangle(antBrush,
                                        ant.Position.X * CellSize,
                                        ant.Position.Y * CellSize,
                                        CellSize,
                                        CellSize * 3.5f); // Increase height  
                    }
                    else
                    {
                        g.FillRectangle(antBrush,
                                        ant.Position.X * CellSize,
                                        ant.Position.Y * CellSize,
                                        CellSize,
                                        CellSize);
                    }
                }
            }

            return bitmap;
        }

        // Provide accessors for ant use  
        public int GridWidth => gridWidth;
        public int GridHeight => gridHeight;
        public int[,] Tunnels => tunnels;
        public double[,] Pheromones => pheromones;
        public List<Point> FoodPositions => foodPositions;
        public List<Point> EggPositions => eggPositions;
        public Point BasePosition => basePosition;
        public Point EggStoragePosition => eggStoragePosition;
        public Point FoodStoragePosition => foodStoragePosition;
    }
}