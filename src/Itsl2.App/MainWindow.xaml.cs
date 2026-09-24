using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using Itsl2.Core.Models;
using Itsl2.Core.Services;

namespace Itsl2.App;

public partial class MainWindow : Window
{
    private readonly JavaRuntimeService _javaRuntimeService = new();
    private readonly InstanceStore _instanceStore = new();
    private readonly ObservableCollection<GameInstance> _instances = new();
    private JavaRuntime? _javaRuntime;

    public MainWindow()
    {
        InitializeComponent();
        InstanceList.ItemsSource = _instances;
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
        var instance = new GameInstance($"新实例 {_instances.Count + 1}", "待配置", "未设置游戏目录");
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

        SetBusy(true, "正在检查启动条件...");
        await Task.Delay(180);
        LaunchStatus.Text = "待安装";
        SetBusy(false, "实例目录已配置，下一步接入版本文件下载后即可启动");
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
}