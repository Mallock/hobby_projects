using System;

namespace TinyGptDemo.Utils
{
    public static class CliPrompts
    {
        public static int ReadInt(string prompt, int defaultValue, string description)
        {
            Console.WriteLine(description);
            Console.Write(prompt);
            string? input = Console.ReadLine();
            if (int.TryParse(input, out int value) && value > 0) return value;
            return defaultValue;
        }

        public static float ReadFloat(string prompt, float defaultValue, string description)
        {
            Console.WriteLine(description);
            Console.Write(prompt);
            string? input = Console.ReadLine();
            if (float.TryParse(input, out float value) && value > 0f) return value;
            return defaultValue;
        }
    }
}