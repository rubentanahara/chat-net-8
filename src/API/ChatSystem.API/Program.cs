using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Json;
using System.Threading.RateLimiting;
using Polly;
using ChatSystem.Infrastructure.Data;
using ChatSystem.Infrastructure.Repositories;
using ChatSystem.Domain.Repositories;
using ChatSystem.Application.Interfaces;
using ChatSystem.Application.Services;
using ChatSystem.API.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Add Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ChatDbContext>()
    .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? throw new InvalidOperationException("Redis connection string not found."));

// Add SignalR with backplane configuration
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? 
    throw new InvalidOperationException("Redis connection string not found.");

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
    options.StreamBufferCapacity = 10;
}).AddStackExchangeRedis(redisConnectionString, options =>
{
    options.Configuration.ConnectTimeout = 5000; // 5 seconds
    options.Configuration.AbortOnConnectFail = false;
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
        builder.SetIsOriginAllowed(_ => true)
               .AllowAnyMethod()
               .AllowAnyHeader()
               .AllowCredentials());
});

// Configure MySQL with connection pooling and resilience
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Database connection string not found.");

builder.Services.AddDbContext<ChatDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
        mysqlOptions =>
        {
            mysqlOptions.EnableRetryOnFailure(
                maxRetryCount: 10,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            mysqlOptions.MigrationsAssembly("ChatSystem.Infrastructure");
            mysqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            mysqlOptions.CommandTimeout(30);
            mysqlOptions.MinBatchSize(5);
            mysqlOptions.MaxBatchSize(100);
        })
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
        .EnableSensitiveDataLogging(builder.Environment.IsDevelopment())
        .EnableDetailedErrors(builder.Environment.IsDevelopment());
});

// Register services
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IChatService, ChatService>();

// Add Rate Limiting
var rateLimiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
{
    AutoReplenishment = true,
    PermitLimit = 100,
    QueueLimit = 50,
    Window = TimeSpan.FromSeconds(10)
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.AutoReplenishment = true;
        opt.PermitLimit = 100;
        opt.QueueLimit = 50;
        opt.Window = TimeSpan.FromSeconds(10);
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler("/error");
app.UseStatusCodePages();

app.UseCors("AllowAll");

// Map health checks
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                duration = entry.Value.Duration
            })
        });

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(result);
    }
});

// Map SignalR hub
app.MapHub<ChatHub>("/chathub");

// Minimal API endpoints with rate limiting
app.MapGet("/api/chat/requests", async (IChatService chatService) =>
    await chatService.GetPendingChatRequestsAsync())
    .RequireRateLimiting("fixed");

app.MapGet("/api/chat/rooms/{userId}", async (string userId, IChatService chatService) =>
    await chatService.GetActiveChatRoomsForUserAsync(userId))
    .RequireRateLimiting("fixed");

app.MapGet("/api/chat/history/{chatRoomId}", async (Guid chatRoomId, IChatService chatService) =>
    await chatService.GetChatHistoryAsync(chatRoomId))
    .RequireRateLimiting("fixed");

// Database initialization with retry policy
var retryPolicy = Policy
    .Handle<Exception>()
    .WaitAndRetryAsync(3, retryAttempt => 
        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ChatDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        
        await retryPolicy.ExecuteAsync(async () =>
        {
            logger.LogInformation("Starting database migration...");
            
            if (await context.Database.CanConnectAsync())
            {
                await context.Database.MigrateAsync();
                logger.LogInformation("Database migration completed successfully");
            }
            else
            {
                logger.LogError("Cannot connect to database");
                throw new Exception("Database connection failed");
            }
        });
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while initializing the database");
        throw;
    }
}

app.Run();
