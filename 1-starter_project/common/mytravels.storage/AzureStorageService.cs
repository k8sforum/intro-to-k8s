using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using mytravels.contract.CustomException;
using mytravels.contract.Interfaces;

namespace mytravels.common.Services;

[ExcludeFromCodeCoverage]
public class AzureStorageService : IObjectStorageService
{
    private readonly string _connectionString;

    public AzureStorageService
    (
        IConfiguration configuration
    )
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        _connectionString = configuration.GetValue<string>("StorageAccountConnectionString") ?? throw new KeyNotFoundException("StorageAccountConnectionString");
    }

    public async Task SaveObjectAsync(string bucketName, string objectName, Stream stream, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new RequiredParameterNotFoundException(nameof(bucketName));
        }

        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new RequiredParameterNotFoundException(nameof(objectName));
        }

        BlobServiceClient blobServiceClient = new BlobServiceClient(_connectionString);
        // Create a bucketName client from the blob service client
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(bucketName);
        // Create the bucketName if it doesn't already exist
        containerClient.CreateIfNotExists(cancellationToken: cancellationToken);
        // Create a block blob client and upload the file
        BlobClient blobClient = containerClient.GetBlobClient(objectName);
        await blobClient.UploadAsync(stream, true, cancellationToken: cancellationToken);
    }

    public async Task<string> SaveBase64StringAsync(string base64string, string bucketName, string extensions, CancellationToken cancellationToken)
    {
        string objectName = $"{Guid.NewGuid().ToString("N")}{extensions}";
        byte[] imageBytes = Convert.FromBase64String(base64string);
        using (var stream = new MemoryStream(imageBytes))
        {
            stream.Position = 0;
            await SaveObjectAsync(bucketName, objectName, stream, cancellationToken);
        }
        return objectName;
    }

    public async Task<bool> ObjectExistsAsync(string bucketName, string objectName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new RequiredParameterNotFoundException(nameof(bucketName));
        }

        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new RequiredParameterNotFoundException(nameof(objectName));
        }

        BlobServiceClient blobServiceClient = new BlobServiceClient(_connectionString);
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(bucketName);
        bool containerExists = await containerClient.ExistsAsync(cancellationToken);
        if (!containerExists) return false;
        BlobClient blobClient = containerClient.GetBlobClient(objectName);
        return await blobClient.ExistsAsync();
    }

    public async Task<string> SaveObjectAsync(IFormFile file, string bucketName, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(file.FileName);
        string objectName = $"{Guid.NewGuid().ToString("N")}{extension}";

        using (var stream = new MemoryStream())
        {
            await file.CopyToAsync(stream, cancellationToken: cancellationToken);
            stream.Position = 0;
            await SaveObjectAsync(bucketName, objectName, stream, cancellationToken);
        }

        return objectName;
    }

    public async Task SaveObjectAsync<T>(T obj, string bucketName, string objectName, CancellationToken cancellationToken) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new RequiredParameterNotFoundException(nameof(bucketName));
        }

        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new RequiredParameterNotFoundException(nameof(objectName));
        }

        if (obj == default)
        {
            throw new RequiredParameterNotFoundException(nameof(obj));
        }

        string json = JsonConvert.SerializeObject(obj, new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        });

        byte[] byteArray = Encoding.UTF8.GetBytes(json);
        using var stream = new MemoryStream(byteArray);
        await this.SaveObjectAsync(bucketName, objectName, stream, cancellationToken);
    }

    public async Task<string> GetBase64Async(string bucketName, string objectName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new RequiredParameterNotFoundException(nameof(bucketName));
        }

        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new RequiredParameterNotFoundException(nameof(objectName));
        }

        BlobContainerClient containerClient = new BlobContainerClient(_connectionString, bucketName);
        if (!await containerClient.ExistsAsync(cancellationToken)) return string.Empty;
        BlobClient blobClient = containerClient.GetBlobClient(objectName);
        if (!await blobClient.ExistsAsync(cancellationToken)) return string.Empty;

        Response<BlobDownloadResult> result = await blobClient.DownloadContentAsync(cancellationToken: cancellationToken);
        byte[] imageData = result.Value.Content.ToArray();
        return Convert.ToBase64String(imageData, 0, imageData.Length);
    }

    public async Task<T> GetObjectAsync<T>(string bucketName, string objectName, CancellationToken cancellationToken) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new RequiredParameterNotFoundException(nameof(bucketName));
        }

        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new RequiredParameterNotFoundException(nameof(objectName));
        }

        T t = new T();
        Stream stream = await GetStreamAsync(bucketName, objectName, default);
        using var reader = new StreamReader(stream);
        string jsonContent = await reader.ReadToEndAsync();
        if (!string.IsNullOrEmpty(jsonContent))
        {
            T? deserialized = JsonConvert.DeserializeObject<T>(jsonContent);
            if (deserialized != null)
            {
                t = deserialized;
            }
        }
        return t;
    }

    public async Task<Stream> GetStreamAsync(string bucketName, string objectName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new RequiredParameterNotFoundException(nameof(bucketName));
        }

        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new RequiredParameterNotFoundException(nameof(objectName));
        }

        BlobContainerClient containerClient = new BlobContainerClient(_connectionString, bucketName);
        if (!await containerClient.ExistsAsync(cancellationToken)) throw new EntityNotFoundException(bucketName);
        BlobClient blobClient = containerClient.GetBlobClient(objectName);
        if (!await blobClient.ExistsAsync(cancellationToken)) throw new EntityNotFoundException(objectName);

        Response<BlobDownloadResult> result = await blobClient.DownloadContentAsync(cancellationToken);
        return result.Value.Content.ToStream();
    }

    public async Task<List<string>> ListObjectsAsync(string bucketName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new RequiredParameterNotFoundException(nameof(bucketName));
        }

        BlobServiceClient blobServiceClient = new BlobServiceClient(_connectionString);
        // Create a bucketName client from the blob service client
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(bucketName);
        // Create the bucketName if it doesn't already exist
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        List<string> fileNames = new();
        await foreach (var blobItem in containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            fileNames.Add(blobItem.Name);
        }
        return fileNames;
    }

    public async Task<List<string>> ListBucketsAsync(CancellationToken cancellationToken)
    {
        BlobServiceClient blobServiceClient = new BlobServiceClient(_connectionString);
        List<string> containerNames = new();
        await foreach (BlobContainerItem container in blobServiceClient.GetBlobContainersAsync())
        {
            containerNames.Add(container.Name);
        }
        return containerNames;
    }

    public async Task RemoveObjectAsync(string bucketName, string objectName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new RequiredParameterNotFoundException(nameof(bucketName));
        }

        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new RequiredParameterNotFoundException(nameof(objectName));
        }

        BlobContainerClient containerClient = new BlobContainerClient(_connectionString, bucketName);
        if (await containerClient.ExistsAsync(cancellationToken))
        {
            BlobClient blobClient = containerClient.GetBlobClient(objectName);
            await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
    }
}
