# PlexWebhookSharp

A Plex Media Server C# API webhook service.

## POC webhook endpoint

This repository now includes a minimal ASP.NET Core service at:

- `POST /webhooks/plex`

The endpoint inspects incoming Plex webhook payloads and captures only `media.play` events.
For each captured event, it appends request information to:

- `Path.Combine(Path.GetTempPath(), "plex-media-play-webhooks.log")`

Run locally:

```bash
cd PlexWebhookSharp.Api
PlexWebhook__Secret=replace-with-a-secret dotnet run
```

Configure Plex to include the matching `X-Plex-Webhook-Secret` header with webhook requests. Requests without the configured secret are rejected, and request bodies are limited to 1 MiB.
