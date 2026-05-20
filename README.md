# AutoMeatBot

AutoMeatBot is a C# ASP.NET Core MVP for detecting online meetings from Telegram group chats.

It includes:

- Telegram webhook receiver.
- PostgreSQL persistence for chats, users, messages, meeting candidates, and email mappings.
- Ollama-based local LLM extraction.
- Simple web UI for reviewing meetings and linking Telegram users to email addresses.

## Requirements

- Telegram bot token from `@BotFather`.
- Bot added to target groups.
- Bot must see group messages: make it an admin or disable Privacy Mode in `@BotFather`.
- Public HTTPS webhook URL for production, or a tunnel such as ngrok/cloudflared for local testing.
- Docker for the default local setup, or .NET 8 SDK plus PostgreSQL if running manually.

## Quick Start

1. Copy environment file:

```bash
cp .env.example .env
```

2. Set `TELEGRAM__BOTTOKEN` in `.env`.

3. Choose host binding in `.env`.

For public access on port 8080:

```env
HOST_BIND_IP=0.0.0.0
HOST_HTTP_PORT=8080
```

If port 8080 is already used, choose another host port:

```env
HOST_HTTP_PORT=8081
```

4. Start the API and PostgreSQL:

```bash
docker compose up --build
```

5. Optional: start local Ollama on the same server:

```bash
docker compose --profile llm up -d ollama
```

The Ollama image is large. On a small VPS, either increase disk space or run Ollama on another machine and point `Ollama__BaseUrl` to that host.

6. Pull a local model for Ollama:

```bash
docker compose exec ollama ollama pull qwen2.5:7b
```

7. Register webhook:

```bash
curl -X POST "https://api.telegram.org/bot<TOKEN>/setWebhook" \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://your-domain.com/telegram/webhook",
    "allowed_updates": [
      "message",
      "edited_message",
      "business_connection",
      "business_message",
      "edited_business_message",
      "deleted_business_messages"
    ]
  }'
```

8. Open the web UI:

```text
http://localhost:8080
```

## API

- `POST /telegram/webhook` - Telegram updates.
- `GET /api/meetings` - list meeting candidates.
- `PATCH /api/meetings/{id}` - edit meeting fields.
- `POST /api/meetings/{id}/approve` - mark as approved.
- `POST /api/meetings/{id}/cancel` - mark as cancelled.
- `POST /api/meetings/{id}/participants` - add participant manually.
- `GET /api/people` - list Telegram-to-email mappings.
- `POST /api/people` - create manual mapping.
- `PATCH /api/people/{id}` - update mapping.

## MVP Boundaries

Calendar creation is intentionally not implemented yet. The server stops at human approval. The next step is adding Google Calendar or Microsoft Graph OAuth and creating events from `approved` meetings.

The LLM output is validated as JSON, but humans should still review meetings before creating calendar events.
