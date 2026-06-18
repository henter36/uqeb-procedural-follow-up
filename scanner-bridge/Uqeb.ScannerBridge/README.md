# Uqeb Local Scanner Bridge

Windows-only local HTTP bridge for Uqeb scanner UI. Listens on loopback only (`127.0.0.1` / `[::1]`) and does not modify `Uqeb.Api`.

## Requirements

- Windows 10/11 or Windows Server
- .NET 8 runtime (ASP.NET Core 8). The bridge targets `net8.0` for broader Windows deployment; `Uqeb.Api` may run on a newer runtime separately.
- Optional: WIA-compatible scanner

## Run locally

```powershell
cd scanner-bridge/Uqeb.ScannerBridge
dotnet run
```

Development profile uses `Provider=Mock` when `appsettings.Development.json` is present locally (see `appsettings.Development.example.json`), or set:

```powershell
$env:ScannerBridge__Provider = "Mock"
```

Production-style run without mock:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet run
```

## Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/status` | Health and scanner API info |
| GET | `/scanners` | Available scanners |
| POST | `/scan` | Perform scan |
| GET | `/scan/{scanId}/file` | Full-resolution file |
| DELETE | `/scan/{scanId}` | Delete temp scan |

Base URL: `http://127.0.0.1:5055`

## Configuration

`appsettings.json`

- `ScannerBridge:Provider` — `Auto`, `WIA`, or `Mock`
- `ScannerBridge:AllowMockFallback` — allow mock when WIA is unavailable
- `ScannerBridge:TempTtlMinutes` — temp file TTL (default 10)
- `ScannerBridge:MaxFileSizeBytes` — max scan size (default 25 MB)
- `Cors:AllowedOrigins` — allowed browser origins (localhost / IIS site URL)

Temp files: `%LOCALAPPDATA%\Uqeb\ScannerBridge\temp`

## Quick test

```powershell
curl http://127.0.0.1:5055/status
curl http://127.0.0.1:5055/scanners
curl -X POST http://127.0.0.1:5055/scan -H "Content-Type: application/json" -d "{\"scannerId\":\"mock:scanner-1\",\"format\":\"image/jpeg\",\"dpi\":300,\"colorMode\":\"color\"}"
```

Replace `scanId` from the scan response:

```powershell
curl http://127.0.0.1:5055/scan/<scanId>/file --output scan.jpg
curl -X DELETE http://127.0.0.1:5055/scan/<scanId>
```

## Security notes

- Loopback bind only — not exposed on LAN
- CORS limited to configured local origins
- `scanId` is a GUID; client cannot choose file paths
- No OCR or content analysis
