# Troubleshooting

本文记录常见问题和排查方式。

## 找不到 Rust 动态库

典型错误：

```text
Unable to load Rust native library 'xxx'
```

检查：

- 输出目录里是否存在当前平台对应的原生库。
- `RustPluginEntry.RustLibraryName` 是否正确。
- `.csproj` 中 `RustLibName` 是否正确。
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

## 构建后输出目录里没有 .so 或 .dll

检查 `.csproj` 是否有类似逻辑：

```xml
<Target Name="BuildRust" BeforeTargets="CopyRustLib">
  <Exec WorkingDirectory="$(MSBuildProjectDirectory)/rust"
        Command="cargo build --release" />
</Target>

<Target Name="CopyRustLib" AfterTargets="Build">
  <Copy SourceFiles="$(RustTarget)"
        DestinationFolder="$(OutDir)"
        Condition="Exists('$(RustTarget)')" />
</Target>
```

再检查 `RustTarget` 路径是否和实际 Cargo 产物一致。

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
