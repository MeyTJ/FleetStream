using System.ComponentModel.DataAnnotations;
using FleetStream.Application.Shared.Results;
using FleetStream.Infrastructure.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetStream.UnitTests.Infrastructure.Options;

/// <summary>
/// Guards the Redis endpoint configuration against a defect that made every
/// Redis-backed endpoint return 500 while the host still reported healthy:
/// <c>ConnectionMultiplexer</c> throws ArgumentException("EndPoints must be
/// unique") when the same host:port is registered twice.
/// </summary>
public class RedisOptionsTests
{
    private static RedisOptions BindFromAppSettingsShape()
    {
        // Mirrors appsettings.json, which declares the same endpoint the options
        // class seeds by default.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:Endpoints:0:Host"] = "localhost",
                ["Redis:Endpoints:0:Port"] = "6379",
                ["Redis:KeyPrefix"] = "fleetstream",
            })
            .Build();

        var options = new RedisOptions();
        config.GetSection(RedisOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void Binder_appends_to_the_seeded_default_endpoint_list()
    {
        // Documents the .NET configuration binder behaviour that caused the bug:
        // a list with a default entry is *not* cleared before binding.
        var options = BindFromAppSettingsShape();

        options.Endpoints.Should().HaveCount(2,
            "the seeded default plus the configured entry both survive binding");
        options.Endpoints.Select(e => $"{e.Host}:{e.Port}")
            .Should().AllBeEquivalentTo("localhost:6379");
    }

    [Fact]
    public void DistinctEndpoints_collapses_the_duplicate_localhost_entry()
    {
        var options = BindFromAppSettingsShape();

        options.DistinctEndpoints().Should().HaveCount(1);
    }

    [Fact]
    public void DistinctEndpoints_treats_host_comparison_as_case_insensitive_and_trims()
    {
        var options = new RedisOptions
        {
            Endpoints = new List<RedisEndpoint>
            {
                new() { Host = "redis-a", Port = 6379 },
                new() { Host = "  REDIS-A ", Port = 6379 },
                new() { Host = "redis-a", Port = 6380 },
                new() { Host = "redis-b", Port = 6379 },
            },
        };

        var distinct = options.DistinctEndpoints();

        distinct.Select(e => $"{e.Host.Trim().ToLowerInvariant()}:{e.Port}")
            .Should()
            .Equal("redis-a:6379", "redis-a:6380", "redis-b:6379");
    }

    [Fact]
    public void DistinctEndpoints_preserves_declaration_order_for_a_real_cluster()
    {
        var options = new RedisOptions
        {
            Endpoints = new List<RedisEndpoint>
            {
                new() { Host = "node-3", Port = 6379 },
                new() { Host = "node-1", Port = 6379 },
                new() { Host = "node-2", Port = 6379 },
            },
        };

        options.DistinctEndpoints().Select(e => e.Host)
            .Should().Equal("node-3", "node-1", "node-2");
    }
}
