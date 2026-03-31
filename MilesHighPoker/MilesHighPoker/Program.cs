using MilesHighPoker.Components;
using MilesHighPoker.Hubs;
using MilesHighPoker.Services;

var builder = WebApplication.CreateBuilder(args);

// UI (Blazor server interactive)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Multiplayer transport
builder.Services.AddSignalR();

// Game orchestration/state services
// Choose lifetimes carefully:
// - Singleton if you want in-memory tables shared across all users
// - Scoped if table state should be per-request/circuit (usually NOT for multiplayer tables)
builder.Services.AddSingleton<IGameManager, GameManager>();
builder.Services.AddSingleton<ITableRegistry, TableRegistry>();

// Optional operational endpoint
builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// SignalR hub route clients connect to
app.MapHub<PokerHub>("/hubs/poker");

// Optional health endpoint
app.MapHealthChecks("/health");

app.Run();