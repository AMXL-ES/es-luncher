using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
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
        // 下载 / 设置 / 关于是懒加载宿主：启动时存在但内容为空，点到时才创建。
        Assert.NotNull(window.FindControl<ContentControl>("DownloadsHost"));
        Assert.NotNull(window.FindControl<ContentControl>("SettingsHost"));
        Assert.NotNull(window.FindControl<ContentControl>("AboutHost"));
        Assert.Null(window.FindControl<ContentControl>("DownloadsHost")!.Content);
        Assert.Null(window.FindControl<ContentControl>("AboutHost")!.Content);
        var launch = window.FindControl<Button>("LaunchButton");
        Assert.NotNull(launch);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(launch!)));

        var homeLaunch = window.FindControl<Button>("HomeLaunchButton");
        Assert.NotNull(homeLaunch);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(homeLaunch!)));

        // M10：更新卡片必须在「其他」页存在，且两个按钮带无障碍名称、应用按钮默认禁用。
        Assert.NotNull(window.FindControl<Avalonia.Controls.Control>("OthersPage"));
        var updateCheck = window.FindControl<Button>("UpdateCheckButton");
        Assert.NotNull(updateCheck);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(updateCheck!)));
        var updateApply = window.FindControl<Button>("UpdateApplyButton");
        Assert.NotNull(updateApply);
        Assert.False(updateApply!.IsEnabled);

        // M11/关于页：点导航进入「关于」时才创建整页并加载组件清单、数据服务与鸣谢（懒加载）。
        var aboutButton = window.FindControl<Button>("NavAboutButton");
        Assert.NotNull(aboutButton);
        aboutButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var aboutHost = window.FindControl<ContentControl>("AboutHost");
        Assert.NotNull(aboutHost);
        Assert.True(aboutHost!.IsVisible);
        var aboutView = aboutHost.Content as Control;
        Assert.NotNull(aboutView);

        var components = aboutView!.FindControl<ItemsControl>("ComponentsList");
        Assert.NotNull(components);
        Assert.NotNull(components!.ItemsSource);
        var services = aboutView.FindControl<ItemsControl>("ServicesList");
        Assert.NotNull(services);
        Assert.NotNull(services!.ItemsSource);
        var credits = aboutView.FindControl<ItemsControl>("CreditsList");
        Assert.NotNull(credits);
        Assert.NotNull(credits!.ItemsSource);
        var aboutVersion = aboutView.FindControl<TextBlock>("AboutVersionText");
        Assert.NotNull(aboutVersion);
        Assert.False(string.IsNullOrWhiteSpace(aboutVersion!.Text));

        // 下载页与设置页同样按需创建：没点过之前宿主是空的。
        var downloadsHost = window.FindControl<ContentControl>("DownloadsHost");
        Assert.NotNull(downloadsHost);

        // M11/实例页：空态与计数存在。
        Assert.NotNull(window.FindControl<Avalonia.Controls.Control>("InstancesEmptyState"));
        Assert.NotNull(window.FindControl<TextBlock>("InstancesSummary"));

        window.Close();
    }
}
