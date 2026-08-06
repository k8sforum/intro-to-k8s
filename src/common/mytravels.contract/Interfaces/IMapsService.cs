using mytravels.contract.Dtos;

namespace mytravels.contract.Interfaces;

public interface IMapsService
{
    Task<string> GetAddressAsync(double latitude, double longitude, CancellationToken cancellationToken);
    Task<List<PlaceDto>> SearchPlacesAsync(string query, int limit, CancellationToken cancellationToken);
}
