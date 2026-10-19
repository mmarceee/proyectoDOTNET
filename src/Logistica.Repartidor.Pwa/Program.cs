using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Logistica.Repartidor.Pwa;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<Logistica.Repartidor.Pwa.Services.HojaDeRutaService>();
builder.Services.AddScoped<Logistica.Repartidor.Pwa.Services.CargaService>();

await builder.Build().RunAsync();
