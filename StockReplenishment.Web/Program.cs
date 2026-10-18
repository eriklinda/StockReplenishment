using MudBlazor.Services;
using StockReplenishment.Web.Components;
using StockReplenishment.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5037/";
builder.Services.AddHttpClient<RequestsApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl));

var app = builder.Build();

app.UseExceptionHandler();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
