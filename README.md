# Health Monitor

A lightweight, self-hosted API health monitor built with ASP.NET Core Minimal
API. It periodically checks a list of URLs and shows their status, response
time, and last check time on a small web dashboard.

## Features

- Add and remove monitored URLs from the dashboard
- Validate URLs and prevent duplicate entries
- Check all enabled URLs every 10 seconds
- Check a single URL immediately with **Check now**
- Pause and resume individual URLs
- Show HTTP status code, response time, and last check time
- Persist the URL list and pause state in `urls.json`
- Responsive, dependency-free HTML/CSS/JavaScript dashboard
- English user interface and API messages

## Tech stack

- .NET 10
- ASP.NET Core Minimal API
- `System.Text.Json`
- Plain HTML, CSS, and JavaScript

## Getting started

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Run locally

```powershell
cd C:\Users\beril.tezcan\HealthMonitor
dotnet run
```

Open the dashboard at:

```text
http://localhost:5140
```

The application starts its first check immediately and then repeats checks
every 10 seconds.

## Usage

1. Enter a URL in the form and select **Add**.
2. Wait for the next background check, or select **Check now** on a row.
3. Use **Pause** to stop automatic checks without deleting the URL.
4. Use **Resume** to enable automatic checks again.
5. Use **Delete** to remove the URL from the list.

## Status meanings

| Result | Meaning |
| --- | --- |
| 2xx / 3xx | Up (green) |
| 4xx | Warning (yellow) |
| 5xx or connection error | Down (red) |
| Disabled | Paused (gray) |
| No check yet | Waiting for the first check |

Each request has a 10-second timeout.

## API

| Method | Endpoint | Description |
| --- | --- | --- |
| `GET` | `/` | Web dashboard |
| `GET` | `/status` | Current results for all configured URLs |
| `GET` | `/check?url=...` | Check one URL immediately |
| `GET` | `/check-all` | Check all enabled URLs immediately |
| `GET` | `/api/urls` | List configured URLs and their enabled state |
| `POST` | `/api/urls` | Add a URL |
| `PUT` | `/api/urls/enabled` | Pause or resume a URL |
| `DELETE` | `/api/urls?url=...` | Remove a URL |

### Add a URL

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5140/api/urls `
  -ContentType "application/json" `
  -Body '{"url":"https://example.com"}'
```

### Pause a URL

```powershell
Invoke-RestMethod `
  -Method Put `
  -Uri http://localhost:5140/api/urls/enabled `
  -ContentType "application/json" `
  -Body '{"url":"https://example.com/","enabled":false}'
```

### Delete a URL

The URL must be URL-encoded because it is sent as a query parameter.

```powershell
$url = "https://example.com/"
$encoded = [uri]::EscapeDataString($url)

Invoke-RestMethod `
  -Method Delete `
  -Uri "http://localhost:5140/api/urls?url=$encoded"
```

## Data and configuration

The URL list is stored in `urls.json` in the project root:

```json
[
  {
    "url": "https://example.com/",
    "enabled": true
  },
  {
    "url": "https://httpbin.org/status/500",
    "enabled": false
  }
]
```

`urls.json` is intentionally ignored by Git because it contains local
configuration. If the file does not exist, the application starts with a
small default list.

Older string-only files are still accepted:

```json
[
  "https://example.com/"
]
```

They are converted to the object format the next time the list is saved.

Current check results are kept in memory only. Restarting the application
clears the last results, while the URL list and pause state remain.

## How it works

1. The application loads the URL list from `urls.json`.
2. A background loop checks all enabled URLs immediately and then every
   10 seconds.
3. Each URL is checked with an HTTP `GET` request.
4. Results are cached in memory and exposed through `/status`.
5. The dashboard polls `/status` every 5 seconds.
6. Manual checks update the cached result immediately.

Checks are performed sequentially. A slow or timing-out URL can therefore
delay the rest of that check cycle.

## Project structure

```text
HealthMonitor/
├── Program.cs              # API, background checks, persistence
├── wwwroot/
│   └── index.html          # Dashboard UI
├── Properties/
│   └── launchSettings.json
├── HealthMonitor.csproj
├── appsettings.json
└── urls.json               # Local data, ignored by Git
```

## Security note

This project is intended for local use. It does not include authentication,
authorization, or protection against SSRF. Do not expose it to the public
internet without adding authentication and restricting which addresses can
be monitored.
