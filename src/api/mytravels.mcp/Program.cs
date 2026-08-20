using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using RabbitMQ.Client;
using System.Globalization;
using System.Reflection;
using mytravels.common.Config;
using mytravels.common.Extensions;
using mytravels.common.Services;
using mytravels.contract.Interfaces;
using mytravels.domain;
using mytravels.domain.Features.PointOfInterest;
using mytravels.mcp.Tools;
using mytravels.storage;

var builder = WebApplication.CreateBuilder(args);

var otelServiceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? "mytravels-mcp";

builder.Services.AddOpenTelemetry()
       .ConfigureResource(resource => resource.AddService(otelServiceName))
       .WithMetrics(metrics => metrics
           .AddAspNetCoreInstrumentation()
           .AddHttpClientInstrumentation()
           .AddRuntimeInstrumentation())
       .WithTracing(tracing => tracing
           .AddAspNetCoreInstrumentation()
           .AddHttpClientInstrumentation()
           .AddSource("Npgsql"))
       .UseOtlpExporter();

builder.Configuration
       .SetBasePath(Directory.GetCurrentDirectory())
       .AddJsonFile("appsettings.json", optional: true)
       .AddEnvironmentVariables()
       .AddUserSecrets<Program>()
       .Build();

builder.Services.AddSingleton<IConnectionFactory>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    return new ConnectionFactory()
    {
        Uri = new Uri(configuration.GetValue<string>("RabbitMQ:Uri")),
        ClientProvidedName = Assembly.GetExecutingAssembly().GetName().Name
    };
});

builder.Services.AddTransient<IMessagePublisher, MessagePublisher>();
builder.Services.AddTransient<IObjectStorageService, MinIOStorageService>();
builder.Services.AddTransient<IGeoService, ImageMetadataService>();
builder.Services.AddMapsService(builder.Configuration);
builder.Services.AddTransient<IPointOfInterestService, PointOfInterestService>();
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

builder.Services.AddDbContext<ICoreDbContext, CoreDbContext>(
options =>
{
    string connectionString = builder.Configuration.GetConnectionString("CoreDbContext")
                    ?? throw new InvalidOperationException("Connection string 'CoreDbContext' not found.");
    options.UseNpgsql(connectionString, options =>
    {
        options.MigrationsHistoryTable("EFMigrationsHistory", schema: "config");
        options.EnableRetryOnFailure();
    });
});

builder.Services.Configure<MinIOConfig>(builder.Configuration.GetSection("MinIO"));

builder.Services
       .AddMcpServer()
       .WithHttpTransport()
       .WithTools<PointOfInterestMcpTools>()
       .WithTools<PlaceMcpTools>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("mcp is running..."));
app.MapMcp();

CultureInfo ci = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentCulture = ci;
Thread.CurrentThread.CurrentUICulture = ci;

await app.RunAsync();
