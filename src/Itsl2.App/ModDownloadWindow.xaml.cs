using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Itsl2.Core.Models;
using Itsl2.Core.Services;

namespace Itsl2.App;

public partial class ModDownloadWindow : Window
{
    private readonly ModrinthService _modrinthService = new();
    private readonly string _gameDirectory;

    public ModDownloadWindow(string gameDirectory)
    {
        InitializeComponent();
        _gameDirectory = gameDirectory;
        Loaded += async (_, _) => await SearchAsync();
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async Task SearchAsync()
    {
        SetBusy(true, "正在搜索 Modrinth...");
        try
        {
            var loader = (LoaderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var results = await _modrinthService.SearchAsync(SearchBox.Text, VersionBox.Text, loader);
            ResultsList.ItemsSource = results;
            StatusText.Text = results.Count == 0 ? "没有找到匹配的模组" : $"找到 {results.Count} 个结果";
        }
        catch (HttpRequestException)
        {
            StatusText.Text = "网络请求失败，请检查网络连接后重试";
        }
        catch (TaskCanceledException)
        {
            StatusText.Text = "搜索已取消";
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ModSearchResult mod }) return;
        var loader = (LoaderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        SetBusy(true, $"正在下载 {mod.Title}...");
        try
        {
            var path = await _modrinthService.DownloadLatestAsync(mod, _gameDirectory, VersionBox.Text, loader);
            StatusText.Text = $"已安装到 {path}";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            StatusText.Text = $"下载失败：{ex.Message}";
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private void SetBusy(bool busy, string message)
    {
        SearchButton.IsEnabled = !busy;
        ResultsList.IsEnabled = !busy;
        DownloadProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = message;
    }

    protected override void OnClosed(EventArgs e)
    {
        _modrinthService.Dispose();
        base.OnClosed(e);
    }
}