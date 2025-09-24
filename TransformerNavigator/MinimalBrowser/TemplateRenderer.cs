using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransformerNavigator
{
    public static class TemplateRenderer
    {
        public static string Render(string template, IDictionary<string, string> tokens)
        {
            if (string.IsNullOrEmpty(template) || tokens == null || tokens.Count == 0)
                return template ?? string.Empty;

            var sb = new StringBuilder(template);
            foreach (var kvp in tokens)
            {
                var marker = "{{" + kvp.Key + "}}";
                sb.Replace(marker, kvp.Value ?? string.Empty);
            }
            return sb.ToString();
        }
    }
}
