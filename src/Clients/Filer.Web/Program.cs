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
// Singleton, not scoped (#166): IHttpClientFactory builds its handler chain in its own
// DI scope, so a scoped store would give BearerTokenHandler a different instance than
// the one FilerAuthenticationStateProvider subscribes to - its Changed event would
// never reach the UI.
builder.Services.AddSingleton<ITokenStore, LocalStorageTokenStore>();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, FilerAuthenticationStateProvider>();

// Auth flows for the UI (#133): pages drive sign-in/out through this seam rather
// than touching the Kiota client or the token store directly.
builder.Services.AddScoped<IAuthSession, AuthSession>();

await builder.Build().RunAsync();
