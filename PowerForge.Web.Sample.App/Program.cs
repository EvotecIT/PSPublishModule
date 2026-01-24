using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PowerForge.Web.Sample.App;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
await builder.Build().RunAsync();
