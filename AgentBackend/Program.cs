using ModelContextProtocol.Client;
using OpenAI;
using Microsoft.Agents.AI;
using System.Diagnostics;
using System.Text;
using System.Web;
using System.Net;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAGUI();
var app = builder.Build();

var clientId = builder.Configuration["OAuth:ClientId"] ?? throw new InvalidOperationException("OAuth:ClientId is not configured");
var clientSecret = builder.Configuration["OAuth:ClientSecret"] ?? throw new InvalidOperationException("OAuth:ClientSecret is not configured");
var dataverseEnvironmentId = builder.Configuration["DataverseEnvironmentId"] ?? throw new InvalidOperationException("DataverseEnvironmentId is not configured");
var dataverseUrl = builder.Configuration["DataverseUrl"] ?? throw new InvalidOperationException("DataverseUrl is not configured");
var openAiApiKey = builder.Configuration["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured");

// Two HttpClient configurations are needed because different MCP servers have different requirements:
// - Most MCP servers (Agent365) work fine with standard HTTP chunked transfer encoding
// - Dataverse MCP server does NOT support chunked encoding and requires Content-Length header
using var httpClient = new HttpClient();
using var httpClientWithContentLength = new HttpClient(new ContentLengthEnforcingHandler());

var consoleLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

// Create OAuth handler with semaphore to serialize auth flows (they share the same redirect port)
var oauthHandler = new OAuthAuthorizationHandler();

// Create SSE client transport for the MCP server
var managementMcpServerUrl = $"https://agent365.svc.cloud.microsoft/mcp/environments/{dataverseEnvironmentId}/servers/MCPManagement";
var managementMcpTransport = new HttpClientTransport(new()
{
    Endpoint = new Uri(managementMcpServerUrl),
    Name = "Agent365 Management Client",
    OAuth = new()
    {
        ClientId = clientId,
        ClientSecret = clientSecret,
        Scopes = ["ea9ffc3e-8a23-4a7d-836d-234d7c7565c1/McpServers.Management.All", "offline_access", "openid", "profile"],
        RedirectUri = new Uri("http://localhost:1179/callback"),
        AuthorizationRedirectDelegate = oauthHandler.HandleAuthorizationUrlAsync,
    }
}, httpClient, consoleLoggerFactory);

var wordMcpServerUrl = $"https://agent365.svc.cloud.microsoft/mcp/environments/{dataverseEnvironmentId}/servers/mcp_WordServer";

var wordMcpTransport = new HttpClientTransport(new()
{
    Endpoint = new Uri(wordMcpServerUrl),
    Name = "Agent365 Word Client",
    OAuth = new()
    {
        ClientId = clientId,
        ClientSecret = clientSecret,
        Scopes = ["ea9ffc3e-8a23-4a7d-836d-234d7c7565c1/.default", "offline_access", "openid", "profile"],
        RedirectUri = new Uri("http://localhost:1179/callback"),
        AuthorizationRedirectDelegate = oauthHandler.HandleAuthorizationUrlAsync,
    }
}, httpClient, consoleLoggerFactory);

var teamsMcpServerUrl = $"https://agent365.svc.cloud.microsoft/mcp/environments/{dataverseEnvironmentId}/servers/mcp_TeamsServer";

var teamsMcpTransport = new HttpClientTransport(new()
{
    Endpoint = new Uri(teamsMcpServerUrl),
    Name = "Agent365 Teams Client",
    OAuth = new()
    {
        ClientId = clientId,
        ClientSecret = clientSecret,
        Scopes = ["ea9ffc3e-8a23-4a7d-836d-234d7c7565c1/McpServers.Teams.All", "offline_access", "openid", "profile"],
        RedirectUri = new Uri("http://localhost:1179/callback"),
        AuthorizationRedirectDelegate = oauthHandler.HandleAuthorizationUrlAsync,
    }
}, httpClient, consoleLoggerFactory);

var dataverseMcpServer = $"{dataverseUrl}/api/mcp";

var dataverseMcpTransport = new HttpClientTransport(new()
{
    Endpoint = new Uri(dataverseMcpServer),
    Name = "Dataverse MCP Client",
    OAuth = new()
    {
        ClientId = clientId,
        ClientSecret = clientSecret,
        Scopes = [$"{dataverseUrl}/user_impersonation", "offline_access", "openid", "profile"],
        RedirectUri = new Uri("http://localhost:1179/callback"),
        AuthorizationRedirectDelegate = oauthHandler.HandleAuthorizationUrlAsync,
    }
}, httpClientWithContentLength, consoleLoggerFactory);

await using var dataverseMcpClient = await McpClient.CreateAsync(dataverseMcpTransport, loggerFactory: consoleLoggerFactory);
await using var managementMcpClient = await McpClient.CreateAsync(managementMcpTransport, loggerFactory: consoleLoggerFactory);
await using var wordMcpClient = await McpClient.CreateAsync(wordMcpTransport, loggerFactory: consoleLoggerFactory);
await using var teamsMcpClient = await McpClient.CreateAsync(teamsMcpTransport, loggerFactory: consoleLoggerFactory);

var managementTools = await managementMcpClient.ListToolsAsync().ConfigureAwait(false);
var wordTools = await wordMcpClient.ListToolsAsync().ConfigureAwait(false);
var teamsTools = await teamsMcpClient.ListToolsAsync().ConfigureAwait(false);
var dataverseTools = await dataverseMcpClient.ListToolsAsync().ConfigureAwait(false);

AIAgent agent = new OpenAIClient(openAiApiKey)
    .GetChatClient("gpt-5.1")
    .CreateAIAgent(instructions: "You are a helpful agent that allows the user to perform management tasks in Agent 365 and call Agent 365 MCP Servers.", tools: [.. managementTools, ..wordTools, ..teamsTools, ..dataverseTools]);

app.MapAGUI("/", agent);

app.Run();

/// <summary>
/// Handles OAuth authorization with a semaphore to serialize flows that share the same redirect port.
/// </summary>
class OAuthAuthorizationHandler
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<string?> HandleAuthorizationUrlAsync(Uri authorizationUrl, Uri redirectUri, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            Console.WriteLine("Starting OAuth authorization flow...");
            Console.WriteLine($"Opening browser to: {authorizationUrl}");

            var listenerPrefix = redirectUri.GetLeftPart(UriPartial.Authority);
            if (!listenerPrefix.EndsWith("/", StringComparison.InvariantCultureIgnoreCase))
            {
                listenerPrefix += "/";
            }

            using var listener = new HttpListener();
            listener.Prefixes.Add(listenerPrefix);

            try
            {
                listener.Start();
                Console.WriteLine($"Listening for OAuth callback on: {listenerPrefix}");

                OpenBrowser(authorizationUrl);

                var context = await listener.GetContextAsync();
                var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty);
                var code = query["code"];
                var error = query["error"];

                const string ResponseHtml = "<html><body><h1>Authentication complete</h1><p>You can close this window now.</p></body></html>";
                byte[] buffer = Encoding.UTF8.GetBytes(ResponseHtml);
                context.Response.ContentLength64 = buffer.Length;
                context.Response.ContentType = "text/html";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.Close();

                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine($"Auth error: {error}");
                    return null;
                }

                if (string.IsNullOrEmpty(code))
                {
                    Console.WriteLine("No authorization code received");
                    return null;
                }

                Console.WriteLine("Authorization code received successfully.");
                return code;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting auth code: {ex.Message}");
                return null;
            }
            finally
            {
                if (listener.IsListening)
                {
                    listener.Stop();
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static void OpenBrowser(Uri url)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = url.ToString(),
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error opening browser. {ex.Message}");
            Console.WriteLine($"Please manually open this URL: {url}");
        }
    }
}

/// <summary>
/// HTTP handler that buffers request content to set Content-Length header instead of using chunked transfer encoding.
/// Required for servers like Dataverse MCP that don't support HTTP chunked encoding.
/// </summary>
class ContentLengthEnforcingHandler() : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

