# Getting Started

本文按“我要写一个自己的 MSLX Rust 插件”的视角说明完整流程。

## 先理解两个产物

使用 RustBridge 后，一个可部署插件通常至少有两个文件：

```text
My.Plugin.dll
libmy_plugin_native.so
```

Windows 是：

```text
My.Plugin.dll
my_plugin_native.dll
```

`My.Plugin.dll` 是 MSLX 能识别的 C# 插件。`my_plugin_native` 是真正执行 Rust 业务逻辑的动态库。

## 方式一：从示例复制

这是最简单的方式，适合第一次接入。

复制示例目录：

```text
samples/RustBridgeDemo/
```

然后按下面步骤修改。

### 修改插件 ID

在 `RustPluginEntry.cs` 中修改：

```csharp
public override string Id => "mslx-plugin-my-plugin";
public override string Name => "My Plugin";
public override string Version => "1.0.0";
```

插件 ID 建议符合 MSLX 约定：

```text
mslx-plugin-xxx
```

### 修改 Controller 路由

在 `RustBridgeController.cs` 中修改：

```csharp
[Route("api/plugins/mslx-plugin-my-plugin")]
```

这里的插件 ID 应该和 `RustPluginEntry.Id` 一致。这样前端请求才会进入正确的插件。

### 修改 Rust 库名

如果你继续使用示例默认库名 `mslx_plugin_rustbridge`，可以不改。

如果你想改成自己的库名，例如 `my_plugin_native`，需要改三处。

第一处，`RustPluginEntry.cs`：

```csharp
protected override string RustLibraryName => "my_plugin_native";
```

第二处，插件 `.csproj`：

```xml
<RustLibName>my_plugin_native</RustLibName>
```

第三处，`rust/Cargo.toml`：

```toml
[lib]
name = "my_plugin_native"
crate-type = ["cdylib"]
```

### 修改 Rust 业务逻辑

主要改这个文件：

```text
rust/src/router.rs
```

新增接口示例：

```rust
("GET", "/hello") => PluginResponse::ok(json!({
    "message": "Hello from Rust"
})),
```

如果逻辑变复杂，建议把处理函数拆出来：

```rust
("GET", "/hello") => handle_hello(&req, sdk),
```

```rust
fn handle_hello(_req: &PluginRequest, _sdk: &SdkBridge) -> PluginResponse {
    PluginResponse::ok(json!({
        "message": "Hello from Rust"
    }))
}
```

### 构建

在插件工程目录执行：

```bash
dotnet build -c Release
```

如果你沿用示例 `.csproj`，它会自动执行：

```bash
cargo build --release
```

并把 Rust 动态库复制到输出目录。

## 方式二：在已有 C# 插件中引用 RustBridge

如果你已经有一个 MSLX 插件工程，可以只引用 RustBridge 类库。

如果使用本仓库生成的本地 nupkg：

```bash
dotnet pack csharp/MSLX.Plugin.RustBridge.csproj -c Release
dotnet nuget add source ./csharp/bin/Release -n rustbridge-local
```

然后在你的插件工程中添加：

```xml
<PackageReference Include="MSLX.Plugin.RustBridge" Version="1.0.0" />
```

开发阶段也可以直接用 `ProjectReference`：

```xml
<ProjectReference Include="../../csharp/MSLX.Plugin.RustBridge.csproj" />
```

### 添加插件入口

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
    public override string Version => "1.0.0";
    public override string Developer => "Your Name";

    protected override string RustLibraryName => "my_plugin_native";
}
```

`Instance` 的作用是让 Controller 能找到 MSLX 已加载的插件实例。MSLX 负责创建插件入口实例，ASP.NET 负责创建 Controller 实例，这两者不是同一个创建流程，所以需要一个连接点。

### 添加 Controller

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

`RustBridgeControllerBase` 会处理 `GET`、`POST`、`PUT`、`DELETE` 和 `PATCH`。请求路径中插件前缀之后的部分会变成 Rust 请求里的 `sub_path`。

例如：

```text
GET /api/plugins/mslx-plugin-my-plugin/servers/1
```

Rust 收到：

```json
{
  "method": "GET",
  "sub_path": "/servers/1"
}
```

### 添加 Rust 工程

最小 `Cargo.toml`：

```toml
[package]
name = "my-plugin-native"
version = "1.0.0"
edition = "2024"

[lib]
name = "my_plugin_native"
crate-type = ["cdylib"]

[dependencies]
serde = { version = "1", features = ["derive"] }
serde_json = "1"
```

最小导出函数请参考：

```text
samples/RustBridgeDemo/rust/src/lib.rs
```

## 部署

部署时至少复制两个文件：

```text
My.Plugin.dll
libmy_plugin_native.so
```

如果你的构建没有把依赖合并到插件 DLL，还需要复制相关依赖 DLL。示例工程通过 ILRepack 合并托管依赖，所以输出目录最后只保留：

```text
MSLX.Plugin.RustBridge.Demo.dll
libmslx_plugin_rustbridge.so
```

## 检查清单

发布或试装前建议检查：

- `RustPluginEntry.Id` 和 Controller `[Route]` 中的插件 ID 一致。
- `RustLibraryName`、`.csproj` 的 `RustLibName`、`Cargo.toml` 的 `[lib] name` 一致。
- 输出目录里有插件 DLL。
- 输出目录里有当前平台对应的 Rust 原生库。
- Rust 导出了 `plugin_init`、`plugin_handle_request`、`plugin_free_response`、`plugin_unload`。
- Rust 响应 JSON 能被 `JObject.Parse` 解析。
