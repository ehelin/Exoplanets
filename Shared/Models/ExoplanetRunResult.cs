namespace Shared.Models
{
    public class ExoplanetRunResult
    {
        public int Fetched { get; set; }
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int ValidIncoming { get; set; }
        public int Existing { get; set; }
    }
}
