using Amazon.S3;
using Amazon.S3.Model;
using CloudDocumentService.Contracts;

namespace CloudDocumentService.Infrastructure;

public sealed class S3DocumentStorage : IDocumentStorage
{
    private const string BucketName = "documents";

    private readonly IAmazonS3 _s3;

    public S3DocumentStorage(IAmazonS3 s3)
    {
        _s3 = s3;
    }

    public async Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var safeFileName = Path.GetFileName(fileName);

        var key = $"uploads/{Guid.NewGuid()}/{safeFileName}";

        await _s3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = BucketName,
                Key = key,
                InputStream = content,
                ContentType = contentType
            },
            cancellationToken);

        return key;
    }

    public async Task<Stream> DownloadAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var response = await _s3.GetObjectAsync(
            BucketName,
            key,
            cancellationToken);

        // Copy into an independent stream so the S3 response can be disposed.
        using (response)
        {
            var stream = new MemoryStream();

            await response.ResponseStream.CopyToAsync(
                stream,
                cancellationToken);

            stream.Position = 0;

            return stream;
        }
    }

    public async Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await _s3.DeleteObjectAsync(
            BucketName,
            key,
            cancellationToken);
    }
}