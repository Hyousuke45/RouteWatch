# RouteWatch — Test and Verification Record

**Course:** ITSD81 — Desktop Application Development (Performance Task 6)  
**Pair Members:** `Gil Scudge B. Ybanez` and `Stephen Joseph D. Rubin`  
**Date:** 2026-11-24

## 1. Automated Test Execution (`TC-01` through `TC-08`)

**Command Executed:**
```powershell
dotnet test RouteWatch.sln
```

**Execution Summary:** Total: 9 | Passed: 9 | Failed: 0 | Skipped: 0

| Test ID | Test Method | Verification Scope | Outcome |
|---|---|---|---|
| Example | `FakeHandlerReturnsTheConfiguredReply` | Supplied starter HTTP test double example | Passed |
| TC-01 | `TC01_ValidApiResponseProcessing` | Requests `BR-001`, persists all shipment fields and UTC timestamps, and rejects mismatched branch responses without modifying existing records | Passed |
| TC-02 | `TC02_HttpFailurePreservation` | Reports failure on HTTP 500/400 (even with a valid empty JSON payload) and leaves saved records unchanged | Passed |
| TC-03 | `TC03_InvalidJsonRecovery` | Reports invalid data on malformed JSON (`invalid.json`) and preserves existing stored records | Passed |
| TC-04 | `TC04_IdempotentSynchronization` | Prevents duplicates across repeated refreshes, updates `TRACK-01`, inserts `TRACK-03`, retains omitted `TRACK-02`, and deletes nothing on `empty-001.json` | Passed |
| TC-05 | `TC05_BranchIsolation` | Isolates retrieval, filtering, and summary counts between `BR-001` and `BR-002` with overlapping `TRACK-01` | Passed |
| TC-06 | `TC06_FilteringAndSummaryAccuracy` | Displays only matching `InTransit` shipments while preserving full-branch summary counts, and restores all shipments on `All` | Passed |
| TC-07 | `TC07_CsvExportCorrectness` | Exports only displayed rows with exact header `TrackingNumber,Origin,Destination,Status,LastUpdatedUtc` and RFC-4180 escaping for commas, quotes, and newlines | Passed |
| TC-08 | `TC08_PersistenceAcrossApplicationSessions` | Persists saved records and fields in file-backed SQLite across disposed and reopened service/context sessions | Passed |

## 2. Manual Acceptance Verification

### MA-01 — Responsive External Operations
- **Method:** Started the local `HubLink-Local-Server` (`[http://127.0.0.1:5080](http://127.0.0.1:5080)`), checked **Use delay test (MA-01)**, and clicked **Refresh from service** for `BR-001` (10-second delayed response). Interacted with the **Status** filter dropdown while waiting, cleared the delay checkbox afterward, then stopped the HubLink service and triggered another refresh.
- **Outcome:** Passed. During the 10-second delay, the status banner indicated the delay test was in progress, `Working...` appeared in the footer, action buttons disabled overlapping refreshes, and the status filter remained responsive. After completion, 5 `BR-001` shipments were saved and displayed. When HubLink was stopped, RouteWatch reported the refresh failure while keeping previously saved records visible without misrepresenting them as newly retrieved.

### MA-02 — Self-Contained Application Release
- **Method:** Published the desktop project with `dotnet publish .\RouteWatch.Desktop\RouteWatch.Desktop.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64` and launched `RouteWatch.Desktop.exe` directly outside Visual Studio.
- **Outcome:** Passed. Confirmed `appsettings.json`, `e_sqlite3.dll`, Avalonia native libraries, and self-contained .NET runtime files are present in `publish\win-x64\`. The standalone executable launched outside Visual Studio, read `appsettings.json`, loaded persisted local records across sessions, filtered by status, and exported valid CSV files.

## 3. Incomplete Checks
- `MA-02` was verified outside Visual Studio on our Windows x64 development machine; a separate Windows x64 machine without a separately installed .NET runtime was not available for secondary testing.

## 4. Acknowledgments
- AI assistance (Gemini) was consulted to audit starter defects, implement code fixes in `RouteWatch.Core` and `RouteWatch.Desktop`, and structure the xUnit tests (`TC-01`–`TC-08`). All code, test assertions, and verification steps were reviewed, executed, and validated by the student pair.