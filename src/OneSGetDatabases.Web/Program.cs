using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Events;
using OneSGetDatabases.Web.Workers;
using OneSGetDatabases.Core.Extensions;

try
{
    Console.Title = "1С: Get Databases";
}
catch
{
    // Ignore when running non-interactively as a service
}

var webAppOptions = new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
};

var builder = WebApplication.CreateBuilder(webAppOptions);

// Enable Windows Service hosting
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "OneSGetDatabasesWeb";
});

// Serilog Structured Logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(AppContext.BaseDirectory, "logs", "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Core Services (RAC, Consul, AD, DBMS, Confluence, Mail)
builder.Services.AddOneSGetDatabasesCore(builder.Configuration);

// Background Worker (Periodic Discovery, Local Cache Pre-warming & Confluence Auto-sync)
builder.Services.AddHostedService<DiscoveryBackgroundWorker>();

// Controllers & JSON options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "1С: Get Databases Web API",
        Version = "v1",
        Description = "API панели управления, инспекции СУБД и выгрузки в Confluence баз 1С:Предприятие"
    });
});

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("1С: Get Databases Service is running"));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "1С: Get Databases Web API v1");
    c.RoutePrefix = "swagger";
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        ctx.Context.Response.Headers["Pragma"] = "no-cache";
        ctx.Context.Response.Headers["Expires"] = "0";
    }
});

app.UseRouting();

app.MapHealthChecks("/health");
app.MapControllers();

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

try
{
    string url = builder.Configuration["Kestrel:Endpoints:Http:Url"] ?? "http://*:5070";
    Log.Information("Starting 1С: Get Databases Service with Web Control Surface on {Url}", url);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "1С: Get Databases Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
