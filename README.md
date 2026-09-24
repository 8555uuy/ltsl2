# itsl2

基于 C#、.NET 10 和 WPF 的 Minecraft 启动器。

## 启动方式

在 Windows 10 或更高版本、安装 .NET 8 SDK 或 .NET 8 Desktop Runtime 后，在项目根目录执行：

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
- 标准 Yggdrasil 第三方皮肤站登录
- Minecraft Server List Ping 联机服务器查询
- Mojang 原版版本清单、客户端、依赖库和资源自动准备
- 使用 Java 真实启动 Minecraft，并在第三方账户登录后注入 authlib-injector

当前启动链路以 Mojang 原版版本为基础，模组加载器和更复杂的版本继承关系将在后续完善。
