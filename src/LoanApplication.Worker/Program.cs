using LoanApplication.Api.Data;
using LoanApplication.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace LoanApplication.Worker;

public static class WorkerProgram
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        if (string.IsNullOrWhiteSpace(builder.Configuration["RabbitMq:Host"]))
            throw new InvalidOperationException("RabbitMq:Host is not configured.");

        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
        builder.Services.AddScoped<LoanAssignmentHandler>();
        builder.Services.AddHostedService<LoanSubmittedConsumer>();

        var host = builder.Build();
        host.Run();
    }
}
