using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ProjectDS.DbEf.Models;

namespace ProjectDS.DbEf;

public partial class ProjectDsContext : DbContext
{
    public ProjectDsContext()
    {
    }

    public ProjectDsContext(DbContextOptions<ProjectDsContext> options)
        : base(options)
    {
    }

    public virtual DbSet<GameSession> GameSessions { get; set; }

    public virtual DbSet<Player> Players { get; set; }

    public virtual DbSet<SessionEvent> SessionEvents { get; set; }

    public virtual DbSet<SessionSystem> SessionSystems { get; set; }

    public virtual DbSet<Snapshot> Snapshots { get; set; }

    public virtual DbSet<SnapshotShip> SnapshotShips { get; set; }

    public virtual DbSet<StarSystem> StarSystems { get; set; }

    public virtual DbSet<VwSessionSummary> VwSessionSummaries { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer("Server=WIN-2TR20CR1C3K\\SQLEXPRESS;Database=ProjectDS;Trusted_Connection=True;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameSession>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("PK__GameSess__C9F49290A05ACFD2");

            entity.Property(e => e.StartedUtc).HasDefaultValueSql("(sysutcdatetime())", "DF_Sessions_StartedUtc");

            entity.HasOne(d => d.Player).WithMany(p => p.GameSessions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__GameSessi__Playe__3C69FB99");
        });

        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(e => e.PlayerId).HasName("PK__Players__4A4E74C80FEA8F91");

            entity.Property(e => e.CreatedUtc).HasDefaultValueSql("(sysutcdatetime())", "DF_Players_CreatedUtc");
        });

        modelBuilder.Entity<SessionEvent>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("PK__SessionE__7944C81007E01E06");

            entity.Property(e => e.CreatedUtc).HasDefaultValueSql("(sysutcdatetime())", "DF_Events_CreatedUtc");

            entity.HasOne(d => d.Session).WithMany(p => p.SessionEvents)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__SessionEv__Sessi__4BAC3F29");
        });

        modelBuilder.Entity<SessionSystem>(entity =>
        {
            entity.HasOne(d => d.Session).WithMany(p => p.SessionSystems)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__SessionSy__Sessi__412EB0B6");

            entity.HasOne(d => d.System).WithMany(p => p.SessionSystems)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__SessionSy__Syste__4222D4EF");
        });

        modelBuilder.Entity<Snapshot>(entity =>
        {
            entity.HasKey(e => e.SnapshotId).HasName("PK__Snapshot__664F572B8AD803A9");

            entity.Property(e => e.CreatedUtc).HasDefaultValueSql("(sysutcdatetime())", "DF_Snapshots_CreatedUtc");

            entity.HasOne(d => d.Session).WithMany(p => p.Snapshots)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Snapshots__Sessi__44FF419A");
        });

        modelBuilder.Entity<SnapshotShip>(entity =>
        {
            entity.HasOne(d => d.Snapshot).WithMany(p => p.SnapshotShips).HasConstraintName("FK__SnapshotS__Snaps__48CFD27E");
        });

        modelBuilder.Entity<StarSystem>(entity =>
        {
            entity.HasKey(e => e.SystemId).HasName("PK__StarSyst__9394F68A18CA343E");
        });

        modelBuilder.Entity<VwSessionSummary>(entity =>
        {
            entity.ToView("vw_SessionSummary");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
