using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncMagic
{
    public class SimpleFishTankSimulation
    {
        private int canvasWidth = 240;
        private int canvasHeight = 240;
        DigitalClockWeather clockWeather = new DigitalClockWeather();
        private Dictionary<FishType, int> fishTypeCounts;
        private PointF[] sandPoints;


        private class Fish
        {
            public float X;
            public float Y;
            public float SpeedX;
            public float SpeedY;
            public float MaxSpeedX;
            public float MaxSpeedY;
            public Bitmap Image;
            public bool IsEating = false;
            public int EatingTimer = 0;
            public bool FacingRight = true;
            public int DirectionChangeTimer = 0;
            public float DesiredSpeedX;
            public float DesiredSpeedY;
            public int Lifespan;
            public int MaxLifespan;
            public bool IsDead = false;
            public bool IsGoingToFood = false;
            public float TargetFoodX;
            public float TargetFoodY;
            public float PreviousX;
            public float PreviousY;
            public int IdleFrames = 0;
        }
        public enum FishSpeedCategory
        {
            Slow,
            Medium,
            Fast
        }
        public class FishType
        {
            public Bitmap Image { get; set; }
            public float MinSpeedX { get; private set; }
            public float MaxSpeedX { get; private set; }
            public float MinSpeedY { get; private set; }
            public float MaxSpeedY { get; private set; }
            public float MaxSpeedXLimit { get; private set; }
            public float MaxSpeedYLimit { get; private set; }

            private static readonly (float minX, float maxX, float minY, float maxY, float limitX, float limitY) SlowSpeed = (-0.1f, 0.1f, -0.05f, 0.05f, 0.2f, 0.1f);
            private static readonly (float minX, float maxX, float minY, float maxY, float limitX, float limitY) MediumSpeed = (-0.3f, 0.3f, -0.1f, 0.1f, 0.6f, 0.2f);
            private static readonly (float minX, float maxX, float minY, float maxY, float limitX, float limitY) FastSpeed = (-0.5f, 0.5f, -0.3f, 0.3f, 1.0f, 0.5f);

            public FishType(Bitmap image, FishSpeedCategory speedCategory)
            {
                Image = image;
                Random rand = new Random();

                var speedSettings = speedCategory switch
                {
                    FishSpeedCategory.Slow => SlowSpeed,
                    FishSpeedCategory.Medium => MediumSpeed,
                    FishSpeedCategory.Fast => FastSpeed,
                    _ => throw new ArgumentException("Unknown speed category")
                };

                MinSpeedX = (float)(rand.NextDouble() * (speedSettings.maxX - speedSettings.minX) + speedSettings.minX);
                MaxSpeedX = (float)(rand.NextDouble() * (speedSettings.maxX - speedSettings.minX) + speedSettings.minX);
                MinSpeedY = (float)(rand.NextDouble() * (speedSettings.maxY - speedSettings.minY) + speedSettings.minY);
                MaxSpeedY = (float)(rand.NextDouble() * (speedSettings.maxY - speedSettings.minY) + speedSettings.minY);
                MaxSpeedXLimit = speedSettings.limitX;
                MaxSpeedYLimit = speedSettings.limitY;
            }
        }

        private List<Fish> fishList;
        private List<FishType> fishTypes;

        private class Food
        {
            public float X;
            public float Y;
            public bool IsBeingTargeted = false;
        }

        private List<Food> foodList;

        private float sandOffset = 0f;

        private class Plant
        {
            public float X;
            public float Y;
            public Bitmap Image;
        }

        private List<Plant> plants;
        private List<Bitmap> plantImages;

        private class Bubble
        {
            public float X;
            public float Y;
            public Bitmap Image;
        }

        private List<Bubble> bubbles;
        private List<Bitmap> bubbleImages;
        private Random rand;

        private class HermitCrab
        {
            public float X;
            public float Y;
            public float SpeedX;
            public Bitmap Image;
            public bool FacingRight;
            public int DirectionChangeTimer;
            public float MaxSpeedX;
            public float DesiredSpeedX;
        }

        private HermitCrab hermitCrab;

        public SimpleFishTankSimulation()
        {
            rand = new Random();
            InitializeSimulation();
        }
        private void InitializeSandWavePoints()
        {
            int sandWavePoints = 20;
            sandPoints = new PointF[sandWavePoints + 2];

            float sandWaveAmplitude = 5f;
            float sandWaveFrequency = 0.5f;

            float sandWaveInterval = (float)canvasWidth / (sandWavePoints - 1);

            float randomPhase = (float)(rand.NextDouble() * 2 * Math.PI);

            for (int i = 0; i < sandWavePoints; i++)
            {
                float x = i * sandWaveInterval;
                float y = canvasHeight - 20 + sandWaveAmplitude * (float)Math.Sin((i * sandWaveFrequency) + randomPhase);

                sandPoints[i] = new PointF(x, y);
            }

            sandPoints[sandWavePoints] = new PointF(canvasWidth, canvasHeight);
            sandPoints[sandWavePoints + 1] = new PointF(0, canvasHeight);
        }

        private void InitializeSimulation()
        {
            InitializeSandWavePoints();

            fishTypes = new List<FishType>
            {
                new FishType(new Bitmap(SyncMagic.Properties.Resources._2427851_fish_fishing_food_sea_sea_creature_icon_right), FishSpeedCategory.Fast),
                //new FishType(new Bitmap(SyncMagic.Properties.Resources._8935917_aquarium_fish_hippocampus_marine_seahorse_icon), FishSpeedCategory.Slow),
                new FishType(new Bitmap(SyncMagic.Properties.Resources.parvi), FishSpeedCategory.Fast),
                //new FishType(new Bitmap(SyncMagic.Properties.Resources._8935924_aquarium_blowfish_fish_puffer_pufferfish_icon), FishSpeedCategory.Slow),
                new FishType(new Bitmap(SyncMagic.Properties.Resources.big_gold_fish), FishSpeedCategory.Fast),
                //new FishType(new Bitmap(SyncMagic.Properties.Resources.medusa), FishSpeedCategory.Slow),
                new FishType(new Bitmap(SyncMagic.Properties.Resources.gold_fish), FishSpeedCategory.Fast),
                //new FishType(new Bitmap(SyncMagic.Properties.Resources.clown_fish), FishSpeedCategory.Medium),
                new FishType(new Bitmap(SyncMagic.Properties.Resources.surgeon_fish), FishSpeedCategory.Fast),
                //new FishType(new Bitmap(SyncMagic.Properties.Resources.angelfish), FishSpeedCategory.Medium),
                //new FishType(new Bitmap(SyncMagic.Properties.Resources.sardine), FishSpeedCategory.Medium),
                //new FishType(new Bitmap(SyncMagic.Properties.Resources.shrimp), FishSpeedCategory.Slow),
            };

            fishList = new List<Fish>();
            fishTypeCounts = new Dictionary<FishType, int>();
            int totalFishDesired = 4;
            int maxDuplicatesPerFishType = 2;

            int fishAdded = 0;
            while (fishAdded < totalFishDesired)
            {
                FishType fishType = fishTypes[rand.Next(fishTypes.Count)];

                if (!fishTypeCounts.ContainsKey(fishType))
                {
                    fishTypeCounts[fishType] = 0;
                }

                if (fishTypeCounts[fishType] < maxDuplicatesPerFishType)
                {
                    AddFish(fishType);
                    fishTypeCounts[fishType]++;
                    fishAdded++;
                }
                else
                {
                    int availableFishTypes = fishTypes.Count(ft =>
                        !fishTypeCounts.ContainsKey(ft) || fishTypeCounts[ft] < maxDuplicatesPerFishType);
                    if (availableFishTypes == 0)
                    {
                        break;
                    }
                }
            }

            foodList = new List<Food>();

            plantImages = new List<Bitmap>
            {
                new Bitmap(SyncMagic.Properties.Resources._8936057_algae_bloom_cladophora_seaweed_underwater_icon),
                new Bitmap(SyncMagic.Properties.Resources._8935921_algae_bloom_cladophora_seaweed_underwater_icon),
                new Bitmap(SyncMagic.Properties.Resources._11823407_game_pearl_shells_blue_pearl_sea_icon),
                new Bitmap(SyncMagic.Properties.Resources._8935921_algae_bloom_cladophora_seaweed_underwater_icon),
                new Bitmap(SyncMagic.Properties.Resources._8935918_coral_diving_nature_ocean_reef_icon),
                new Bitmap(SyncMagic.Properties.Resources._8935918_coral_diving_nature_ocean_reef_icon),
                new Bitmap(SyncMagic.Properties.Resources.sea_grass_flip),
                new Bitmap(SyncMagic.Properties.Resources.sea_grass),
                new Bitmap(SyncMagic.Properties.Resources.sea_leaves),
                new Bitmap(SyncMagic.Properties.Resources.sea_leaves_flip),
                new Bitmap(SyncMagic.Properties.Resources.algae2),
                new Bitmap(SyncMagic.Properties.Resources._11823408_game_castle_prince_princess_building_icon),
                new Bitmap(SyncMagic.Properties.Resources.coral_scaled),
                new Bitmap(SyncMagic.Properties.Resources.coral_scaled_flip),
                new Bitmap(SyncMagic.Properties.Resources.StarFish),
                new Bitmap(SyncMagic.Properties.Resources.fishleaves),
                new Bitmap(SyncMagic.Properties.Resources.coral1),
                new Bitmap(SyncMagic.Properties.Resources.coral2),
                new Bitmap(SyncMagic.Properties.Resources.coral3),
            };

            plants = new List<Plant>();
            Dictionary<Bitmap, int> plantImageCounts = new Dictionary<Bitmap, int>();
            int numPlants = 5;
            int maxDuplicatesPerPlant = 2;
            float approximateSpacing = canvasWidth / numPlants;

            int plantsAdded = 0;
            while (plantsAdded < numPlants)
            {
                Bitmap plantImage = plantImages[rand.Next(plantImages.Count)];

                if (!plantImageCounts.ContainsKey(plantImage))
                {
                    plantImageCounts[plantImage] = 0;
                }

                if (plantImageCounts[plantImage] < maxDuplicatesPerPlant)
                {
                    plantImageCounts[plantImage]++;

                    float x = plantsAdded * approximateSpacing + rand.Next(-10, 10);
                    x = Math.Max(0, Math.Min(x, canvasWidth - plantImage.Width));

                    float y = canvasHeight - plantImage.Height - 5 + rand.Next(-5, 5);

                    Plant plant = new Plant
                    {
                        X = x,
                        Y = y,
                        Image = plantImage
                    };
                    plants.Add(plant);
                    plantsAdded++;
                }
                else
                {
                    int availablePlantImages = plantImages.Count(pi =>
                        !plantImageCounts.ContainsKey(pi) || plantImageCounts[pi] < maxDuplicatesPerPlant);
                    if (availablePlantImages == 0)
                    {
                        break;
                    }
                }
            }

            bubbleImages = new List<Bitmap>
            {
                new Bitmap(SyncMagic.Properties.Resources.bubble_1),
                new Bitmap(SyncMagic.Properties.Resources.bubble_2),
                new Bitmap(SyncMagic.Properties.Resources.bubble_3),
                new Bitmap(SyncMagic.Properties.Resources.bubble_4),
                new Bitmap(SyncMagic.Properties.Resources.bubble_5),
                new Bitmap(SyncMagic.Properties.Resources.bubble_6),
                new Bitmap(SyncMagic.Properties.Resources.bubble_7),
            };

            bubbles = new List<Bubble>();

            Bitmap hermitCrabImage = new Bitmap(SyncMagic.Properties.Resources.hermit_crab);

            hermitCrab = new HermitCrab
            {
                X = rand.Next(0, canvasWidth - hermitCrabImage.Width),
                Y = canvasHeight - hermitCrabImage.Height - 8,
                SpeedX = (float)(rand.NextDouble() * 0.2f - 0.1f),
                MaxSpeedX = 0.2f,
                Image = hermitCrabImage,
                FacingRight = true,
                DirectionChangeTimer = rand.Next(60, 180),
                DesiredSpeedX = 0f
            };
        }

        private void AddFish(FishType fishType)
        {
            float baseSpeedX = (float)(rand.NextDouble() * (fishType.MaxSpeedX - fishType.MinSpeedX) + fishType.MinSpeedX);
            float baseSpeedY = (float)(rand.NextDouble() * (fishType.MaxSpeedY - fishType.MinSpeedY) + fishType.MinSpeedY);
            float maxSpeedX = fishType.MaxSpeedXLimit;
            float maxSpeedY = fishType.MaxSpeedYLimit;

            Fish fish = new Fish
            {
                X = rand.Next(20, canvasWidth - 20),
                Y = rand.Next(20, canvasHeight - 60),
                SpeedX = baseSpeedX,
                SpeedY = baseSpeedY,
                MaxSpeedX = maxSpeedX,
                MaxSpeedY = maxSpeedY,
                Image = fishType.Image,
                FacingRight = baseSpeedX >= 0,
                DesiredSpeedX = baseSpeedX,
                DesiredSpeedY = baseSpeedY,
                DirectionChangeTimer = rand.Next(30, 120),
                MaxLifespan = 30000,
                Lifespan = rand.Next(10000, 30000),
                IsDead = false,
                PreviousX = rand.Next(20, canvasWidth - 20),
                PreviousY = rand.Next(20, canvasHeight - 60),
                IdleFrames = 0
            };

            fishList.Add(fish);

            if (!fishTypeCounts.ContainsKey(fishType))
            {
                fishTypeCounts[fishType] = 0;
            }
            fishTypeCounts[fishType]++;
        }

        private void UpdateSimulation()
        {
            sandOffset += 0.1f;
            if (sandOffset > 10f) sandOffset = 0f;

            if (rand.NextDouble() < 0.005)
            {
                Food food = new Food
                {
                    X = rand.Next(10, canvasWidth - 10),
                    Y = 0
                };
                foodList.Add(food);
            }

            const float foodAttractionThreshold = 100f;

            for (int i = foodList.Count - 1; i >= 0; i--)
            {
                Food food = foodList[i];
                food.Y += 0.5f;

                if (food.Y > canvasHeight - 20)
                {
                    foodList.RemoveAt(i);
                }
                else
                {
                    foreach (var fish in fishList)
                    {
                        if (!fish.IsDead && Math.Abs(fish.X - food.X) < 10 && Math.Abs(fish.Y - food.Y) < 10)
                        {
                            fish.IsEating = true;
                            fish.EatingTimer = 60;
                            foodList.RemoveAt(i);
                            break;
                        }
                    }

                    if (!food.IsBeingTargeted && food.Y > foodAttractionThreshold)
                    {
                        var availableFish = fishList.FindAll(f => !f.IsDead && !f.IsGoingToFood);
                        if (availableFish.Count > 0)
                        {
                            var fish = availableFish[rand.Next(availableFish.Count)];
                            fish.IsGoingToFood = true;
                            fish.TargetFoodX = food.X;
                            fish.TargetFoodY = food.Y;
                            food.IsBeingTargeted = true;
                        }
                    }
                }
            }

            for (int i = fishList.Count - 1; i >= 0; i--)
            {
                Fish fish = fishList[i];

                if (!fish.IsDead)
                {
                    fish.Lifespan--;
                    if (fish.Lifespan <= 0)
                    {
                        fish.IsDead = true;
                        fish.SpeedX = 0;
                        fish.SpeedY = -0.05f;
                        fish.FacingRight = true;
                        continue;
                    }

                    if (fish.IsEating)
                    {
                        fish.EatingTimer--;
                        if (fish.EatingTimer <= 0)
                        {
                            fish.IsEating = false;

                            fish.DirectionChangeTimer = rand.Next(30, 120);

                            fish.DesiredSpeedX = (float)(rand.NextDouble() * fish.MaxSpeedX * 2 - fish.MaxSpeedX);
                            fish.DesiredSpeedY = (float)(rand.NextDouble() * fish.MaxSpeedY * 2 - fish.MaxSpeedY);

                            fish.SpeedX = fish.DesiredSpeedX;
                            fish.SpeedY = fish.DesiredSpeedY;
                        }
                        continue;
                    }

                    if (fish.IsGoingToFood)
                    {
                        float dx = fish.TargetFoodX - fish.X;
                        float dy = fish.TargetFoodY - fish.Y;
                        float distance = (float)Math.Sqrt(dx * dx + dy * dy);

                        if (distance > 0)
                        {
                            float speedMultiplier = 2.0f;
                            fish.DesiredSpeedX = (dx / distance) * fish.MaxSpeedX * speedMultiplier;
                            fish.DesiredSpeedY = (dy / distance) * fish.MaxSpeedY * speedMultiplier;
                        }

                        float speedAdjustmentFactor = 0.2f;

                        fish.SpeedX += (fish.DesiredSpeedX - fish.SpeedX) * speedAdjustmentFactor;
                        fish.SpeedY += (fish.DesiredSpeedY - fish.SpeedY) * speedAdjustmentFactor;

                        fish.X += fish.SpeedX;
                        fish.Y += fish.SpeedY;

                        fish.FacingRight = fish.SpeedX >= 0;

                        if (Math.Abs(fish.X - fish.TargetFoodX) < 10 && Math.Abs(fish.Y - fish.TargetFoodY) < 10)
                        {
                            fish.IsGoingToFood = false;

                            fish.IsEating = true;
                            fish.EatingTimer = 60;

                            fish.Lifespan += 300;
                            if (fish.Lifespan > fish.MaxLifespan)
                            {
                                fish.Lifespan = fish.MaxLifespan;
                            }

                            for (int j = foodList.Count - 1; j >= 0; j--)
                            {
                                Food food = foodList[j];
                                if (Math.Abs(food.X - fish.TargetFoodX) < 5 && Math.Abs(food.Y - fish.TargetFoodY) < 5)
                                {
                                    foodList.RemoveAt(j);
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        fish.DirectionChangeTimer--;

                        if (fish.DirectionChangeTimer <= 0)
                        {
                            if (fish.X <= 10)
                            {
                                fish.DesiredSpeedX = Math.Abs(fish.MaxSpeedX) * ((float)rand.NextDouble() * 0.5f + 0.5f);
                            }
                            else if (fish.X + fish.Image.Width >= canvasWidth - 10)
                            {
                                fish.DesiredSpeedX = -Math.Abs(fish.MaxSpeedX) * ((float)rand.NextDouble() * 0.5f + 0.5f);
                            }
                            else
                            {
                                fish.DesiredSpeedX = (float)(rand.NextDouble() * fish.MaxSpeedX * 2 - fish.MaxSpeedX);
                            }

                            if (fish.Y <= 20)
                            {
                                fish.DesiredSpeedY = Math.Abs(fish.MaxSpeedY) * ((float)rand.NextDouble() * 0.5f + 0.5f);
                            }
                            else if (fish.Y + fish.Image.Height >= canvasHeight - 20)
                            {
                                fish.DesiredSpeedY = -Math.Abs(fish.MaxSpeedY) * ((float)rand.NextDouble() * 0.5f + 0.5f);
                            }
                            else
                            {
                                fish.DesiredSpeedY = (float)(rand.NextDouble() * fish.MaxSpeedY * 2 - fish.MaxSpeedY);
                            }

                            fish.DirectionChangeTimer = rand.Next(30, 120);
                        }

                        float speedAdjustmentFactor = 0.03f;

                        fish.SpeedX += (fish.DesiredSpeedX - fish.SpeedX) * speedAdjustmentFactor;
                        fish.SpeedY += (fish.DesiredSpeedY - fish.SpeedY) * speedAdjustmentFactor;

                        fish.X += fish.SpeedX;
                        fish.Y += fish.SpeedY;

                        fish.FacingRight = fish.SpeedX >= 0;
                    }

                    if (fish.X > canvasWidth)
                    {
                        fish.X = -fish.Image.Width;
                    }
                    else if (fish.X + fish.Image.Width < 0)
                    {
                        fish.X = canvasWidth;
                    }

                    if (fish.Y <= 20)
                    {
                        fish.Y = 20;
                        fish.SpeedY = Math.Abs(fish.SpeedY) * 0.5f;
                        fish.DesiredSpeedY = Math.Abs(fish.DesiredSpeedY);
                    }
                    else if (fish.Y + fish.Image.Height >= canvasHeight - 20)
                    {
                        fish.Y = canvasHeight - 20 - fish.Image.Height;
                        fish.SpeedY = -Math.Abs(fish.SpeedY) * 0.5f;
                        fish.DesiredSpeedY = -Math.Abs(fish.DesiredSpeedY);
                    }

                    if (Math.Abs(fish.X - fish.PreviousX) < 0.01f && Math.Abs(fish.Y - fish.PreviousY) < 0.01f)
                    {
                        fish.IdleFrames++;
                    }
                    else
                    {
                        fish.IdleFrames = 0;
                    }

                    fish.PreviousX = fish.X;
                    fish.PreviousY = fish.Y;

                    if (fish.IdleFrames >= 40)
                    {
                        fish.DesiredSpeedX = (float)(rand.NextDouble() * fish.MaxSpeedX * 2 - fish.MaxSpeedX);
                        fish.DesiredSpeedY = (float)(rand.NextDouble() * fish.MaxSpeedY * 2 - fish.MaxSpeedY);

                        fish.IdleFrames = 0;
                    }
                }
                else
                {
                    fish.Y += fish.SpeedY;

                    fish.X += (float)(rand.NextDouble() * 0.02f - 0.01f);

                    if (fish.Y + fish.Image.Height <= 0)
                    {
                        FishType fishType = fishTypes.Find(ft => ft.Image == fish.Image);
                        if (fishType != null)
                        {
                            fishTypeCounts[fishType]--;
                        }

                        fishList.RemoveAt(i);

                        AddNewFishConsideringDuplicateLimits();

                        continue;
                    }
                }
            }

            hermitCrab.DirectionChangeTimer--;

            if (hermitCrab.DirectionChangeTimer <= 0)
            {
                hermitCrab.DesiredSpeedX = (float)(rand.NextDouble() * hermitCrab.MaxSpeedX * 2 - hermitCrab.MaxSpeedX);
                hermitCrab.DirectionChangeTimer = rand.Next(60, 180);
            }

            float crabSpeedAdjustmentFactor = 0.05f;
            hermitCrab.SpeedX += (hermitCrab.DesiredSpeedX - hermitCrab.SpeedX) * crabSpeedAdjustmentFactor;

            hermitCrab.X += hermitCrab.SpeedX;

            hermitCrab.FacingRight = hermitCrab.SpeedX >= 0;

            if (hermitCrab.X <= 0)
            {
                hermitCrab.X = 0;
                hermitCrab.SpeedX = Math.Abs(hermitCrab.SpeedX) * 0.5f;
                hermitCrab.DesiredSpeedX = Math.Abs(hermitCrab.DesiredSpeedX);
                hermitCrab.FacingRight = true;
            }
            else if (hermitCrab.X + hermitCrab.Image.Width >= canvasWidth)
            {
                hermitCrab.X = canvasWidth - hermitCrab.Image.Width;
                hermitCrab.SpeedX = -Math.Abs(hermitCrab.SpeedX) * 0.5f;
                hermitCrab.DesiredSpeedX = -Math.Abs(hermitCrab.DesiredSpeedX);
                hermitCrab.FacingRight = false;
            }

            //if (rand.NextDouble() < 0.015)
            //{
            //    Bubble bubble = new Bubble
            //    {
            //        X = rand.Next(0, canvasWidth),
            //        Y = canvasHeight,
            //        Image = bubbleImages[rand.Next(bubbleImages.Count)]
            //    };
            //    bubbles.Add(bubble);
            //}
            //for (int i = bubbles.Count - 1; i >= 0; i--)
            //{
            //    Bubble bubble = bubbles[i];
            //    bubble.Y -= 1f;
            //    bubble.X += (float)(rand.NextDouble() * 1 - 0.5f);

            //    if (bubble.Y + bubble.Image.Height < 0)
            //    {
            //        bubbles.RemoveAt(i);
            //    }
            //}
        }

        private void AddNewFishConsideringDuplicateLimits()
        {
            int maxDuplicatesPerFishType = 2;

            var availableFishTypes = fishTypes.FindAll(ft =>
                !fishTypeCounts.ContainsKey(ft) || fishTypeCounts[ft] < maxDuplicatesPerFishType);

            if (availableFishTypes.Count > 0)
            {
                FishType fishType = availableFishTypes[rand.Next(availableFishTypes.Count)];
                AddFish(fishType);
            }
            else
            {
                fishTypeCounts.Clear();

                foreach (var fish in fishList)
                {
                    FishType fishType = fishTypes.Find(ft => ft.Image == fish.Image);
                    if (fishType != null)
                    {
                        if (!fishTypeCounts.ContainsKey(fishType))
                        {
                            fishTypeCounts[fishType] = 0;
                        }
                        fishTypeCounts[fishType]++;
                    }
                }

                availableFishTypes = fishTypes.FindAll(ft =>
                    !fishTypeCounts.ContainsKey(ft) || fishTypeCounts[ft] < maxDuplicatesPerFishType);

                if (availableFishTypes.Count > 0)
                {
                    FishType fishType = availableFishTypes[rand.Next(availableFishTypes.Count)];
                    AddFish(fishType);
                }
                else
                {
                    fishTypeCounts.Clear();
                    FishType fishType = fishTypes[rand.Next(fishTypes.Count)];
                    AddFish(fishType);
                }
            }
        }

        public Bitmap GetFrame()
        {
            int updatesPerFrame = 1;
            for (int i = 0; i < updatesPerFrame; i++)
            {
                UpdateSimulation();
            }

            Bitmap bmp = new Bitmap(canvasWidth, canvasHeight);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);

                Brush sandBrush = new SolidBrush(Color.SandyBrown);
                g.FillPolygon(sandBrush, sandPoints);

                float plantThresholdY = canvasHeight * 0.6f;

                foreach (var plant in plants)
                {
                    if (plant.Y < plantThresholdY)
                    {
                        g.DrawImage(plant.Image, plant.X, plant.Y);
                    }
                }

                foreach (var bubble in bubbles)
                {
                    g.DrawImage(bubble.Image, bubble.X, bubble.Y);
                }

                Brush foodBrush = new SolidBrush(Color.Pink);
                foreach (var food in foodList)
                {
                    g.FillEllipse(foodBrush, food.X - 2, food.Y - 2, 4, 4);
                }

                foreach (var fish in fishList)
                {
                    Bitmap fishImage = fish.Image;

                    bool needsTransform = !fish.FacingRight || fish.IsDead;
                    if (needsTransform)
                    {
                        fishImage = (Bitmap)fishImage.Clone();
                    }

                    if (!fish.FacingRight)
                    {
                        fishImage.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    }

                    if (fish.IsDead)
                    {
                        fishImage.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    }

                    if (fish.IsEating)
                    {
                        using (ImageAttributes imageAttributes = new ImageAttributes())
                        {
                            float[][] colorMatrixElements = {
                            new float[] {1,  0,  0,  0, 0},
                            new float[] {0,  1,  0,  0, 0},
                            new float[] {0,  0,  1,  0, 0},
                            new float[] {0,  0,  0,  0.5f, 0},
                            new float[] {0,  0,  0,  0, 1}
                        };
                            ColorMatrix colorMatrix = new ColorMatrix(colorMatrixElements);
                            imageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                            g.DrawImage(fishImage, new Rectangle((int)fish.X, (int)fish.Y, fishImage.Width, fishImage.Height),
                                0, 0, fishImage.Width, fishImage.Height, GraphicsUnit.Pixel, imageAttributes);
                        }
                    }
                    else if (fish.IsDead)
                    {
                        using (ImageAttributes imageAttributes = new ImageAttributes())
                        {
                            float[][] colorMatrixElements = {
                            new float[] {0.3f, 0.3f, 0.3f, 0, 0},
                            new float[] {0.59f, 0.59f, 0.59f, 0, 0},
                            new float[] {0.11f, 0.11f, 0.11f, 0, 0},
                            new float[] {0,  0,  0,  1, 0},
                            new float[] {0,  0,  0,  0, 1}
                        };
                            ColorMatrix colorMatrix = new ColorMatrix(colorMatrixElements);
                            imageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                            g.DrawImage(fishImage, new Rectangle((int)fish.X, (int)fish.Y, fishImage.Width, fishImage.Height),
                                0, 0, fishImage.Width, fishImage.Height, GraphicsUnit.Pixel, imageAttributes);
                        }
                    }
                    else
                    {
                        g.DrawImage(fishImage, fish.X, fish.Y);
                    }

                    if (needsTransform)
                    {
                        fishImage.Dispose();
                    }
                }

                Bitmap crabImageToDraw = hermitCrab.Image;

                if (!hermitCrab.FacingRight)
                {
                    crabImageToDraw = (Bitmap)hermitCrab.Image.Clone();
                    crabImageToDraw.RotateFlip(RotateFlipType.RotateNoneFlipX);
                }

                g.DrawImage(crabImageToDraw, hermitCrab.X, hermitCrab.Y);

                if (!hermitCrab.FacingRight)
                {
                    crabImageToDraw.Dispose();
                }

                foreach (var plant in plants)
                {
                    if (plant.Y >= plantThresholdY)
                    {
                        g.DrawImage(plant.Image, plant.X, plant.Y);
                    }
                }

                Rectangle clockArea = new Rectangle(0, 20, 120, 80);
                clockWeather.DrawSmallClock(g, clockArea);
            }

            return bmp;
        }
    }
}