// This tool scans Civilization VII XML files for German (de_DE) text entries and replaces them with Finnish translations
// or proofreads existing Finnish text, preserving all placeholders and formatting.

using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Xml.Linq;

// Shared HttpClient instance for all requests, with increased timeout
HttpClient SharedHttpClient = new HttpClient
{
    Timeout = TimeSpan.FromMinutes(10)
};

// --- Mode selection: translation or proofreading via console input ---
string mode;
if (args.Length > 0 && (args[0].Equals("translate", StringComparison.OrdinalIgnoreCase) || args[0].Equals("proofread", StringComparison.OrdinalIgnoreCase)))
{
    mode = args[0].ToLowerInvariant();
}
else
{
    Console.WriteLine("Select mode:");
    Console.WriteLine("1 = Translate (German to Finnish)");
    Console.WriteLine("2 = Proofread (Finnish text)");
    Console.Write("Enter 1 or 2 (default: 2): ");
    var input = Console.ReadLine();
    if (input?.Trim() == "1" || input?.Trim().Equals("translate", StringComparison.OrdinalIgnoreCase) == true)
        mode = "translate";
    else
        mode = "proofread";
}

int argOffset = (args.Length > 0 && (args[0].Equals("translate", StringComparison.OrdinalIgnoreCase) || args[0].Equals("proofread", StringComparison.OrdinalIgnoreCase))) ? 1 : 0;

string inputRoot = args.Length > argOffset ? args[argOffset] : @"C:\Program Files (x86)\Steam\steamapps\common\Sid Meier's Civilization VII";
string outputRoot = args.Length > argOffset + 1 ? args[argOffset + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CivVII-Fi-Localization");
string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
string model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4.1";

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set your OpenAI API key in the OPENAI_API_KEY environment variable.");
    return;
}

Console.WriteLine($"Mode:   {mode}");
Console.WriteLine($"Input:  {inputRoot}");
Console.WriteLine($"Output: {outputRoot}");
Console.WriteLine($"Model:  {model}");
Console.WriteLine();

var xmlFiles = Directory.EnumerateFiles(inputRoot, "*.xml", SearchOption.AllDirectories)
    .Where(f => f.Contains("de_DE", StringComparison.OrdinalIgnoreCase))
    .ToList();

if (!xmlFiles.Any())
{
    Console.WriteLine("No XML files with 'de_DE' found.");
    return;
}

// Count total translatable entries (excluding city/citizen names)
int totalTranslatableEntries = 0;
foreach (var file in xmlFiles)
{
    try
    {
        var doc = XDocument.Load(file, LoadOptions.PreserveWhitespace);
        totalTranslatableEntries += doc.Descendants("Replace")
            .Count(e =>
                (string?)e.Attribute("Language") == "de_DE" &&
                e.Element("Text") != null &&
                !string.IsNullOrEmpty((string?)e.Attribute("Tag")) &&
                !((string?)e.Attribute("Tag")).StartsWith("LOC_CITY_NAME_", StringComparison.OrdinalIgnoreCase) &&
                !((string?)e.Attribute("Tag")).StartsWith("LOC_CITIZEN_", StringComparison.OrdinalIgnoreCase)
            );
    }
    catch { }
}

int totalFiles = 0, totalEntries = 0, totalTranslated = 0;
int totalFileCount = xmlFiles.Count;
int currentFileIndex = 0;

var stopwatch = Stopwatch.StartNew();

const int BATCH_SIZE = 10; // Tune this as needed

foreach (var file in xmlFiles)
{
    currentFileIndex++;
    double percentDone = (double)(currentFileIndex - 1) / totalFileCount * 100.0;
    double percentRemaining = (double)(totalFileCount - currentFileIndex + 1) / totalFileCount * 100.0;

    Console.WriteLine($"\nProcessing file {currentFileIndex}/{totalFileCount}: {file}");
    Console.WriteLine($"Progress: {percentDone:F1}% done, {percentRemaining:F1}% remaining");

    XDocument inputDoc;
    try
    {
        inputDoc = XDocument.Load(file, LoadOptions.PreserveWhitespace);
    }
    catch
    {
        Console.WriteLine($"Failed to load: {file}");
        continue;
    }
    bool changed = false;
    string relPath = Path.GetRelativePath(inputRoot, file);
    string outFile = Path.Combine(outputRoot, relPath);
    Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);

    // If output file exists, load it to preserve already translated entries
    XDocument outDoc;
    if (File.Exists(outFile))
    {
        try
        {
            outDoc = XDocument.Load(outFile, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            outDoc = new XDocument(inputDoc);
        }
    }
    else
    {
        outDoc = new XDocument(inputDoc);
    }

    // Build a map of Tag->Text for the output file
    var outReplaceMap = outDoc.Descendants("Replace")
        .Where(e => (string?)e.Attribute("Language") == "de_DE" && e.Element("Text") != null)
        .ToDictionary(
            e => (string?)e.Attribute("Tag") ?? "",
            e => e.Element("Text")!.Value.Trim()
        );

    // Prepare jobs: only those not yet processed
    var jobs = new List<(XElement replace, string originalText)>();
    var inputReplaces = inputDoc.Descendants("Replace")
        .Where(e => (string?)e.Attribute("Language") == "de_DE" && e.Element("Text") != null)
        .ToList();

    foreach (var replace in inputReplaces)
    {
        var tagAttr = (string?)replace.Attribute("Tag");
        if (string.IsNullOrEmpty(tagAttr) ||
            tagAttr.StartsWith("LOC_CITY_NAME_", StringComparison.OrdinalIgnoreCase) ||
            tagAttr.StartsWith("LOC_CITIZEN_", StringComparison.OrdinalIgnoreCase))
            continue;

        var textElem = replace.Element("Text");
        if (textElem == null) continue;

        string originalText = textElem.Value.Trim();
        if (string.IsNullOrWhiteSpace(originalText)) continue;

        // Check if already processed in output
        if (outReplaceMap.TryGetValue(tagAttr, out var outText) && outText != originalText)
        {
            // Already processed, skip
            continue;
        }

        // Find the corresponding Replace in outDoc (by Tag)
        var outReplace = outDoc.Descendants("Replace")
            .FirstOrDefault(e => (string?)e.Attribute("Tag") == tagAttr && (string?)e.Attribute("Language") == "de_DE");
        if (outReplace != null)
            jobs.Add((outReplace, originalText));
    }

    int fileEntryCount = inputReplaces.Count(e =>
        !string.IsNullOrEmpty((string?)e.Attribute("Tag")) &&
        !((string?)e.Attribute("Tag")).StartsWith("LOC_CITY_NAME_", StringComparison.OrdinalIgnoreCase) &&
        !((string?)e.Attribute("Tag")).StartsWith("LOC_CITIZEN_", StringComparison.OrdinalIgnoreCase)
    );
    int fileEntryIndex = fileEntryCount - jobs.Count; // Already processed count

    // Process in batches
    int translationsSinceLastSave = 0;

    for (int i = 0; i < jobs.Count; i += BATCH_SIZE)
    {
        var batch = jobs.Skip(i).Take(BATCH_SIZE).ToList();
        var tasks = batch.Select(async job =>
        {
            var (replace, originalText) = job;
            var textElem = replace.Element("Text");
            if (textElem == null) return (replace, originalText, originalText);

            Console.WriteLine("Original:   " + originalText);
            string processed;
            if (mode == "translate")
                processed = await TranslateTextAsync(originalText, apiKey, model);
            else
                processed = await ProofreadFinnishTextAsync(originalText, apiKey, model);

            Console.WriteLine(mode == "translate" ? "Translated: " : "Proofread:  " + processed);
            Console.WriteLine(new string('-', 40));
            return (replace, originalText, processed);
        }).ToList();

        var results = await Task.WhenAll(tasks);

        foreach (var (replace, originalText, processed) in results)
        {
            fileEntryIndex++;
            totalEntries++;

            if (!string.IsNullOrWhiteSpace(processed) && processed != originalText)
            {
                replace.Element("Text")!.Value = processed;
                changed = true;
                totalTranslated++;
                translationsSinceLastSave++;
            }
            Console.WriteLine("-------------------------------");
            // Progress and ETA
            double filePercent = (double)fileEntryIndex / fileEntryCount * 100.0;
            double totalPercent = (double)totalTranslated / totalTranslatableEntries * 100.0;
            double avgSecondsPerEntry = stopwatch.Elapsed.TotalSeconds / (totalTranslated == 0 ? 1 : totalTranslated);
            double estSecondsLeft = avgSecondsPerEntry * (totalTranslatableEntries - totalTranslated);
            TimeSpan estTimeLeft = TimeSpan.FromSeconds(estSecondsLeft);
            Console.WriteLine("-------------------------------");
            Console.WriteLine("-------------------------------");
            Console.WriteLine($"File progress:  {fileEntryIndex}/{fileEntryCount} ({filePercent:F1}%)");
            Console.WriteLine($"Total progress: {totalTranslated}/{totalTranslatableEntries} ({totalPercent:F1}%)");
            Console.WriteLine($"Estimated time remaining: {estTimeLeft:hh\\:mm\\:ss}");
            Console.WriteLine("-------------------------------");
            Console.WriteLine();
            Console.WriteLine();

            // Save every 100 translations
            if (translationsSinceLastSave >= 100)
            {
                if (SaveWithRetry(outDoc, outFile, SaveOptions.DisableFormatting))
                    Console.WriteLine($"Saved: {outFile}");
                else
                    Console.WriteLine($"ERROR: Could not save {outFile} after multiple attempts.");
                translationsSinceLastSave = 0;
            }
        }
    }

    // Final save if there are unsaved changes
    if (changed && translationsSinceLastSave > 0)
    {
        if (SaveWithRetry(outDoc, outFile, SaveOptions.DisableFormatting))
            Console.WriteLine($"Final save: {outFile}");
        else
            Console.WriteLine($"ERROR: Could not save {outFile} after multiple attempts.");
    }
}
bool SaveWithRetry(XDocument doc, string path, SaveOptions options, int maxRetries = 5, int delayMs = 500)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            doc.Save(path, options);
            return true;
        }
        catch (IOException ex)
        {
            if (attempt == maxRetries)
            {
                Console.WriteLine($"Failed to save {path} after {maxRetries} attempts: {ex.Message}");
                return false;
            }
            Console.WriteLine($"Save failed (attempt {attempt}/{maxRetries}) for {path}: {ex.Message}. Retrying in {delayMs}ms...");
            Thread.Sleep(delayMs);
        }
    }
    return false;
}
Console.WriteLine($"\nDone. Files written: {totalFiles}, Entries translated: {totalTranslated}.");

// --- Helper: OpenAI translation ---
async Task<string> TranslateTextAsync(string text, string apiKey, string model)
{
    // If the text is only a variable/parameter (e.g., "{1_reward}"), skip translation
    if (Regex.IsMatch(text.Trim(), @"^\{[^\}]+\}$"))
        return text;

    // Mask curly braces and percent tokens to preserve game parameters
    var masked = text
        .Replace("{", "[[LEFTBRACE]]")
        .Replace("}", "[[RIGHTBRACE]]")
        .Replace("%", "[[PERCENT]]");

    string systemInstruction =
        "You are a professional game localizer. " +
        "If the text contains English game parameters (such as 'reward', 'unit', 'science'), translate the rest of the text from German to Finnish, " +
        "but keep those English parameter words unchanged and in their original position. " +
        "Preserve all placeholders and formatting (such as [[LEFTBRACE]], [[RIGHTBRACE]], [[PERCENT]]). " +
        "Only output the translated Finnish text, do not add explanations.";

    string prompt = $"""
Translate the following Civilization game text from German to Finnish. 
Preserve all placeholders, parameters, and formatting (such as [[LEFTBRACE]], [[RIGHTBRACE]], [[PERCENT]]) exactly as in the input.
If the text contains English game parameters (such as 'reward', 'unit', 'science'), keep those words unchanged and in their original position.
Only return the translated Finnish text, do not add explanations.

German:
{masked}
Finnish:
""";

    var client = SharedHttpClient;
    client.DefaultRequestHeaders.Remove("Authorization");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

    var req = new
    {
        model,
        messages = new[]
        {
            new { role = "system", content = systemInstruction },
            new { role = "user", content = prompt }
        },
        temperature = 0.1
    };

    var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
    var resp = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
    if (!resp.IsSuccessStatusCode)
        return text;

    using var stream = await resp.Content.ReadAsStreamAsync();
    using var doc = await JsonDocument.ParseAsync(stream);
    var result = doc.RootElement
        .GetProperty("choices")[0]
        .GetProperty("message")
        .GetProperty("content")
        .GetString() ?? text;

    // Unmask
    return result
        .Replace("[[LEFTBRACE]]", "{")
        .Replace("[[RIGHTBRACE]]", "}")
        .Replace("[[PERCENT]]", "%")
        .Trim();
}

async Task<string> ProofreadFinnishTextAsync(string text, string apiKey, string model)
{
    var trimmed = text.Trim();

    // Jos teksti on pelkkä muuttuja/placeholder, älä muokkaa
    if (Regex.IsMatch(trimmed, @"^\{[^}]+\}$"))
        return text;

    // Jos teksti on vain yksi sana (ei välejä)
    if (!trimmed.Contains(' '))
        return text;

    // Maskataan kaikki aaltosulkeet ja %-merkit
    var masked = text
        .Replace("{", "[[LB]]")
        .Replace("}", "[[RB]]")
        .Replace("%", "[[PCT]]");

    string systemInstruction =
        "Toimit kokeneena suomen kielen peli-lokalisoijana ja oikolukijana. " +
        "Tehtäväsi on tehdä tekstistä selkeää, luonnollista ja sujuvaa yleiskielistä suomea, " +
        "mutta säilyttää alkuperäinen merkitys muuttumattomana. " +
        "Säilytä kaikki parametrit ja placeholderit (esim. [[LB]], [[RB]], [[PCT]], {unit}) " +
        "täsmälleen alkuperäisessä muodossaan. " +
        "Älä lisää mitään uutta tietoa, älä poista sisältöä, älä muuta pelimekaanisia elementtejä. " +
        "Jos teksti on jo täysin luonnollista suomea, palauta se sellaisenaan. " +
        "Palauta vain lopullinen teksti ilman selityksiä.";

    string prompt = $"""
Oikolue ja paranna seuraava Civilization-pelin teksti suomeksi.
Säilytä kaikki parametrit ja placeholderit muuttumattomina.

Teksti:
{masked}

Parannettu:
""";

    var client = SharedHttpClient;
    client.DefaultRequestHeaders.Remove("Authorization");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

    var req = new
    {
        model,
        messages = new[]
        {
            new { role = "system", content = systemInstruction },
            new { role = "user", content = prompt }
        },
        temperature = 0.1
    };

    var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
    var resp = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
    if (!resp.IsSuccessStatusCode)
        return text;

    using var stream = await resp.Content.ReadAsStreamAsync();
    using var doc = await JsonDocument.ParseAsync(stream);
    var result = doc.RootElement
        .GetProperty("choices")[0]
        .GetProperty("message")
        .GetProperty("content")
        .GetString() ?? text;

    // Poistetaan whitespace ja palautetaan maskit
    return result.Trim()
        .Replace("[[LB]]", "{")
        .Replace("[[RB]]", "}")
        .Replace("[[PCT]]", "%");
}