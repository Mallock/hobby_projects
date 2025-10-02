using System;

namespace TinyGptDemo.Utils
{
    public static class TimeFormatting
    {
        public static string Format(TimeSpan span)
        {
            if (span.TotalSeconds < 1) return "<1s";
            if (span.TotalMinutes < 1) return $"{(int)span.TotalSeconds}s";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m {(int)span.Seconds}s";
            return $"{(int)span.TotalHours}h {(int)span.Minutes}m";
        }
    }
}