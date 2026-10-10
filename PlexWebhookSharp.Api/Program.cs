using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new SemaphoreSlim(1, 1));
var webhookSecret = builder.Configuration["PlexWebhook:Secret"];
if (string.IsNullOrWhiteSpace(webhookSecret))
{
    throw new InvalidOperationException("Configure the PlexWebhook:Secret setting before starting the service.");
}

var app = builder.Build();
const string LogFileName = "plex-media-play-webhooks.log";
const int MaxRequestBodySize = 1024 * 1024;
var logWriteTimeout = TimeSpan.FromSeconds(10);

app.MapGet("/", () => Results.Ok("Plex webhook service is running."));

app.MapPost("/webhooks/plex", async (HttpContext context, ILogger<Program> logger, SemaphoreSlim logWriteLock) =>
{
    if (!context.Request.Headers.TryGetValue("X-Plex-Webhook-Secret", out var providedSecret)
        || !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(providedSecret.ToString()),
            Encoding.UTF8.GetBytes(webhookSecret)))
    {
        return Results.Unauthorized();
    }

    if (context.Request.ContentLength > MaxRequestBodySize)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

    context.Request.EnableBuffering(bufferThreshold: 32 * 1024, bufferLimit: MaxRequestBodySize + 1);
    using var memoryStream = new MemoryStream();
    var buffer = new byte[81920];
    while (true)
    {
        var remainingBytes = MaxRequestBodySize + 1 - (int)memoryStream.Length;
        var bytesRead = await context.Request.Body.ReadAsync(
            buffer.AsMemory(0, Math.Min(buffer.Length, remainingBytes)),
            context.RequestAborted);
        if (bytesRead == 0)
        {
            break;
        }

        if (memoryStream.Length + bytesRead > MaxRequestBodySize)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), context.RequestAborted);
    }

    var rawBodyBytes = memoryStream.ToArray();
    context.Request.Body.Position = 0;

    string? payload = null;
    var formFields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    var fileMetadata = new List<object>();

    if (context.Request.HasFormContentType)
    {
        var form = await context.Request.ReadFormAsync();

        foreach (var field in form)
        {
            formFields[field.Key] = field.Value.ToString();
        }

        foreach (var file in form.Files)
        {
            fileMetadata.Add(new
            {
                file.Name,
                file.FileName,
                file.ContentType,
                file.Length
            });
        }

        formFields.TryGetValue("payload", out payload);
    }
    else
    {
        payload = Encoding.UTF8.GetString(rawBodyBytes);
    }

    string? eventType = null;

    if (!string.IsNullOrWhiteSpace(payload))
    {
        try
        {
            using var payloadDocument = JsonDocument.Parse(payload);
            if (payloadDocument.RootElement.ValueKind == JsonValueKind.Object
                && payloadDocument.RootElement.TryGetProperty("event", out var eventProperty)
                && eventProperty.ValueKind == JsonValueKind.String)
            {
                eventType = eventProperty.GetString();
            }
        }
        catch (JsonException)
        {
            // Ignore malformed JSON payloads and treat as non-media.play events.
        }
    }

    if (!string.Equals(eventType, "media.play", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Accepted(value: new
        {
            status = "ignored",
            reason = "Only media.play events are captured.",
            eventType
        });
    }

    var requestHeaders = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

    var capturedRequest = new
    {
        receivedAtUtc = DateTimeOffset.UtcNow,
        request = new
        {
            context.Request.Method,
            path = context.Request.Path.Value,
            queryString = context.Request.QueryString.Value,
            contentType = context.Request.ContentType,
            contentLength = context.Request.ContentLength,
            headers = requestHeaders,
            formFields,
            files = fileMetadata,
            rawBodyBase64 = Convert.ToBase64String(rawBodyBytes)
        }
    };

    var logFilePath = Path.Combine(Path.GetTempPath(), LogFileName);
    var entry = JsonSerializer.Serialize(capturedRequest, new JsonSerializerOptions
    {
        WriteIndented = true
    });
    var logEntry = $"{entry}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";

    using var writeTimeout = new CancellationTokenSource(logWriteTimeout);
    await logWriteLock.WaitAsync(writeTimeout.Token);
    try
    {
        await File.AppendAllTextAsync(logFilePath, logEntry, writeTimeout.Token);
    }
    finally
    {
        logWriteLock.Release();
    }

    logger.LogInformation("Captured media.play event. Log file: {LogFilePath}", logFilePath);

    return Results.Ok(new
    {
        status = "captured",
        logFile = logFilePath
    });
});

app.Run();
