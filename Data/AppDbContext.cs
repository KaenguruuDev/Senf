using Microsoft.EntityFrameworkCore;
using Senf.Models;

namespace Senf.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<EnvFile> EnvFiles { get; set; }
    public DbSet<SshKey> SshKeys { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        // Configure EnvFile entity
        modelBuilder.Entity<EnvFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.EncryptedContent).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
            
            entity.HasOne(e => e.User)
                .WithMany(u => u.EnvFiles)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<SshKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PublicKey).IsRequired();
            entity.Property(e => e.Fingerprint).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.SshKeys)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.Fingerprint).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
        });
    }
}
