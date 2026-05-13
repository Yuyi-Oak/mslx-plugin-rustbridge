# Architecture

RustBridge 的目标是把“MSLX 必须加载 C# 插件”和“业务逻辑想写在 Rust”这两件事接起来。

## 为什么需要 C# 外壳

MSLX 插件运行在 .NET / ASP.NET 环境中，插件入口需要实现 `MSLX.SDK.IPlugin`，HTTP 接口也走 ASP.NET Controller。

Rust 动态库不能直接被 MSLX 当作插件加载。所以 RustBridge 提供一个 C# 类库，让你的插件只写很少的 C#：

- 一个插件入口，继承 `RustPluginBase`。
- 一个 Controller，继承 `RustBridgeControllerBase`。
- 一个 Rust cdylib，导出固定 ABI。

## 运行时组件

```text
MSLX host
  |
  | loads plugin dll
  v
Plugin entry : RustPluginBase
  |
  | loads native library
  v
RustNativeLoader
  |
  | C ABI calls
  v
Rust cdylib

HTTP request
  |
  v
Controller : RustBridgeControllerBase
  |
  | JSON request
  v
plugin_handle_request()
  |
  | JSON response
  v
ASP.NET IActionResult
```

## 类库工程和示例工程

根目录 `csharp/` 是可复用类库。它应该被打包成 NuGet，供别的插件引用。

```text
csharp/MSLX.Plugin.RustBridge.csproj
```

示例工程是一个真正的 MSLX 插件，用来证明类库如何使用。

```text
samples/RustBridgeDemo/MSLX.Plugin.RustBridge.Demo.csproj
```

示例工程负责：

- 引用 RustBridge 类库。
- 提供插件元数据。
- 提供插件 API 路由前缀。
- 构建 Rust cdylib。
- 把 Rust cdylib 内嵌到插件 DLL。
- 合并托管依赖，方便试装。

## 生命周期

### 加载

```text
MSLX 创建插件入口实例
  -> RustPluginEntry 构造函数保存 Instance
  -> MSLX 调用 OnLoad()
  -> RustPluginBase 创建 RustNativeLoader
  -> RustNativeLoader 加载原生库
  -> RustNativeLoader 查找四个导出函数
  -> 调用 plugin_init(...)
  -> Rust 保存日志回调和 SDK 回调
```

`plugin_init` 返回 `0` 时表示加载成功。返回其他值时，C# 会写错误日志。

### 请求

```text
HTTP 请求进入 Controller
  -> RustBridgeControllerBase 读取 method/path/query/headers/body
  -> 转成 request JSON
  -> 调用 RustPluginBase.HandleRequest(...)
  -> 调用 Rust plugin_handle_request(...)
  -> Rust 返回 response JSON 指针和长度
  -> C# 复制响应字节
  -> C# 调用 plugin_free_response(...)
  -> C# 把 response JSON 转成 IActionResult
```

### 卸载

```text
MSLX 调用 OnUnload()
  -> RustPluginBase.OnRustUnloading()
  -> plugin_unload()
  -> RustNativeLoader.Dispose()
  -> NativeLibrary.Free(...)
```

## SDK 调用方向

Rust 不能直接调用 .NET SDK，所以 SDK 调用方向是反过来的：

```text
Rust sdk.config_servers_get_list()
  -> sdk_call("config.servers.get_list", "{}")
  -> C# RustPluginBase.HandleSdkCall(...)
  -> MSLX.SDK
  -> JSON result
  -> Rust serde_json::Value
```

这样 Rust 侧只依赖字符串协议，不需要直接绑定 .NET。

## 产物关系

RustBridge 类库产物：

```text
MSLX.Plugin.RustBridge.1.2.0.nupkg
```

示例插件产物：

```text
MSLX.Plugin.RustBridge.Demo.dll
```

类库 NuGet 不包含 Rust 动态库。Rust 动态库属于具体插件，示例工程会把它作为资源内嵌到插件 DLL。

## 为什么不把 Rust 打进 NuGet

如果 NuGet 包内置一个 Rust 动态库，会带来几个问题：

- 每个插件的 Rust 业务逻辑都不同。
- 不同平台需要不同原生库。
- 插件作者需要自己控制 Rust 构建、依赖和发布节奏。
- NuGet 包应该只提供可复用 C# 桥接能力。

所以当前设计是：RustBridge NuGet 只带 C# 桥接代码和默认构建规则；插件工程自己构建 Rust cdylib，并决定是内嵌到插件 DLL，还是作为旁边文件部署。
