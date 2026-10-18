# Stock Replenishment

## Run

```sh
dotnet run --project StockReplenishment.AppHost
```

This starts the API and the UI. In Visual Studio, set **StockReplenishment.AppHost** as the startup project.

UI: http://localhost:5038 · API: http://localhost:5037

Without Aspire, run the API and UI in separate terminals:

```sh
dotnet run --project StockReplenishment.Api
dotnet run --project StockReplenishment.Web
```

## Test

```sh
dotnet test
```

Example API calls are in [StockReplenishment.Api.http](StockReplenishment.Api/StockReplenishment.Api.http).

## Assumptions

- Approval requires sufficient stock for all items.
- Fulfilment closes the request even if some quantities remain undelivered.
