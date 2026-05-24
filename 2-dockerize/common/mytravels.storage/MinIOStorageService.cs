using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using mytravels.common.Config;
using mytravels.contract.CustomException;
using mytravels.contract.Interfaces;

namespace mytravels.storage;

[ExcludeFromCodeCoverage]
public class MinIOStorageService : IObjectStorageService
{
    private readonly IMinioClient _minioClient;

    public MinIOStorageService(IOptions<MinIOConfig> options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        MinIOConfig config = options.Value;

        _minioClient = new MinioClient()
                          .WithEndpoint(config.Endpoint)
                          .WithCredentials(config.AccessKey, config.SecretKey)
                          .WithSSL(config.UseSSL)
                          .Build();
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

        bool bucketExists = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName), cancellationToken);
        if (!bucketExists) return false;

        try
        {
            var stat = await _minioClient.StatObjectAsync(
                new StatObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectName),
                cancellationToken);
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
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

        await CreateBucketIfNotExistsAsync(bucketName, cancellationToken);

        await _minioClient.PutObjectAsync(new PutObjectArgs()
                          .WithBucket(bucketName)
                          .WithObject(objectName)
                          .WithStreamData(stream)
                          .WithObjectSize(stream.Length)
                          .WithContentType("application/octet-stream"), cancellationToken);
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

        string objJson = JsonConvert.SerializeObject(obj, new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        });

        byte[] byteArray = Encoding.UTF8.GetBytes(objJson);
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

        await CreateBucketIfNotExistsAsync(bucketName, cancellationToken);

        try
        {
            using var ms = new MemoryStream();

            await _minioClient.GetObjectAsync(
                new GetObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectName)
                    .WithCallbackStream(stream =>
                    {
                        stream.CopyTo(ms);
                    }),
                cancellationToken);

            byte[] data = ms.ToArray();
            return Convert.ToBase64String(data);
        }
        catch (ObjectNotFoundException)
        {
            return string.Empty;
        }
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

        bool bucketExists = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName), cancellationToken);
        if (!bucketExists) throw new EntityNotFoundException(bucketName);

        try
        {
            var ms = new MemoryStream();
            await _minioClient.GetObjectAsync(
                new GetObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectName)
                    .WithCallbackStream(stream =>
                    {
                        stream.CopyTo(ms);
                    }),
                cancellationToken);

            ms.Position = 0;
            return ms;
        }
        catch (ObjectNotFoundException)
        {
            throw new EntityNotFoundException(objectName);
        }
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
            if (deserialized is not null)
            {
                t = deserialized;
            }
        }
        return t;
    }

    //https://github.com/minio/minio-dotnet/blob/master/Minio.Examples/Cases/ListObjects.cs
    public async Task<List<string>> ListObjectsAsync(string bucketName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new RequiredParameterNotFoundException(nameof(bucketName));
        }

        await CreateBucketIfNotExistsAsync(bucketName, cancellationToken);
        string? prefix = null;
        bool recursive = true;
        bool versions = false;
        List<string> fileNames = new();

        try
        {
            Console.WriteLine("Running example for API: ListObjectsAsync");
            var listArgs = new ListObjectsArgs()
                .WithBucket(bucketName)
                .WithPrefix(prefix)
                .WithRecursive(recursive)
                .WithVersions(versions);

            await foreach (Item item in _minioClient.ListObjectsEnumAsync(listArgs).ConfigureAwait(false))
            {
                fileNames.Add(item.Key);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Bucket]  Exception: {e}");
        }

        return fileNames;
    }

    public async Task<List<string>> ListBucketsAsync(CancellationToken cancellationToken)
    {
        var buckets = await _minioClient.ListBucketsAsync();
        return buckets.Buckets.Select(x => x.Name).ToList();
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

        string? versionId = null;

        try
        {
            var args = new RemoveObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectName);
            var versions = "";
            if (!string.IsNullOrEmpty(versionId))
            {
                args = args.WithVersionId(versionId);
                versions = ", with version ID " + versionId + " ";
            }
            Console.WriteLine("Running example for API: RemoveObjectAsync");
            await _minioClient.RemoveObjectAsync(args).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Bucket-Object]  Exception: {e}");
        }
    }

    private async Task CreateBucketIfNotExistsAsync(string bucketName, CancellationToken cancellationToken)
    {
        BucketExistsArgs args = new BucketExistsArgs().WithBucket(bucketName);
        bool found = await _minioClient.BucketExistsAsync(args, cancellationToken);
        if (!found)
        {
            await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName), cancellationToken);
        }
    }
}
