using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using System.Globalization;
using System.Reflection;
using mytravels.common.Config;
using mytravels.common.Services;
using mytravels.contract.Interfaces;
using mytravels.domain;
using mytravels.domain.Features.PointOfInterest;
using mytravels.functions;
using mytravels.storage;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddTransient<IGeoService, GeoService>();
builder.Services.AddTransient<IMessagePublisher, MessagePublisher>();
string googleApiKey = builder.Configuration.GetValue<string>("GoogleApiKey");
if (string.IsNullOrEmpty(googleApiKey) || googleApiKey == "<YOUR_GOOGLE_API_KEY>")
{
    builder.Services.AddTransient<IMapsService, OpenStreetMapsService>();
}
else
{
    builder.Services.AddTransient<IMapsService, GoogleMapsService>();
}
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
