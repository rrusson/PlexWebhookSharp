using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Ok("Plex webhook service is running."));

app.MapPost("/webhooks/plex", async (HttpContext context, ILogger<Program> logger) =>
{
    context.Request.EnableBuffering();

    using var memoryStream = new MemoryStream();
    await context.Request.Body.CopyToAsync(memoryStream);
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
            if (payloadDocument.RootElement.TryGetProperty("event", out var eventProperty))
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

    var logFilePath = Path.Combine(Path.GetTempPath(), "plex-media-play-webhooks.log");
    var entry = JsonSerializer.Serialize(capturedRequest, new JsonSerializerOptions
    {
        WriteIndented = true
    });

    await File.AppendAllTextAsync(logFilePath, entry + Environment.NewLine + new string('-', 80) + Environment.NewLine);

    logger.LogInformation("Captured media.play event. Log file: {LogFilePath}", logFilePath);

    return Results.Ok(new
    {
        status = "captured",
        logFile = logFilePath
    });
});

app.Run();
