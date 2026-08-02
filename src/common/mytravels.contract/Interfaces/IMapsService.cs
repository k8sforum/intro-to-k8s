namespace mytravels.contract.Interfaces;

public interface IMapsService
{
    Task<string> GetAddressAsync(double latitude, double longitude, CancellationToken cancellationToken);
}