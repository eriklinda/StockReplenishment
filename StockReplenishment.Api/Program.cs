using System.Text.Json.Serialization;
using StockReplenishment.Application;
using StockReplenishment.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<RequestLock>();
builder.Services.AddScoped<RequestService>();
builder.Services.AddStockInfrastructure(builder.Configuration["DatabaseName"] ?? "StockReplenishment");

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
await app.Services.SeedStockDataAsync();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapControllers();
app.Run();

public partial class Program;

