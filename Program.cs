namespace TravelTimesDownloader
{
    using System;
    using System.Linq;
    using HtmlAgilityPack;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using System.Globalization;
    using PetaPoco;
    using System.Data.SqlClient;

    /// <summary>
    /// Contains the program entry point.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Defines the program entry point. 
        /// </summary>
        /// <param name="args">An array of <see cref="T:System.String"/> containing command line parameters.</param>
        private static void Main(string[] args)
        {
            Database db = new Database("TravelTimeDb");
            db.OpenSharedConnection();

            var hw = new HtmlWeb();
            string url = @"http://www.wsdot.wa.gov/traffic/seattle/traveltimes/";
            var doc = hw.Load(url);

            var docNode = doc.DocumentNode;
            var asOfString = docNode.DescendantNodes().Where(n =>
                n.Attributes.Any(a => a.Name == "class" && a.Value == "sansserif"))
                .First().InnerText.Substring("Travel times as of ".Length).Replace(".", "").Replace(",", "").Trim();

            var observedTime = DateTime.ParseExact(asOfString, "h:mm tt dddd MMMM d yyyy", CultureInfo.InvariantCulture);

            var tableRows = docNode.SelectNodes("//tr");
            
            TravelTime last = null;

            foreach (var row in tableRows.Skip(1))
            {
                var cells = row.SelectNodes(".//td");

                int i = 0;

                var time = new TravelTime();
                time.TimeObserved = observedTime;

                if (cells.Count > 5)
                {
                    time.Roads = string.Join(", ", cells[i++]
                        .DescendantNodes()
                        .Where(n => n.Name.Equals("img", StringComparison.InvariantCultureIgnoreCase))
                        .Select(n => n.Attributes["alt"].Value));
                }

                var name = Regex.Replace(cells[i++].InnerText.Trim(), "\\s+", " ");
                if (name.StartsWith("Via"))
                {
                    time.IsExpressLane = true;
                    time.FromCity = last.FromCity;
                    time.ToCity = last.ToCity;
                    time.Roads = last.Roads;
                }
                else
                {
                    var split = name.Split(new[] { " to " }, StringSplitOptions.RemoveEmptyEntries);
                    time.FromCity = split[0].Trim();
                    time.ToCity = split[1].Trim();
                }

                double distance = 0.0;
                double.TryParse(cells[i++].InnerText, out distance);
                time.Distance = (int)(distance * 10);

                int average = 0;
                int.TryParse(cells[i++].InnerText, out average);
                time.AverageTime = average;

                var current = ProcessCell(cells[i++]);
                time.CurrentTime = current.Item1;
                time.CurrentRating = current.Item2;

                var hov = ProcessCell(cells[i++]);
                time.HovTime = current.Item1;
                time.HovRating = current.Item2;
                
                last = time;

                try
                {
                    db.Insert("TravelTime", "Id", time);
                }
                catch (SqlException ex)
                {
                    Console.WriteLine("Failed to insert time: " + ex.Message);
                }
            }
            db.CloseSharedConnection();
        }

        // Define other methods and classes here
        private static Tuple<int, Rating> ProcessCell(HtmlNode cell)
        {
            var text = cell.InnerText;
            int value;
            string color = null;
            Rating rating = Rating.None;
            if (int.TryParse(text, out value))
            {
                color = cell.SelectSingleNode(".//font").Attributes["color"].Value.ToUpper();
                if (color == "#006600") rating = Rating.Good;
                else if (color == "#0000FF") rating = Rating.Average;
                else if (color == "#FF0000") rating = Rating.Bad;
            }
            return Tuple.Create(value, rating);
        }

        public enum Rating
        {
            None,
            Bad,
            Average,
            Good
        }

        public class TravelTime
        {
            public DateTime TimeObserved { get; set; }
            public string Roads { get; set; }
            public string FromCity { get; set; }
            public string ToCity { get; set; }
            public bool IsExpressLane { get; set; }
            public int Distance { get; set; }
            public int AverageTime { get; set; }
            public int CurrentTime { get; set; }
            public Rating CurrentRating { get; set; }
            public int HovTime { get; set; }
            public Rating HovRating { get; set; }
        }
    }
}
