// VillageSimulation class  
using SyncMagic;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

public class VillageSimulation
{
    private int width = 240;
    private int height = 240;
    private int gridSize = 20; // Each grid cell is 20x20 pixels  
    private int gridRows = 12;
    private int gridCols = 12;
    private Random random;
    private GridCell[,] grid;
    private List<Villager> villagers = new List<Villager>();
    private int frameCounter = 0;
    private const int VEGETATION_UPDATE_INTERVAL = 240; // Update vegetation every ~2 minutes  
    private int timeFrameCounter = 0;
    private const int TIME_OF_DAY_INTERVAL = 240; // Or whatever value makes sense  
    private TimeOfDay currentTimeOfDay = TimeOfDay.Morning; // Initial value  
    private List<Building> buildings = new List<Building>();

    public VillageSimulation(int seed = 0)
    {
        // Use a seed for reproducibility  
        random = seed == 0 ? new Random() : new Random(seed);
        InitializeGrid();
        InitializeVillagers();
    }

    private void InitializeGrid()
    {
        // Create grid  
        grid = new GridCell[gridRows, gridCols];

        // Initialize all cells as open spaces  
        for (int row = 0; row < gridRows; row++)
        {
            for (int col = 0; col < gridCols; col++)
            {
                grid[row, col] = new GridCell
                {
                    Row = row,
                    Col = col,
                    CellType = CellType.OpenSpace
                };
            }
        }

        // Assign buildings  
        AssignBuildings();

        // Assign paths connecting buildings  
        GeneratePaths();

        // Assign vegetation with denser patches near paths and buildings  
        AssignVegetation();
    }

    private void AssignBuildings()
    {
        int totalCells = gridRows * gridCols;
        int buildingCellsCount = random.Next((int)(totalCells * 0.1), (int)(totalCells * 0.2) + 1);

        for (int i = 0; i < buildingCellsCount; i++)
        {
            int row, col;
            do
            {
                row = random.Next(0, gridRows);
                col = random.Next(0, gridCols);
            } while (grid[row, col].CellType != CellType.OpenSpace);

            grid[row, col].CellType = CellType.Building;
            grid[row, col].Color = GetRandomMutedColor();

            // Create a Building object  
            Rectangle buildingRect = new Rectangle(col * gridSize, row * gridSize, gridSize, gridSize);
            GridCell cell = grid[row, col];
            Building building = new Building(cell);
            building.Rect = buildingRect;
            buildings.Add(building);
        }
    }

    private void GeneratePaths()
    {
        // Implement path generation logic here, similar adjustments as above  
    }

    private void AssignVegetation()
    {
        int totalCells = gridRows * gridCols;
        int vegetationCellsCount = random.Next((int)(totalCells * 0.2), (int)(totalCells * 0.3) + 1);

        for (int i = 0; i < vegetationCellsCount; i++)
        {
            int row, col;
            do
            {
                // Bias towards cells near paths and buildings  
                bool nearSpecialCell = random.Next(0, 100) < 70; // 70% chance to place near paths/buildings  

                if (nearSpecialCell)
                {
                    // Get a random adjacent cell to a path or building  
                    List<GridCell> adjacentCells = GetAdjacentOpenCells(CellType.Path, CellType.Building);
                    if (adjacentCells.Count > 0)
                    {
                        GridCell chosenCell = adjacentCells[random.Next(adjacentCells.Count)];
                        row = chosenCell.Row;
                        col = chosenCell.Col;
                    }
                    else
                    {
                        // No adjacent cells available, fall back to random cell  
                        row = random.Next(0, gridRows);
                        col = random.Next(0, gridCols);
                    }
                }
                else
                {
                    row = random.Next(0, gridRows);
                    col = random.Next(0, gridCols);
                }
            } while (grid[row, col].CellType != CellType.OpenSpace);

            grid[row, col].CellType = CellType.Vegetation;
            grid[row, col].Size = random.Next(3, 11);
        }
    }

    private List<GridCell> GetCellsOfType(CellType type)
    {
        List<GridCell> cells = new List<GridCell>();
        foreach (var cell in grid)
        {
            if (cell.CellType == type)
                cells.Add(cell);
        }
        return cells;
    }

    private List<GridCell> GetAdjacentOpenCells(params CellType[] types)
    {
        List<GridCell> adjacentCells = new List<GridCell>();

        foreach (var cell in grid)
        {
            if (types.Contains(cell.CellType))
            {
                List<GridCell> neighbors = GetNeighboringCells(cell.Row, cell.Col);
                foreach (var neighbor in neighbors)
                {
                    if (neighbor.CellType == CellType.OpenSpace && !adjacentCells.Contains(neighbor))
                        adjacentCells.Add(neighbor);
                }
            }
        }

        return adjacentCells;
    }

    private List<GridCell> GetNeighboringCells(int row, int col)
    {
        List<GridCell> neighbors = new List<GridCell>();
        int[] dRows = { -1, 1, 0, 0 };
        int[] dCols = { 0, 0, -1, 1 };

        for (int i = 0; i < 4; i++)
        {
            int newRow = row + dRows[i];
            int newCol = col + dCols[i];

            if (newRow >= 0 && newRow < gridRows && newCol >= 0 && newCol < gridCols)
            {
                neighbors.Add(grid[newRow, newCol]);
            }
        }

        return neighbors;
    }

    private void InitializeVillagers()
    {
        int numberOfVillagers = random.Next(5, 16);

        List<Villager> villagersToAssign = new List<Villager>();

        for (int i = 0; i < numberOfVillagers; i++)
        {
            Villager villager = new Villager(grid)
            {
                Name = GenerateRandomName(),
                PersonalityTrait = GenerateRandomPersonalityTrait(),
                Occupation = GenerateRandomOccupation(),
                ClothingColor = GetRandomClothingColor(),
                HasHeadgear = random.Next(0, 2) == 0, // 50% chance  
                HairStyle = GenerateRandomHairStyle(),
                HairColor = GetRandomHairColor(),
                MovementSpeed = random.NextDouble() * 0.5 + 0.5, // Speed between 0.5 and 1.0  
            };

            // Assign a starting cell to the villager  
            int row, col;
            do
            {
                row = random.Next(0, gridRows);
                col = random.Next(0, gridCols);
            } while (grid[row, col].CellType != CellType.OpenSpace);

            villager.CurrentCell = grid[row, col];

            villagersToAssign.Add(villager);
        }

        AssignHomesToVillagers(villagersToAssign);

        villagers.AddRange(villagersToAssign);
    }

    private void AssignHomesToVillagers(List<Villager> villagersToAssign)
    {
        // Get buildings that are not assigned as homes  
        List<Building> availableHomes = buildings.Where(b => !b.IsHome).ToList();

        // Prioritize buildings near the village center  
        Point center = new Point(gridCols / 2, gridRows / 2);
        availableHomes = availableHomes.OrderBy(b => GetDistance(center, b.Cell)).ToList();

        int villagerIndex = 0;
        foreach (var home in availableHomes)
        {
            for (int i = 0; i < 2 && villagerIndex < villagersToAssign.Count; i++)
            {
                Villager villager = villagersToAssign[villagerIndex];
                villager.Home = home;
                home.Residents.Add(villager);
                villagerIndex++;
            }
            home.IsHome = true;

            if (villagerIndex >= villagersToAssign.Count)
                break;
        }
    }

    private double GetDistance(Point a, GridCell bCell)
    {
        Point b = new Point(bCell.Col, bCell.Row);
        int deltaX = a.X - b.X;
        int deltaY = a.Y - b.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    public Bitmap GetVillage()
    {
        // Increment frame counters  
        frameCounter++;
        timeFrameCounter++;

        // Update time of day if necessary  
        if (timeFrameCounter % TIME_OF_DAY_INTERVAL == 0)
        {
            AdvanceTimeOfDay();
        }

        // Update vegetation less frequently  
        if (frameCounter % VEGETATION_UPDATE_INTERVAL == 0)
        {
            UpdateVegetation();
        }

        Bitmap villageBitmap = new Bitmap(width, height);
        using (Graphics g = Graphics.FromImage(villageBitmap))
        {
            // Clear background  
            g.Clear(Color.LightGreen);

            DrawGrid(g);
            UpdateAndDrawVillagers(g);
        }
        return villageBitmap;
    }

    private void AdvanceTimeOfDay()
    {
        // Cycle through time of day  
        switch (currentTimeOfDay)
        {
            case TimeOfDay.Morning:
                currentTimeOfDay = TimeOfDay.Midday;
                break;
            case TimeOfDay.Midday:
                currentTimeOfDay = TimeOfDay.Evening;
                break;
            case TimeOfDay.Evening:
                currentTimeOfDay = TimeOfDay.Night;
                break;
            case TimeOfDay.Night:
                currentTimeOfDay = TimeOfDay.Morning;
                break;
        }

        // Implement any events or changes when time advances  
        HandleTimeOfDayChange();
    }

    private void HandleTimeOfDayChange()
    {
        // For example, trigger events or change villager behaviors  
        // Randomly decide to trigger a market day or festival  
        int chance = random.Next(0, 100);
        if (chance < 10) // 10% chance  
        {
            TriggerRandomEvent();
        }
    }

    private void TriggerRandomEvent()
    {
        int eventType = random.Next(0, 2);
        switch (eventType)
        {
            case 0:
                // Trigger Market Day  
                foreach (var villager in villagers)
                {
                    villager.CurrentEvent = VillagerEvent.MarketDay;
                }
                break;
            case 1:
                // Trigger Festival  
                foreach (var villager in villagers)
                {
                    villager.CurrentEvent = VillagerEvent.Festival;
                }
                break;
                // Add more events as needed  
        }
    }

    private void UpdateVegetation()
    {
        // Small chance to shift or replace one vegetation element  
        int chance = random.Next(0, 100);
        if (chance < 10) // 10% chance to modify vegetation  
        {
            List<GridCell> vegetationCells = GetCellsOfType(CellType.Vegetation);
            if (vegetationCells.Count > 0)
            {
                int index = random.Next(vegetationCells.Count);
                GridCell vegCell = vegetationCells[index];
                vegCell.CellType = CellType.OpenSpace;
                vegCell.Size = 0;

                // Add a new vegetation element somewhere else  
                AssignVegetation();
            }
        }
    }

    private void DrawGrid(Graphics g)
    {
        for (int row = 0; row < gridRows; row++)
        {
            for (int col = 0; col < gridCols; col++)
            {
                GridCell cell = grid[row, col];
                int x = col * gridSize;
                int y = row * gridSize;

                switch (cell.CellType)
                {
                    case CellType.OpenSpace:
                        // No drawing needed for open spaces (background color)  
                        break;
                    case CellType.Building:
                        using (SolidBrush brush = new SolidBrush(cell.Color))
                        {
                            g.FillRectangle(brush, x, y, gridSize, gridSize);
                        }
                        break;
                    case CellType.Path:
                        using (SolidBrush brush = new SolidBrush(Color.SaddleBrown))
                        {
                            g.FillRectangle(brush, x, y, gridSize, gridSize);
                        }
                        break;
                    case CellType.Vegetation:
                        using (SolidBrush brush = new SolidBrush(Color.ForestGreen))
                        {
                            int size = cell.Size;
                            int centerX = x + gridSize / 2;
                            int centerY = y + gridSize / 2;
                            g.FillEllipse(brush, centerX - size / 2, centerY - size / 2, size, size);
                        }
                        break;
                }
            }
        }
    }

    private void UpdateAndDrawVillagers(Graphics g)
    {
        foreach (var villager in villagers)
        {
            villager.Move(random, currentTimeOfDay, villagers);

            // Draw the villager  
            int villagerSize = 8;
            int x = villager.CurrentCell.Col * gridSize + gridSize / 2 - villagerSize / 2;
            int y = villager.CurrentCell.Row * gridSize + gridSize / 2 - villagerSize / 2;

            // Composite villager appearance  
            DrawVillager(g, villager, x, y, villagerSize);
        }
    }

    // Below are placeholder methods for generating random names, traits, and drawing villagers  
    private string GenerateRandomName()
    {
        string[] names = { "Alice", "Bob", "Clara", "David", "Eva", "Frank", "Grace", "Henry" };
        return names[random.Next(names.Length)];
    }

    private string GenerateRandomPersonalityTrait()
    {
        string[] traits = { "Friendly", "Grumpy", "Curious", "Shy", "Outgoing" };
        return traits[random.Next(traits.Length)];
    }

    private Occupation GenerateRandomOccupation()
    {
        Occupation[] occupations = { Occupation.Farmer, Occupation.Shopkeeper, Occupation.Wanderer, Occupation.Blacksmith };
        return occupations[random.Next(occupations.Length)];
    }

    private Color GetRandomClothingColor()
    {
        Color[] colors = { Color.Blue, Color.Green, Color.Red, Color.Yellow, Color.Purple };
        return colors[random.Next(colors.Length)];
    }

    private Color GetRandomHairColor()
    {
        Color[] hairColors = { Color.Black, Color.Brown, Color.Gold, Color.Gray, Color.Red };
        return hairColors[random.Next(hairColors.Length)];
    }

    private string GenerateRandomHairStyle()
    {
        string[] styles = { "Short", "Long", "Curly", "Straight", "Bald" };
        return styles[random.Next(styles.Length)];
    }

    private void DrawVillager(Graphics g, Villager villager, int x, int y, int size)
    {
        // Draw clothing  
        using (SolidBrush brush = new SolidBrush(villager.ClothingColor))
        {
            g.FillEllipse(brush, x, y, size, size);
        }
        // Draw headgear if any  
        if (villager.HasHeadgear)
        {
            using (SolidBrush brush = new SolidBrush(Color.DarkGray))
            {
                int hatHeight = size / 3;
                g.FillRectangle(brush, x, y - hatHeight / 2, size, hatHeight);
            }
        }
        // Draw hair  
        else
        {
            using (SolidBrush brush = new SolidBrush(villager.HairColor))
            {
                int hairHeight = size / 4;
                g.FillEllipse(brush, x, y - hairHeight / 2, size, hairHeight);
            }
        }
        // Optionally, add more details like face features  
    }

    private Color GetRandomMutedColor()
    {
        return Color.FromArgb(
            255,
            random.Next(100, 156),
            random.Next(100, 156),
            random.Next(100, 156)
        );
    }
}