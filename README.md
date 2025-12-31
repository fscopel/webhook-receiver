# Webhook Receiver

A real-time webhook testing tool built with ASP.NET Core, SignalR, and Firebase. Capture, inspect, and debug webhook payloads from any service with secure authentication.

## Features

- **Real-time updates** - See webhooks appear instantly via SignalR
- **Firebase Authentication** - Passwordless email link sign-in
- **Per-user storage** - Each user gets their own webhook view
- **Firestore persistence** - Webhooks stored in Firebase Firestore
- **24-hour retention** - Automatic cleanup via Firebase scheduled functions
- **Flexible endpoint** - Accepts any HTTP method (GET, POST, PUT, DELETE, PATCH)
- **Channel support** - Organize webhooks with optional channels (`/api/webhook/my-channel`)
- **Full request capture** - Headers, body, query strings, source IP
- **Modern UI** - Dark theme dashboard with search and filtering
- **Minified production build** - Obfuscated JavaScript for production

## Webhook Endpoint

Send webhooks to:

```
POST https://your-app.azurewebsites.net/api/webhook
POST https://your-app.azurewebsites.net/api/webhook/{channel}
```

Any HTTP method is supported (GET, POST, PUT, DELETE, PATCH, etc.)

## Prerequisites

- .NET 8 SDK
- Node.js 20+
- Firebase project with:
  - Authentication (Email Link sign-in enabled)
  - Firestore database
  - Cloud Functions (for scheduled cleanup)

## Local Development

### 1. Clone and install dependencies

```bash
git clone https://github.com/fscopel/webhook-receiver.git
cd webhook-receiver
npm install
```

### 2. Configure Firebase

1. Create a Firebase project at [console.firebase.google.com](https://console.firebase.google.com)
2. Enable **Email Link** sign-in in Authentication
3. Create a **Firestore** database
4. Download a service account key:
   - Go to Project Settings → Service Accounts
   - Click "Generate new private key"
   - Save as `firebase-service-account.json` in the project root

### 3. Set environment variable

```bash
# Windows
set GOOGLE_APPLICATION_CREDENTIALS=C:\path\to\firebase-service-account.json

# Linux/macOS
export GOOGLE_APPLICATION_CREDENTIALS=/path/to/firebase-service-account.json
```

### 4. Run the app

```bash
cd src
dotnet run
```

Open http://localhost:5171 to view the dashboard.

## Access Control

Access is restricted to:

- `@ldeat.com` email domain
- Specific whitelisted Gmail addresses

To modify allowed emails, edit `src/Services/FirebaseAuthService.cs`:

```csharp
private static readonly string[] AllowedDomains = { "ldeat.com" };
private static readonly string[] AllowedEmails =
{
    "user1@gmail.com",
    "user2@gmail.com"
};
```

## Deployment

### Azure App Service (via GitHub Actions)

The app auto-deploys to Azure on push to `main`. Ensure these secrets are set:

- `AZURE_WEBAPP_PUBLISH_PROFILE`

### Azure Configuration

Set the Firebase service account credentials as a Base64-encoded App Setting:

1. **Base64 encode** your service account JSON (PowerShell):

```powershell
$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes("firebase-service-account.json"))
Write-Host $base64
```

2. **Set the App Setting** in Azure:

```bash
az webapp config appsettings set \
  --resource-group YOUR_RESOURCE_GROUP \
  --name YOUR_APP_NAME \
  --settings "FIREBASE_SERVICE_ACCOUNT_BASE64=YOUR_BASE64_STRING"
```

Or via Azure Portal: App Service → Configuration → Application settings → New application setting:

- **Name**: `FIREBASE_SERVICE_ACCOUNT_BASE64`
- **Value**: (paste the base64 string)

> **Note**: Base64 encoding avoids escaping issues with the private key's newline characters.

### Deploy Firebase Functions

```bash
npx firebase-tools deploy --only functions
```

## Build Frontend

The frontend is minified and obfuscated for production:

```bash
npm run build
```

Output goes to `src/wwwroot-dist/`. In production, the app serves from this folder.

## Tech Stack

- **Backend**: ASP.NET Core 8, SignalR
- **Database**: Firebase Firestore
- **Authentication**: Firebase Auth (Email Link)
- **Cleanup**: Firebase Cloud Functions (scheduled)
- **Frontend**: Vanilla JavaScript, CSS
- **Build**: esbuild, javascript-obfuscator
- **Hosting**: Azure App Service

## API Endpoints

| Endpoint                   | Method | Auth | Description                   |
| -------------------------- | ------ | ---- | ----------------------------- |
| `/api/webhook`             | ANY    | ❌   | Receive webhooks (public)     |
| `/api/webhook/{channel}`   | ANY    | ❌   | Receive webhooks with channel |
| `/api/auth/validate-email` | POST   | ❌   | Validate email before sign-in |
| `/api/webhooks`            | GET    | ✅   | List user's webhooks          |
| `/api/webhooks/{id}`       | GET    | ✅   | Get single webhook            |
| `/api/webhooks/{id}`       | DELETE | ✅   | Delete webhook                |
| `/api/webhooks`            | DELETE | ✅   | Clear all user's webhooks     |
| `/api/health`              | GET    | ❌   | Health check                  |

## Project Structure

```
webhook-receiver/
├── src/
│   ├── Hubs/              # SignalR hub
│   ├── Middleware/        # Firebase auth middleware
│   ├── Models/            # Data models
│   ├── Services/          # WebhookStore, FirebaseAuthService
│   ├── wwwroot/           # Source frontend files
│   └── wwwroot-dist/      # Built/minified frontend (gitignored)
├── functions/             # Firebase Cloud Functions
├── build/                 # Frontend build scripts
├── firestore.rules        # Firestore security rules
└── firebase.json          # Firebase configuration
```

## License

MIT
