using Microsoft.EntityFrameworkCore;
using ExternalTodoApi.Data;

var builder = WebApplication.CreateBuilder(args);

// Configure CORS for local development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add Entity Framework with SQL Server
builder.Services.AddDbContext<ExternalTodoContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ExternalTodoContext")));

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null; // Keep original property names
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { 
        Title = "External Todo API", 
        Version = "v1",
        Description = "External Todo API for synchronization testing"
    });
});

// Configure to run on port 8080
builder.WebHost.UseUrls("http://0.0.0.0:8080");

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "External Todo API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at root
    });
}

app.UseCors("AllowAll");

app.UseAuthorization();

app.MapControllers();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ExternalTodoContext>();
    try
    {
        context.Database.EnsureCreated();
        app.Logger.LogInformation("External Todo API database initialized");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to initialize database");
    }
}

app.Logger.LogInformation("🚀 External Todo API starting on http://localhost:8080");
app.Logger.LogInformation("📊 Swagger UI available at http://localhost:8080");

app.Run();
