
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using mytravels.domain;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<ICoreDbContext, CoreDbContext>(options =>
{
    string connectionString = builder.Configuration.GetConnectionString("CoreDbContext")
        ?? throw new InvalidOperationException("Connection string 'CoreDbContext' not found.");

    options.UseNpgsql(connectionString, options =>
    {
        options.MigrationsHistoryTable("EFMigrationsHistory", schema: "config");
        options.EnableRetryOnFailure();
    });
});

var host = builder.Build();
await host.RunAsync();
