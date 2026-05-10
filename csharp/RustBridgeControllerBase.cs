using System.Text;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSLX.Plugin.RustBridge;

public abstract class RustBridgeControllerBase : ControllerBase
{
    protected abstract RustPluginBase Plugin { get; }

    [HttpGet("{**subPath}")]
    [HttpPost("{**subPath}")]
    [HttpPut("{**subPath}")]
    [HttpDelete("{**subPath}")]
    [HttpPatch("{**subPath}")]
    public async Task<IActionResult> Forward(string subPath = "")
    {
        string requestJson = BuildRequestJson(subPath, await ReadBodyAsync());

        var result = ReadRustResponse(requestJson);
        if (result.ErrorCode != 0)
            return StatusCode(500, new { error = $"RustBridge handler returned error code {result.ErrorCode}" });

        return ParseRustResponse(result.Json);
    }

    private async Task<string> ReadBodyAsync()
    {
        if (Request.ContentLength <= 0)
            return "";

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private string BuildRequestJson(string subPath, string body)
        => new JObject
        {
            ["method"] = Request.Method,
            ["sub_path"] = "/" + subPath,
            ["query"] = Request.QueryString.Value ?? "",
            ["headers"] = BuildHeadersJson(),
            ["body"] = body,
        }.ToString(Formatting.None);

    private RustResponseReadResult ReadRustResponse(string requestJson)
    {
        int ret = Plugin.HandleRequest(requestJson, out IntPtr ptr, out nuint len);
        if (ret != 0)
            return new RustResponseReadResult(ret, "");

        try
        {
            var responseJson = len == 0
                ? "{}"
                : Encoding.UTF8.GetString(ToByteArray(ptr, len));
            return new RustResponseReadResult(0, responseJson);
        }
        finally
        {
            Plugin.FreeResponse(ptr, len);
        }
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
        try
        {
            resp = JObject.Parse(json);
        }
        catch
        {
            return StatusCode(500, new { error = "RustBridge returned invalid JSON", raw = json });
        }

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

    private readonly record struct RustResponseReadResult(int ErrorCode, string Json);
}
