using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncMagic
{
    public class SimpleFishTankSimulation
    {
        private int canvasWidth = 240;
        private int canvasHeight = 240;
        private readonly int bottomBarHeight = 30; // status bar height (now at top)
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

            private static readonly (float minX, float maxX, float minY, float maxY, float limitX, float limitY) SlowSpeed = (-0.15f, 0.15f, -0.08f, 0.08f, 0.3f, 0.15f);
            private static readonly (float minX, float maxX, float minY, float maxY, float limitX, float limitY) MediumSpeed = (-0.4f, 0.4f, -0.15f, 0.15f, 0.8f, 0.3f);
            private static readonly (float minX, float maxX, float minY, float maxY, float limitX, float limitY) FastSpeed = (-0.65f, 0.65f, -0.4f, 0.4f, 1.3f, 0.65f);

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

        private class Decor
        {
            public float X;
            public float Y;
            public Bitmap Image;
        }
        private readonly List<Decor> decors = new List<Decor>();

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

            // Curated species: gold, parvi, surgeon, and the 2427851 icon fish if present
            fishTypes = new List<FishType>();
            var goldA = SafeCloneBitmap(() => SyncMagic.Properties.Resources.big_gold_fish);
            var goldB = SafeCloneBitmap(() => SyncMagic.Properties.Resources.gold_fish);
            Bitmap? parviBmp = FindResourceBitmap("parvi");
            Bitmap? surgeonBmp = FindResourceBitmap("surgeon");
            Bitmap? iconBmp = FindResourceBitmap("2427851");

            if (goldA != null) fishTypes.Add(new FishType(goldA, FishSpeedCategory.Fast));
            if (goldB != null) fishTypes.Add(new FishType(goldB, FishSpeedCategory.Fast));
            if (parviBmp != null) fishTypes.Add(new FishType(parviBmp, FishSpeedCategory.Medium));
            if (surgeonBmp != null) fishTypes.Add(new FishType(surgeonBmp, FishSpeedCategory.Fast));
            if (iconBmp != null) fishTypes.Add(new FishType(iconBmp, FishSpeedCategory.Slow));

            fishList = new List<Fish>();
            fishTypeCounts = new Dictionary<FishType, int>();

            // Target: 3 fish total for 240px
            int targetTotalFish = 3;
            int spawned = 0;
            spawned += SpawnSpecies("gold", Math.Min(2, targetTotalFish - spawned));
            string[] priority = new[] { "surgeon", "parvi", "2427851", "gold" };
            foreach (var key in priority)
            {
                if (spawned >= targetTotalFish) break;
                spawned += SpawnSpecies(key, 1);
            }
            while (spawned < targetTotalFish)
            {
                if (fishTypes.Count == 0) break;
                AddFish(fishTypes[rand.Next(Math.Min(2, fishTypes.Count))]);
                spawned++;
            }

            foodList = new List<Food>();

            // Curated, consistent bottom assets
            plantImages = new List<Bitmap>
            {
                new Bitmap(SyncMagic.Properties.Resources._8936057_algae_bloom_cladophora_seaweed_underwater_icon),
                new Bitmap(SyncMagic.Properties.Resources._8935921_algae_bloom_cladophora_seaweed_underwater_icon),
                new Bitmap(SyncMagic.Properties.Resources._8935918_coral_diving_nature_ocean_reef_icon),
                new Bitmap(SyncMagic.Properties.Resources.sea_grass_flip),
                new Bitmap(SyncMagic.Properties.Resources.sea_grass),
                new Bitmap(SyncMagic.Properties.Resources.sea_leaves),
                new Bitmap(SyncMagic.Properties.Resources.sea_leaves_flip),
                new Bitmap(SyncMagic.Properties.Resources.algae2),
                new Bitmap(SyncMagic.Properties.Resources.coral_scaled),
                new Bitmap(SyncMagic.Properties.Resources.coral_scaled_flip),
                new Bitmap(SyncMagic.Properties.Resources.coral1),
                new Bitmap(SyncMagic.Properties.Resources.coral2),
                new Bitmap(SyncMagic.Properties.Resources.coral3),
            };

            plants = new List<Plant>();
            Dictionary<Bitmap, int> plantImageCounts = new Dictionary<Bitmap, int>();
            int numPlants = 3; // toned down for 240px width
            int maxDuplicatesPerPlant = 1;
            float approximateSpacing = canvasWidth / numPlants;
            float waterHeightForPlants = canvasHeight;

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

                    int targetH = rand.Next(22, 36);
                    Bitmap scaledPlant = ScaleToHeight(plantImage, targetH);
                    float y = waterHeightForPlants - scaledPlant.Height - 5 + rand.Next(-2, 2);

                    bool placed = false;
                    for (int attempt = 0; attempt < 8 && !placed; attempt++)
                    {
                        float x = plantsAdded * approximateSpacing + rand.Next(-12, 12);
                        x = Math.Max(0, Math.Min(x, canvasWidth - scaledPlant.Width));
                        var rect = new RectangleF(x, y, scaledPlant.Width, scaledPlant.Height);
                        bool intersectsDecor = decors.Any(d => RectsIntersect(rect, new RectangleF(d.X, d.Y, d.Image.Width, d.Image.Height), 4));
                        if (!intersectsDecor)
                        {
                            Plant plant = new Plant { X = x, Y = y, Image = scaledPlant };
                            plants.Add(plant);
                            plantsAdded++;
                            placed = true;
                        }
                    }
                    if (!placed) scaledPlant.Dispose();
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

            // Static decor at bottom (toned down): choose up to 2 items among castle/hermit/crab/octopus
            var decorCandidates = new List<(string key, int h)>
            {
                ("castle", Math.Min(56, Math.Max(44, canvasHeight / 5))),
                ("hermit", 32),
                ("crab", 32),
                ("octopus", 36)
            };
            foreach (var kv in decorCandidates.OrderBy(_ => rand.Next()).Take(2))
                TryAddDecorAtBottom(kv.key, kv.h);

            Bitmap hermitCrabImage = SafeCloneBitmap(() => SyncMagic.Properties.Resources.hermit_crab) ?? new Bitmap(8, 8);

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

        private static Bitmap ScaleToHeight(Bitmap src, int height)
        {
            if (height <= 0) return new Bitmap(src);
            int w = Math.Max(1, (int)Math.Round(src.Width * (height / (double)src.Height)));
            var dst = new Bitmap(w, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, height));
            }
            return dst;
        }

        private void AddFish(FishType fishType)
        {
            float baseSpeedX = (float)(rand.NextDouble() * (fishType.MaxSpeedX - fishType.MinSpeedX) + fishType.MinSpeedX);
            float baseSpeedY = (float)(rand.NextDouble() * (fishType.MaxSpeedY - fishType.MinSpeedY) + fishType.MinSpeedY);
            float maxSpeedX = fishType.MaxSpeedXLimit;
            float maxSpeedY = fishType.MaxSpeedYLimit;

            Fish fish = new Fish
            {
                X = rand.Next(0, Math.Max(1, canvasWidth - fishType.Image.Width)),
                Y = rand.Next((bottomBarHeight + 20), Math.Max(bottomBarHeight + 21, canvasHeight - 20 - fishType.Image.Height)),
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
            // Disable food mechanics to avoid revealing non-animating periods
            foodList.Clear();

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

                    // Bounce off left/right walls to keep fish inside the aquarium
                    if (fish.X <= 0)
                    {
                        fish.X = 0;
                        fish.SpeedX = Math.Abs(fish.SpeedX) * 0.5f;
                        fish.DesiredSpeedX = Math.Abs(fish.DesiredSpeedX);
                        fish.FacingRight = true;
                    }
                    else if (fish.X + fish.Image.Width >= canvasWidth)
                    {
                        fish.X = canvasWidth - fish.Image.Width;
                        fish.SpeedX = -Math.Abs(fish.SpeedX) * 0.5f;
                        fish.DesiredSpeedX = -Math.Abs(fish.DesiredSpeedX);
                        fish.FacingRight = false;
                    }

                    if (fish.Y <= (bottomBarHeight + 20))
                    {
                        fish.Y = bottomBarHeight + 20;
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

            // Simple fish-fish collision avoidance (separation)
            for (int i = 0; i < fishList.Count; i++)
            {
                var a = fishList[i];
                var rectA = new RectangleF(a.X, a.Y, a.Image.Width, a.Image.Height);
                for (int j = i + 1; j < fishList.Count; j++)
                {
                    var b = fishList[j];
                    var rectB = new RectangleF(b.X, b.Y, b.Image.Width, b.Image.Height);
                    if (rectA.IntersectsWith(rectB))
                    {
                        float ax = a.X + a.Image.Width * 0.5f;
                        float ay = a.Y + a.Image.Height * 0.5f;
                        float bx = b.X + b.Image.Width * 0.5f;
                        float by = b.Y + b.Image.Height * 0.5f;
                        float dx = ax - bx;
                        float dy = ay - by;
                        if (Math.Abs(dx) < 0.001f && Math.Abs(dy) < 0.001f)
                        {
                            dx = (float)(rand.NextDouble() - 0.5);
                            dy = (float)(rand.NextDouble() - 0.5);
                        }
                        float len = (float)Math.Sqrt(dx * dx + dy * dy);
                        dx /= len; dy /= len;
                        float push = 0.6f;
                        a.X = Math.Max(0, Math.Min(canvasWidth - a.Image.Width, a.X + dx * push));
                        a.Y = Math.Max(bottomBarHeight + 10, Math.Min(canvasHeight - 10 - a.Image.Height, a.Y + dy * push));
                        b.X = Math.Max(0, Math.Min(canvasWidth - b.Image.Width, b.X - dx * push));
                        b.Y = Math.Max(bottomBarHeight + 10, Math.Min(canvasHeight - 10 - b.Image.Height, b.Y - dy * push));
                        rectA = new RectangleF(a.X, a.Y, a.Image.Width, a.Image.Height);
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

            // Disable bubbles entirely
            bubbles.Clear();
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
                // Use opaque background to avoid GIF transparency artifacts on device
                g.Clear(Color.Black);

                Brush sandBrush = new SolidBrush(Color.SandyBrown);
                g.FillPolygon(sandBrush, sandPoints);

                foreach (var d in decors)
                {
                    g.DrawImage(d.Image, d.X, d.Y);
                }

                float plantThresholdY = canvasHeight * 0.6f;

                foreach (var plant in plants)
                {
                    if (plant.Y < plantThresholdY)
                    {
                        g.DrawImage(plant.Image, plant.X, plant.Y);
                    }
                }

                // Bubbles and food visuals removed for seamless appearance during uploads

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
                            // Brighten without changing alpha so fish stays visible in GIF
                            float boost = 1.2f;
                            float[][] colorMatrixElements = {
                                new float[] {boost, 0,     0,     0, 0},
                                new float[] {0,     boost, 0,     0, 0},
                                new float[] {0,     0,     boost, 0, 0},
                                new float[] {0,     0,     0,     1, 0},
                                new float[] {0,     0,     0,     0, 1}
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

                    // Draw a top status bar with time (left) and temperature (right), optionally mirrored
                    int barHeight = bottomBarHeight;
                    Rectangle topBar = new Rectangle(0, 0, canvasWidth, barHeight);
                    clockWeather.DrawStatusBar(g, topBar, RenderOptions.MirrorStatusBar);
            }

            return bmp;
        }

        private static bool RectsIntersect(RectangleF a, RectangleF b, float inflate)
        {
            a.Inflate(inflate, inflate);
            b.Inflate(inflate, inflate);
            return a.IntersectsWith(b);
        }

        // Helpers
        private static Bitmap? SafeCloneBitmap(Func<Bitmap> getter)
        {
            try { var b = getter(); return b != null ? new Bitmap(b) : null; } catch { return null; }
        }

        private Bitmap? FindResourceBitmap(params string[] keywords)
        {
            try
            {
                var rm = SyncMagic.Properties.Resources.ResourceManager;
                var set = rm.GetResourceSet(CultureInfo.CurrentUICulture, true, true);
                foreach (DictionaryEntry entry in set)
                {
                    var key = entry.Key as string;
                    if (string.IsNullOrEmpty(key)) continue;
                    if (keywords.Any(k => key.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        if (entry.Value is Bitmap bmp)
                            return new Bitmap(bmp);
                    }
                }
            }
            catch { }
            return null;
        }

        private int SpawnSpecies(string keyword, int count)
        {
            int added = 0;
            for (int i = 0; i < count; i++)
            {
                var candidates = fishTypes.Where(ft => TryMatchBitmap(ft.Image, keyword)).ToList();
                FishType? ftPick = candidates.Count > 0 ? candidates[rand.Next(candidates.Count)] : null;
                if (ftPick == null)
                {
                    var goldCandidates = fishTypes.Where(ft => TryMatchBitmap(ft.Image, "gold")).ToList();
                    ftPick = goldCandidates.Count > 0 ? goldCandidates[rand.Next(goldCandidates.Count)] : (fishTypes.Count > 0 ? fishTypes[rand.Next(Math.Min(2, fishTypes.Count))] : null);
                }
                if (ftPick != null)
                {
                    AddFish(ftPick);
                    added++;
                }
            }
            return added;
        }

        private bool TryMatchBitmap(Bitmap bmp, string keyword)
        {
            if (keyword.Equals("gold", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var g1 = SyncMagic.Properties.Resources.big_gold_fish;
                    var g2 = SyncMagic.Properties.Resources.gold_fish;
                    return (bmp.Width == g1.Width && bmp.Height == g1.Height) || (bmp.Width == g2.Width && bmp.Height == g2.Height);
                }
                catch { return false; }
            }
            return true;
        }

        private void TryAddDecorAtBottom(string keyword, int preferredHeight)
        {
            var bmp = FindResourceBitmap(keyword);
            if (bmp == null) return;
            var scaled = ScaleToHeight(bmp, preferredHeight);
            float x = rand.Next(0, Math.Max(1, canvasWidth - scaled.Width));
            var rect = new RectangleF(x, canvasHeight - scaled.Height - 1, scaled.Width, scaled.Height);
            int attempts = 0;
            while (decors.Any(d => OverlapTooHigh(rect, new RectangleF(d.X, d.Y, d.Image.Width, d.Image.Height))) && attempts++ < 25)
            {
                x = rand.Next(0, Math.Max(1, canvasWidth - scaled.Width));
                rect = new RectangleF(x, canvasHeight - scaled.Height - 1, scaled.Width, scaled.Height);
            }
            float y = canvasHeight - scaled.Height - 1;
            decors.Add(new Decor { X = x, Y = y, Image = scaled });
        }

        private static bool OverlapTooHigh(RectangleF a, RectangleF b)
        {
            var inter = RectangleF.Intersect(a, b);
            if (inter.Width <= 0 || inter.Height <= 0) return false;
            float interArea = inter.Width * inter.Height;
            float minArea = Math.Min(a.Width * a.Height, b.Width * b.Height);
            return interArea > 0.08f * minArea; // >8% overlap considered too much
        }
    }
}