using System.IO;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Itsl2.Core.Models;
using Itsl2.Core.Services;

namespace Itsl2.App;

public partial class MainWindow : Window
{
    private readonly JavaRuntimeService _javaRuntimeService = new();
    private readonly MinecraftInstallService _minecraftInstallService = new();
    private readonly MinecraftLaunchService _minecraftLaunchService = new();
    private readonly InstanceStore _instanceStore = new();
    private readonly ObservableCollection<GameInstance> _instances = new();
    private JavaRuntime? _javaRuntime;
    private ThirdPartyAccount? _account;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Random _random = new();
    private static readonly string[] Tips =
    [
        "愿你的世界今天一次加载成功",
        "今天适合探索一片没去过的地形",
        "先备份存档，再去挑战远古城市",
        "好玩的整合包，值得慢慢研究",
        "把截图留住，下一次登录还能看见",
        "服务器延迟低的时候，适合和朋友联机"
    ];

    public MainWindow()
    {
        InitializeComponent();
        InstanceList.ItemsSource = _instances;
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        SetBusy(true, "正在加载实例...");
        var savedInstances = await _instanceStore.LoadAsync();
        if (savedInstances.Count == 0)
            _instances.Add(new GameInstance("生存服实例", "Minecraft 1.20.1", "未设置游戏目录"));
        else
            foreach (var instance in savedInstances) _instances.Add(instance);

        InstanceList.SelectedIndex = 0;
        InstanceCount.Text = _instances.Count.ToString();
        await DetectJavaAsync();
        SetBusy(false, _javaRuntime is null ? "启动前需要配置 Java 运行环境" : "环境检查完成");
    }

    private async void DetectJava_Click(object sender, RoutedEventArgs e) => await DetectJavaAsync();

    private async Task DetectJavaAsync()
    {
        SetBusy(true, "正在检测 Java...");
        JavaStatus.Text = "正在检测 Java...";
        _javaRuntime = await _javaRuntimeService.DetectAsync();
        JavaStatus.Text = _javaRuntime is null
            ? "未找到 Java，请安装 Java 17 或更高版本"
            : $"Java {_javaRuntime.Version.Major} · {_javaRuntime.Vendor}";
        SetBusy(false, _javaRuntime is null ? "启动前需要配置 Java 运行环境" : "Java 环境已就绪");
    }

    private void InstanceList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (InstanceList.SelectedItem is not GameInstance instance) return;
        SelectedInstanceName.Text = instance.Name;
        SelectedInstancePath.Text = instance.DisplayPath;
        CurrentVersion.Text = instance.Version.Replace("Minecraft ", "", StringComparison.Ordinal);
    }

    private async void AddInstance_Click(object sender, RoutedEventArgs e)
    {
        var instance = new GameInstance($"新实例 {_instances.Count + 1}", "Minecraft 1.20.1", "未设置游戏目录");
        _instances.Add(instance);
        InstanceList.SelectedItem = instance;
        InstanceCount.Text = _instances.Count.ToString();
        ActionMessage.Text = "实例已创建，下一步可以配置游戏目录和版本";
        await SaveInstancesAsync();
    }

    private async void ConfigureInstance_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not GameInstance instance) return;

        var dialog = new OpenFolderDialog
        {
            Title = "选择 Minecraft 游戏目录",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        var updated = instance with { GameDirectory = dialog.FolderName };
        var index = _instances.IndexOf(instance);
        _instances[index] = updated;
        InstanceList.SelectedIndex = index;
        await SaveInstancesAsync();
        ActionMessage.Text = "游戏目录已保存，可以继续接入版本安装";
    }

    private void DownloadCenter_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not GameInstance { GameDirectory: var gameDirectory }
            || gameDirectory == "未设置游戏目录")
        {
            ActionMessage.Text = "请先为当前实例配置游戏目录";
            return;
        }

        var window = new ModDownloadWindow(gameDirectory) { Owner = this };
        window.ShowDialog();
    }

    private void LoginAccount_Click(object sender, RoutedEventArgs e)
    {
        var window = new ThirdPartyLoginWindow { Owner = this };
        if (window.ShowDialog() != true || window.Account is null) return;
        _account = window.Account;
        AccountStatus.Text = $"皮肤站：{_account.ProfileName}";
        AccountCardName.Text = _account.ProfileName;
        AccountCardMeta.Text = $"{_account.Username} · {_account.ServerUrl}";
        ActionMessage.Text = $"已登录 {_account.ProfileName}，启动时将使用该账户会话";
    }

    private void Multiplayer_Click(object sender, RoutedEventArgs e)
    {
        var window = new MultiplayerWindow { Owner = this };
        window.ShowDialog();
    }

    private void OpenGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not GameInstance instance)
        {
            ActionMessage.Text = "请先选择实例";
            return;
        }
        OpenDirectory(instance.GameDirectory, "游戏目录尚未配置");
    }

    private void OpenModsDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not GameInstance instance)
        {
            ActionMessage.Text = "请先选择实例";
            return;
        }
        if (instance.GameDirectory == "未设置游戏目录")
        {
            ActionMessage.Text = "请先配置游戏目录";
            return;
        }

        var modsDirectory = Path.Combine(instance.GameDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);
        OpenDirectory(modsDirectory, "无法打开模组目录");
    }

    private void RandomTip_Click(object sender, RoutedEventArgs e)
    {
        TipText.Text = Tips[_random.Next(Tips.Length)];
    }

    private void UpdateClock()
    {
        ClockText.Text = $"{DateTime.Now:yyyy 年 M 月 d 日 HH:mm:ss} · 今天也要顺利启动";
    }

    private void OpenDirectory(string path, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            ActionMessage.Text = errorMessage;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ActionMessage.Text = $"打开目录失败：{ex.Message}";
        }
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not GameInstance instance) return;
        if (_javaRuntime is null)
        {
            LaunchStatus.Text = "缺少 Java";
            ActionMessage.Text = "未检测到可用的 Java 运行环境";
            return;
        }

        if (instance.GameDirectory == "未设置游戏目录")
        {
            LaunchStatus.Text = "待配置";
            ActionMessage.Text = "请先设置游戏目录和 Minecraft 版本";
            return;
        }

        SetBusy(true, "正在准备 Minecraft 文件...");
        try
        {
            var version = instance.Version.Replace("Minecraft ", "", StringComparison.Ordinal).Trim();
            var progress = new Progress<string>(message => ActionMessage.Text = message);
            var prepared = await _minecraftInstallService.PrepareAsync(version, instance.GameDirectory, progress);
            string? authlibPath = null;
            if (_account is not null)
                authlibPath = await _minecraftInstallService.EnsureAuthlibInjectorAsync(progress);

            var process = _minecraftLaunchService.Launch(prepared, _javaRuntime, _account, authlibPath);
            LaunchStatus.Text = "运行中";
            ActionMessage.Text = $"{instance.Name} 已启动";
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispatcher.Invoke(() =>
            {
                LaunchStatus.Text = "已退出";
                ActionMessage.Text = $"{instance.Name} 已退出，退出码：{process.ExitCode}";
            });
        }
        catch (OperationCanceledException)
        {
            LaunchStatus.Text = "已取消";
            ActionMessage.Text = "启动操作已取消";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or FileNotFoundException)
        {
            LaunchStatus.Text = "启动失败";
            ActionMessage.Text = ex.Message;
        }
        finally
        {
            SetBusy(false, ActionMessage.Text);
        }
    }

    private async Task SaveInstancesAsync()
    {
        try
        {
            await _instanceStore.SaveAsync(_instances);
        }
        catch (IOException)
        {
            ActionMessage.Text = "实例保存失败，请检查应用数据目录权限";
        }
    }

    private void SetBusy(bool isBusy, string message)
    {
        LaunchButton.IsEnabled = !isBusy;
        DetectJavaButton.IsEnabled = !isBusy;
        OperationProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        ActionMessage.Text = message;
    }

    protected override void OnClosed(EventArgs e)
    {
        _clockTimer.Stop();
        _minecraftInstallService.Dispose();
        base.OnClosed(e);
    }
}