using System.Collections.Concurrent;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
// builder.Services.AddOpenApi();


builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var config = new AmazonS3Config
    {
        ServiceURL = builder.Configuration["S3:ServiceUrl"]
            ?? "http://localhost:8333",
        ForcePathStyle = true
    };

    var accessKey = builder.Configuration["S3:AccessKey"]
        ?? "admin";

    var secretKey = builder.Configuration["S3:SecretKey"]
        ?? "secret";

    return new AmazonS3Client(
        new BasicAWSCredentials(accessKey, secretKey),
        config);
});


var app = builder.Build();

// Configure the HTTP request pipeline.
// if (app.Environment.IsDevelopment())
// {
//     app.MapOpenApi();
// }

var documents = new ConcurrentDictionary<Guid, Document>();

app.MapGet("/health", () =>
    Results.Ok(new { status = "Healthy", service = "CloudDocumentService" }));

app.MapGet("/documents", () =>
    Results.Ok(documents.Values));

app.MapGet("/documents/{id:guid}", (Guid id) =>
    documents.TryGetValue(id, out var document)
        ? Results.Ok(document)
        : Results.NotFound());

app.MapPost("/documents", (CreateDocument request) =>
{
    if (string.IsNullOrWhiteSpace(request.FileName))
        return Results.BadRequest("FileName is required.");

    var document = new Document(
        Guid.NewGuid(),
        request.FileName,
        DateTimeOffset.UtcNow);

    documents[document.Id] = document;

    return Results.Created($"/documents/{document.Id}", document);
});
app.MapPost("/documents/upload",
    async (IFormFile file, IAmazonS3 s3) =>
{
    if (file.Length == 0)
        return Results.BadRequest("File is empty.");

    var key = $"uploads/{Guid.NewGuid()}/{Path.GetFileName(file.FileName)}";

    await using var stream = file.OpenReadStream();

    await s3.PutObjectAsync(new PutObjectRequest
    {
        BucketName = "documents",
        Key = key,
        InputStream = stream,
        ContentType = file.ContentType
    });

    return Results.Ok(new
    {
        FileName = file.FileName,
        Bucket = "documents",
        Key = key
    });
})
.DisableAntiforgery();

app.MapDelete("/documents/{id:guid}", (Guid id) =>
    documents.TryRemove(id, out _)
        ? Results.NoContent()
        : Results.NotFound());

app.Run();

record CreateDocument(string FileName);

record Document(
    Guid Id,
    string FileName,
    DateTimeOffset CreatedAt);