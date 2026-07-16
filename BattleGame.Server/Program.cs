using BattleGame.Online;
using BattleGame.Server;
using BattleGame.Persistence;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// 本地默认监听所有网卡，另一台 Mac 才能通过局域网 IP 连接；部署公网时使用
// --urls 或 ASPNETCORE_URLS 覆盖，协议与业务代码完全不变。
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])
    && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5088");
}

builder.Services.AddSingleton<IRoomCodeGenerator, RandomRoomCodeGenerator>();
builder.Services.AddSingleton<IQuestionProvider, BuiltInQuestionProvider>();
builder.Services.AddSingleton<ILastChanceProvider, BuiltInLastChanceProvider>();
builder.Services.AddSingleton<ITagCatalog, BuiltInTagCatalog>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IOnlineFateAgent>(services =>
{
    var fallback = new BuiltInOnlineFateAgent();
    string? apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return fallback;
    }

    return new DeepSeekOnlineFateAgent(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(),
        apiKey,
        Environment.GetEnvironmentVariable("DEEPSEEK_MODEL") ?? "deepseek-chat",
        fallback);
});
string databasePath = builder.Configuration["BATTLEGAME_DATA_PATH"]
    ?? Path.Combine(AppContext.BaseDirectory, "data", "battle-game.db");
builder.Services.AddSingleton<IPlayerProfileService>(services =>
    new SqlitePlayerProfileService(
        databasePath,
        services.GetRequiredService<ITagCatalog>()));
builder.Services.AddSingleton<OnlineRoomManager>();
builder.Services.AddSingleton<OnlineConnectionRegistry>();
builder.Services.AddSingleton<OnlineCommandProcessor>();
builder.Services.AddSingleton<WebSocketGameEndpoint>();
builder.Services.AddHostedService<GameDeadlineService>();

WebApplication app = builder.Build();
await app.Services.GetRequiredService<IPlayerProfileService>().InitializeAsync();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    protocolVersion = 1
}));
app.MapGet("/server-info", () => Results.Ok(LanServerInfo.Create()));
app.Map("/ws", async context =>
{
    WebSocketGameEndpoint endpoint = context.RequestServices
        .GetRequiredService<WebSocketGameEndpoint>();
    await endpoint.HandleAsync(context);
});

app.Run();

public partial class Program;
