# Troubleshooting

本文记录常见问题和排查方式。

## 找不到 Rust 动态库

典型错误：

```text
Unable to load Rust native library 'xxx'
```

检查：

- 插件 DLL 内是否存在当前平台对应的 `RustBridge.Native.<rid>.<file>` 资源。
- 如果没有内嵌资源，输出目录里是否存在当前平台对应的原生库。
- `RustPluginEntry.RustLibraryName` 是否正确。
- `.csproj` 中 `RustBridgeRustLibName` 是否正确。
- `Cargo.toml` 中 `[lib] name` 是否正确。
- Linux 文件名是否带 `lib` 前缀和 `.so` 后缀。
- Windows 文件名是否是 `.dll`。
- macOS 文件名是否带 `lib` 前缀和 `.dylib` 后缀。

示例：

```text
RustLibraryName = "my_plugin_native"
Linux file      = libmy_plugin_native.so
Windows file    = my_plugin_native.dll
macOS file      = libmy_plugin_native.dylib
```

## 找不到导出函数

如果库能加载，但初始化失败，可能是导出符号不对。

检查 Rust 是否有：

```rust
#[unsafe(no_mangle)]
pub extern "C" fn plugin_init(...) -> i32
```

以及：

```rust
plugin_handle_request
plugin_free_response
plugin_unload
```

函数名必须完全一致。不要改大小写，不要加命名空间前缀。

## Rust 返回 invalid JSON

典型响应：

```json
{
  "error": "RustBridge returned invalid JSON",
  "raw": "..."
}
```

说明 `plugin_handle_request` 返回的字符串不是合法 JSON 对象。

检查：

- 是否调用了 `serde_json::to_string(&response)`。
- `response_ptr` 和 `response_len` 是否正确。
- 返回的内存是否在 C# 读取前仍然有效。
- 是否把普通文本当成完整响应返回了。

正确响应至少应该像这样：

```json
{
  "status": 200,
  "body": {
    "ok": true
  }
}
```

## 请求返回 500 且带 RustBridge handler error code

典型响应：

```json
{
  "error": "RustBridge handler returned error code -2"
}
```

这是 Rust 的 `plugin_handle_request` 返回了非 `0`。

按当前示例：

```text
-1  request_json 不是有效 UTF-8
-2  请求 JSON 解析失败
-3  响应 JSON 序列化失败
```

建议在 Rust 中用日志回调记录具体错误，例如：

```rust
sdk().error(&format!("Failed to parse request JSON: {e}"));
```

## plugin.config.* 返回 SDK 版本错误

`plugin.config.get_data_path`、`plugin.config.read`、`plugin.config.write`、`plugin.config.read_key`、`plugin.config.write_key` 需要 MSLX SDK 1.4.3+。

如果运行环境还没有这组接口，RustBridge 会返回类似错误：

```json
{
  "error": "MSLX SDK plugin config API requires MSLX.SDK 1.4.3 or newer."
}
```

处理方式：

- 升级 MSLX 到包含 SDK 1.4.3+ 的版本。
- 如果插件依赖这组方法，建议把插件入口的 `MinSDKVersion` 设为 `1.4.3` 或更高。
- 如果只是读取 MSLX 主配置，继续使用 `config.main.*`；不要把插件自己的业务数据写进主配置。

## Controller 能访问，但 Rust 收到的路径不对

检查 Controller 路由。

推荐：

```csharp
[Route("api/plugins/mslx-plugin-my-plugin")]
```

如果写成：

```csharp
[Route("api/plugins/mslx-plugin-my-plugin/[controller]")]
```

那么 URL 中会多一个 Controller 段，Rust 收到的 `sub_path` 也会和预期不同。

## 插件 ID 和路由不一致

插件入口：

```csharp
public override string Id => "mslx-plugin-my-plugin";
```

Controller：

```csharp
[Route("api/plugins/mslx-plugin-my-plugin")]
```

这两个地方建议保持一致。大小写也建议一致，统一小写最稳妥。

## 插件 DLL 里没有内嵌原生库

先确认插件工程是否引用了 RustBridge NuGet，或者手动导入了本仓库的构建规则：

```xml
<PackageReference Include="MSLX.Plugin.RustBridge" Version="1.2.0" />
```

默认规则只会在插件工程下存在 `rust/Cargo.toml` 时启用。常见配置如下：

```xml
<PropertyGroup>
  <RustBridgeRustProjectDir>$(MSBuildProjectDirectory)/rust</RustBridgeRustProjectDir>
  <RustBridgeRustLibName>my_plugin_native</RustBridgeRustLibName>
</PropertyGroup>
```

再检查下面几项：

- `RustBridgeRustProjectDir` 是否指向真正的 Rust 工程。
- `RustBridgeRustLibName` 是否和 Rust `Cargo.toml` 的 `[lib] name` 一致。
- `RustBridgeRuntimeIdentifier` 是否是当前要打包的 RID，例如 `linux-x64`、`linux-arm64`、`win-arm64`、`osx-arm64`。
- 显式指定 RID 时，是否已经安装对应 Rust target，例如 `rustup target add aarch64-unknown-linux-gnu`。
- 如果使用 `RustBridgeCargoCommand=cross`，CI 机器是否能运行 Docker 或 Podman。
- 如果你手动指定了 `RustBridgeNativePath`，路径是否指向真实存在的 `.dll`、`.so` 或 `.dylib`。

内嵌资源名应该类似：

```text
RustBridge.Native.linux-arm64.libmy_plugin_native.so
RustBridge.Native.win-arm64.my_plugin_native.dll
RustBridge.Native.osx-arm64.libmy_plugin_native.dylib
```

## Linux 下权限或依赖问题

如果 `.so` 存在但仍加载失败，可能是原生依赖缺失或平台不匹配。

可检查：

```bash
file libmy_plugin_native.so
ldd libmy_plugin_native.so
```

常见问题：

- 在不同架构机器上编译，例如 x64 和 arm64 混用。
- Rust 库依赖了系统动态库，但部署环境没有安装。
- 文件复制时权限异常。

## Windows 下 DLL 加载失败

检查：

- 文件名是否是 `my_plugin_native.dll`，不是 `libmy_plugin_native.dll`。
- DLL 是否和插件 DLL 在同一个目录。
- 编译目标架构是否和 MSLX 进程一致。
- 是否缺少 Rust 或其他原生依赖引入的 DLL。

可以使用开发者命令行或工具查看 DLL 依赖。

## `cargo` 命令找不到

说明 Rust 工具链没有安装，或 `cargo` 不在 PATH。

检查：

```bash
cargo --version
rustc --version
```

如果命令不存在，先安装 Rust 工具链。

## NuGet restore 失败

检查：

- 当前环境是否能访问 NuGet 源。
- 是否能获取 `MSLX.SDK`。
- 是否配置了本地 RustBridge nupkg 源。

本地源示例：

```bash
dotnet nuget add source ./csharp/bin/Release -n rustbridge-local
```

如果只是开发本仓库，优先使用解决方案里的 `ProjectReference`，不需要先安装 nupkg。

## ILRepack 后还留下多余 DLL

示例工程 Release 会合并托管依赖，并删除合并过的 DLL。如果你增加了新的托管依赖，可能需要把它加入 `.csproj` 的 `MergeDependencies` 目标。

如果你不想合并依赖，可以移除 ILRepack 目标，然后部署输出目录里的全部必要 DLL。

## Rust panic 导致进程异常

不要让 panic 穿过 FFI 边界。建议在 Rust 业务层把错误转成：

```rust
PluginResponse::internal_error(&err)
```

或在 FFI 边界附近使用 `std::panic::catch_unwind` 包住不可信逻辑。

## 什么时候应该问我

如果你遇到下面情况，建议先停下确认设计：

- 想让多个 Rust 动态库共用一个 C# 插件入口。
- 想在 Rust 中启动长期后台任务。
- 想把异步运行时如 Tokio 引入插件生命周期。
- 想把大量 MSLX SDK 方法暴露给 Rust。
- 想把前端资源一起嵌入插件 DLL。

这些都能做，但需要先定好生命周期和部署策略。
