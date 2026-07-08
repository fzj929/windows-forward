using Microsoft.EntityFrameworkCore;
using WindowForward.Api.Models;

namespace WindowForward.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ForwardRule> ForwardRules => Set<ForwardRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ForwardRule>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Protocol).HasConversion<string>().HasMaxLength(8);
            entity.Property(x => x.ListenAddress).HasMaxLength(64);
            entity.Property(x => x.ConnectAddress).HasMaxLength(128);
            entity.Property(x => x.NatName).HasMaxLength(80);
            entity.Property(x => x.Prefix).HasMaxLength(64);
            entity.Property(x => x.RouteDestination).HasMaxLength(64);
            entity.Property(x => x.RouteMask).HasMaxLength(64);
            entity.Property(x => x.RouteGateway).HasMaxLength(64);
            entity.Property(x => x.SshHost).HasMaxLength(128);
            entity.Property(x => x.SshUser).HasMaxLength(80);
        });
    }
}
