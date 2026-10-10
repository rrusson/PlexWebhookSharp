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

Running or debugging with any launch profile opens the tester automatically. Otherwise, after starting the service, open:

- `http://localhost:5146/webhook-tester.html`

Enter the value configured for `PlexWebhook__Secret`, then use the buttons to verify the endpoint's `media.play` capture, ignored event, malformed JSON, and unauthorized-request behaviors. The page submits Plex-style `multipart/form-data` requests, including a sample thumbnail file.

### cURL equivalents

Each tester button has a cURL equivalent. Replace `YOUR_SECRET` with the configured secret. The multipart examples read the payload from a file (`-F "payload=<file"`) so the JSON is sent unmodified; on Windows, use `curl.exe` in PowerShell.

Create the sample files:

```bash
echo -n '{"event":"media.play"}' > play.json
echo -n '{"event":"media.pause"}' > pause.json
echo -n 'test thumbnail' > thumbnail.txt
```

**media.play (captured, expects `200`)**

```bash
curl -i -X POST http://localhost:5146/webhooks/plex \
  -H "X-Plex-Webhook-Secret: YOUR_SECRET" \
  -F "payload=<play.json" \
  -F "thumb=@thumbnail.txt;type=text/plain"
```

**media.pause (ignored, expects `202`)**

```bash
curl -i -X POST http://localhost:5146/webhooks/plex \
  -H "X-Plex-Webhook-Secret: YOUR_SECRET" \
  -F "payload=<pause.json" \
  -F "thumb=@thumbnail.txt;type=text/plain"
```

**Malformed JSON (ignored, expects `202`)**

```bash
curl -i -X POST http://localhost:5146/webhooks/plex \
  -H "X-Plex-Webhook-Secret: YOUR_SECRET" \
  -H "Content-Type: application/json" \
  --data '{ invalid JSON'
```

**Missing secret (expects `401`)**

```bash
curl -i -X POST http://localhost:5146/webhooks/plex \
  -F "payload=<play.json" \
  -F "thumb=@thumbnail.txt;type=text/plain"
```

The tester page sends a fuller Plex-style payload (account, server, player, and metadata); the minimal payloads above exercise the same code paths.
