# C# API

本文说明 RustBridge 类库提供的 C# 类型，以及你在插件工程里通常需要覆盖什么。

## RustPluginBase

`RustPluginBase` 是插件入口基类。它实现了 `MSLX.SDK.IPlugin`，并负责：

- 暴露 MSLX 插件元数据。
- 在 `OnLoad()` 中加载 Rust 原生库。
- 把日志回调传给 Rust。
- 把 SDK 回调传给 Rust。
- 在 `OnUnload()` 中通知 Rust 卸载并释放原生库。
- 给 Controller 提供 `HandleRequest()` 和 `FreeResponse()`。

最小示例：

```csharp
using MSLX.Plugin.RustBridge;

public sealed class MyPluginEntry : RustPluginBase
{
    public MyPluginEntry()
    {
        Instance = this;
    }

    public static MyPluginEntry? Instance { get; private set; }

    public override string Id => "mslx-plugin-my-plugin";
}
```

## 插件元数据

必须覆盖：

```csharp
public override string Id => "mslx-plugin-my-plugin";
```

建议覆盖：

```csharp
public override string Name => "My Plugin";
public override string Description => "插件说明";
public override string Version => "1.1.0";
public override string Developer => "Your Name";
public override string AuthorUrl => "https://example.com";
public override string PluginUrl => "https://github.com/example/my-plugin";
```

`Id` 应该和 Controller 路由中的插件 ID 一致。

## RustLibraryName

默认值：

```csharp
protected virtual string RustLibraryName => "mslx_plugin_rustbridge";
```

如果你的 Rust 库名是 `my_plugin_native`，需要覆盖：

```csharp
protected override string RustLibraryName => "my_plugin_native";
```

加载器会自动按平台补文件名：

```text
Linux:   libmy_plugin_native.so
Windows: my_plugin_native.dll
macOS:   libmy_plugin_native.dylib
```

## OnRustLoaded 和 OnRustUnloading

如果你需要在 Rust 初始化成功后做一些 C# 侧工作，可以覆盖：

```csharp
protected override void OnRustLoaded()
{
    // Rust plugin_init 已成功返回 0
}
```

如果你需要在 Rust 卸载前做一些工作，可以覆盖：

```csharp
protected override void OnRustUnloading()
{
    // plugin_unload 调用前
}
```

大多数插件不需要覆盖这两个方法。

## HandleSdkCall

Rust 调用 MSLX SDK 时，会走到：

```csharp
protected virtual string HandleSdkCall(string method, string argsJson)
```

`method` 是 Rust 传来的方法名，例如：

```text
config.servers.get_list
```

`argsJson` 是参数 JSON，例如：

```json
{
  "id": 1
}
```

基类已经内置一些常用 SDK 调用。你可以覆盖它来增加自己的方法：

```csharp
protected override string HandleSdkCall(string method, string argsJson)
{
    if (method == "my.feature")
    {
        return "{\"ok\":true}";
    }

    return base.HandleSdkCall(method, argsJson);
}
```

建议返回合法 JSON 字符串。即使只是返回成功，也推荐：

```json
{"ok":true}
```

而不是普通文本。

## RustBridgeControllerBase

`RustBridgeControllerBase` 是 HTTP 转发基类。它负责：

- 接收 ASP.NET 请求。
- 读取请求体。
- 整理请求头和查询字符串。
- 构造 Rust 请求 JSON。
- 调用 Rust。
- 读取 Rust 响应 JSON。
- 转成 ASP.NET 响应。

最小示例：

```csharp
using Microsoft.AspNetCore.Mvc;
using MSLX.Plugin.RustBridge;

[ApiController]
[Route("api/plugins/mslx-plugin-my-plugin")]
public sealed class MyPluginController : RustBridgeControllerBase
{
    protected override RustPluginBase Plugin
        => MyPluginEntry.Instance
           ?? throw new InvalidOperationException("Plugin has not been loaded.");
}
```

支持的 HTTP 方法：

```text
GET
POST
PUT
DELETE
PATCH
```

如果你需要支持更多方法，可以在自己的 Controller 中加对应 Attribute，或者扩展基类。

## Controller 的路由

推荐格式：

```csharp
[Route("api/plugins/mslx-plugin-my-plugin")]
```

请求：

```text
GET /api/plugins/mslx-plugin-my-plugin/demo
```

Rust 收到的 `sub_path`：

```text
/demo
```

请求：

```text
POST /api/plugins/mslx-plugin-my-plugin/admin/users
```

Rust 收到的 `sub_path`：

```text
/admin/users
```

## RustNativeLoader

`RustNativeLoader` 负责加载原生库并查找导出函数。

加载顺序：

1. 先查找插件程序集内的 `RustBridge.Native.<rid>.<file>` 资源。
2. 如果找到，释放到本机缓存目录并用 `NativeLibrary.Load(path)` 加载。
3. 如果没有内嵌资源，再尝试用 `NativeLibrary.TryLoad(libraryName)` 让系统按默认规则查找。
4. 再尝试 `AppContext.BaseDirectory` 下的平台文件名。
5. 再尝试当前程序集所在目录下的平台文件名。
6. 全部失败时抛出 `DllNotFoundException`。

它会查找四个导出符号：

```text
plugin_init
plugin_unload
plugin_handle_request
plugin_free_response
```

普通插件作者通常不需要直接使用 `RustNativeLoader`。

## Instance 为什么需要自己保存

MSLX 插件入口和 ASP.NET Controller 的生命周期不是同一个创建流程。

`RustPluginEntry` 是 MSLX 创建的。`RustBridgeController` 是 ASP.NET 创建的。Controller 需要找到已经加载好的插件入口，才能调用同一个 Rust 原生库实例。

示例用静态属性连接它们：

```csharp
public static RustPluginEntry? Instance { get; private set; }

public RustPluginEntry()
{
    Instance = this;
}
```

这是最简单的方式。如果你的插件有自己的依赖注入体系，也可以改成更正式的注册方式。
