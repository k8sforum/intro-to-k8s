using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using RabbitMQ.Client;
using System.Globalization;
using System.Reflection;
using mytravels.api.Middleware;
using mytravels.common.Config;
using mytravels.common.Services;
using mytravels.contract.Interfaces;
using mytravels.domain;
using mytravels.domain.Features.PointOfInterest;
using mytravels.storage;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
       .SetBasePath(Directory.GetCurrentDirectory())
       .AddJsonFile("appsettings.json", optional: true)
       .AddEnvironmentVariables()
       .AddUserSecrets<Program>()
       .Build();

builder.Services
       .AddControllers()
       .AddJsonOptions(options =>
       {
       });

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "MyTravels API", Version = "v1" });
    options.TagActionsBy(apiDesc =>
    {
        var controllerName = apiDesc.ActionDescriptor.RouteValues["controller"];
        return new[] { controllerName?.ToLower() };
    });
});

string allowedOrigins = builder.Configuration.GetValue<string>("CorsHosts");

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin",
        builder => builder
            .WithOrigins(allowedOrigins.Split(';'))
            .AllowAnyHeader()
            .AllowAnyMethod());
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

builder.Services.AddTransient<IMessagePublisher, MessagePublisher>();
builder.Services.AddTransient<IObjectStorageService, MinIOStorageService>();
builder.Services.AddTransient<IGeoService, GeoService>();
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

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "MyTravels API V1");
    options.DocumentTitle = "MyTravels API Docs";
});

if (app.Environment.IsProduction())
{
    app.Use(async (context, next) =>
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        await next.Invoke();
    });
    app.UseHsts();
}

app.UseApiExceptionHandler();
app.UseHttpsRedirection();
app.UseCors("AllowSpecificOrigin");
app.UseRouting();
app.MapControllers();

CultureInfo ci = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentCulture = ci;
Thread.CurrentThread.CurrentUICulture = ci;

await app.RunAsync();
