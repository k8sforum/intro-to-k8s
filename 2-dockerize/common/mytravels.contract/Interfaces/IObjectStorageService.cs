using Microsoft.AspNetCore.Http;

namespace mytravels.contract.Interfaces;

public interface IObjectStorageService
{
    Task<string> GetBase64Async(string bucketName, string objectName, CancellationToken cancellationToken);
    Task<T> GetObjectAsync<T>(string bucketName, string objectName, CancellationToken cancellationToken) where T : class, new();
    Task<Stream> GetStreamAsync(string bucketName, string objectName, CancellationToken cancellationToken);
    Task<List<string>> ListObjectsAsync(string bucketName, CancellationToken cancellationToken);
    Task<List<string>> ListBucketsAsync(CancellationToken cancellationToken);
    Task<bool> ObjectExistsAsync(string bucketName, string objectName, CancellationToken cancellationToken);
    Task RemoveObjectAsync(string bucketName, string objectName, CancellationToken cancellationToken);
    Task<string> SaveObjectAsync(IFormFile file, string bucketName, CancellationToken cancellationToken);
    Task SaveObjectAsync(string bucketName, string objectName, Stream stream, CancellationToken cancellationToken);
    Task SaveObjectAsync<T>(T obj, string containerName, string objectName, CancellationToken cancellationToken) where T : class, new();
    Task<string> SaveBase64StringAsync(string base64string, string bucketName, string extensions, CancellationToken cancellationToken);
}

