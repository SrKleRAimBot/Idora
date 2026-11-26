using Microsoft.EntityFrameworkCore;

namespace Idora.EntityFrameworkCore.Tests.Support;

/// <summary>
/// The main model: every configuration method used the way an application would use it.
/// </summary>
public sealed class TestDbContext(DbContextOptions<TestDbContext> options, IEntityPrefixRegistry prefixes)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<Animal> Animals => Set<Animal>();

    public DbSet<Note> Notes => Set<Note>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseIdoraIds();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIdoraKey(user => user.Id, prefixes);

        modelBuilder.Entity<Order>().HasIdoraKey(order => order.Id);

        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.HasIdoraKey(i => i.Id, prefixes);
            invoice.HasOne<Invoice>().WithMany().HasForeignKey(i => i.CorrectsId);
            invoice.Property(i => i.PublicReference).HasIdoraValueGenerator();
        });

        modelBuilder.Entity<Animal>().HasIdoraKey(animal => animal.Id);
        modelBuilder.Entity<Dog>();
        modelBuilder.Entity<Cat>();

        modelBuilder.Entity<Note>().HasIdoraKey(note => note.Id);
    }
}
