using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TransformerNavigator
{
    public class PortalSettings
    {
        public string DefaultLanguage { get; set; } = "fi";
        public OpenAISection OpenAI { get; set; } = new();
        public PromptsSection Prompts { get; set; } = new();
        public TemplatesSection Templates { get; set; } = new();

        public static PortalSettings Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Portal settings file was not found at '{path}'.");

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<PortalSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (settings == null)
                throw new InvalidOperationException("Unable to deserialize portal settings.");
            return settings;
        }

        public class OpenAISection
        {
            public string PrimaryModel { get; set; } = string.Empty;
            public string TeaserModel { get; set; } = string.Empty;
            public string SystemPrompt { get; set; } = string.Empty;
            public string TeaserSystemPrompt { get; set; } = string.Empty;
            public string FinalInstructionTemplate { get; set; } = string.Empty;
            public string TeaserFinalInstructionTemplate { get; set; } = string.Empty;
        }

        public class PromptsSection
        {
            public string ResponseQualityDirective { get; set; } = string.Empty;
            public string HomePage { get; set; } = string.Empty;
            public string NavigationTemplate { get; set; } = string.Empty;
            public string SearchTemplate { get; set; } = string.Empty;
            public string MainEnrichmentTemplate { get; set; } = string.Empty;
            public string LoadingSnippetTemplate { get; set; } = string.Empty;
        }

        public class TemplatesSection
        {
            public string StaticSearchUtility { get; set; } = string.Empty;
            public string PageLayout { get; set; } = string.Empty;
        }
    }
}
