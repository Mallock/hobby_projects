using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.ServiceModel.Syndication;
using System.Xml;

public class OfficeSimulation
{
    // Fields        
    private List<string> phrases;
    private Random random;
    private string lastPhrase;
    private Bitmap backgroundBitmap;

    private DateTime lastPhraseUpdateTime;
    private int speechBubbleUpdateInterval;

    // Fields for news data              
    private List<string> _newsHeadlines;
    private DateTime _lastNewsFetchTime;
    private int _currentNewsIndex;

    // List of RSS feed URLs      
    private List<string> _rssFeedUrls;

    // Worker positions    
    private List<Point> workerPositions;

    // Selected worker for speech bubble    
    private Point selectedWorkerPosition;

    // Constructor        
    public OfficeSimulation()
    {
        // Initialize phrases        
        phrases = new List<string>()
        {
            "Kuka tarvitsee viikonloppua, kun maanantai on täällä taas?",
            "Aika on vain käsite – kunnes työpäivä loppuu.",
            "Kahvi: ainoa syy, miksi tämä päivä on mahdollinen.",
            "Sanotaan, että motivaatio tulee sisältäpäin – minulla se ei ole löytynyt.",
            "Olen työteliäs... ainakin kahvin äärellä.",
            "En ole unohtanut mitään – siirsin vain muistini pilveen.",
            "Joskus ajattelen olla tuottelias... mutta sitten muistan, että en ole.",
            "Kuka tarvitsi vielä yhden kokouksen tähän päivään?",
            "Elämä on peli – harmi, ettei siinä ole tallennuspisteitä.",
            "Aika kuluu nopeasti, kun et tee mitään järkevää.",
            "Olen oppinut kaiken tärkeän 80-luvun videopeleistä.",
            "Onko tämä se päivä, jolloin vihdoin siivoan työpöydän? Ei ole.",
            "Koirat ovat iloisia – ne eivät tiedä mitään maanantaista.",
            "Olisin optimisti, mutta realismi vaatii enemmän kahvia.",
            "Päivän tavoitteeni? Selvitä lounastaukoon asti.",
            "Onko tämä työtehtävä vai vain jonkun toiveajattelua?",
            "Täällä yritän ratkaista ongelmia, joita en luonut.",
            "Jos maailma loppuu huomenna, tänään voisi olla kahvia lisää.",
            "Miksi tehdä asiat yksinkertaisesti, kun ne voi monimutkaistaa?",
            "Jokainen päivä on seikkailu – vain kartta ja ohjeet puuttuvat.",
            "Elämä on simulaatio – ja minulta puuttuu kaikki opetusvideot.",
            "Kahvipaussi – päivän tärkein hetki.",
            "En tiedä kaikkea, mutta tiedän miten googlettaa.",
            "Tämä voi näyttää työnteolta, mutta oikeasti ajattelen kahvia.",
            "Luotan deadlineen – se saa minut toimimaan.",
            "Elämä on liian lyhyt stressaamiseen, mutta liian pitkä huolettomaan asenteeseen.",
            "Mottoni? 'Yritä uudestaan huomenna.'",
            "Voisin olla tehokas, jos vain tietäisin miten.",
            "80-luku soittaa – haluaa rentoutumisen takaisin.",
            "Raskas päivä? Koirat eivät koskaan valita.",
            "Kahvi auttaa minua keskittymään... ainakin kolmeksi sekunniksi.",
            "Toisille työ, minulle elämän simulaatio ilman käsikirjoitusta.",
            "Odottelen innolla lounastaukoa, kunhan pääsen aamukahvista ohi.",
            "Ajattelin aloittaa kuntoilun... mutta tuuli käänsi suunnitelmat.",
            "Ehkä huomenna teen kaiken – mutta todennäköisesti en.",
            "Jos ajattelen oikein kovasti, ehkä ongelmat ratkeavat itsestään.",
            "En kaipaa seikkailuja – riittää kun selviän työpäivästä.",
            "Pomoni? Voi, hän uskoo ihmeisiin.",
            "En ole stressaantunut – ainoastaan todella motivoitunut pysymään paikallaan.",
            "Aamukahvi, iltapäiväkahvi, ja kaikki siltä väliltä.",
            "Vain yksi sana: viivyttely.",
            "Tärkeät asiat ensin: miten vältän päivän velvollisuudet?",
            "Koirat tietävät totuuden elämästä – siinä ei kiirettä ole.",
            "Pienet asiat ilahduttavat – kuten tauot ja viikonloppu.",
            "Rutiini on paras kaverini, ainakin silloin kun muistan sen.",
            "Olen työpöytäni hallitsija – paperipinoineen ja kahvikupeineni.",
            "Ei paniikkia – onhan minulla kahvi ja koirat.",
            "Päivän motto: Yksi askel kerrallaan (tai mahdollisimman vähän askelia).",
            "Työ on leikkiä – tai ainakin siltä se tuntuu, kun ei tee mitään.",
            "Ei stressiä, ei kiirettä, vain yksi työtehtävä kerrallaan... ehkä."
        };

        random = new Random();
        lastPhrase = null;

        speechBubbleUpdateInterval = 5; // Speech bubble updates every 5 seconds                
        lastPhraseUpdateTime = DateTime.Now;

        // Initialize worker positions list    
        workerPositions = new List<Point>();

        // Generate the static background bitmap        
        GenerateBackgroundBitmap();

        // Initialize news data              
        _newsHeadlines = new List<string>();
        _lastNewsFetchTime = DateTime.MinValue;
        _currentNewsIndex = 0;

        // Initialize RSS feed URLs      
        _rssFeedUrls = new List<string>()
        {
            "https://feeds.yle.fi/uutiset/v1/majorHeadlines/YLE_UUTISET.rss", // Major headlines      
            "https://feeds.yle.fi/uutiset/v1/recent.rss?publisherIds=YLE_UUTISET&concepts=18-34837", // Kotimaa      
            "https://feeds.yle.fi/uutiset/v1/recent.rss?publisherIds=YLE_UUTISET&concepts=18-34953", // Ulkomaat      
            "https://feeds.yle.fi/uutiset/v1/recent.rss?publisherIds=YLE_UUTISET&concepts=18-19274", // Talous      
            "https://feeds.yle.fi/uutiset/v1/recent.rss?publisherIds=YLE_UUTISET&concepts=18-38033", // Politiikka      
            "https://feeds.yle.fi/uutiset/v1/recent.rss?publisherIds=YLE_UUTISET&concepts=18-150067", // Kulttuuri      
            "https://feeds.yle.fi/uutiset/v1/recent.rss?publisherIds=YLE_UUTISET&concepts=18-36066", // Viihde      
            "https://feeds.yle.fi/uutiset/v1/recent.rss?publisherIds=YLE_UUTISET&concepts=18-819", // Tiede      
            "https://feeds.yle.fi/uutiset/v1/recent.rss?publisherIds=YLE_UUTISET&concepts=18-35354", // Luonto      
            "https://feeds.yle.fi/uutiset/v1/recent.rss?publisherIds=YLE_UUTISET&concepts=18-35138", // Terveys      
            "https://feeds.yle.fi/uutiset/v1/recent.rss?publisherIds=YLE_UUTISET&concepts=18-35057", // Media      
            "https://feeds.yle.fi/uutiset/v1/recent.rss?publisherIds=YLE_UUTISET&concepts=18-12", // Liikenne      
            "https://feeds.yle.fi/uutiset/v1/recent.rss?publisherIds=YLE_UUTISET&concepts=18-35381", // Näkökulmat
            "https://www.brainyquote.com/link/quotear.rss"                                                                                         // 
        };
    }

    // Method to get the office state bitmap        
    public Bitmap GetOfficeState()
    {
        // Fetch news data if needed              
        GetNewsData();

        // Check if it's time to update the speech bubble text                
        TimeSpan timeSinceLastUpdate = DateTime.Now - lastPhraseUpdateTime;

        if (timeSinceLastUpdate.TotalSeconds >= speechBubbleUpdateInterval)
        {
            // Alternate between news headline and random phrase            
            if (random.NextDouble() < 0.5)
            {
                // Update the lastPhrase with the next news headline              
                lastPhrase = GetNextNewsHeadline();
            }
            else
            {
                lastPhrase = GetNewPhrase();
            }
            lastPhraseUpdateTime = DateTime.Now;

            // Randomly select a worker for the speech bubble    
            if (workerPositions.Count > 0)
            {
                int index = random.Next(workerPositions.Count);
                selectedWorkerPosition = workerPositions[index];
            }
            else
            {
                // Default position if no workers    
                selectedWorkerPosition = new Point(120, 180);
            }
        }

        // Create a new bitmap with the background and speech bubble        
        Bitmap bitmap = new Bitmap(backgroundBitmap);

        using (Graphics g = Graphics.FromImage(bitmap))
        {
            // Draw speech bubble with text        
            DrawSpeechBubble(g, lastPhrase, selectedWorkerPosition);
        }

        return bitmap;
    }

    private string GetNewPhrase()
    {
        string newPhrase;
        if (phrases.Count <= 1)
        {
            newPhrase = phrases[0];
        }
        else
        {
            do
            {
                int index = random.Next(phrases.Count);
                newPhrase = phrases[index];
            } while (newPhrase == lastPhrase);
        }
        return newPhrase;
    }

    private void GenerateBackgroundBitmap()
    {
        backgroundBitmap = new Bitmap(240, 240);
        using (Graphics g = Graphics.FromImage(backgroundBitmap))
        {
            // Fill background        
            g.Clear(Color.LightGray);

            // Draw static elements        
            DrawOfficeScene(g);
        }
    }

    private void DrawOfficeScene(Graphics g)
    {
        // Clear worker positions    
        workerPositions.Clear();

        // Fill background    
        g.Clear(Color.Transparent);

        // Calculate how many workers to draw based on the image size and desired spacing    
        int workerCountX = 3; // Number of workers horizontally    
        int workerCountY = 1; // Only one row of workers  

        int workerSpacingX = 80; // Horizontal spacing between workers    

        // List of shirt colors    
        List<Brush> shirtColors = new List<Brush> { Brushes.Blue, Brushes.Green, Brushes.Red };

        // Calculate baseY to position workers at the bottom  
        int imageHeight = 240;
        int workerHeight = 50; // Approximate height of the worker drawing  
        int marginBottom = 5; // Margin from the bottom of the image  
        int baseY = imageHeight - workerHeight - marginBottom; // Position workers at the bottom  

        for (int i = 0; i < workerCountX; i++)
        {
            for (int j = 0; j < workerCountY; j++)
            {
                // Adjust baseX to center the workers horizontally  
                int totalWorkerWidth = (workerCountX - 1) * workerSpacingX;
                int startX = (240 - totalWorkerWidth) / 2; // Center the workers  
                int baseX = startX + i * workerSpacingX;

                Brush shirtColor = shirtColors[i % shirtColors.Count];

                DrawWorkerAtPosition(g, baseX, baseY, shirtColor);

                // Store the position of the worker's head for speech bubble origin    
                int bodyX = baseX + 22;
                int bodyY = baseY + 30;
                int bodyWidth = 6;
                int headRadius = 5;
                int headX = bodyX + (bodyWidth / 2) - headRadius;
                int headY = bodyY - 8 - headRadius;

                // Center point of the head    
                int headCenterX = headX + headRadius;
                int headCenterY = headY + headRadius;

                workerPositions.Add(new Point(headCenterX, headCenterY));
            }
        }
    }

    private void DrawWorkerAtPosition(Graphics g, int baseX, int baseY, Brush shirtColor)
    {
        // Adjust sizes to make the desk and worker smaller    

        // Draw desk    
        Rectangle deskRect = new Rectangle(baseX, baseY + 40, 50, 10); // Smaller desk    
        g.FillRectangle(Brushes.SaddleBrown, deskRect);
        g.DrawRectangle(Pens.Black, deskRect);

        // Draw computer monitor    
        Rectangle monitorRect = new Rectangle(baseX + 15, baseY + 20, 20, 13); // Smaller monitor    
        g.FillRectangle(Brushes.Black, monitorRect);
        g.DrawRectangle(Pens.Black, monitorRect);

        // Draw monitor screen    
        Rectangle screenRect = new Rectangle(monitorRect.X + 2, monitorRect.Y + 2, monitorRect.Width - 4, monitorRect.Height - 4);
        g.FillRectangle(Brushes.DarkGray, screenRect);

        // Draw keyboard    
        Rectangle keyboardRect = new Rectangle(baseX + 10, baseY + 50, 30, 3); // Smaller keyboard    
        g.FillRectangle(Brushes.DarkGray, keyboardRect);
        g.DrawRectangle(Pens.Black, keyboardRect);

        // Draw mouse    
        Rectangle mouseRect = new Rectangle(baseX + 42, baseY + 47, 4, 2); // Smaller mouse    
        g.FillEllipse(Brushes.DarkGray, mouseRect);
        g.DrawEllipse(Pens.Black, mouseRect);

        // Draw character's body (shirt)    
        int bodyX = baseX + 22;
        int bodyY = baseY + 30;
        int bodyWidth = 6;
        int bodyHeight = 12;
        g.FillRectangle(shirtColor, bodyX, bodyY, bodyWidth, bodyHeight);
        g.DrawRectangle(Pens.Black, bodyX, bodyY, bodyWidth, bodyHeight);

        // Draw character's neck    
        g.FillRectangle(Brushes.Beige, bodyX + (bodyWidth / 2) - 2, bodyY - 4, 4, 4);

        // Draw character's head    
        int headRadius = 5;
        int headX = bodyX + (bodyWidth / 2) - headRadius;
        int headY = bodyY - 8 - headRadius;
        g.FillEllipse(Brushes.Beige, headX, headY, headRadius * 2, headRadius * 2);
        g.DrawEllipse(Pens.Black, headX, headY, headRadius * 2, headRadius * 2);

        // Draw character's eyes    
        int eyeRadius = 1;
        int eyeY = headY + 4;
        g.FillEllipse(Brushes.Black, headX + 2, eyeY, eyeRadius, eyeRadius);
        g.FillEllipse(Brushes.Black, headX + 6, eyeY, eyeRadius, eyeRadius);

        // Draw character's mouth    
        g.DrawArc(Pens.Black, headX + 2, eyeY + 3, 5, 3, 0, 180);

        // Draw character's arms    
        Pen armPen = new Pen(Brushes.Beige, 1);
        // Left arm to keyboard    
        g.DrawLine(armPen, bodyX, bodyY + 5, keyboardRect.X + 5, keyboardRect.Y + 2);
        // Right arm to mouse    
        g.DrawLine(armPen, bodyX + bodyWidth, bodyY + 5, mouseRect.X + 2, mouseRect.Y + 1);
        armPen.Dispose();
    }

    private void DrawSpeechBubble(Graphics g, string text, Point tailPoint)
    {
        // Set the speech bubble to fill the upper area of the screen  
        int bubbleWidth = backgroundBitmap.Width-5;
        int bubbleX = 0;
        int bubbleY = 0;
        // Set bubbleHeight to desired value, e.g., upper area excluding the tail  
        int bubbleHeight = backgroundBitmap.Height-50;

        // Draw speech bubble with rounded corners  
        Rectangle bubbleRect = new Rectangle(bubbleX, bubbleY, bubbleWidth, bubbleHeight);
        GraphicsPath bubblePath = RoundedRectangle(bubbleRect, 10);
        g.FillPath(Brushes.White, bubblePath);
        g.DrawPath(Pens.Black, bubblePath);

        // Draw speech bubble tail pointing to the worker  
        // Tail height is set to a maximum of 10 pixels  
        int tailHeight = 10;
        Point tailTip = new Point(tailPoint.X, tailPoint.Y - 5); // Tip of the tail near the worker's head  
        int tailBaseY = bubbleY + bubbleHeight; // Bottom of the bubble  

        // Adjust tail base Y to ensure the tail height does not exceed tailHeight  
        if (tailBaseY > tailTip.Y - tailHeight)
        {
            tailBaseY = tailTip.Y - tailHeight;
        }

        Point[] tailPoints = new Point[]
        {
        tailTip, // Tail tip near worker's head  
        new Point(tailTip.X - 7, tailBaseY), // Left point of tail  
        new Point(tailTip.X + 7, tailBaseY)  // Right point of tail  
        };
        g.FillPolygon(Brushes.White, tailPoints);
        g.DrawPolygon(Pens.Black, tailPoints);

        // Set text font and format  
        FontFamily fontFamily = new FontFamily("Arial");
        Font font = new Font(fontFamily, 12);

        RectangleF textRect = new RectangleF(bubbleX + 10, bubbleY + 10, bubbleWidth - 20, bubbleHeight - 20);

        // Adjust font size to fill the bubble  
        font = AdjustFontSizeToFill(g, text, fontFamily, textRect.Size);

        StringFormat format = new StringFormat();
        format.Alignment = StringAlignment.Center;
        format.LineAlignment = StringAlignment.Center;
        format.Trimming = StringTrimming.Word;
        format.FormatFlags = StringFormatFlags.LineLimit;

        // Draw the text within the bubble  
        g.DrawString(text, font, Brushes.Black, textRect, format);

        font.Dispose();
    }

    private Font AdjustFontSizeToFill(Graphics g, string text, FontFamily fontFamily, SizeF maxSize)
    {
        float minFontSize = 20f; // Minimum font size    
        float maxFontSize = 1000f; // Maximum font size    
        float fontSize = minFontSize;

        Font font = null;

        // Define a minimal acceptable difference to avoid infinite loop  
        const float epsilon = 0.1f;

        while ((maxFontSize - minFontSize) > epsilon)
        {
            float midFontSize = (minFontSize + maxFontSize) / 2f;

            if (font != null) font.Dispose();
            font = new Font(fontFamily, midFontSize, GraphicsUnit.Pixel);

            // Use TextRenderer to measure text size    
            Size textSize = TextRenderer.MeasureText(text, font);

            if (textSize.Height > maxSize.Height || textSize.Width > maxSize.Width)
            {
                maxFontSize = midFontSize;
            }
            else
            {
                minFontSize = midFontSize;
                fontSize = midFontSize; // Update fontSize to the latest fitting size  
            }
        }

        if (font != null) font.Dispose();
        return new Font(fontFamily, fontSize, GraphicsUnit.Pixel);
    }

    private GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        // Create a rectangle with rounded corners        
        GraphicsPath path = new GraphicsPath();

        int diameter = radius * 2;
        Size size = new Size(diameter, diameter);
        Rectangle arc = new Rectangle(bounds.Location, size);

        // Top-left corner        
        path.AddArc(arc, 180, 90);

        // Top-right corner        
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);

        // Bottom-right corner        
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);

        // Bottom-left corner        
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }

    private void GetNewsData()
    {
        try
        {
            // Check if the cached data is still valid (less than 1 hour old)      
            if (_newsHeadlines != null && _newsHeadlines.Count > 0 && (DateTime.Now - _lastNewsFetchTime).TotalMinutes < 60)
            {
                Debug.WriteLine("Using cached news data.");
                return;
            }

            Debug.WriteLine("Fetching news data from RSS feeds...");

            _newsHeadlines.Clear();

            HashSet<string> headlinesSet = new HashSet<string>(); // To avoid duplicates      

            foreach (var url in _rssFeedUrls)
            {
                try
                {
                    Debug.WriteLine($"Fetching news data from URL: {url}");

                    // Fetch and parse the RSS feed      
                    using (XmlReader reader = XmlReader.Create(url))
                    {
                        SyndicationFeed feed = SyndicationFeed.Load(reader);

                        foreach (var item in feed.Items)
                        {
                            if (headlinesSet.Add(item.Summary.Text))
                            {
                                _newsHeadlines.Add(item.Summary.Text);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error fetching from {url}: {ex.Message}");
                    // Continue with next feed      
                }
            }

            if (_newsHeadlines.Count > 0)
            {
                _lastNewsFetchTime = DateTime.Now;
                _currentNewsIndex = 0;
                Debug.WriteLine("Updated news data cache.");
            }
            else
            {
                throw new Exception("No news items found in the feeds.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetNewsData: {ex.Message}");
            // Handle errors (e.g., network issues, parsing errors)              
            _newsHeadlines = new List<string>();
        }
    }

    private string GetNextNewsHeadline()
    {
        if (_newsHeadlines == null || _newsHeadlines.Count == 0)
        {
            // If no news headlines, fall back to random phrase              
            return GetNewPhrase();
        }
        else
        {
            string headline = _newsHeadlines[_currentNewsIndex];
            _currentNewsIndex = (_currentNewsIndex + 1) % _newsHeadlines.Count;
            return headline;
        }
    }
}