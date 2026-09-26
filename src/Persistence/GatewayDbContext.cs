using Microsoft.EntityFrameworkCore;

namespace IotDaq.Persistence;

/// <summary>
/// One model for SQLite and PostgreSQL. The schema is created with EnsureCreated
/// plus a schema_info version row. See docs/database.md for why this is not a
/// pair of EF migration snapshots.
/// </summary>
public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public DbSet<SchemaInfoRow> SchemaInfo => Set<SchemaInfoRow>();

    public DbSet<CatalogMetaRow> CatalogMeta => Set<CatalogMetaRow>();

    public DbSet<BrandRow> Brands => Set<BrandRow>();

    public DbSet<ControllerModelRow> ControllerModels => Set<ControllerModelRow>();

    public DbSet<CatalogItemRow> CatalogItems => Set<CatalogItemRow>();

    public DbSet<BrandItemRow> BrandItems => Set<BrandItemRow>();

    public DbSet<AdapterRow> Adapters => Set<AdapterRow>();

    public DbSet<DeviceGroupRow> DeviceGroups => Set<DeviceGroupRow>();

    public DbSet<UserRow> Users => Set<UserRow>();

    public DbSet<ConfigBundleRow> ConfigBundles => Set<ConfigBundleRow>();

    public DbSet<ConfigGatewayRow> ConfigGateways => Set<ConfigGatewayRow>();

    public DbSet<ConfigDeviceRow> ConfigDevices => Set<ConfigDeviceRow>();

    public DbSet<ConfigTemplateRow> ConfigTemplates => Set<ConfigTemplateRow>();

    public DbSet<ConfigTemplatePointRow> ConfigTemplatePoints => Set<ConfigTemplatePointRow>();

    public DbSet<ConfigPointSetRow> ConfigPointSets => Set<ConfigPointSetRow>();

    public DbSet<ConfigMqttRow> ConfigMqtt => Set<ConfigMqttRow>();

    public DbSet<ConfigRevisionRow> ConfigRevisions => Set<ConfigRevisionRow>();

    public DbSet<SampleLatestRow> SampleLatest => Set<SampleLatestRow>();

    public DbSet<SampleHistoryRow> SampleHistory => Set<SampleHistoryRow>();

    public DbSet<AlarmRow> Alarms => Set<AlarmRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SchemaInfoRow>().ToTable("schema_info").HasKey(row => row.Id);
        modelBuilder.Entity<CatalogMetaRow>().ToTable("catalog_meta").HasKey(row => row.Id);
        modelBuilder.Entity<BrandRow>().ToTable("brands").HasKey(row => row.Id);
        modelBuilder.Entity<ControllerModelRow>().ToTable("controller_models").HasKey(row => row.Id);
        modelBuilder.Entity<ControllerModelRow>().HasIndex(row => row.BrandId);
        modelBuilder.Entity<CatalogItemRow>().ToTable("catalog_items").HasKey(row => row.Id);
        modelBuilder.Entity<BrandItemRow>().ToTable("brand_items").HasKey(row => new { row.BrandId, row.SourceName });
        modelBuilder.Entity<BrandItemRow>().HasIndex(row => new { row.BrandId, row.ItemId });
        modelBuilder.Entity<AdapterRow>().ToTable("adapters").HasKey(row => row.Id);
        modelBuilder.Entity<AdapterRow>().HasIndex(row => row.BrandId);
        modelBuilder.Entity<DeviceGroupRow>().ToTable("device_groups").HasKey(row => row.Id);
        modelBuilder.Entity<UserRow>().ToTable("users").HasKey(row => row.Username);

        modelBuilder.Entity<ConfigBundleRow>().ToTable("config_bundles").HasKey(row => row.Slot);
        modelBuilder.Entity<ConfigGatewayRow>().ToTable("config_gateways").HasKey(row => row.Slot);
        modelBuilder.Entity<ConfigDeviceRow>().ToTable("config_devices").HasKey(row => new { row.Slot, row.Id });
        modelBuilder.Entity<ConfigTemplateRow>().ToTable("config_templates").HasKey(row => new { row.Slot, row.Id });
        modelBuilder.Entity<ConfigTemplatePointRow>().ToTable("config_template_points")
            .HasKey(row => new { row.Slot, row.TemplateId, row.PointId });
        modelBuilder.Entity<ConfigPointSetRow>().ToTable("config_point_sets")
            .HasKey(row => new { row.Slot, row.DeviceId, row.PointId });
        modelBuilder.Entity<ConfigMqttRow>().ToTable("config_mqtt").HasKey(row => row.Slot);
        modelBuilder.Entity<ConfigRevisionRow>().ToTable("config_revisions").HasKey(row => row.Id);
        modelBuilder.Entity<ConfigRevisionRow>().HasIndex(row => row.Revision);
        modelBuilder.Entity<ConfigRevisionRow>().HasIndex(row => row.CreatedUnixMs);

        modelBuilder.Entity<SampleLatestRow>().ToTable("sample_latest").HasKey(row => new { row.DeviceId, row.PointId });
        modelBuilder.Entity<SampleHistoryRow>().ToTable("sample_history").HasKey(row => row.Id);
        modelBuilder.Entity<SampleHistoryRow>().HasIndex(row => new { row.DeviceId, row.PointId, row.TimestampUnixMs });
        modelBuilder.Entity<AlarmRow>().ToTable("alarms").HasKey(row => row.Id);
        modelBuilder.Entity<AlarmRow>().HasIndex(row => new { row.DeviceId, row.RaisedUnixMs });
    }
}
