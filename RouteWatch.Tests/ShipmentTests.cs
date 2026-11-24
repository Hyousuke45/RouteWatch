using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RouteWatch.Core;
using RouteWatch.Desktop;
using Xunit;

namespace RouteWatch.Tests;

public sealed class ShipmentTests
{
    private sealed record ExpectedShipment(
        string TrackingNumber,
        string BranchCode,
        string Origin,
        string Destination,
        string Status,
        DateTimeOffset LastUpdatedUtc);

    private static List<ExpectedShipment> ParseFixtureShipments(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ExpectedShipment>();
        foreach (var item in doc.RootElement.GetProperty("shipments").EnumerateArray())
        {
            list.Add(new ExpectedShipment(
                item.GetProperty("trackingNumber").GetString()!,
                item.GetProperty("branchCode").GetString()!,
                item.GetProperty("origin").GetString()!,
                item.GetProperty("destination").GetString()!,
                item.GetProperty("status").GetString()!,
                item.GetProperty("lastUpdatedUtc").GetDateTimeOffset()));
        }
        return list;
    }

    private static List<string[]> ParseRfc4180Csv(string csv)
    {
        var rows = new List<string[]>();
        var currentRow = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    currentRow.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    currentRow.Add(field.ToString());
                    field.Clear();
                    rows.Add([.. currentRow]);
                    currentRow.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }
        }

        if (field.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(field.ToString());
            rows.Add([.. currentRow]);
        }

        return rows;
    }

    private static void AssertShipmentMatches(ExpectedShipment expected, Shipment actual)
    {
        Assert.Equal(expected.TrackingNumber, actual.TrackingNumber);
        Assert.Equal(expected.BranchCode, actual.BranchCode);
        Assert.Equal(expected.Origin, actual.Origin);
        Assert.Equal(expected.Destination, actual.Destination);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.LastUpdatedUtc, actual.LastUpdatedUtc);
        Assert.Equal(TimeSpan.Zero, actual.LastUpdatedUtc.Offset);
    }

    private static void AssertSnapshotsEqual(IReadOnlyList<Shipment> expected, IReadOnlyList<Shipment> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].TrackingNumber, actual[i].TrackingNumber);
            Assert.Equal(expected[i].BranchCode, actual[i].BranchCode);
            Assert.Equal(expected[i].Origin, actual[i].Origin);
            Assert.Equal(expected[i].Destination, actual[i].Destination);
            Assert.Equal(expected[i].Status, actual[i].Status);
            Assert.Equal(expected[i].LastUpdatedUtc, actual[i].LastUpdatedUtc);
        }
    }

    [Fact]
    public async Task TC01_ValidApiResponseProcessing()
    {
        await using var env = new TestEnvironment();
        var branch001Json = TestEnvironment.Fixture("branch-001.json");
        var expectedShipments = ParseFixtureShipments(branch001Json);

        env.Handler.Status = HttpStatusCode.OK;
        env.Handler.Body = branch001Json;

        var received = await env.Service.RefreshAsync("http://hublink.test", "BR-001");

        Assert.Equal(expectedShipments.Count, received);
        Assert.NotNull(env.Handler.LastRequest);
        Assert.Equal("/api/shipments", env.Handler.LastRequest!.AbsolutePath);
        Assert.Equal("?branchCode=BR-001", env.Handler.LastRequest.Query);

        var stored = await env.Service.GetBranchAsync("BR-001");
        Assert.Equal(expectedShipments.Count, stored.Count);
        foreach (var exp in expectedShipments.OrderBy(x => x.TrackingNumber))
        {
            var match = Assert.Single(stored, s => s.TrackingNumber == exp.TrackingNumber);
            AssertShipmentMatches(exp, match);
        }

        // Verify a payload for a different branch is rejected without modifying existing records.
        env.Handler.Body = TestEnvironment.Fixture("branch-002.json");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            env.Service.RefreshAsync("http://hublink.test", "BR-001"));

        var afterMismatch = await env.Service.GetBranchAsync("BR-001");
        AssertSnapshotsEqual(stored, afterMismatch);
    }

    [Fact]
    public async Task TC02_HttpFailurePreservation()
    {
        await using var env = new TestEnvironment();
        env.Handler.Status = HttpStatusCode.OK;
        env.Handler.Body = TestEnvironment.Fixture("branch-001.json");
        await env.Service.RefreshAsync("http://hublink.test", "BR-001");
        var baseline = await env.Service.GetBranchAsync("BR-001");
        Assert.NotEmpty(baseline);

        // Simulate HTTP 500 with a valid empty JSON body.
        env.Handler.Status = HttpStatusCode.InternalServerError;
        env.Handler.Body = TestEnvironment.Fixture("empty-001.json");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            env.Service.RefreshAsync("http://hublink.test", "BR-001"));
        AssertSnapshotsEqual(baseline, await env.Service.GetBranchAsync("BR-001"));

        // Also verify ViewModel reports failure and preserves saved records.
        env.Handler.Status = HttpStatusCode.BadRequest;
        var vm = new MainViewModel(env.Service)
        {
            BaseAddress = "http://hublink.test",
            BranchCode = "BR-001"
        };
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.StartsWith("Refresh failed:", vm.Message);
        Assert.Equal(baseline.Count, vm.VisibleShipments.Count);
        AssertSnapshotsEqual(baseline, await env.Service.GetBranchAsync("BR-001"));
    }

    [Fact]
    public async Task TC03_InvalidJsonRecovery()
    {
        await using var env = new TestEnvironment();
        env.Handler.Status = HttpStatusCode.OK;
        env.Handler.Body = TestEnvironment.Fixture("branch-001.json");
        await env.Service.RefreshAsync("http://hublink.test", "BR-001");
        var baseline = await env.Service.GetBranchAsync("BR-001");
        Assert.NotEmpty(baseline);

        env.Handler.Status = HttpStatusCode.OK;
        env.Handler.Body = TestEnvironment.Fixture("invalid.json");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            env.Service.RefreshAsync("http://hublink.test", "BR-001"));
        AssertSnapshotsEqual(baseline, await env.Service.GetBranchAsync("BR-001"));

        var vm = new MainViewModel(env.Service)
        {
            BaseAddress = "http://hublink.test",
            BranchCode = "BR-001"
        };
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.StartsWith("Refresh failed:", vm.Message);
        AssertSnapshotsEqual(baseline, await env.Service.GetBranchAsync("BR-001"));
    }

    [Fact]
    public async Task TC04_IdempotentSynchronization()
    {
        await using var env = new TestEnvironment();
        var initialJson = TestEnvironment.Fixture("branch-001.json");
        var updateJson = TestEnvironment.Fixture("branch-001-update.json");
        var emptyJson = TestEnvironment.Fixture("empty-001.json");

        var initialExpected = ParseFixtureShipments(initialJson);
        var updateExpected = ParseFixtureShipments(updateJson);

        // Step 1: Refresh branch-001.json twice; no duplicates created.
        env.Handler.Status = HttpStatusCode.OK;
        env.Handler.Body = initialJson;
        await env.Service.RefreshAsync("http://hublink.test", "BR-001");
        await env.Service.RefreshAsync("http://hublink.test", "BR-001");

        var afterRepeat = await env.Service.GetBranchAsync("BR-001");
        Assert.Equal(2, afterRepeat.Count);
        Assert.Equal(2, afterRepeat.Select(s => (s.BranchCode, s.TrackingNumber)).Distinct().Count());
        var originalTrack02 = Assert.Single(afterRepeat, s => s.TrackingNumber == "TRACK-02");

        // Step 2: Refresh branch-001-update.json; TRACK-01 updates, TRACK-03 inserts, omitted TRACK-02 remains.
        env.Handler.Body = updateJson;
        await env.Service.RefreshAsync("http://hublink.test", "BR-001");

        var afterUpdate = await env.Service.GetBranchAsync("BR-001");
        Assert.Equal(3, afterUpdate.Count);
        Assert.Equal(["TRACK-01", "TRACK-02", "TRACK-03"], afterUpdate.Select(s => s.TrackingNumber).ToArray());

        var expectedTrack01 = Assert.Single(updateExpected, x => x.TrackingNumber == "TRACK-01");
        var actualTrack01 = Assert.Single(afterUpdate, s => s.TrackingNumber == "TRACK-01");
        AssertShipmentMatches(expectedTrack01, actualTrack01);

        var expectedTrack03 = Assert.Single(updateExpected, x => x.TrackingNumber == "TRACK-03");
        var actualTrack03 = Assert.Single(afterUpdate, s => s.TrackingNumber == "TRACK-03");
        AssertShipmentMatches(expectedTrack03, actualTrack03);

        var retainedTrack02 = Assert.Single(afterUpdate, s => s.TrackingNumber == "TRACK-02");
        Assert.Equal(originalTrack02.Origin, retainedTrack02.Origin);
        Assert.Equal(originalTrack02.Destination, retainedTrack02.Destination);
        Assert.Equal(originalTrack02.Status, retainedTrack02.Status);
        Assert.Equal(originalTrack02.LastUpdatedUtc, retainedTrack02.LastUpdatedUtc);

        // Step 3: Empty response deletes nothing.
        env.Handler.Body = emptyJson;
        var emptyCount = await env.Service.RefreshAsync("http://hublink.test", "BR-001");
        Assert.Equal(0, emptyCount);
        AssertSnapshotsEqual(afterUpdate, await env.Service.GetBranchAsync("BR-001"));
    }

    [Fact]
    public async Task TC05_BranchIsolation()
    {
        await using var env = new TestEnvironment();
        var branch001Json = TestEnvironment.Fixture("branch-001.json");
        var branch002Json = TestEnvironment.Fixture("branch-002.json");

        var expected001 = ParseFixtureShipments(branch001Json);
        var expected002 = ParseFixtureShipments(branch002Json);

        env.Handler.Status = HttpStatusCode.OK;
        env.Handler.Body = branch001Json;
        await env.Service.RefreshAsync("http://hublink.test", "BR-001");

        env.Handler.Body = branch002Json;
        await env.Service.RefreshAsync("http://hublink.test", "BR-002");

        var stored001 = await env.Service.GetBranchAsync("BR-001");
        var stored002 = await env.Service.GetBranchAsync("BR-002");

        Assert.Equal(expected001.Count, stored001.Count);
        Assert.All(stored001, s => Assert.Equal("BR-001", s.BranchCode));

        Assert.Equal(expected002.Count, stored002.Count);
        Assert.All(stored002, s => Assert.Equal("BR-002", s.BranchCode));

        // Both branches have TRACK-01 with distinct branch-specific fields.
        var track01Branch1 = Assert.Single(stored001, s => s.TrackingNumber == "TRACK-01");
        var track01Branch2 = Assert.Single(stored002, s => s.TrackingNumber == "TRACK-01");
        AssertShipmentMatches(Assert.Single(expected001, x => x.TrackingNumber == "TRACK-01"), track01Branch1);
        AssertShipmentMatches(Assert.Single(expected002, x => x.TrackingNumber == "TRACK-01"), track01Branch2);

        // Verify ViewModel load, filter, and summary isolation per branch.
        var vm = new MainViewModel(env.Service) { BranchCode = "BR-002" };
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(expected002.Count, vm.TotalCount);
        Assert.Equal(expected002.Count, vm.VisibleShipments.Count);
        Assert.All(vm.VisibleShipments, s => Assert.Equal("BR-002", s.BranchCode));
        var vmTrack01 = Assert.Single(vm.VisibleShipments, s => s.TrackingNumber == "TRACK-01");
        Assert.Equal(track01Branch2.Origin, vmTrack01.Origin);
        Assert.Equal(track01Branch2.Destination, vmTrack01.Destination);
    }

    [Fact]
    public async Task TC06_FilteringAndSummaryAccuracy()
    {
        await using var env = new TestEnvironment();
        var branch001Json = TestEnvironment.Fixture("branch-001.json");
        var expected001 = ParseFixtureShipments(branch001Json);

        env.Handler.Status = HttpStatusCode.OK;
        env.Handler.Body = branch001Json;
        await env.Service.RefreshAsync("http://hublink.test", "BR-001");

        var vm = new MainViewModel(env.Service) { BranchCode = "BR-001" };
        await vm.LoadCommand.ExecuteAsync(null);

        var expectedInTransitIds = expected001
            .Where(s => s.Status == "InTransit")
            .Select(s => s.TrackingNumber)
            .OrderBy(id => id)
            .ToArray();

        // Filter to InTransit: only matching shipment(s) displayed, summary retains full branch counts.
        vm.SelectedStatus = "InTransit";
        Assert.Equal(expectedInTransitIds, vm.VisibleShipments.Select(s => s.TrackingNumber).OrderBy(id => id).ToArray());
        Assert.Equal(expected001.Count, vm.TotalCount);
        Assert.Equal(expected001.Count(s => s.Status == "Pending"), vm.PendingCount);
        Assert.Equal(expected001.Count(s => s.Status == "InTransit"), vm.InTransitCount);
        Assert.Equal(expected001.Count(s => s.Status == "Delivered"), vm.DeliveredCount);
        Assert.Equal(expected001.Count(s => s.Status == "Exception"), vm.ExceptionCount);

        // Restoring "All" brings back the complete list.
        vm.SelectedStatus = "All";
        Assert.Equal(expected001.Count, vm.VisibleShipments.Count);

        // Also test a 4-status branch collection (BR-010) to verify all four summary counters independently.
        env.Handler.Body = """
        {
          "branchCode": "BR-010",
          "shipments": [
            { "trackingNumber": "S-1", "branchCode": "BR-010", "origin": "A", "destination": "B", "status": "Pending", "lastUpdatedUtc": "2026-11-10T01:00:00Z" },
            { "trackingNumber": "S-2", "branchCode": "BR-010", "origin": "A", "destination": "C", "status": "InTransit", "lastUpdatedUtc": "2026-11-10T02:00:00Z" },
            { "trackingNumber": "S-3", "branchCode": "BR-010", "origin": "A", "destination": "D", "status": "Delivered", "lastUpdatedUtc": "2026-11-10T03:00:00Z" },
            { "trackingNumber": "S-4", "branchCode": "BR-010", "origin": "A", "destination": "E", "status": "Exception", "lastUpdatedUtc": "2026-11-10T04:00:00Z" }
          ]
        }
        """;
        await env.Service.RefreshAsync("http://hublink.test", "BR-010");
        vm.BranchCode = "BR-010";
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedStatus = "InTransit";
        Assert.Equal(["S-2"], vm.VisibleShipments.Select(s => s.TrackingNumber).ToArray());
        Assert.Equal(4, vm.TotalCount);
        Assert.Equal(1, vm.PendingCount);
        Assert.Equal(1, vm.InTransitCount);
        Assert.Equal(1, vm.DeliveredCount);
        Assert.Equal(1, vm.ExceptionCount);

        vm.SelectedStatus = "All";
        Assert.Equal(4, vm.VisibleShipments.Count);
    }

    [Fact]
    public async Task TC07_CsvExportCorrectness()
    {
        await using var env = new TestEnvironment();
        var ts1 = new DateTimeOffset(2026, 11, 10, 8, 30, 15, TimeSpan.Zero);
        var ts2 = new DateTimeOffset(2026, 11, 10, 9, 45, 0, TimeSpan.Zero);

        env.Handler.Status = HttpStatusCode.OK;
        env.Handler.Body = """
        {
          "branchCode": "BR-001",
          "shipments": [
            {
              "trackingNumber": "CSV-01",
              "branchCode": "BR-001",
              "origin": "Cagayan de Oro, Misamis Oriental",
              "destination": "Pier \"North\", Gate 4",
              "status": "InTransit",
              "lastUpdatedUtc": "2026-11-10T08:30:15Z"
            },
            {
              "trackingNumber": "CSV-02",
              "branchCode": "BR-001",
              "origin": "Warehouse A\nDock 2",
              "destination": "Valencia, Bukidnon",
              "status": "InTransit",
              "lastUpdatedUtc": "2026-11-10T09:45:00Z"
            },
            {
              "trackingNumber": "CSV-03",
              "branchCode": "BR-001",
              "origin": "Excluded Origin",
              "destination": "Excluded Destination",
              "status": "Delivered",
              "lastUpdatedUtc": "2026-11-10T10:00:00Z"
            }
          ]
        }
        """;
        await env.Service.RefreshAsync("http://hublink.test", "BR-001");

        var exportPath = Path.Combine(Path.GetTempPath(), $"routewatch-csv-{Guid.NewGuid():N}.csv");
        try
        {
            var vm = new MainViewModel(env.Service)
            {
                BranchCode = "BR-001",
                ChooseExportFileAsync = _ => Task.FromResult<string?>(exportPath)
            };
            await vm.LoadCommand.ExecuteAsync(null);
            vm.SelectedStatus = "InTransit";

            await vm.ExportCommand.ExecuteAsync(null);

            Assert.True(File.Exists(exportPath));
            var rawCsv = await File.ReadAllTextAsync(exportPath);
            var parsedRows = ParseRfc4180Csv(rawCsv);

            Assert.Equal(3, parsedRows.Count);
            Assert.Equal(["TrackingNumber", "Origin", "Destination", "Status", "LastUpdatedUtc"], parsedRows[0]);

            Assert.Equal("CSV-01", parsedRows[1][0]);
            Assert.Equal("Cagayan de Oro, Misamis Oriental", parsedRows[1][1]);
            Assert.Equal("Pier \"North\", Gate 4", parsedRows[1][2]);
            Assert.Equal("InTransit", parsedRows[1][3]);
            Assert.Equal(ts1, DateTimeOffset.Parse(parsedRows[1][4], CultureInfo.InvariantCulture));

            Assert.Equal("CSV-02", parsedRows[2][0]);
            Assert.Equal("Warehouse A\nDock 2", parsedRows[2][1]);
            Assert.Equal("Valencia, Bukidnon", parsedRows[2][2]);
            Assert.Equal("InTransit", parsedRows[2][3]);
            Assert.Equal(ts2, DateTimeOffset.Parse(parsedRows[2][4], CultureInfo.InvariantCulture));
        }
        finally
        {
            if (File.Exists(exportPath)) File.Delete(exportPath);
        }
    }

    [Fact]
    public async Task TC08_PersistenceAcrossApplicationSessions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "routewatch-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "shipments.db");
        var connectionString = $"Data Source={dbPath};Pooling=False";

        try
        {
            var branch001Json = TestEnvironment.Fixture("branch-001.json");
            var expected = ParseFixtureShipments(branch001Json);

            // Session 1: Retrieve and persist valid response, then dispose client/service.
            using (var handler = new FakeHttpHandler { Status = HttpStatusCode.OK, Body = branch001Json })
            using (var client = new HttpClient(handler))
            {
                var session1Options = new DbContextOptionsBuilder<ShipmentDb>()
                    .UseSqlite(connectionString)
                    .Options;
                var session1Service = new ShipmentService(client, session1Options);
                var count = await session1Service.RefreshAsync("http://hublink.test", "BR-001");
                Assert.Equal(expected.Count, count);
            }

            SqliteConnection.ClearAllPools();

            // Session 2: Open a brand-new DbContextOptions and ShipmentService on the same SQLite file.
            using var session2Client = new HttpClient(new FakeHttpHandler());
            var session2Options = new DbContextOptionsBuilder<ShipmentDb>()
                .UseSqlite(connectionString)
                .Options;
            var session2Service = new ShipmentService(session2Client, session2Options);

            var persisted = await session2Service.GetBranchAsync("BR-001");
            Assert.Equal(expected.Count, persisted.Count);
            foreach (var exp in expected.OrderBy(x => x.TrackingNumber))
            {
                var actual = Assert.Single(persisted, s => s.TrackingNumber == exp.TrackingNumber);
                AssertShipmentMatches(exp, actual);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}