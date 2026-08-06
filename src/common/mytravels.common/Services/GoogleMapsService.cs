using Flurl;
using Flurl.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using System.Globalization;
using System.Linq;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;

namespace mytravels.common.Services
{
    public class GoogleMapsService : IMapsService
    {
        private readonly IConfiguration _configuration;
        private readonly int _maxRetryAttempts = 2;
        private readonly AsyncRetryPolicy _policy;

        public GoogleMapsService(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _policy = Policy
               .Handle<FlurlHttpTimeoutException>()
               .Or<FlurlHttpException>()
               .WaitAndRetryAsync(_maxRetryAttempts, i => TimeSpan.FromSeconds(Math.Pow(2, i)));
        }

        public async Task<string> GetAddressAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            string latlong = $"{latitude.ToString(CultureInfo.InvariantCulture)},{longitude.ToString(CultureInfo.InvariantCulture)}";

            return await _policy.ExecuteAsync(async () =>
            {
                JToken[] results = await GeocodeAsync("latlng", latlong, cancellationToken);
                return results[0]["formatted_address"]?.ToString()
                    ?? throw new InvalidOperationException("formatted_address missing in response");
            });
        }

        public async Task<List<PlaceDto>> SearchPlacesAsync(string query, int limit, CancellationToken cancellationToken)
        {
            return await _policy.ExecuteAsync(async () =>
            {
                JToken[] results = await GeocodeAsync("address", query, cancellationToken);
                return results.Take(limit)
                              .Select(result => new PlaceDto
                              {
                                  FormattedAddress = result["formatted_address"]?.ToString(),
                                  Latitude = result["geometry"]?["location"]?["lat"]?.Value<double>() ?? 0,
                                  Longitude = result["geometry"]?["location"]?["lng"]?.Value<double>() ?? 0
                              })
                              .Where(place => !string.IsNullOrEmpty(place.FormattedAddress))
                              .ToList();
            });
        }

        private async Task<JToken[]> GeocodeAsync(string queryParamName, string queryParamValue, CancellationToken cancellationToken)
        {
            string apiKey = _configuration.GetValue<string>("GoogleApiKey");
            string baseUrl = _configuration.GetValue<string>("GoogleMapsUrl");

            string response = await baseUrl.AppendPathSegment("maps")
                                           .AppendPathSegment("api")
                                           .AppendPathSegment("geocode")
                                           .AppendPathSegment("json")
                                           .SetQueryParam(queryParamName, queryParamValue)
                                           .SetQueryParam("key", apiKey)
                                           .GetStringAsync(cancellationToken: cancellationToken);

            JObject json = JObject.Parse(response);
            string status = json["status"]?.ToString();
            if (status != "OK" || json["results"] == null || !json["results"].Any())
                throw new InvalidOperationException($"Google Maps geocode failed: {status}");

            return json["results"].ToArray();
        }
    }
}
