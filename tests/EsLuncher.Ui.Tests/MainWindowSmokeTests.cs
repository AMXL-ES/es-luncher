using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using EsLuncher.App;
using Xunit;

namespace EsLuncher.Ui.Tests;

public sealed class MainWindowSmokeTests
{
    [Fact]
    public void Main_window_starts_and_shows_title()
    {
        AppBuilder.Configure<EsLuncher.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .SetupWithoutStarting();

        var window = new MainWindow();
        window.Show();
        Assert.Equal("ES Luncher", window.Title);
        Assert.True(window.IsVisible);
        Assert.NotNull(window.FindControl<Avalonia.Controls.Control>("HomePage"));
        Assert.True(window.FindControl<Avalonia.Controls.Control>("HomePage")!.IsVisible);
        Assert.NotNull(window.FindControl<Avalonia.Controls.Control>("InstancesPage"));
        Assert.NotNull(window.FindControl<Avalonia.Controls.Control>("DownloadsPage"));
        Assert.NotNull(window.FindControl<Avalonia.Controls.Control>("SettingsPage"));
        Assert.NotNull(window.FindControl<Avalonia.Controls.Control>("AboutPage"));
        window.Close();
    }
}
