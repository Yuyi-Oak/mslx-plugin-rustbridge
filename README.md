# MSLX Plugin RustBridge

RustBridge 是一个给 MSLX 插件使用的 C# 类库。它的定位接近 `MSLX.SDK`：别人的插件工程引用它，然后把真正的业务逻辑写在自己的 Rust 动态库里。

它不是一个需要单独安装到 MSLX 面板里的“前置插件”。真正能安装试跑的是本仓库里的示例插件：`samples/RustBridgeDemo`。

## 它解决什么问题

MSLX 插件入口必须是 C# / ASP.NET。RustBridge 保留一个很薄的 C# 外壳，让 MSLX 能正常加载插件；之后把生命周期、HTTP 请求、日志和 SDK 调用转给 Rust。

简单理解：

```text
MSLX 宿主
  加载 C# 插件 DLL
    调用 RustPluginBase.OnLoad()
      加载 Rust cdylib
        调用 plugin_init()

浏览器或前端请求插件 API
  进入 C# Controller
    转成 JSON 请求
      调用 Rust plugin_handle_request()
        Rust 返回 JSON 响应
          C# 转回 ASP.NET IActionResult
```

## 当前仓库形态

```text
mslx-plugin-rustbridge/
├── csharp/                                  RustBridge 类库工程
│   ├── MSLX.Plugin.RustBridge.csproj
│   ├── RustPluginBase.cs                    插件生命周期、日志、SDK 回调
│   ├── RustBridgeControllerBase.cs          HTTP 请求转发基类
│   └── RustNativeLoader.cs                  Rust 动态库加载器
├── samples/RustBridgeDemo/                  可试装的示例插件
│   ├── MSLX.Plugin.RustBridge.Demo.csproj
│   ├── RustPluginEntry.cs
│   ├── RustBridgeController.cs
│   └── rust/                                示例 Rust cdylib
├── docs/
│   ├── architecture.md
│   ├── csharp-api.md
│   ├── getting-started.md
│   ├── rust-plugin-api.md
│   ├── sample.md
│   └── troubleshooting.md
├── mslx-plugin-rustbridge.sln
├── build.sh
└── build.bat
```

## 环境要求

- .NET SDK 10 或更新的兼容版本。
- Rust 工具链，包含 `cargo`。
- 能获取 NuGet 包 `MSLX.SDK`、`Newtonsoft.Json` 和 `ILRepack.Lib.MSBuild.Task`。

## 快速构建

Linux / macOS:

```bash
chmod +x build.sh
./build.sh
```

Windows:

```bat
build.bat
```

构建完成后会得到两类产物：

```text
csharp/bin/Release/MSLX.Plugin.RustBridge.1.2.0.nupkg
samples/RustBridgeDemo/bin/Release/net10.0/MSLX.Plugin.RustBridge.Demo.dll
```

示例插件会把当前平台的 Rust 原生库内嵌到 `MSLX.Plugin.RustBridge.Demo.dll`。运行时会释放到本机缓存目录后再加载。默认构建会使用当前 .NET SDK 的宿主 RID；显式指定 `RuntimeIdentifier` 时，会优先支持常见的 Windows、Linux、Linux musl、macOS 和 FreeBSD x64/Arm64/Arm 目标。

常用构建参数：

```bash
SKIP_CLEAN=1 ./build.sh
CONFIGURATION=Debug ./build.sh
```

`SKIP_CLEAN=1` 用于跳过清理，加快重复构建。`CONFIGURATION=Debug` 用于构建 Debug。

## 试跑示例插件

构建后，把下面这个文件放进 MSLX 插件目录：

```text
samples/RustBridgeDemo/bin/Release/net10.0/MSLX.Plugin.RustBridge.Demo.dll
```

然后在 MSLX 中加载插件。示例插件 ID 是：

```text
mslx-plugin-rustbridge-demo
```

示例路由：

```text
GET  /api/plugins/mslx-plugin-rustbridge-demo/demo
GET  /api/plugins/mslx-plugin-rustbridge-demo/servers
GET  /api/plugins/mslx-plugin-rustbridge-demo/servers/{id}
POST /api/plugins/mslx-plugin-rustbridge-demo/echo
```

## 在自己的插件中使用

推荐先读 [Getting Started](docs/getting-started.md)。最小接入需要两个 C# 类型和一个 Rust cdylib；示例工程会在构建时把 cdylib 内嵌进插件 DLL。

插件入口：

```csharp
using MSLX.Plugin.RustBridge;

namespace My.Plugin;

public sealed class MyPluginEntry : RustPluginBase
{
    public MyPluginEntry()
    {
        Instance = this;
    }

    public static MyPluginEntry? Instance { get; private set; }

    public override string Id => "mslx-plugin-my-plugin";
    public override string Name => "My Plugin";
    public override string Version => "1.2.0";

    protected override string RustLibraryName => "my_plugin_native";
}
```

HTTP Controller：

```csharp
using Microsoft.AspNetCore.Mvc;
using MSLX.Plugin.RustBridge;

namespace My.Plugin.Controllers;

[ApiController]
[Route("api/plugins/mslx-plugin-my-plugin")]
public sealed class MyPluginController : RustBridgeControllerBase
{
    protected override RustPluginBase Plugin
        => MyPluginEntry.Instance
           ?? throw new InvalidOperationException("Plugin has not been loaded.");
}
```

Rust 的 `Cargo.toml` 要让 `[lib] name` 和 `RustLibraryName` 对齐：

```toml
[lib]
name = "my_plugin_native"
crate-type = ["cdylib"]
```

默认部署时只需要插件 DLL。若关闭了内嵌原生库，插件 DLL 和 Rust 原生库才需要在同一个输出目录，或位于系统动态库加载器能找到的位置。

## 文档导航

- [Getting Started](docs/getting-started.md)：从零创建或改造一个 RustBridge 插件。
- [Architecture](docs/architecture.md)：整体架构、生命周期、请求流和产物关系。
- [C# API](docs/csharp-api.md)：`RustPluginBase`、`RustBridgeControllerBase`、`RustNativeLoader`。
- [Rust Plugin API](docs/rust-plugin-api.md)：Rust 侧 ABI、JSON 协议、内存所有权、SDK 回调。
- [Sample](docs/sample.md)：示例插件目录、构建目标和示例路由。
- [Troubleshooting](docs/troubleshooting.md)：常见构建、加载和运行问题。

## 开发建议

- 如果你只是想写业务逻辑，优先改自己的 Rust `router.rs`。
- 如果你要增加 MSLX SDK 能力，需要同时扩展 C# 的 `HandleSdkCall` 和 Rust 的 `SdkBridge`。
- 如果你要改 Rust 动态库名字，要同时改 C# 的 `RustLibraryName`、项目构建里的 `RustBridgeRustLibName` 和 Rust `Cargo.toml` 的 `[lib] name`。
- 不要把 Rust 源码目录复制到插件部署目录；默认内嵌打包后，部署目录只需要插件 DLL。
