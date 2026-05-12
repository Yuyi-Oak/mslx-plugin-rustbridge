using MSLX.Plugin.RustBridge;

namespace MSLX.Plugin.RustBridge.Demo;

public sealed class RustPluginEntry : RustPluginBase
{
    public RustPluginEntry()
    {
        Instance = this;
    }

    public static RustPluginEntry? Instance { get; private set; }

    public override string Id => "mslx-plugin-rustbridge-demo";
    public override string Name => "MSLX RustBridge Demo";
    public override string Description => "RustBridge 类库的示例 MSLX 插件";
    public override string Version => "1.1.0";
    public override string Developer => "Yuyi-Oak";
}
