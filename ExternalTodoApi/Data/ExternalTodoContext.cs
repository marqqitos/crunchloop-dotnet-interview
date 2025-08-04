using Microsoft.EntityFrameworkCore;
using ExternalTodoApi.Models;

namespace ExternalTodoApi.Data;

public class ExternalTodoContext : DbContext
{
    public ExternalTodoContext(DbContextOptions<ExternalTodoContext> options) : base(options)
    {
    }

    public DbSet<TodoList> TodoLists { get; set; } = null!;
    public DbSet<TodoItem> TodoItems { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure TodoList
        modelBuilder.Entity<TodoList>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever(); // We'll generate GUIDs manually
            entity.Property(e => e.SourceId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            // Configure the one-to-many relationship
            entity.HasMany(e => e.Items)
                  .WithOne(e => e.TodoList)
                  .HasForeignKey(e => e.TodoListId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure TodoItem
        modelBuilder.Entity<TodoItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever(); // We'll generate GUIDs manually
            entity.Property(e => e.SourceId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.TodoListId).IsRequired();
        });
    }
}