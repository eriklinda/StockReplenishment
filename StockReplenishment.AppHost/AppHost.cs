// Starts the API and the UI with one command. The Aspire dashboard is not needed for this demo.
var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = args, DisableDashboard = true });

var api = builder.AddProject<Projects.StockReplenishment_Api>("api", launchProfileName: "http");

builder.AddProject<Projects.StockReplenishment_Web>("web", launchProfileName: "http")
    .WithEnvironment("Api__BaseUrl", api.GetEndpoint("http"))
    .WaitFor(api);

builder.Build().Run();
