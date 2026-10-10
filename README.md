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

## Local webhook tester

After starting the service, open:

- `http://localhost:5146/webhook-tester.html`

Enter the value configured for `PlexWebhook__Secret`, then use the buttons to verify the endpoint's `media.play` capture, ignored event, malformed JSON, and unauthorized-request behaviors. The page submits Plex-style `multipart/form-data` requests, including a sample thumbnail file.
