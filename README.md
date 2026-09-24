# itsl2

基于 C#、.NET 10 和 WPF 的 Minecraft 启动器。

## 启动方式

在 Windows 10 或更高版本、安装 .NET 10 SDK 后，在项目根目录执行：

```powershell
dotnet run --project .\src\Itsl2.App\Itsl2.App.csproj
```

也可以先生成可执行文件：

```powershell
dotnet publish .\src\Itsl2.App\Itsl2.App.csproj -c Release -r win-x64 --self-contained false
```

生成目录位于 `src\Itsl2.App\bin\Release\net10.0-windows\win-x64\publish`。

## 当前功能

- 蓝色简洁的启动器总览界面
- 实例列表和新建实例
- 实例数据自动保存到本地应用数据目录
- 选择 Minecraft 游戏目录
- Java 运行环境检测
- 启动前条件检查和异步状态反馈
- Modrinth 模组搜索、版本筛选和下载到实例 `mods` 目录

完整的 Minecraft 版本下载、账户登录和 classpath 启动将在后续模块中接入。
