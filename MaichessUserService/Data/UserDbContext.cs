using MaichessUserService.Entities;
using Microsoft.EntityFrameworkCore;

namespace MaichessUserService.Data;

internal sealed class UserDbContext(DbContextOptions<UserDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).HasColumnName("id");
            entity.Property(u => u.Username).HasColumnName("username").HasMaxLength(50).IsRequired();
            entity.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(u => u.Elo).HasColumnName("elo").HasDefaultValue(1200).IsRequired();
            entity.Property(u => u.Wins).HasColumnName("wins").HasDefaultValue(0).IsRequired();
            entity.Property(u => u.Losses).HasColumnName("losses").HasDefaultValue(0).IsRequired();
            entity.Property(u => u.Draws).HasColumnName("draws").HasDefaultValue(0).IsRequired();
            entity.HasIndex(u => u.Username).IsUnique();
        });
    }
}
