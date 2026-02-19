using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MERSEL.Services.GibUserList.Application.Interfaces;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

/// <summary>
/// AWS S3 veya S3-uyumlu (MinIO) depolama üzerinde arşiv depolama.
/// Credentials ortam değişkenleri (AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY) veya IAM role ile sağlanır.
/// </summary>
public sealed class S3ArchiveStorage : IArchiveStorage, IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly ILogger<S3ArchiveStorage> _logger;

    public S3ArchiveStorage(IOptions<ArchiveStorageOptions> options, ILogger<S3ArchiveStorage> logger)
    {
        _logger = logger;
        var opts = options.Value;
        _bucketName = opts.BucketName;

        if (string.IsNullOrWhiteSpace(_bucketName))
            throw new InvalidOperationException(
                "ArchiveStorage:BucketName yapılandırılmamış. S3 provider için bucket adı zorunludur.");

        var config = new AmazonS3Config();
        if (!string.IsNullOrEmpty(opts.Region))
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(opts.Region);
        if (!string.IsNullOrEmpty(opts.ServiceUrl))
        {
            config.ServiceURL = opts.ServiceUrl;
            config.ForcePathStyle = true;
        }

        _s3Client = !string.IsNullOrEmpty(opts.AccessKey) && !string.IsNullOrEmpty(opts.SecretKey)
            ? new AmazonS3Client(opts.AccessKey, opts.SecretKey, config)
            : new AmazonS3Client(config);
    }

    public async Task SaveAsync(string fileName, Stream content, CancellationToken ct)
    {
        var contentType = fileName.EndsWith(".xml.gz", StringComparison.OrdinalIgnoreCase)
            ? "application/gzip"
            : "application/zip";

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = fileName,
            InputStream = content,
            ContentType = contentType
        };

        await _s3Client.PutObjectAsync(request, ct);
        _logger.LogInformation("Arşiv dosyası S3'e yüklendi: s3://{Bucket}/{Key}", _bucketName, fileName);
    }

    public async Task<Stream?> GetAsync(string fileName, CancellationToken ct)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(_bucketName, fileName, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ArchiveFileInfo>> ListAsync(string? prefix = null, CancellationToken ct = default)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = _bucketName,
            Prefix = prefix
        };
        var result = new List<ArchiveFileInfo>();

        ListObjectsV2Response response;
        do
        {
            response = await _s3Client.ListObjectsV2Async(request, ct);
            foreach (var obj in response.S3Objects)
            {
                if (obj.Key.EndsWith(".xml.gz", StringComparison.OrdinalIgnoreCase)
                    || obj.Key.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    result.Add(new ArchiveFileInfo(obj.Key, obj.Size ?? 0, obj.LastModified ?? DateTime.Now));
            }
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);

        return result.OrderByDescending(f => f.CreatedAt).ToList();
    }

    public async Task DeleteAsync(string fileName, CancellationToken ct)
    {
        await _s3Client.DeleteObjectAsync(_bucketName, fileName, ct);
        _logger.LogInformation("Arşiv dosyası S3'ten silindi: s3://{Bucket}/{Key}", _bucketName, fileName);
    }

    public void Dispose() => _s3Client.Dispose();
}
