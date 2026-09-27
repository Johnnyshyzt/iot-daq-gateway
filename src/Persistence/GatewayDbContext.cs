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

    public DbSet<StateTransitionRow> StateTransitions => Set<StateTransitionRow>();

    public DbSet<AppSettingRow> AppSettings => Set<AppSettingRow>();

    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    public DbSet<NotificationChannelRow> NotificationChannels => Set<NotificationChannelRow>();

    public DbSet<NotificationRuleRow> NotificationRules => Set<NotificationRuleRow>();

    public DbSet<NotificationDeliveryRow> NotificationDeliveries => Set<NotificationDeliveryRow>();

    public DbSet<ReportScheduleRow> ReportSchedules => Set<ReportScheduleRow>();

    public DbSet<LinkStatusRow> LinkStatus => Set<LinkStatusRow>();

    public DbSet<HttpPushTargetRow> HttpPushTargets => Set<HttpPushTargetRow>();

    public DbSet<ApiKeyRow> ApiKeys => Set<ApiKeyRow>();

    public DbSet<InstalledLicenseRow> InstalledLicense => Set<InstalledLicenseRow>();

    public DbSet<SecurityStateRow> SecurityState => Set<SecurityStateRow>();

    public DbSet<SelfTestRunRow> SelfTestRuns => Set<SelfTestRunRow>();

    public DbSet<ComputedPointRow> ComputedPoints => Set<ComputedPointRow>();

    public DbSet<EdgeRuleRow> EdgeRules => Set<EdgeRuleRow>();

    public DbSet<RuleLogRow> RuleLogs => Set<RuleLogRow>();

    public DbSet<RuleEventRow> RuleEvents => Set<RuleEventRow>();

    public DbSet<DowntimeReasonRow> DowntimeReasons => Set<DowntimeReasonRow>();

    public DbSet<DowntimeEventRow> DowntimeEvents => Set<DowntimeEventRow>();

    public DbSet<StateMapRow> StateMaps => Set<StateMapRow>();

    public DbSet<PlannedStopRow> PlannedStops => Set<PlannedStopRow>();

    public DbSet<CycleTimeRow> CycleTimes => Set<CycleTimeRow>();

    public DbSet<ScrapEntryRow> ScrapEntries => Set<ScrapEntryRow>();

    public DbSet<ToolRow> Tools => Set<ToolRow>();

    public DbSet<ToolPocketRow> ToolPockets => Set<ToolPocketRow>();

    public DbSet<ToolLifeRow> ToolLives => Set<ToolLifeRow>();

    public DbSet<ToolCursorRow> ToolCursors => Set<ToolCursorRow>();

    public DbSet<ToolChangeRow> ToolChanges => Set<ToolChangeRow>();

    public DbSet<NcProgramRow> NcPrograms => Set<NcProgramRow>();

    public DbSet<NcProgramVersionRow> NcProgramVersions => Set<NcProgramVersionRow>();

    public DbSet<NcProgramDeviceRow> NcProgramDevices => Set<NcProgramDeviceRow>();

    public DbSet<NcTransferRow> NcTransfers => Set<NcTransferRow>();

    public DbSet<EnrollmentTokenRow> EnrollmentTokens => Set<EnrollmentTokenRow>();

    public DbSet<FleetGatewayRow> FleetGateways => Set<FleetGatewayRow>();

    public DbSet<FleetGroupRow> FleetGroups => Set<FleetGroupRow>();

    public DbSet<CentralTemplateRow> CentralTemplates => Set<CentralTemplateRow>();

    public DbSet<ConfigPushRow> ConfigPushes => Set<ConfigPushRow>();

    public DbSet<ConfigPushTargetRow> ConfigPushTargets => Set<ConfigPushTargetRow>();

    public DbSet<RolloutRow> Rollouts => Set<RolloutRow>();

    public DbSet<RolloutTargetRow> RolloutTargets => Set<RolloutTargetRow>();

    public DbSet<CentralAlertRow> CentralAlerts => Set<CentralAlertRow>();

    public DbSet<CentralAuditRow> CentralAudits => Set<CentralAuditRow>();

    public DbSet<CentralDocumentRow> CentralDocuments => Set<CentralDocumentRow>();

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
        modelBuilder.Entity<SampleHistoryRow>().HasIndex(row => new { row.DeviceId, row.PointId, row.TimestampUnixMs })
            .HasDatabaseName("ix_sample_history_device_point_time");
        modelBuilder.Entity<SampleHistoryRow>().HasIndex(row => row.TimestampUnixMs)
            .HasDatabaseName("ix_sample_history_time");
        modelBuilder.Entity<SampleHistoryRow>().HasIndex(row => new { row.DeviceId, row.TimestampUnixMs })
            .HasDatabaseName("ix_sample_history_device_time");
        modelBuilder.Entity<AlarmRow>().ToTable("alarms").HasKey(row => row.Id);
        modelBuilder.Entity<AlarmRow>().HasIndex(row => new { row.DeviceId, row.RaisedUnixMs })
            .HasDatabaseName("ix_alarms_device_raised");
        modelBuilder.Entity<AlarmRow>().HasIndex(row => new { row.Active, row.RaisedUnixMs })
            .HasDatabaseName("ix_alarms_active_raised");
        modelBuilder.Entity<AlarmRow>().HasIndex(row => new { row.DeviceId, row.Code })
            .HasDatabaseName("ix_alarms_device_code");
        modelBuilder.Entity<StateTransitionRow>().ToTable("state_transitions").HasKey(row => row.Id);
        modelBuilder.Entity<StateTransitionRow>().HasIndex(row => new { row.DeviceId, row.StartedUnixMs })
            .HasDatabaseName("ix_state_device_started");
        modelBuilder.Entity<StateTransitionRow>().HasIndex(row => row.EndedUnixMs)
            .HasDatabaseName("ix_state_ended");
        modelBuilder.Entity<AppSettingRow>().ToTable("app_settings").HasKey(row => row.Key);
        modelBuilder.Entity<AuditEventRow>().ToTable("audit_events").HasKey(row => row.Id);
        modelBuilder.Entity<AuditEventRow>().Property(row => row.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<AuditEventRow>().HasIndex(row => row.UnixMs).HasDatabaseName("ix_audit_unix");

        modelBuilder.Entity<NotificationChannelRow>().ToTable("notification_channels").HasKey(row => row.Id);
        modelBuilder.Entity<NotificationRuleRow>().ToTable("notification_rules").HasKey(row => row.Id);
        modelBuilder.Entity<NotificationDeliveryRow>().ToTable("notification_deliveries").HasKey(row => row.Id);
        modelBuilder.Entity<NotificationDeliveryRow>().Property(row => row.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<NotificationDeliveryRow>().HasIndex(row => row.CreatedUnixMs).HasDatabaseName("ix_delivery_created");
        modelBuilder.Entity<NotificationDeliveryRow>().HasIndex(row => new { row.Status, row.CreatedUnixMs }).HasDatabaseName("ix_delivery_status");
        modelBuilder.Entity<ReportScheduleRow>().ToTable("report_schedules").HasKey(row => row.Id);

        modelBuilder.Entity<LinkStatusRow>().ToTable("link_status").HasKey(row => row.DeviceId);
        modelBuilder.Entity<HttpPushTargetRow>().ToTable("http_push_targets").HasKey(row => row.Id);
        modelBuilder.Entity<ApiKeyRow>().ToTable("api_keys").HasKey(row => row.Id);
        modelBuilder.Entity<ApiKeyRow>().HasIndex(row => row.KeyHash).IsUnique().HasDatabaseName("ix_api_keys_hash");
        modelBuilder.Entity<ApiKeyRow>().HasIndex(row => row.Prefix).HasDatabaseName("ix_api_keys_prefix");
        modelBuilder.Entity<InstalledLicenseRow>().ToTable("installed_license").HasKey(row => row.Id);
        modelBuilder.Entity<SecurityStateRow>().ToTable("security_state").HasKey(row => row.Id);
        modelBuilder.Entity<SelfTestRunRow>().ToTable("self_test_runs").HasKey(row => row.Id);
        modelBuilder.Entity<SelfTestRunRow>().Property(row => row.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<SelfTestRunRow>().HasIndex(row => new { row.DeviceId, row.FinishedUnixMs })
            .HasDatabaseName("ix_self_test_device_time");

        modelBuilder.Entity<ComputedPointRow>().ToTable("computed_points").HasKey(row => row.Id);
        modelBuilder.Entity<ComputedPointRow>().HasIndex(row => new { row.Scope, row.OwnerId, row.PointId }).IsUnique()
            .HasDatabaseName("ix_computed_owner_point");
        modelBuilder.Entity<EdgeRuleRow>().ToTable("edge_rules").HasKey(row => row.Id);
        modelBuilder.Entity<RuleLogRow>().ToTable("rule_logs").HasKey(row => row.Id);
        modelBuilder.Entity<RuleLogRow>().Property(row => row.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<RuleLogRow>().HasIndex(row => row.UnixMs).HasDatabaseName("ix_rule_log_time");
        modelBuilder.Entity<RuleEventRow>().ToTable("rule_events").HasKey(row => row.Id);
        modelBuilder.Entity<RuleEventRow>().Property(row => row.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<RuleEventRow>().HasIndex(row => new { row.Published, row.UnixMs }).HasDatabaseName("ix_rule_event_pending");
        modelBuilder.Entity<DowntimeReasonRow>().ToTable("downtime_reasons").HasKey(row => row.Id);
        modelBuilder.Entity<DowntimeEventRow>().ToTable("downtime_events").HasKey(row => row.Id);
        modelBuilder.Entity<DowntimeEventRow>().HasIndex(row => new { row.DeviceId, row.StartedUnixMs }).HasDatabaseName("ix_downtime_device_time");
        modelBuilder.Entity<StateMapRow>().ToTable("state_maps").HasKey(row => row.Id);
        modelBuilder.Entity<PlannedStopRow>().ToTable("planned_stops").HasKey(row => row.Id);
        modelBuilder.Entity<CycleTimeRow>().ToTable("cycle_times").HasKey(row => row.Id);
        modelBuilder.Entity<ScrapEntryRow>().ToTable("scrap_entries").HasKey(row => row.Id);
        modelBuilder.Entity<ScrapEntryRow>().HasIndex(row => new { row.DeviceId, row.UnixMs }).HasDatabaseName("ix_scrap_device_time");

        modelBuilder.Entity<ToolRow>().ToTable("tools").HasKey(row => row.Id);
        modelBuilder.Entity<ToolRow>().HasIndex(row => row.ToolNumber).IsUnique().HasDatabaseName("ix_tools_number");
        modelBuilder.Entity<ToolPocketRow>().ToTable("tool_pockets").HasKey(row => row.Id);
        modelBuilder.Entity<ToolPocketRow>().HasIndex(row => new { row.DeviceId, row.Pocket }).IsUnique().HasDatabaseName("ix_tool_pocket");
        modelBuilder.Entity<ToolLifeRow>().ToTable("tool_life").HasKey(row => row.Id);
        modelBuilder.Entity<ToolLifeRow>().HasIndex(row => new { row.DeviceId, row.ToolNumber }).IsUnique().HasDatabaseName("ix_tool_life_device");
        modelBuilder.Entity<ToolCursorRow>().ToTable("tool_cursors").HasKey(row => row.DeviceId);
        modelBuilder.Entity<ToolChangeRow>().ToTable("tool_changes").HasKey(row => row.Id);
        modelBuilder.Entity<ToolChangeRow>().HasIndex(row => new { row.DeviceId, row.UnixMs }).HasDatabaseName("ix_tool_change_time");
        modelBuilder.Entity<NcProgramRow>().ToTable("nc_programs").HasKey(row => row.Id);
        modelBuilder.Entity<NcProgramVersionRow>().ToTable("nc_program_versions").HasKey(row => row.Id);
        modelBuilder.Entity<NcProgramVersionRow>().HasIndex(row => new { row.ProgramId, row.Version }).IsUnique().HasDatabaseName("ix_nc_version");
        modelBuilder.Entity<NcProgramDeviceRow>().ToTable("nc_program_devices").HasKey(row => new { row.ProgramId, row.DeviceId });
        modelBuilder.Entity<NcTransferRow>().ToTable("nc_transfers").HasKey(row => row.Id);
        modelBuilder.Entity<NcTransferRow>().HasIndex(row => row.UnixMs).HasDatabaseName("ix_nc_transfer_time");
        modelBuilder.Entity<EnrollmentTokenRow>().ToTable("enrollment_tokens").HasKey(row => row.Id);
        modelBuilder.Entity<EnrollmentTokenRow>().HasIndex(row => row.TokenHash).IsUnique().HasDatabaseName("ix_enroll_hash");
        modelBuilder.Entity<FleetGatewayRow>().ToTable("fleet_gateways").HasKey(row => row.Id);
        modelBuilder.Entity<FleetGroupRow>().ToTable("fleet_groups").HasKey(row => row.Id);
        modelBuilder.Entity<CentralTemplateRow>().ToTable("central_templates").HasKey(row => row.Id);
        modelBuilder.Entity<CentralTemplateRow>().HasIndex(row => new { row.Key, row.Version }).IsUnique().HasDatabaseName("ix_central_template_ver");
        modelBuilder.Entity<ConfigPushRow>().ToTable("config_pushes").HasKey(row => row.Id);
        modelBuilder.Entity<ConfigPushTargetRow>().ToTable("config_push_targets").HasKey(row => row.Id);
        modelBuilder.Entity<ConfigPushTargetRow>().HasIndex(row => new { row.PushId, row.GatewayId }).IsUnique().HasDatabaseName("ix_push_target");
        modelBuilder.Entity<RolloutRow>().ToTable("rollouts").HasKey(row => row.Id);
        modelBuilder.Entity<RolloutTargetRow>().ToTable("rollout_targets").HasKey(row => row.Id);
        modelBuilder.Entity<RolloutTargetRow>().HasIndex(row => new { row.RolloutId, row.GatewayId }).IsUnique().HasDatabaseName("ix_rollout_target");
        modelBuilder.Entity<CentralAlertRow>().ToTable("central_alerts").HasKey(row => row.Id);
        modelBuilder.Entity<CentralAuditRow>().ToTable("central_audits").HasKey(row => row.Id);
        modelBuilder.Entity<CentralAuditRow>().Property(row => row.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<CentralAuditRow>().HasIndex(row => row.UnixMs).HasDatabaseName("ix_central_audit_time");
        modelBuilder.Entity<CentralDocumentRow>().ToTable("central_documents").HasKey(row => row.Kind);
    }
}
