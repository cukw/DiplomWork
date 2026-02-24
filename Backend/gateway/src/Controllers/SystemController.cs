using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

[ApiController]
[Route("api/system")]
[Authorize]
public class SystemController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SystemController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        var checks = new[]
        {
            new HealthTarget("gateway", "http://localhost:8080/health"),
            new HealthTarget("activityservice", "http://activityservice:5002/health"),
            new HealthTarget("authservice", "http://authservice:5007/health"),
            new HealthTarget("userservice", "http://userservice:5005/health"),
            new HealthTarget("metricservice", "http://metricservice:5011/health"),
            new HealthTarget("notificationservice", "http://notificationservice:5017/health"),
            new HealthTarget("reportservice", "http://reportservice:5014/health"),
            new HealthTarget("agentmanagementservice", "http://agentmanagementservice:5016/health"),
            new HealthTarget("rabbitmq", "http://rabbitmq:15672/api/overview", UseBasicAuth: true),
        };

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(3);

        var results = new List<object>(checks.Length);

        foreach (var target in checks)
        {
            results.Add(await ProbeAsync(client, target, cancellationToken));
        }

        var statuses = results
            .Select(r => (string?)r.GetType().GetProperty("status")?.GetValue(r))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .ToList();

        var overall = statuses.All(s => s == "healthy")
            ? "healthy"
            : statuses.Any(s => s == "healthy")
                ? "degraded"
                : "unhealthy";

        return Ok(new
        {
            status = overall,
            timestamp = DateTime.UtcNow,
            services = results
        });
    }

    private static async Task<object> ProbeAsync(HttpClient client, HealthTarget target, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
            if (target.UseBasicAuth)
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("guest:guest"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            sw.Stop();

            var httpStatus = (int)response.StatusCode;
            var reachable = response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized;

            return new
            {
                name = target.Name,
                url = target.Url,
                status = reachable ? "healthy" : "unhealthy",
                httpStatus,
                latencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new
            {
                name = target.Name,
                url = target.Url,
                status = "unhealthy",
                httpStatus = (int?)null,
                latencyMs = sw.ElapsedMilliseconds,
                error = ex.Message
            };
        }
    }

    private sealed record HealthTarget(string Name, string Url, bool UseBasicAuth = false);
}
