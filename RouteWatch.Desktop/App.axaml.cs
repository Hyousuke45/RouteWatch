using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RouteWatch.Core;

namespace RouteWatch.Desktop;

public sealed partial class App : Application
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RouteWatch-Student");
            Directory.CreateDirectory(folder);
            var options = new DbContextOptionsBuilder<ShipmentDb>()
                .UseSqlite($"Data Source={Path.Combine(folder, "shipments.db")}")
                .Options;

            var services = new ServiceCollection();
            services.AddSingleton(options);
            services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
            services.AddSingleton<ShipmentService>();
            services.AddSingleton<MainViewModel>();
            _services = services.BuildServiceProvider();

            var vm = _services.GetRequiredService<MainViewModel>();
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidDataException("Settings are empty.");

                vm.BaseAddress = settings.ApiBaseAddress?.Trim() ?? "";
                vm.BranchCode = settings.DefaultBranch?.Trim() ?? "";

                if (!Uri.TryCreate(vm.BaseAddress, UriKind.Absolute, out var root) ||
                    (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
                {
                    vm.SetStartupConfigurationError("Configuration has an invalid ApiBaseAddress. Enter a valid HTTP or HTTPS service URL.");
                }
                else if (!ShipmentRules.ValidBranch(vm.BranchCode))
                {
                    vm.SetStartupConfigurationError("Configuration has an invalid DefaultBranch. Enter a valid branch such as BR-001.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                vm.SetStartupConfigurationError("Configuration could not be read. Enter a service URL and branch: " + ex.Message);
            }

            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.Exit += (_, _) => _services.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private sealed record Settings(string? ApiBaseAddress, string? DefaultBranch);
}