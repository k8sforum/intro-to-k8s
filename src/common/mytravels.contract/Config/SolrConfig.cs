namespace mytravels.contract.Config
{
    public class SolrConfig
    {
        public string Url { get; set; } = "http://localhost:8983";
        public string Collection { get; set; } = "mytravels-pois";
        public int TimeoutSeconds { get; set; } = 30;
        public int BatchSize { get; set; } = 500;
    }
}
