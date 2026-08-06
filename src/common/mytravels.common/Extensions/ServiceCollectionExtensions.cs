using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using mytravels.common.Services;
using mytravels.contract.Interfaces;

namespace mytravels.common.Extensions
{
    public static class ServiceCollectionExtensions
    {
        private const string UnsetGoogleApiKey = "<YOUR_GOOGLE_API_KEY>";

        /// <summary>
        /// Registers the maps provider used for geocoding, falling back to OpenStreetMap when no Google API key is configured.
        /// </summary>
        public static IServiceCollection AddMapsService(this IServiceCollection services, IConfiguration configuration)
        {
            string googleApiKey = configuration.GetValue<string>("GoogleApiKey");

            if (string.IsNullOrEmpty(googleApiKey) || googleApiKey == UnsetGoogleApiKey)
            {
                return services.AddTransient<IMapsService, OpenStreetMapsService>();
            }

            return services.AddTransient<IMapsService, GoogleMapsService>();
        }
    }
}
