using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace SyncMagic
{
    public enum AntRole
    {
        Worker,
        Forager,
        Nurse,
        QueenAttendant,
        Queen
    }

    public enum AntState
    {
        Exploring,
        ReturningToBase,
        TaskInProgress,
        CarryingFood,
        CarryingEgg,
        Idle
    }

    public class Ant
    {
        // Properties    
        public Point Position { get; set; }
        public AntState State { get; set; }
        public Point? Goal { get; set; }
        public AntRole Role { get; private set; }
        public int Age { get; private set; }
        public int Lifespan { get; private set; } // Lifespan in time units    

        // Private Fields    
        private Random random;
        private int stepsSinceGoal;
        private Point? previousPosition;
        private readonly AntFarm antFarm;
        private bool carryingItem;

        private int ForagerSearchRadius = 20; // Will adjust based on food availability  
        private const int NurseSearchRadius = 15;
        private const int MaxIdleSteps = 10; // Max steps to stay idle before exploring    

        // Constructor    
        public Ant(Point startPosition, AntRole role, Random random, AntFarm antFarm)
        {
            this.Position = startPosition;
            this.State = AntState.Exploring;
            this.random = random;
            this.Role = role;
            this.previousPosition = null;
            this.antFarm = antFarm;
            this.carryingItem = false;
            this.Age = 0;

            // Set lifespan based on role    
            if (role == AntRole.Queen)
            {
                Lifespan = int.MaxValue; // Queen lives indefinitely    
            }
            else
            {
                Lifespan = random.Next(500, 1000); // Ant lifespan between 500 and 1000 time units    
            }

            DecideGoal();
        }

        // Decide the next goal based on the ant's role and state    
        public void DecideGoal()
        {
            if (Role == AntRole.Worker)
            {
                DecideWorkerGoal();
            }
            else if (Role == AntRole.Forager)
            {
                DecideForagerGoal();
            }
            else if (Role == AntRole.Nurse)
            {
                DecideNurseGoal();
            }
            else if (Role == AntRole.QueenAttendant)
            {
                // Stay with the queen    
                Position = antFarm.BasePosition;
                Goal = antFarm.BasePosition;
                State = AntState.Idle;
            }
            else if (Role == AntRole.Queen)
            {
                // Queen stays at base    
                Position = antFarm.BasePosition;
                Goal = antFarm.BasePosition;
                State = AntState.Idle;
            }
            stepsSinceGoal = 0;
        }

        // Decide goal for worker ants    
        private void DecideWorkerGoal()
        {
            State = AntState.Exploring;

            // Workers help build tunnels towards unexplored areas when food is scarce    
            var foodTargets = antFarm.FoodPositions.Where(fp => antFarm.Tunnels[fp.X, fp.Y] == 0).ToList();
            if (foodTargets.Count > 0)
            {
                // Choose the closest food target without a tunnel      
                var closestFood = foodTargets.OrderBy(f => Distance(Position, f)).First();
                Goal = closestFood;
            }
            else
            {
                // No food targets, look for any unexplored positions on the grid  
                var unexploredPositions = new List<Point>();
                for (int x = 0; x < antFarm.GridWidth; x++)
                {
                    for (int y = 0; y < antFarm.GridHeight; y++)
                    {
                        if (antFarm.Tunnels[x, y] == 0)
                        {
                            unexploredPositions.Add(new Point(x, y));
                        }
                    }
                }

                if (unexploredPositions.Count > 0)
                {
                    // Find the closest unexplored position  
                    Goal = unexploredPositions.OrderBy(pos => Distance(Position, pos)).FirstOrDefault();
                }
                else
                {
                    // If no unexplored positions, become idle  
                    State = AntState.Idle;
                    Goal = null;
                }
            }
            stepsSinceGoal = 0;
        }

        // Decide goal for forager ants    
        private void DecideForagerGoal()
        {
            State = AntState.Exploring;

            // Adjust search radius based on food availability  
            if (antFarm.FoodPositions.Count == 0)
            {
                ForagerSearchRadius = 50; // Increase search radius when no food is available  
            }
            else
            {
                ForagerSearchRadius = 20; // Default search radius  
            }

            // Foragers try to find food within their search radius  
            var accessibleFood = antFarm.FoodPositions.Where(fp => IsPathAvailable(Position, fp)).ToList();
            if (accessibleFood.Count > 0)
            {
                var closestFood = accessibleFood.OrderBy(f => Distance(Position, f)).First();
                Goal = closestFood;
            }
            else
            {
                // If no accessible food, explore new areas  
                var edgeTunnels = GetEdgeTunnels();
                foreach (var edge in edgeTunnels.OrderBy(e => random.Next()))
                {
                    // Foragers will dig carefully to explore  
                    var possibleMoves = GetPossibleMoves(edge.X, edge.Y, antFarm.GridWidth, antFarm.GridHeight);
                    var unexploredMoves = possibleMoves.Where(move => antFarm.Tunnels[move.X, move.Y] == 0).ToList();

                    if (unexploredMoves.Count > 0)
                    {
                        Goal = unexploredMoves[random.Next(unexploredMoves.Count)];
                        return;
                    }
                }

                // If no unexplored moves from edge tunnels, move randomly within tunnels  
                Goal = null;
            }
        }

        // Decide goal for nurse ants    
        private void DecideNurseGoal()
        {
            State = AntState.Exploring;
            // Nurses handle eggs  
            Goal = antFarm.EggStoragePosition;
        }

        // Main update method called each time step    
        public void Update()
        {
            Age++;

            stepsSinceGoal++;

            if (Role == AntRole.QueenAttendant)
            {
                // Stay with the queen    
                Position = antFarm.BasePosition;
                return;
            }

            if (Role == AntRole.Queen)
            {
                // Queen stays at base    
                Position = antFarm.BasePosition;
                return;
            }

            if (Role == AntRole.Worker)
            {
                UpdateWorkerAnt();
            }
            else if (Role == AntRole.Forager)
            {
                UpdateForagerAnt();
            }
            else if (Role == AntRole.Nurse)
            {
                UpdateNurseAnt();
            }
        }

        // Update method for worker ants    
        private void UpdateWorkerAnt()
        {
            if (stepsSinceGoal > MaxIdleSteps)
            {
                // If idle for too long, pick a new goal    
                DecideWorkerGoal();
            }

            MoveAnt();
        }

        // Update method for forager ants    
        private void UpdateForagerAnt()
        {
            if (stepsSinceGoal > MaxIdleSteps)
            {
                // If idle for too long, pick a new goal    
                DecideForagerGoal();
            }

            MoveAnt();
        }

        // Update method for nurse ants    
        private void UpdateNurseAnt()
        {
            if (stepsSinceGoal > MaxIdleSteps)
            {
                // If idle for too long, pick a new goal    
                DecideNurseGoal();
            }

            MoveAnt();
        }

        // General method to move ants with collective intelligence    
        private void MoveAnt()
        {
            int x = Position.X;
            int y = Position.Y;

            List<Point> possibleMoves = GetPossibleMoves(x, y, antFarm.GridWidth, antFarm.GridHeight);

            Point newPosition = Position;

            if (possibleMoves.Count > 0)
            {
                if (Role == AntRole.Worker)
                {
                    // Workers help build tunnels towards food or explore    
                    List<Point> acceptableMoves = possibleMoves; // Allow moving anywhere  

                    if (acceptableMoves.Count > 0)
                    {
                        if (Goal.HasValue)
                        {
                            newPosition = MoveTowards(Position, Goal.Value, acceptableMoves);
                        }
                        else
                        {
                            newPosition = acceptableMoves[random.Next(acceptableMoves.Count)];
                        }

                        // Dig tunnel    
                        if (antFarm.Tunnels[newPosition.X, newPosition.Y] == 0)
                        {
                            antFarm.Tunnels[newPosition.X, newPosition.Y] = 1;
                        }
                    }
                    else
                    {
                        // No acceptable moves, pick a new goal    
                        DecideWorkerGoal();
                        return;
                    }
                }
                else if (Role == AntRole.Forager)
                {
                    if (carryingItem)
                    {
                        // Return to food storage    
                        Goal = antFarm.FoodStoragePosition;
                        List<Point> tunnelMoves = GetAvailableTunnelMoves(possibleMoves, antFarm.Tunnels);
                        if (tunnelMoves.Count > 0)
                        {
                            newPosition = MoveTowards(Position, Goal.Value, tunnelMoves);
                        }
                        else
                        {
                            // Can't move, pick a new goal    
                            DecideForagerGoal();
                            return;
                        }

                        if (newPosition.Equals(Goal.Value))
                        {
                            // Drop off the food    
                            carryingItem = false;
                            State = AntState.Exploring;
                            Goal = null;
                        }
                    }
                    else
                    {
                        // Foragers look for food    
                        if (Goal.HasValue)
                        {
                            if (IsPathAvailable(Position, Goal.Value))
                            {
                                // Move towards food using tunnels    
                                List<Point> tunnelMoves = GetAvailableTunnelMoves(possibleMoves, antFarm.Tunnels);
                                if (tunnelMoves.Count > 0)
                                {
                                    newPosition = MoveTowards(Position, Goal.Value, tunnelMoves);
                                }
                                else
                                {
                                    // Can't move, try digging  
                                    List<Point> acceptableMoves = possibleMoves;
                                    if (acceptableMoves.Count > 0)
                                    {
                                        newPosition = MoveTowards(Position, Goal.Value, acceptableMoves);
                                        // Dig tunnel  
                                        if (antFarm.Tunnels[newPosition.X, newPosition.Y] == 0)
                                        {
                                            antFarm.Tunnels[newPosition.X, newPosition.Y] = 1;
                                        }
                                    }
                                    else
                                    {
                                        // Can't move, pick a new goal  
                                        DecideForagerGoal();
                                        return;
                                    }
                                }

                                if (newPosition.Equals(Goal.Value))
                                {
                                    // Collect food    
                                    if (antFarm.FoodPositions.Contains(newPosition))
                                    {
                                        antFarm.FoodPositions.Remove(newPosition);
                                        carryingItem = true;
                                        State = AntState.CarryingFood;
                                        Goal = antFarm.FoodStoragePosition;
                                    }
                                    else
                                    {
                                        // Food already collected, pick a new goal    
                                        DecideForagerGoal();
                                    }
                                }
                            }
                            else
                            {
                                // Can't reach the food, pick a new goal    
                                DecideForagerGoal();
                                return;
                            }
                        }
                        else
                        {
                            // Explore new areas  
                            List<Point> acceptableMoves = possibleMoves;
                            if (acceptableMoves.Count > 0)
                            {
                                newPosition = acceptableMoves[random.Next(acceptableMoves.Count)];
                                // Dig tunnel  
                                if (antFarm.Tunnels[newPosition.X, newPosition.Y] == 0)
                                {
                                    antFarm.Tunnels[newPosition.X, newPosition.Y] = 1;
                                }
                            }
                            else
                            {
                                // Random movement within tunnels    
                                List<Point> tunnelMoves = GetAvailableTunnelMoves(possibleMoves, antFarm.Tunnels);
                                if (tunnelMoves.Count > 0)
                                {
                                    newPosition = tunnelMoves[random.Next(tunnelMoves.Count)];
                                }
                                else
                                {
                                    // Can't move, pick a new goal  
                                    DecideForagerGoal();
                                    return;
                                }
                            }
                        }
                    }
                }
                else if (Role == AntRole.Nurse)
                {
                    // Nurses handle eggs    
                    List<Point> tunnelMoves = GetAvailableTunnelMoves(possibleMoves, antFarm.Tunnels);
                    if (tunnelMoves.Count > 0)
                    {
                        newPosition = tunnelMoves[random.Next(tunnelMoves.Count)];
                    }
                    else
                    {
                        // No moves, pick a new goal    
                        DecideNurseGoal();
                        return;
                    }
                }

                // Update ant's position    
                previousPosition = Position;
                Position = newPosition;
                stepsSinceGoal = 0;

                // Leave pheromones    
                antFarm.Pheromones[Position.X, Position.Y] += 1.0;
            }
            else
            {
                // No valid moves, ant picks a new goal or tries to backtrack    
                DecideGoal();
            }
        }

        // Helper method to get possible moves    
        private List<Point> GetPossibleMoves(int x, int y, int gridWidth, int gridHeight)
        {
            List<Point> possibleMoves = new List<Point>();
            if (y > 0) possibleMoves.Add(new Point(x, y - 1));     // Up    
            if (x < gridWidth - 1) possibleMoves.Add(new Point(x + 1, y)); // Right    
            if (y < gridHeight - 1) possibleMoves.Add(new Point(x, y + 1)); // Down    
            if (x > 0) possibleMoves.Add(new Point(x - 1, y));     // Left    
            return possibleMoves;
        }

        // Helper method to move towards a target    
        private Point MoveTowards(Point current, Point target, List<Point> possibleMoves)
        {
            List<Point> bestMoves = new List<Point>();
            int bestDistance = int.MaxValue;

            foreach (var move in possibleMoves)
            {
                int distance = Math.Abs(move.X - target.X) + Math.Abs(move.Y - target.Y);
                if (distance < bestDistance)
                {
                    bestMoves.Clear();
                    bestMoves.Add(move);
                    bestDistance = distance;
                }
                else if (distance == bestDistance)
                {
                    bestMoves.Add(move);
                }
            }

            return bestMoves[random.Next(bestMoves.Count)];
        }

        // Get available tunnel moves (ants can overlap)    
        private List<Point> GetAvailableTunnelMoves(List<Point> possibleMoves, int[,] tunnels)
        {
            List<Point> tunnelMoves = new List<Point>();
            foreach (var move in possibleMoves)
            {
                if (tunnels[move.X, move.Y] > 0)
                {
                    tunnelMoves.Add(move);
                }
            }
            return tunnelMoves;
        }

        // Checks if a path is available between two points via tunnels    
        private bool IsPathAvailable(Point start, Point end)
        {
            // Early exit if start and end are the same  
            if (start.Equals(end))
                return true;

            // Limit search depth to prevent infinite loops  
            int maxSearchDepth = 10000;

            // Simple BFS pathfinding to check if a path exists    
            Queue<Point> queue = new Queue<Point>();
            HashSet<Point> visited = new HashSet<Point>();

            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0 && visited.Count < maxSearchDepth)
            {
                Point current = queue.Dequeue();

                if (current.Equals(end))
                {
                    return true;
                }

                foreach (var neighbor in GetAvailableTunnelMoves(GetPossibleMoves(current.X, current.Y, antFarm.GridWidth, antFarm.GridHeight), antFarm.Tunnels))
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return false;
        }

        // Helper method to calculate distance between two points    
        private int Distance(Point a, Point b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        // Helper method to get edge tunnels (cells with only one adjacent tunnel)    
        private List<Point> GetEdgeTunnels()
        {
            List<Point> edgeTunnels = new List<Point>();
            for (int x = 0; x < antFarm.GridWidth; x++)
            {
                for (int y = 0; y < antFarm.GridHeight; y++)
                {
                    if (antFarm.Tunnels[x, y] > 0)
                    {
                        int adjacentTunnels = 0;
                        foreach (var neighbor in GetPossibleMoves(x, y, antFarm.GridWidth, antFarm.GridHeight))
                        {
                            if (antFarm.Tunnels[neighbor.X, neighbor.Y] > 0)
                            {
                                adjacentTunnels++;
                            }
                        }

                        if (adjacentTunnels == 1)
                        {
                            edgeTunnels.Add(new Point(x, y));
                        }
                    }
                }
            }
            return edgeTunnels;
        }
    }
}