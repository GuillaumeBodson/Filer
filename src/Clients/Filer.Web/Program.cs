using Filer.ApiClient;
using Filer.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Typed Filer API client (Kiota, ADR-011). The base address is configuration-driven
// (wwwroot/appsettings*.json) so each environment points at its own API. Auth plumbing
// - token store, bearer handler, 401 refresh - replaces the anonymous provider in #128.
var apiBaseAddress = builder.Configuration["FilerApi:BaseAddress"]
    ?? throw new InvalidOperationException(
        "Configuration 'FilerApi:BaseAddress' is required (wwwroot/appsettings.json).");
builder.Services.AddFilerApiClient(new Uri(apiBaseAddress, UriKind.Absolute));

await builder.Build().RunAsync();
