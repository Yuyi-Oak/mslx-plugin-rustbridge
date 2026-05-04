using System.Text;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSLX.Plugin.RustBridge;

[ApiController]
[Route("api/plugins/mslx-plugin-rustbridge/[controller]")]
public class RustBridgeController : ControllerBase
{
    [HttpGet("{**subPath}")]
    [HttpPost("{**subPath}")]
    [HttpPut("{**subPath}")]
    [HttpDelete("{**subPath}")]
    [HttpPatch("{**subPath}")]
    public async Task<IActionResult> Forward(string subPath = "")
    {
        string body = "";
        if (Request.ContentLength > 0)
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            body = await reader.ReadToEndAsync();
        }

        var reqObj = new JObject
        {
            ["method"]   = Request.Method,
            ["sub_path"] = "/" + subPath,
            ["query"]    = Request.QueryString.Value ?? "",
            ["headers"]  = BuildHeadersJson(),
            ["body"]     = body,
        };
        string requestJson = reqObj.ToString(Formatting.None);

        int ret = RustInterop.HandleRequest(requestJson, out IntPtr ptr, out nuint len);
        if (ret != 0)
            return StatusCode(500, new { error = $"RustBridge handler returned error code {ret}" });

        string responseJson;
        try
        {
            responseJson = len == 0
                ? "{}"
                : Encoding.UTF8.GetString(ToByteArray(ptr, len));
        }
        finally
        {
            RustInterop.FreeResponse(ptr, len);
        }

        return ParseRustResponse(responseJson);
    }

    private JObject BuildHeadersJson()
    {
        var obj = new JObject();
        foreach (var (k, v) in Request.Headers)
            obj[k] = v.ToString();
        return obj;
    }

    private IActionResult ParseRustResponse(string json)
    {
        JObject resp;
        try { resp = JObject.Parse(json); }
        catch { return StatusCode(500, new { error = "RustBridge returned invalid JSON", raw = json }); }

        int status = resp["status"]?.ToObject<int>() ?? 200;

        if (resp["headers"] is JObject headers)
            foreach (var prop in headers.Properties())
                Response.Headers[prop.Name] = prop.Value.ToString();

        var body = resp["body"];
        if (body is null)
            return StatusCode(status);
        if (body.Type == JTokenType.String)
            return StatusCode(status, body.ToString());
        return StatusCode(status, body);
    }

    private static unsafe byte[] ToByteArray(IntPtr ptr, nuint len)
    {
        var result = new byte[(int)len];
        fixed (byte* dst = result)
            Buffer.MemoryCopy((void*)ptr, dst, (int)len, (int)len);
        return result;
    }
}
