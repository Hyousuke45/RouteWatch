# RouteWatch — Desktop Shipment Operations (ITSD81 Performance Task 6)

**Pair Members:** `Gil Scudge B. Ybanez` and `Stephen Joseph D. Rubin`

## Overview
RouteWatch is a Windows x64 desktop application built with C#/.NET 10, Avalonia UI (MVVM), Entity Framework Core, and SQLite for retrieving, synchronizing, filtering, summarizing, and exporting branch shipment records.

## Prerequisites
- Windows x64
- .NET 10 SDK (required for building from source and running automated tests)
- Included `HubLink-Local-Server` (for local manual refresh testing; requires no separate .NET runtime)

## Configuration
`RouteWatch.Desktop/appsettings.json` is copied beside the executable on build and publish:
- `ApiBaseAddress`: Service base URL (default: `[http://127.0.0.1:5080](http://127.0.0.1:5080)`)
- `DefaultBranch`: Initial branch loaded at startup (default: `BR-001`)

Local shipment records persist across sessions in SQLite at:
`%LOCALAPPDATA%\RouteWatch-Student\shipments.db`

## Build, Test, and Run
From the `RouteWatch-Starter` directory:

```powershell
# Restore NuGet packages and build the solution
dotnet restore RouteWatch.sln
dotnet build RouteWatch.sln

# Run the automated xUnit test suite (TC-01 through TC-08)
dotnet test RouteWatch.sln

# Launch the desktop application
dotnet run --project RouteWatch.Desktop
```

## Manual Service Connection (HubLink)
1. Start `HubLink-Local-Server\Start-HubLink.cmd` (or `Start-HubLink-5081.cmd` if port `5080` is in use).
2. In RouteWatch, enter `[http://127.0.0.1:5080](http://127.0.0.1:5080)` (or port `5081`) as **Service address** and `BR-001` as **Branch code**, then click **Refresh from service**.

## Publish Self-Contained Windows x64 Release
```powershell
dotnet publish .\RouteWatch.Desktop\RouteWatch.Desktop.csproj `
    -c Release -r win-x64 --self-contained true -o .\publish\win-x64
```
Distribute the entire `publish\win-x64` directory and run `RouteWatch.Desktop.exe` directly outside Visual Studio.

## Acknowledgments
AI assistance (Gemini) was used to audit defects in the starter codebase, implement repairs in `RouteWatch.Core` and `RouteWatch.Desktop`, and structure the xUnit tests (`TC-01`–`TC-08`). All code, test assertions, and manual verification checks were executed and verified by the student pair.