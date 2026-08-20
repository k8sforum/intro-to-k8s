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
using mytravels.functions;
using mytravels.storage;

var builder = WebApplication.CreateBuilder(args);

var otelServiceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? "mytravels-messaging";

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

builder.Services.AddSingleton<IConnectionFactory>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    return new ConnectionFactory()
    {
        Uri = new Uri(configuration.GetValue<string>("RabbitMQ:Uri")),
        ClientProvidedName = Assembly.GetExecutingAssembly().GetName().Name
    };
});

builder.Services.Configure<MinIOConfig>(builder.Configuration.GetSection("MinIO"));

builder.Services.AddTransient<IGeoService, ImageMetadataService>();
builder.Services.AddTransient<IMessagePublisher, MessagePublisher>();
builder.Services.AddMapsService(builder.Configuration);
builder.Services.AddTransient<IObjectStorageService, MinIOStorageService>();
builder.Services.AddTransient<IPointOfInterestService, PointOfInterestService>();

builder.Services.AddHostedService<AppendFormattedAddress>();
builder.Services.AddHostedService<AppendFormattedAddressSweeper>();
builder.Services.AddHostedService<ResizeImage>();

var app = builder.Build();

CultureInfo ci = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentCulture = ci;
Thread.CurrentThread.CurrentUICulture = ci;

app.MapGet("{**path}", (HttpResponse response) => response.WriteAsync("Service is running..."));

await app.RunAsync();
