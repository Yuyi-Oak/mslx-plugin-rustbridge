using Microsoft.AspNetCore.Mvc;
using MSLX.Plugin.RustBridge;

namespace MSLX.Plugin.RustBridge.Demo.Controllers;

[ApiController]
[Route("api/plugins/mslx-plugin-rustbridge-demo")]
public sealed class RustBridgeController : RustBridgeControllerBase
{
    protected override RustPluginBase Plugin
        => RustPluginEntry.Instance
           ?? throw new InvalidOperationException("RustBridge demo plugin has not been loaded.");
}
