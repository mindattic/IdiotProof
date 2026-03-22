using IdiotProof.Engine;
using IdiotProof.Engine.Storage;
using IdiotProof.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Register IdiotProof engine with web storage
var storage = new WebStorageProvider();
builder.Services.AddIdiotProofEngine(storage);

// Add Blazor services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(IdiotProof.UI.Shared.Layout.MainLayout).Assembly);

app.Run();
