using Azure.Core;
using Azure.Identity;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// ---- Fill these in with your own values, or set as App Service configuration settings ----
string workspaceId = builder.Configuration["PBI_WORKSPACE_ID"] ?? "YOUR_WORKSPACE_ID";
string dashboardId = builder.Configuration["PBI_DASHBOARD_ID"] ?? "YOUR_DASHBOARD_ID";
// --------------------------------------------------------------------------------------------

const string PowerBiResource = "https://analysis.windows.net/powerbi/api/.default";
const string PowerBiApiBase = "https://api.powerbi.com/v1.0/myorg";

var credential = new ManagedIdentityCredential();

var httpClient = new HttpClient();

app.MapGet("/api/getEmbedInfo", async () =>
{
    try
    {
        // 1. Get an AAD token for the Power BI resource, using the App Service's managed identity
        var tokenRequestContext = new TokenRequestContext(new[] { PowerBiResource });
        var accessToken = await credential.GetTokenAsync(tokenRequestContext);

        using var request1 = new HttpRequestMessage(HttpMethod.Get,
            $"{PowerBiApiBase}/groups/{workspaceId}/dashboards/{dashboardId}");
        request1.Headers.Add("Authorization", $"Bearer {accessToken.Token}");

        var dashboardResponse = await httpClient.SendAsync(request1);
        dashboardResponse.EnsureSuccessStatusCode();
        var dashboardJson = JsonDocument.Parse(await dashboardResponse.Content.ReadAsStringAsync());
        var embedUrl = dashboardJson.RootElement.GetProperty("embedUrl").GetString();
        var actualDashboardId = dashboardJson.RootElement.GetProperty("id").GetString();

        // 2. Generate an embed token for the dashboard (view-only)
        var embedTokenBody = new
        {
            accessLevel = "View"
        };

        using var request2 = new HttpRequestMessage(HttpMethod.Post,
            $"{PowerBiApiBase}/groups/{workspaceId}/dashboards/{dashboardId}/GenerateToken")
        {
            Content = new StringContent(JsonSerializer.Serialize(embedTokenBody), Encoding.UTF8, "application/json")
        };
        request2.Headers.Add("Authorization", $"Bearer {accessToken.Token}");

        var embedTokenResponse = await httpClient.SendAsync(request2);
        embedTokenResponse.EnsureSuccessStatusCode();
        var embedTokenJson = JsonDocument.Parse(await embedTokenResponse.Content.ReadAsStringAsync());
        var embedToken = embedTokenJson.RootElement.GetProperty("token").GetString();

        return Results.Ok(new
        {
            embedUrl,
            dashboardId = actualDashboardId,
            embedToken
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to generate embed token");
        return Results.Problem("Failed to generate embed token: " + ex.Message);
    }
});

app.Run();