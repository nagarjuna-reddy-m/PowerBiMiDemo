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
string reportId = builder.Configuration["PBI_REPORT_ID"] ?? "YOUR_REPORT_ID";
// If using a User-Assigned Managed Identity, set its Client ID here (or leave null for System-Assigned)
string tenantId = builder.Configuration["TenantId"]
    ?? throw new Exception("TenantId missing");

string clientId = builder.Configuration["ClientId"]
    ?? throw new Exception("ClientId missing");

string clientSecret = builder.Configuration["ClientSecret"]
    ?? throw new Exception("ClientSecret missing");
// --------------------------------------------------------------------------------------------

const string PowerBiResource = "https://analysis.windows.net/powerbi/api/.default";
const string PowerBiApiBase = "https://api.powerbi.com/v1.0/myorg";

var credential = new ClientSecretCredential(
    tenantId,
    clientId,
    clientSecret);

var httpClient = new HttpClient();

app.MapGet("/api/getEmbedInfo", async () =>
{
    try
    {
        // 1. Get an AAD token for the Power BI resource, using the App Service's managed identity
        var tokenRequestContext = new TokenRequestContext(new[] { PowerBiResource });
        var accessToken = await credential.GetTokenAsync(tokenRequestContext);

        using var request1 = new HttpRequestMessage(HttpMethod.Get,
            $"{PowerBiApiBase}/groups/{workspaceId}/reports/{reportId}");
        request1.Headers.Add("Authorization", $"Bearer {accessToken.Token}");

        var reportResponse = await httpClient.SendAsync(request1);
        reportResponse.EnsureSuccessStatusCode();
        var reportJson = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        var embedUrl = reportJson.RootElement.GetProperty("embedUrl").GetString();
        var datasetId = reportJson.RootElement.GetProperty("datasetId").GetString();
        var actualReportId = reportJson.RootElement.GetProperty("id").GetString();

        // 2. Generate an embed token scoped to just this report (view-only)
        var embedTokenBody = new
        {
            accessLevel = "View",
            datasets = new[] { new { id = datasetId } },
            reports = new[] { new { id = actualReportId } }
        };

        using var request2 = new HttpRequestMessage(HttpMethod.Post,
            $"{PowerBiApiBase}/groups/{workspaceId}/reports/{reportId}/GenerateToken")
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
            reportId = actualReportId,
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
