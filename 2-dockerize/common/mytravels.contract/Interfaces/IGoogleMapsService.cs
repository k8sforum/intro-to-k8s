namespace mytravels.contract.Interfaces;

public interface IGoogleMapsService
{
    Task<string> GetAddressAsync(double latitude, double longitude, CancellationToken cancellationToken);
}