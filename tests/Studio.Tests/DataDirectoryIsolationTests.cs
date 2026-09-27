using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Studio.Tests;

public sealed class DataDirectoryIsolationTests
{
    [Fact]
    public void Environment_directory_is_used_only_when_the_host_has_no_configured_directory()
    {
        var configured = Directory.CreateTempSubdirectory("iso-config").FullName;
        var fromEnv = Directory.CreateTempSubdirectory("iso-env").FullName;
        var previous = Environment.GetEnvironmentVariable("STUDIO_DATA");
        Environment.SetEnvironmentVariable("STUDIO_DATA", fromEnv);
        try
        {
            var withConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Host:DataDirectory"] = configured
            }).Build();
            Assert.Equal(
                Path.GetFullPath(configured),
                IotDaq.Host.HostPaths.ResolveDataDirectory(new StubEnvironment(), withConfig));

            var envOnly = new ConfigurationBuilder().Build();
            Assert.Equal(
                Path.GetFullPath(fromEnv),
                IotDaq.Host.HostPaths.ResolveDataDirectory(new StubEnvironment(), envOnly));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDIO_DATA", previous);
            TryDelete(configured);
            TryDelete(fromEnv);
        }
    }

    [Fact]
    public async Task Host_ignores_process_wide_studio_data_when_its_directory_is_configured()
    {
        var configured = Directory.CreateTempSubdirectory("iso-host").FullName;
        var stolen = Directory.CreateTempSubdirectory("iso-stolen").FullName;
        var previous = Environment.GetEnvironmentVariable("STUDIO_DATA");
        Environment.SetEnvironmentVariable("STUDIO_DATA", stolen);
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Host:DataDirectory", configured);
                builder.UseSetting("Host:Acquisition", "off");
            });
            using var client = factory.CreateClient();
            var health = await client.GetAsync("/healthz");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            Assert.True(File.Exists(Path.Combine(configured, "gateway.db")));
            Assert.False(File.Exists(Path.Combine(stolen, "gateway.db")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDIO_DATA", previous);
            TryDelete(configured);
            TryDelete(stolen);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
