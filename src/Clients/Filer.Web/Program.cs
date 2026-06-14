using Filer.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The typed Filer API client (Kiota, ADR-011) is registered here in #126, and auth
// plumbing (token store, bearer handler, 401 refresh) in #128. No hand-rolled
// HttpClient: all server calls go through the generated client.

await builder.Build().RunAsync();
