using Microsoft.EntityFrameworkCore;

namespace Corvees.Infrastructure;

public sealed class CorveesDbContext(DbContextOptions<CorveesDbContext> options) : DbContext(options)
{
    public Guid? GroupId { get; set; }

    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Step> Steps => Set<Step>();
    public DbSet<ProjectDependency> ProjectDependencies => Set<ProjectDependency>();
    public DbSet<StepDependency> StepDependencies => Set<StepDependency>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Group>(entity =>
        {
            entity.ToTable("groups");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasQueryFilter(x => GroupId != null && x.Id == GroupId);
        });
        model.Entity<Member>(entity =>
        {
            entity.ToTable("members");
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.GroupId, x.Id });
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.Property(x => x.TokenHash).HasMaxLength(64);
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => GroupId != null && x.GroupId == GroupId);
        });
        model.Entity<Location>(entity =>
        {
            entity.ToTable("locations");
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.GroupId, x.Id });
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Address).HasMaxLength(500);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => GroupId != null && x.GroupId == GroupId);
        });
        model.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.GroupId, x.Id });
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(10000);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.StepListVersion).IsConcurrencyToken();
            entity.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>().WithMany().HasForeignKey(x => new { x.GroupId, x.LocationId })
                .HasPrincipalKey(x => new { x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => GroupId != null && x.GroupId == GroupId);
        });
        model.Entity<Step>(entity =>
        {
            entity.ToTable("steps", table => table.HasCheckConstraint("ck_steps_status", "status IN ('todo', 'in_progress', 'done')"));
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.GroupId, x.ProjectId, x.Id });
            entity.Property(x => x.Title).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(10000);
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne<Project>().WithMany().HasForeignKey(x => new { x.GroupId, x.ProjectId })
                .HasPrincipalKey(x => new { x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => GroupId != null && x.GroupId == GroupId);
        });
        model.Entity<ProjectDependency>(entity =>
        {
            entity.ToTable("project_dependencies", table => table.HasCheckConstraint("ck_project_dependency_self", "project_id <> prerequisite_id"));
            entity.HasKey(x => new { x.GroupId, x.ProjectId, x.PrerequisiteId });
            entity.HasOne<Project>().WithMany().HasForeignKey(x => new { x.GroupId, x.ProjectId })
                .HasPrincipalKey(x => new { x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Project>().WithMany().HasForeignKey(x => new { x.GroupId, x.PrerequisiteId })
                .HasPrincipalKey(x => new { x.GroupId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => GroupId != null && x.GroupId == GroupId);
        });
        model.Entity<StepDependency>(entity =>
        {
            entity.ToTable("step_dependencies", table => table.HasCheckConstraint("ck_step_dependency_self", "step_id <> prerequisite_id"));
            entity.HasKey(x => new { x.GroupId, x.ProjectId, x.StepId, x.PrerequisiteId });
            entity.HasOne<Step>().WithMany().HasForeignKey(x => new { x.GroupId, x.ProjectId, x.StepId })
                .HasPrincipalKey(x => new { x.GroupId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Step>().WithMany().HasForeignKey(x => new { x.GroupId, x.ProjectId, x.PrerequisiteId })
                .HasPrincipalKey(x => new { x.GroupId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => GroupId != null && x.GroupId == GroupId);
        });

        foreach (var entity in model.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
    }
}
