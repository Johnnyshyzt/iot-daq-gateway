using IotDaq.Licensing;
using IotDaq.Persistence;
using IotDaq.Persistence.Shop;
using Studio.Host.Config;
using Studio.Host.Endpoints;
using Studio.Host.Licensing;

namespace Studio.Host.Shop;

public static class ProgramEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/programs/capability", () => ApiResults.Ok(new { brands = TransferCatalog.DescribeAll() }));

        api.MapGet("/programs", (GatewayPersistence database, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.NcPrograms))
            {
                return ApiResults.Ok(new
                {
                    licensed = false,
                    message = licensing.Denial(LicenseFeatures.NcPrograms),
                    programs = Array.Empty<object>(),
                    brands = TransferCatalog.DescribeAll()
                });
            }

            var programs = database.ListPrograms().Select(program => new
            {
                program.Id,
                program.Name,
                program.Comment,
                program.Status,
                program.CreatedBy,
                program.UpdatedUnixMs,
                devices = database.ListProgramDevices(program.Id),
                versions = database.ListProgramVersions(program.Id).Select(version => new
                {
                    version.Id,
                    version.Version,
                    version.Checksum,
                    version.Comment,
                    version.Status,
                    version.UploadedBy,
                    version.UploadedUnixMs,
                    version.ApprovedBy,
                    version.ApprovedUnixMs
                })
            });
            return ApiResults.Ok(new
            {
                licensed = true,
                programs,
                transfers = database.ListTransfers(null, 50),
                brands = TransferCatalog.DescribeAll()
            });
        });

        api.MapPost("/programs", (ProgramWrite? body, GatewayPersistence database, LicenseService licensing, HttpContext http) =>
        {
            var gate = Gate(licensing);
            if (gate is not null)
            {
                return gate;
            }

            if (string.IsNullOrWhiteSpace(body?.Name) || string.IsNullOrWhiteSpace(body.Content))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "program_invalid", "请填写程序名和内容。");
            }

            if (body.Content.Length > ProgramTransfer.MaxChars)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "program_too_large", "程序超过 256 KB。");
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var actor = http.Items["studio.user"] as string ?? "";
            var id = string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("N") : body.Id.Trim();
            if (!ConfigValidator.IsSafeId(id))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "program_invalid", "程序 Id 不合法。");
            }

            var content = body.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            database.SaveProgram(new NcProgramRow
            {
                Id = id,
                Name = body.Name.Trim(),
                Comment = body.Comment?.Trim() ?? "",
                Status = "draft",
                CreatedBy = actor,
                UpdatedUnixMs = now
            });
            var version = database.AddProgramVersion(new NcProgramVersionRow
            {
                Id = Guid.NewGuid().ToString("N"),
                ProgramId = id,
                Content = content,
                Checksum = ContentHash.Sha256(content),
                Comment = body.Comment?.Trim() ?? "",
                Status = "draft",
                UploadedBy = actor,
                UploadedUnixMs = now
            });
            if (body.DeviceIds is { Count: > 0 })
            {
                database.SetProgramDevices(id, body.DeviceIds);
            }

            ConfigAudit.Write(http, database, "program.upload", id, version.Checksum);
            return ApiResults.Ok(new { programId = id, version });
        });

        api.MapPost("/programs/{id}/versions", (string id, ProgramWrite? body, GatewayPersistence database, LicenseService licensing, HttpContext http) =>
        {
            var gate = Gate(licensing);
            if (gate is not null)
            {
                return gate;
            }

            if (database.FindProgram(id) is null || string.IsNullOrWhiteSpace(body?.Content))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "program_invalid", "程序不存在或内容为空。");
            }

            if (body.Content.Length > ProgramTransfer.MaxChars)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "program_too_large", "程序超过 256 KB。");
            }

            var content = body.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            var version = database.AddProgramVersion(new NcProgramVersionRow
            {
                Id = Guid.NewGuid().ToString("N"),
                ProgramId = id,
                Content = content,
                Checksum = ContentHash.Sha256(content),
                Comment = body.Comment?.Trim() ?? "",
                Status = "draft",
                UploadedBy = http.Items["studio.user"] as string ?? "",
                UploadedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            ConfigAudit.Write(http, database, "program.upload", id, "v" + version.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + version.Checksum);
            return ApiResults.Ok(version);
        });

        api.MapGet("/programs/{id}/diff", (string id, int? from, int? to, GatewayPersistence database, LicenseService licensing) =>
        {
            if (!licensing.Allows(LicenseFeatures.NcPrograms))
            {
                return ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.NcPrograms));
            }

            var versions = database.ListProgramVersions(id);
            var left = versions.FirstOrDefault(row => row.Version == (from ?? 1));
            var right = versions.FirstOrDefault(row => row.Version == (to ?? versions.LastOrDefault()?.Version ?? 1));
            if (left is null || right is null)
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "version_missing", "找不到要对比的版本。");
            }

            return ApiResults.Ok(new
            {
                from = left.Version,
                to = right.Version,
                fromChecksum = left.Checksum,
                toChecksum = right.Checksum,
                lines = TextDiff.Lines(left.Content, right.Content)
            });
        });

        api.MapPost("/programs/{id}/approve", (string id, VersionWrite? body, GatewayPersistence database, LicenseService licensing, HttpContext http) =>
        {
            var gate = Gate(licensing);
            if (gate is not null)
            {
                return gate;
            }

            var version = Resolve(database, id, body?.VersionId);
            if (version is null)
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "version_missing", "版本不存在。");
            }

            var saved = database.SetProgramVersionStatus(version.Id, "approved", http.Items["studio.user"] as string ?? "", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            ConfigAudit.Write(http, database, "program.approve", id, saved?.Checksum ?? "");
            return ApiResults.Ok(saved!);
        });

        api.MapPost("/programs/{id}/archive", (string id, VersionWrite? body, GatewayPersistence database, LicenseService licensing, HttpContext http) =>
        {
            var gate = Gate(licensing);
            if (gate is not null)
            {
                return gate;
            }

            var version = Resolve(database, id, body?.VersionId);
            if (version is null)
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "version_missing", "版本不存在。");
            }

            var saved = database.SetProgramVersionStatus(version.Id, "archived", http.Items["studio.user"] as string ?? "", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            ConfigAudit.Write(http, database, "program.archive", id, saved?.Checksum ?? "");
            return ApiResults.Ok(saved!);
        });

        api.MapPut("/programs/{id}/devices", (string id, ProgramDevicesWrite? body, GatewayPersistence database, LicenseService licensing, HttpContext http) =>
        {
            var gate = Gate(licensing);
            if (gate is not null)
            {
                return gate;
            }

            if (database.FindProgram(id) is null)
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "program_missing", "程序不存在。");
            }

            database.SetProgramDevices(id, body?.DeviceIds ?? []);
            ConfigAudit.Write(http, database, "program.associate", id, string.Join(',', body?.DeviceIds ?? []));
            return ApiResults.Ok(new { devices = database.ListProgramDevices(id) });
        });

        api.MapPost("/programs/{id}/send", (string id, ProgramMoveWrite? body, GatewayPersistence database, ConfigStore store, LicenseService licensing, HttpContext http) =>
        {
            var gate = Gate(licensing);
            if (gate is not null)
            {
                return gate;
            }

            var version = Resolve(database, id, body?.VersionId);
            if (version is null || string.IsNullOrWhiteSpace(body?.DeviceId))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "program_invalid", "请指定设备和版本。");
            }

            var result = ProgramTransfer.Send(database, store, version, body.DeviceId.Trim(), body.Channel?.Trim() ?? "dnc-folder", body.Folder, http.Items["studio.user"] as string ?? "");
            ConfigAudit.Write(http, database, "program.send", id, result.Ok ? result.Checksum : result.Message);
            return result.Ok
                ? ApiResults.Ok(result)
                : ApiResults.Error(result.Code == "program_not_approved" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest, result.Code, result.Message);
        });

        api.MapPost("/programs/{id}/receive", (string id, ProgramMoveWrite? body, GatewayPersistence database, ConfigStore store, LicenseService licensing, HttpContext http) =>
        {
            var gate = Gate(licensing);
            if (gate is not null)
            {
                return gate;
            }

            var program = database.FindProgram(id);
            if (program is null || string.IsNullOrWhiteSpace(body?.DeviceId))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "program_invalid", "请指定程序和设备。");
            }

            var result = ProgramTransfer.Receive(database, store, program, body.DeviceId.Trim(), body.Channel?.Trim() ?? "dnc-folder", body.Folder, http.Items["studio.user"] as string ?? "");
            ConfigAudit.Write(http, database, "program.receive", id, result.Ok ? result.Checksum : result.Message);
            return result.Ok
                ? ApiResults.Ok(result)
                : ApiResults.Error(StatusCodes.Status400BadRequest, result.Code, result.Message);
        });
    }

    private static IResult? Gate(LicenseService licensing) =>
        licensing.Allows(LicenseFeatures.NcPrograms)
            ? null
            : ApiResults.Error(StatusCodes.Status403Forbidden, "license_feature", licensing.Denial(LicenseFeatures.NcPrograms));

    private static NcProgramVersionRow? Resolve(GatewayPersistence database, string programId, string? versionId)
    {
        if (!string.IsNullOrWhiteSpace(versionId))
        {
            var found = database.FindProgramVersion(versionId);
            return found is not null && found.ProgramId == programId ? found : null;
        }

        return database.ListProgramVersions(programId).LastOrDefault();
    }
}

public sealed class ProgramWrite
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Comment { get; set; }

    public string? Content { get; set; }

    public List<string>? DeviceIds { get; set; }
}

public sealed class VersionWrite
{
    public string? VersionId { get; set; }
}

public sealed class ProgramDevicesWrite
{
    public List<string>? DeviceIds { get; set; }
}

public sealed class ProgramMoveWrite
{
    public string? VersionId { get; set; }

    public string? DeviceId { get; set; }

    public string? Channel { get; set; }

    public string? Folder { get; set; }
}
