using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

[ApiController]
[Route("api/downloads")]
[Authorize]
public sealed class DownloadsController : ControllerBase
{
    private readonly DownloadFileStore _files;

    public DownloadsController(DownloadFileStore files)
    {
        _files = files;
    }

    [HttpGet("{fileName}")]
    public IActionResult Download(string fileName)
    {
        if (!_files.TryResolve(fileName, out var path))
            return NotFound(new { message = "File not found" });

        var contentType = GetContentType(path);
        return PhysicalFile(path, contentType, Path.GetFileName(path));
    }

    private static string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }
}
