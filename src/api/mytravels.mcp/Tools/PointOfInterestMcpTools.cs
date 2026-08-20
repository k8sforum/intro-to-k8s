using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;

namespace mytravels.mcp.Tools;

[McpServerToolType]
public class PointOfInterestMcpTools
{
    private readonly IPointOfInterestService _service;

    public PointOfInterestMcpTools(IPointOfInterestService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    [McpServerTool(Name = "upload_photo")]
    [Description("Uploads a photo as a new point of interest, reading GPS coordinates from the photo's EXIF metadata. Fails if the photo carries no GPS metadata — use upload_photo_with_coordinates instead in that case.")]
    public async Task<SaveEntityResponseDto> UploadPhotoAsync(
        [Description("Base64-encoded raw bytes of the image file.")] string fileContentBase64,
        [Description("Original file name, including extension, e.g. 'beach.jpg'.")] string fileName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileContentBase64)) throw new McpException($"'{nameof(fileContentBase64)}' is required.");
        if (string.IsNullOrWhiteSpace(fileName)) throw new McpException($"'{nameof(fileName)}' is required.");

        IFormFile file = ToFormFile(fileContentBase64, fileName);

        try
        {
            int id = await _service.SaveFileAsPointOfInsterestAsync(file, cancellationToken);
            return new SaveEntityResponseDto { Id = id };
        }
        catch (InvalidOperationException ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "upload_photo_with_coordinates")]
    [Description("Uploads a photo as a new point of interest using explicit latitude/longitude, for photos that carry no GPS metadata.")]
    public async Task<SaveEntityResponseDto> UploadPhotoWithCoordinatesAsync(
        [Description("Base64-encoded raw bytes of the image file.")] string fileContentBase64,
        [Description("Original file name, including extension, e.g. 'beach.jpg'.")] string fileName,
        [Description("Latitude, -90 to 90.")] double latitude,
        [Description("Longitude, -180 to 180.")] double longitude,
        [Description("Optional human-readable address to store alongside the coordinates.")] string formattedAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileContentBase64)) throw new McpException($"'{nameof(fileContentBase64)}' is required.");
        if (string.IsNullOrWhiteSpace(fileName)) throw new McpException($"'{nameof(fileName)}' is required.");

        var coordinates = new SaveCoordinatesDto
        {
            Latitude = latitude,
            Longitude = longitude,
            FormattedAddress = formattedAddress
        };

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(coordinates, new ValidationContext(coordinates), validationResults, validateAllProperties: true))
        {
            string errors = string.Join("; ", validationResults.Select(r => r.ErrorMessage));
            throw new McpException($"Invalid coordinates: {errors}");
        }

        IFormFile file = ToFormFile(fileContentBase64, fileName);
        int id = await _service.SaveFileAsPointOfInsterestAsync(file, coordinates, cancellationToken);
        return new SaveEntityResponseDto { Id = id };
    }

    private static IFormFile ToFormFile(string base64Content, string fileName)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64Content);
        }
        catch (FormatException ex)
        {
            throw new McpException($"'{nameof(base64Content)}' is not valid base64.", ex);
        }

        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, stream.Length, "image", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };
    }
}
