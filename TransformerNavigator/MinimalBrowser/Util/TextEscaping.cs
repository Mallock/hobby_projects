namespace MinimalBrowser.Util
{
    public static class TextEscaping
    {
        public static string EscapeJs(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("`", "\\`").Replace("${", "\\${");
        }
    }
}