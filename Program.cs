using Azure.Core;
using Azure.Identity;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

string workspaceId =
    builder.Configuration["PBI_WORKSPACE_ID"]
    ?? "YOUR_WORKSPACE_ID";

string dashboardId =
    builder.Configuration["PBI_DASHBOARD_ID"]
    ?? "YOUR_DASHBOARD_ID";

const string PowerBiResource =
    "https://analysis.windows.net/powerbi/api/.default";

const string PowerBiApiBase =
    "https://api.powerbi.com/v1.0/myorg";

var credential = new ManagedIdentityCredential();

var httpClient = new HttpClient();

app.MapGet("/api/getEmbedInfo", async () =>
{
    try
    {
        // Get Power BI access token using Managed Identity
        var tokenRequestContext = new TokenRequestContext(
            new[] { PowerBiResource });

        var accessToken =
            await credential.GetTokenAsync(tokenRequestContext);

        // Get Dashboard details
        using var request1 = new HttpRequestMessage(
            HttpMethod.Get,
            $"{PowerBiApiBase}/groups/{workspaceId}/dashboards/{dashboardId}");

        request1.Headers.Add(
            "Authorization",
            $"Bearer {accessToken.Token}");

        var dashboardResponse =
            await httpClient.SendAsync(request1);

        dashboardResponse.EnsureSuccessStatusCode();

        var dashboardJson =
            JsonDocument.Parse(
                await dashboardResponse.Content.ReadAsStringAsync());

        var embedUrl =
            dashboardJson.RootElement
                .GetProperty("embedUrl")
                .GetString();

        var actualDashboardId =
            dashboardJson.RootElement
                .GetProperty("id")
                .GetString();

        // Generate Dashboard Embed Token
        var embedTokenBody = new
        {
            accessLevel = "View"
        };

        using var request2 = new HttpRequestMessage(
            HttpMethod.Post,
            $"{PowerBiApiBase}/groups/{workspaceId}/dashboards/{dashboardId}/GenerateToken")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(embedTokenBody),
                Encoding.UTF8,
                "application/json")
        };

        request2.Headers.Add(
            "Authorization",
            $"Bearer {accessToken.Token}");

        var embedTokenResponse =
            await httpClient.SendAsync(request2);

        embedTokenResponse.EnsureSuccessStatusCode();

        var embedTokenJson =
            JsonDocument.Parse(
                await embedTokenResponse.Content.ReadAsStringAsync());

        var embedToken =
            embedTokenJson.RootElement
                .GetProperty("token")
                .GetString();

        return Results.Ok(new
        {
            embedUrl,
            dashboardId = actualDashboardId,
            embedToken
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to generate dashboard embed token");

        return Results.Problem(
            "Failed to generate dashboard embed token: " +
            ex.Message);
    }
});

app.Run();