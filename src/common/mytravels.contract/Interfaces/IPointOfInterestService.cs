using Microsoft.AspNetCore.Http;
using mytravels.contract.Dtos;
using mytravels.contract.Responses;

namespace mytravels.contract.Interfaces;

public interface IPointOfInterestService
{
    Task<List<GetPointOfInterestResponse>> GetAsync(CancellationToken cancellationToken);
    Task<List<GetPointOfInterestResponse>> GetAsync(string tagName, CancellationToken cancellationToken);
    Task<int> SaveFileAsPointOfInsterestAsync(IFormFile file, CancellationToken cancellationToken);
    Task<int> SaveFileAsPointOfInsterestAsync(IFormFile file, SaveCoordinatesDto coordinates, CancellationToken cancellationToken);
    Task<int> UpdatePointOfInterestAsync(IFormFile file, string pointOfInterestKey, CancellationToken cancellationToken);
    Task<string> GetImageAsync(int id, CancellationToken cancellationToken);
}