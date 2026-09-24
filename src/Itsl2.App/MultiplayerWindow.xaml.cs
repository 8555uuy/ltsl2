using System.IO;
using System.Net.Sockets;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Itsl2.Core.Models;
using Itsl2.Core.Services;

namespace Itsl2.App;

public partial class MultiplayerWindow : Window
{
    private readonly MinecraftServerService _serverService = new();
    private readonly ObservableCollection<ServerListItem> _servers = new();

    public MultiplayerWindow()
    {
        InitializeComponent();
        ServerList.ItemsSource = _servers;
    }

    private async void Query_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            StatusText.Text = "端口必须是 1 到 65535 之间的数字";
            return;
        }

        QueryButton.IsEnabled = false;
        StatusText.Text = "正在查询服务器状态...";
        try
        {
            var host = HostBox.Text.Trim();
            var status = await _serverService.QueryStatusAsync(host, port);
            var item = _servers.FirstOrDefault(server => server.Bookmark.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
                                                         && server.Bookmark.Port == port);
            if (item is null)
            {
                item = new ServerListItem(new ServerBookmark(host, host, port));
                _servers.Add(item);
            }
            item.StatusText = $"{status.Version} · {status.OnlinePlayers}/{status.MaximumPlayers} 人 · {status.LatencyMilliseconds} ms\n{status.Description}";
            ServerList.Items.Refresh();
            StatusText.Text = "查询完成";
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException or EndOfStreamException or InvalidOperationException or OperationCanceledException)
        {
            StatusText.Text = $"查询失败：{ex.Message}";
        }
        finally
        {
            QueryButton.IsEnabled = true;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ServerListItem item })
        {
            Clipboard.SetText(item.Bookmark.Address);
            StatusText.Text = $"已复制 {item.Bookmark.Address}";
        }
    }

    private sealed class ServerListItem
    {
        public ServerListItem(ServerBookmark bookmark) => Bookmark = bookmark;
        public ServerBookmark Bookmark { get; }
        public string Name => Bookmark.Host;
        public string Address => Bookmark.Address;
        public string StatusText { get; set; } = "尚未查询";
    }
}