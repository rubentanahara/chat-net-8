using ChatSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChatSystem.Infrastructure.Data;

public class ChatDbContext : DbContext
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options)
    {
    }

    public DbSet<ChatRoom> ChatRooms { get; set; } = null!;
    public DbSet<ChatMessage> ChatMessages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatRoom>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status)
                .HasConversion<string>();
            entity.HasMany(e => e.Messages)
                .WithOne()
                .HasForeignKey(e => e.ChatRoomId);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content)
                .IsRequired();
        });
    }
} 