// SyncMagic namespace containing GridCell, CellType, Villager, Building, and related enums  
namespace SyncMagic
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;

    public enum CellType
    {
        OpenSpace,
        Vegetation,
        Water,
        Building,
        Path,
        Market,
        Festival,
        // etc.  
    }

    public class GridCell
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public CellType CellType { get; set; }
        public Rectangle Rect { get; set; }
        public Color Color { get; set; } // For buildings  
        public int Size { get; set; }    // For vegetation  
        // Other properties as needed  
    }

    public class Building
    {
        public GridCell Cell { get; set; }
        public Rectangle Rect { get; set; }
        public Color Color { get; set; }
        public List<Villager> Residents { get; set; } = new List<Villager>(); // Up to 2 villagers  
        public bool IsHome { get; set; } = false; // Indicates if the building is assigned as a home  

        public Building(GridCell cell)
        {
            Cell = cell;
        }
    }

    public enum Occupation
    {
        Farmer,
        Shopkeeper,
        Wanderer,
        Blacksmith,
        // Add other occupations as needed  
    }

    public enum TimeOfDay
    {
        Morning,
        Midday,
        Evening,
        Night
    }

    public enum VillagerEvent
    {
        None,
        MarketDay,
        Festival,
        // Add other events  
    }

    public class Villager
    {
        public string Name { get; set; }
        public string PersonalityTrait { get; set; }
        public Occupation Occupation { get; set; }
        public Color ClothingColor { get; set; }
        public bool HasHeadgear { get; set; }
        public string HairStyle { get; set; }
        public Color HairColor { get; set; }
        public double MovementSpeed { get; set; } // Value between 0 and 1  
        public GridCell CurrentCell { get; set; }
        public Building Home { get; set; } // Assumes that Home has a Cell property  
        public TimeOfDay CurrentTimeOfDay { get; set; }
        private GridCell[,] Grid { get; set; }

        public Villager(GridCell[,] grid)
        {
            Grid = grid;
        }

        private void MoveFarmer(Random random, TimeOfDay timeOfDay)
        {
            switch (timeOfDay)
            {
                case TimeOfDay.Morning:
                    // Move towards fields (we'll assume fields are vegetation areas)  
                    MoveTowardsType(CellType.Vegetation, random);
                    break;
                case TimeOfDay.Midday:
                    // Maybe rest or socialize in open spaces  
                    MoveTowardsType(CellType.OpenSpace, random);
                    break;
                case TimeOfDay.Evening:
                case TimeOfDay.Night:
                    // Return or stay home  
                    MoveTowardsCell(Home.Cell, random);
                    break;
            }
        }

        private void MoveShopkeeper(Random random, TimeOfDay timeOfDay)
        {
            switch (timeOfDay)
            {
                case TimeOfDay.Morning:
                case TimeOfDay.Midday:
                    // Stay near their shop (assume their home is their shop)  
                    MoveTowardsCell(Home.Cell, random);
                    break;
                case TimeOfDay.Evening:
                case TimeOfDay.Night:
                    // Return or stay home  
                    MoveTowardsCell(Home.Cell, random);
                    break;
            }
        }

        private void MoveWanderer(Random random, TimeOfDay timeOfDay)
        {
            // Wanderers explore different parts of the village  
            switch (timeOfDay)
            {
                case TimeOfDay.Morning:
                case TimeOfDay.Midday:
                    RandomMove(random);
                    break;
                case TimeOfDay.Evening:
                case TimeOfDay.Night:
                    // Return or stay home  
                    MoveTowardsCell(Home.Cell, random);
                    break;
            }
        }

        private void MoveTowardsType(CellType type, Random random)
        {
            GridCell targetCell = FindNearestCellOfType(type);
            if (targetCell != null)
            {
                MoveTowardsCell(targetCell, random);
            }
            else
            {
                RandomMove(random);
            }
        }

        private void MoveTowardsCell(GridCell targetCell, Random random)
        {
            int dRow = targetCell.Row - CurrentCell.Row;
            int dCol = targetCell.Col - CurrentCell.Col;

            int newRow = CurrentCell.Row;
            int newCol = CurrentCell.Col;

            if (dRow != 0)
            {
                newRow += Math.Sign(dRow); // Move one step towards target in row direction  
            }
            else if (dCol != 0)
            {
                newCol += Math.Sign(dCol); // Move one step towards target in column direction  
            }

            // Check if new position is valid and not blocked  
            if (IsValidCell(newRow, newCol))
            {
                CurrentCell = Grid[newRow, newCol];
            }
            else
            {
                RandomMove(random);
            }
        }

        private bool IsValidCell(int row, int col)
        {
            int maxRow = Grid.GetLength(0);
            int maxCol = Grid.GetLength(1);
            if (row >= 0 && row < maxRow && col >= 0 && col < maxCol)
            {
                GridCell cell = Grid[row, col];
                // Here we can put conditions to check for impassable terrain, obstacles, etc.  
                // For now, let's assume all cells are passable  
                return true;
            }
            return false;
        }

        private GridCell FindNearestCellOfType(CellType type)
        {
            int maxRow = Grid.GetLength(0);
            int maxCol = Grid.GetLength(1);
            bool[,] visited = new bool[maxRow, maxCol];
            Queue<GridCell> queue = new Queue<GridCell>();
            queue.Enqueue(CurrentCell);
            visited[CurrentCell.Row, CurrentCell.Col] = true;

            while (queue.Count > 0)
            {
                GridCell cell = queue.Dequeue();

                if (cell.CellType == type)
                {
                    return cell;
                }

                List<GridCell> neighbors = GetNeighboringCells(cell.Row, cell.Col, Grid);
                foreach (GridCell neighbor in neighbors)
                {
                    if (!visited[neighbor.Row, neighbor.Col])
                    {
                        visited[neighbor.Row, neighbor.Col] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // No cell of specified type was found  
            return null;
        }

        private void RandomMove(Random random)
        {
            // Decide whether to move based on movement speed  
            if (random.NextDouble() < MovementSpeed)
            {
                List<GridCell> neighbors = GetNeighboringCells(CurrentCell.Row, CurrentCell.Col, Grid);
                if (neighbors.Count > 0)
                {
                    CurrentCell = neighbors[random.Next(neighbors.Count)];
                }
            }
            // Otherwise, stay in place  
        }

        private void StayInPlace()
        {
            // Do nothing  
        }

        private List<GridCell> GetNeighboringCells(int row, int col, GridCell[,] grid)
        {
            List<GridCell> neighbors = new List<GridCell>();
            int maxRow = grid.GetLength(0);
            int maxCol = grid.GetLength(1);
            int[] dRows = { -1, 1, 0, 0 };
            int[] dCols = { 0, 0, -1, 1 };

            for (int i = 0; i < 4; i++)
            {
                int newRow = row + dRows[i];
                int newCol = col + dCols[i];

                if (IsValidCell(newRow, newCol))
                {
                    neighbors.Add(grid[newRow, newCol]);
                }
            }

            return neighbors;
        }

        public bool IsInteracting { get; set; }
        private int interactionCounter = 0;

        private void CheckForInteraction(List<Villager> villagers, Random random)
        {
            foreach (var villager in villagers)
            {
                if (villager != this && !villager.IsInteracting)
                {
                    if (villager.CurrentCell == this.CurrentCell)
                    {
                        // Initiate interaction  
                        IsInteracting = true;
                        interactionCounter = random.Next(5, 15); // Interaction lasts for a few frames  
                        villager.IsInteracting = true;
                        villager.interactionCounter = this.interactionCounter;
                        break;
                    }
                }
            }
        }

        public VillagerEvent CurrentEvent { get; set; } = VillagerEvent.None;

        public void Move(Random random, TimeOfDay timeOfDay, List<Villager> villagers)
        {
            if (CurrentEvent != VillagerEvent.None)
            {
                // Modify movement based on event  
                HandleEventMovement(random);
                return;
            }

            if (IsInteracting)
            {
                interactionCounter--;
                if (interactionCounter <= 0)
                {
                    IsInteracting = false;
                }
                StayInPlace(); // During interaction, villager stays in place  
            }
            else
            {
                // Depending on Occupation, call the appropriate move method  
                switch (Occupation)
                {
                    case Occupation.Farmer:
                        MoveFarmer(random, timeOfDay);
                        break;
                    case Occupation.Shopkeeper:
                        MoveShopkeeper(random, timeOfDay);
                        break;
                    case Occupation.Wanderer:
                        MoveWanderer(random, timeOfDay);
                        break;
                        // Other occupations  
                }

                // After moving, check for interaction  
                CheckForInteraction(villagers, random);
            }
        }

        private void HandleEventMovement(Random random)
        {
            switch (CurrentEvent)
            {
                case VillagerEvent.MarketDay:
                    // Move towards market area  
                    MoveTowardsType(CellType.Market, random);
                    break;
                case VillagerEvent.Festival:
                    // Move towards festival area  
                    MoveTowardsType(CellType.Festival, random);
                    break;
            }
        }
    }
}