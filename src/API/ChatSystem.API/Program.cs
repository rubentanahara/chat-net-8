using ChatSystem.API.Hubs;
using ChatSystem.Application.Interfaces;
using ChatSystem.Application.Services;
using ChatSystem.Domain.Repositories;
using ChatSystem.Infrastructure.Data;
using ChatSystem.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;

namespace ChatSystem.API;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Add SignalR
        builder.Services.AddSignalR();

        // Add CORS
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", builder =>
                builder.SetIsOriginAllowed(_ => true)
                       .AllowAnyMethod()
                       .AllowAnyHeader()
                       .AllowCredentials());
        });

        // Configure MySQL
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        builder.Services.AddDbContext<ChatDbContext>(options =>
        {
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        });

        // Add Redis for SignalR backplane
        builder.Services.AddSignalR().AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis"));

        // Register services
        builder.Services.AddScoped<IChatRepository, ChatRepository>();
        builder.Services.AddScoped<IChatService, ChatService>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseCors("AllowAll");

        // Map SignalR hub
        app.MapHub<ChatHub>("/chathub");

        // Minimal API endpoints
        app.MapGet("/api/chat/requests", async (IChatService chatService) =>
            await chatService.GetPendingChatRequestsAsync());

        app.MapGet("/api/chat/rooms/{userId}", async (string userId, IChatService chatService) =>
            await chatService.GetActiveChatRoomsForUserAsync(userId));

        app.MapGet("/api/chat/history/{chatRoomId}", async (Guid chatRoomId, IChatService chatService) =>
            await chatService.GetChatHistoryAsync(chatRoomId));

        // Ensure database is created and migrated
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            try
            {
                var context = services.GetRequiredService<ChatDbContext>();
                var logger = services.GetRequiredService<ILogger<Program>>();
                
                logger.LogInformation("Starting database migration...");
                
                await context.Database.EnsureDeletedAsync();
                
                // Create database if it doesn't exist and apply migrations
                if (await context.Database.CanConnectAsync())
                {
                    logger.LogInformation("Database exists, applying migrations...");
                }
                else
                {
                    logger.LogInformation("Database does not exist, creating and applying migrations...");
                }
                
                await context.Database.MigrateAsync();
                logger.LogInformation("Database migration completed successfully");
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "An error occurred while initializing the database");
                throw;
            }
        }

        app.Run();
    }
}
