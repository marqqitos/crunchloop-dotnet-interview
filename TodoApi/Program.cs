using Microsoft.EntityFrameworkCore;
using TodoApi.Configuration;
using TodoApi.Services;
using TodoApi.Services.ConflictResolver;
using TodoApi.Dtos.External;
using TodoApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Configure External API options
builder.Services.Configure<ExternalApiOptions>(
    builder.Configuration.GetSection(ExternalApiOptions.SectionName));

// Configure retry policy options
builder.Services.Configure<RetryOptions>(
    builder.Configuration.GetSection(RetryOptions.SectionName));

// Configure sync options
builder.Services.Configure<SyncOptions>(
    builder.Configuration.GetSection(SyncOptions.SectionName));

builder
    .Services.AddDbContext<TodoContext>(opt =>
        opt.UseSqlServer(builder.Configuration.GetConnectionString("TodoContext"))
    )
    .AddEndpointsApiExplorer()
    .AddControllers();

// Register sync services
builder.Services.AddScoped<IRetryPolicyService, RetryPolicyService>();
builder.Services.AddScoped<IConflictResolutionStrategyFactory<TodoList, ExternalTodoList>, ConflictResolutionStrategyFactory<TodoList, ExternalTodoList>>();
builder.Services.AddScoped<IConflictResolutionStrategyFactory<TodoItem, ExternalTodoItem>, ConflictResolutionStrategyFactory<TodoItem, ExternalTodoItem>>();
builder.Services.AddScoped<IConflictResolver<TodoList, ExternalTodoList>, TodoListConflictResolver>();
builder.Services.AddScoped<IConflictResolver<TodoItem, ExternalTodoItem>, TodoItemConflictResolver>();
builder.Services.AddScoped<ITodoListService, TodoListService>();
builder.Services.AddScoped<ITodoItemService, TodoItemService>();
builder.Services.AddScoped<ISyncStateService, TodoListSyncStateService>();
builder.Services.AddScoped<ISyncService, TodoListSyncService>();

// Register background service
var syncOptions = builder.Configuration.GetSection(SyncOptions.SectionName).Get<SyncOptions>();
if (syncOptions?.EnableBackgroundSync == true)
{
    builder.Services.AddHostedService<SyncBackgroundService>();
}

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
