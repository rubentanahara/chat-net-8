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
            
            // Add indexes for frequently queried columns
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.RequestorId);
            entity.HasIndex(e => e.ListenerId);
            entity.HasIndex(e => e.CreatedAt);
            
            // Configure string properties with max length
            entity.Property(e => e.RequestorId)
                .IsRequired()
                .HasMaxLength(50);
            
            entity.Property(e => e.ListenerId)
                .HasMaxLength(50);
            
            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);
            
            // Configure timestamps with default values
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            entity.Property(e => e.EndedAt)
                .IsRequired(false);
                
            // Configure relationships
            entity.HasMany(e => e.Messages)
                .WithOne()
                .HasForeignKey(e => e.ChatRoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            // Add indexes for frequently queried columns
            entity.HasIndex(e => e.ChatRoomId);
            entity.HasIndex(e => e.SenderId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.IsRead);
            
            // Configure string properties
            entity.Property(e => e.Content)
                .IsRequired()
                .HasMaxLength(1000);
            
            entity.Property(e => e.SenderId)
                .IsRequired()
                .HasMaxLength(50);
            
            entity.Property(e => e.ReceiverId)
                .HasMaxLength(50);
            
            // Configure timestamp to use UTC
            entity.Property(e => e.Timestamp)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            // Configure IsRead flag with explicit default value
            entity.Property(e => e.IsRead)
                .IsRequired()
                .HasDefaultValue(false)
                .ValueGeneratedNever();
            
            // Configure foreign key relationship
            entity.HasOne<ChatRoom>()
                .WithMany(r => r.Messages)
                .HasForeignKey(m => m.ChatRoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
} 