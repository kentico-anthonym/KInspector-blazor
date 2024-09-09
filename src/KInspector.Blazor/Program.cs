using Autofac;
using Autofac.Extensions.DependencyInjection;

using KInspector.Blazor.Services;
using KInspector.Core;
using KInspector.Infrastructure;
using KInspector.Reports;
using Microsoft.Extensions.FileProviders;
using System.Reflection;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Host
    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
    .ConfigureContainer<ContainerBuilder>((containerBuilder) =>
    {
        containerBuilder.RegisterModule<CoreModule>();
        containerBuilder.RegisterModule<InfrastructureModule>();
        containerBuilder.RegisterModule<ReportsModule>();
        containerBuilder.RegisterModule<ActionsModule>();
    });



builder.Services.AddScoped<StateContainer>();

builder.Services.AddRazorPages().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});
builder.Services.AddServerSideBlazor();
builder.WebHost.UseStaticWebAssets();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseDeveloperExceptionPage();
app.UseHttpsRedirection();

// Needed to embed the script and css files into single executable
var provider = new ManifestEmbeddedFileProvider(Assembly.GetAssembly(type: typeof(Program)), "wwwroot");
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = provider,
    RequestPath = "",
});
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
