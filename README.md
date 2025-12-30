# Webhook Receiver

A real-time webhook testing tool built with ASP.NET Core and SignalR. Capture, inspect, and debug webhook payloads from any service.

## Features

- **Real-time updates** - See webhooks appear instantly via SignalR
- **Flexible endpoint** - Accepts any HTTP method (GET, POST, PUT, DELETE, PATCH)
- **Channel support** - Organize webhooks with optional channels (`/api/webhook/my-channel`)
- **Full request capture** - Headers, body, query strings, source IP
- **24-hour retention** - Automatic cleanup of old entries
- **Modern UI** - Dark theme dashboard with search and filtering

## Webhook Endpoint

Send webhooks to:
```
POST https://your-app.azurewebsites.net/api/webhook
POST https://your-app.azurewebsites.net/api/webhook/{channel}
```

Any HTTP method is supported (GET, POST, PUT, DELETE, PATCH, etc.)

## Local Development

```bash
cd src
dotnet run
```

Open http://localhost:5000 to view the dashboard.

## Deployment

This app is configured for automatic deployment to Azure App Service via GitHub Actions.

### Manual Deployment

```bash
az webapp up --name your-app-name --resource-group your-rg --plan your-plan
```

## Tech Stack

- ASP.NET Core 8
- SignalR for real-time communication
- Vanilla JavaScript frontend
- Azure App Service (Free tier compatible)

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/webhook` | ANY | Receive webhooks |
| `/api/webhook/{channel}` | ANY | Receive webhooks with channel |
| `/api/webhooks` | GET | List all webhooks |
| `/api/webhooks/{id}` | GET | Get single webhook |
| `/api/webhooks/{id}` | DELETE | Delete webhook |
| `/api/webhooks` | DELETE | Clear all webhooks |
| `/api/health` | GET | Health check |

## License

MIT

