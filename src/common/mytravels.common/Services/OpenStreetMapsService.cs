using Flurl;
using Flurl.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using System.Globalization;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;

namespace mytravels.common.Services
{
    public class OpenStreetMapsService : IMapsService
    {
        private const string DefaultBaseUrl = "https://nominatim.openstreetmap.org";
        private const string DefaultUserAgent = "mytravels/1.0";

        private readonly IConfiguration _configuration;
        private readonly int _maxRetryAttempts = 2;
        private readonly AsyncRetryPolicy _policy;

        public OpenStreetMapsService(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _policy = Policy
               .Handle<FlurlHttpTimeoutException>()
               .Or<FlurlHttpException>()
               .WaitAndRetryAsync(_maxRetryAttempts, i => TimeSpan.FromSeconds(Math.Pow(2, i)));
        }

        public async Task<string> GetAddressAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            string lat = latitude.ToString(CultureInfo.InvariantCulture);
            string lon = longitude.ToString(CultureInfo.InvariantCulture);

            return await _policy.ExecuteAsync(async () =>
            {
                string response = await BuildRequest("reverse")
                                       .SetQueryParam("lat", lat)
                                       .SetQueryParam("lon", lon)
                                       .GetStringAsync(cancellationToken: cancellationToken);

                JObject json = JObject.Parse(response);
                if (json["error"] != null)
                    throw new InvalidOperationException($"OpenStreetMap geocode failed: {json["error"]}");

                string address = json["display_name"]?.ToString()
                    ?? throw new InvalidOperationException("display_name missing in response");
                return address;
            });
        }

        public async Task<List<PlaceDto>> SearchPlacesAsync(string query, int limit, CancellationToken cancellationToken)
        {
            return await _policy.ExecuteAsync(async () =>
            {
                string response = await BuildRequest("search")
                                       .SetQueryParam("q", query)
                                       .SetQueryParam("limit", limit)
                                       .GetStringAsync(cancellationToken: cancellationToken);

                JArray results = JArray.Parse(response);
                return results.Select(result => new PlaceDto
                {
                    FormattedAddress = result["display_name"]?.ToString(),
                    Latitude = ParseCoordinate(result["lat"]?.ToString()),
                    Longitude = ParseCoordinate(result["lon"]?.ToString())
                })
                .Where(place => !string.IsNullOrEmpty(place.FormattedAddress))
                .ToList();
            });
        }

        private IFlurlRequest BuildRequest(string pathSegment)
        {
            string baseUrl = _configuration.GetValue<string>("OpenStreetMapsUrl") ?? DefaultBaseUrl;
            string userAgent = _configuration.GetValue<string>("OpenStreetMapsUserAgent") ?? DefaultUserAgent;

            return baseUrl.AppendPathSegment(pathSegment)
                          .SetQueryParam("format", "jsonv2")
                          .WithHeader("User-Agent", userAgent);
        }

        // Nominatim returns coordinates as strings, always in the invariant format.
        private static double ParseCoordinate(string value)
            => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;
    }
}
