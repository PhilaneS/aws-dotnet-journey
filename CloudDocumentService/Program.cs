using System.Collections.Concurrent;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CloudDocumentService.Contracts;
using CloudDocumentService.Infrastructure;

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

builder.Services.AddScoped<IDocumentStorage, S3DocumentStorage>();

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
    async (
        IFormFile file,
        IDocumentStorage storage,
        CancellationToken cancellationToken) =>
    {
        if (file.Length == 0)
            return Results.BadRequest("File is empty.");

        await using var stream = file.OpenReadStream();

        var key = await storage.UploadAsync(
            stream,
            file.FileName,
            file.ContentType,
            cancellationToken);

        return Results.Ok(new
        {
            FileName = file.FileName,
            Bucket = "documents",
            Key = key
        });
    })
    .DisableAntiforgery();
    
app.MapGet("/documents/download",
    async (
        string key,
        IDocumentStorage storage,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(key))
            return Results.BadRequest("Object key is required.");

        try
        {
            var stream = await storage.DownloadAsync(
                key,
                cancellationToken);

            return Results.File(
                stream,
                "application/octet-stream",
                Path.GetFileName(key));
        }
        catch (AmazonS3Exception ex)
            when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound("File not found.");
        }
    });
app.MapDelete("/documents/{id:guid}", (Guid id) =>
    documents.TryRemove(id, out _)
        ? Results.NoContent()
        : Results.NotFound());
app.MapDelete("/documents/storage",
    async (
        string key,
        IDocumentStorage storage,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(key))
            return Results.BadRequest("Object key is required.");

        await storage.DeleteAsync(key, cancellationToken);

        return Results.NoContent();
    });
app.Run();

record CreateDocument(string FileName);

record Document(
    Guid Id,
    string FileName,
    DateTimeOffset CreatedAt);