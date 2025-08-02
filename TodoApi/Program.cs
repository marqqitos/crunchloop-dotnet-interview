using Microsoft.EntityFrameworkCore;
using TodoApi.Configuration;
using TodoApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure External API options
builder.Services.Configure<ExternalApiOptions>(
    builder.Configuration.GetSection(ExternalApiOptions.SectionName));

builder
    .Services.AddDbContext<TodoContext>(opt =>
        opt.UseSqlServer(builder.Configuration.GetConnectionString("TodoContext"))
    )
    .AddEndpointsApiExplorer()
    .AddControllers();

// Configure HTTP client for external API
var externalApiOptions = builder.Configuration.GetSection(ExternalApiOptions.SectionName).Get<ExternalApiOptions>();
builder.Services.AddHttpClient<IExternalTodoApiClient, ExternalTodoApiClient>(client =>
{
    client.BaseAddress = new Uri(externalApiOptions?.BaseUrl ?? "http://localhost:8080");
    client.Timeout = TimeSpan.FromSeconds(externalApiOptions?.TimeoutSeconds ?? 30);
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

app.UseAuthorization();
app.MapControllers();
app.Run();
