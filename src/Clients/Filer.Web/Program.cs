using Filer.ApiClient;
using Filer.ApiClient.Auth;
using Filer.Ui.Auth;
using Filer.Web;
using Filer.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Typed Filer API client (Kiota, ADR-011). The base address is configuration-driven
// (wwwroot/appsettings*.json) so each environment points at its own API.
var apiBaseAddress = builder.Configuration["FilerApi:BaseAddress"]
    ?? throw new InvalidOperationException(
        "Configuration 'FilerApi:BaseAddress' is required (wwwroot/appsettings.json).");
builder.Services.AddFilerApiClient(new Uri(apiBaseAddress, UriKind.Absolute));

// Auth plumbing (#128, 05-security.md): tokens persist in browser localStorage; the
// bearer/refresh handler (registered by AddFilerApiClient) reads them; auth state for
// AuthorizeView/AuthorizeRouteView derives from the same store.
builder.Services.AddScoped<ITokenStore, LocalStorageTokenStore>();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, FilerAuthenticationStateProvider>();

await builder.Build().RunAsync();
