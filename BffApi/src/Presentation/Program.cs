// =============================================================================
//  FleetStream BFF API  (Phase 3)
//  Composition root. Pure-DI vertical slice wiring + the Decorator pattern.
// =============================================================================

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Confluent.Kafka;
using FleetStream.Application.Abstractions;
using FleetStream.Application.Features.FleetAlerts.Acknowledge;
using FleetStream.Application.Shared.Decorators;
using FleetStream.Application.Shared.Messaging;
using FleetStream.Infrastructure.Caching;
using FleetStream.Infrastructure.Graphify;
using FleetStream.Infrastructure.Messaging;
using FleetStream.Infrastructure.Metrics;
using FleetStream.Infrastructure.Options;
using FleetStream.Infrastructure.Services;
using FleetStream.Presentation.Auth;
using FleetStream.Presentation.Controllers;
using FleetStream.Presentation.Hosting;
using FleetStream.Presentation.Hubs;
using FleetStream.Presentation.Middleware;
using FleetStream.Presentation.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.FeatureManagement;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Yarp.ReverseProxy.Transforms;
using Microsoft.Extensions.Http.Resilience;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var services = builder.Services;
var otel = config.GetSection("OpenTelemetry").Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    o.UseUtcTimestamp = true;
});

// -----------------------------------------------------------------------------
//  Strongly-typed options
// -----------------------------------------------------------------------------
services.AddOptions<RedisOptions>().Bind(config.GetSection("Redis")).ValidateDataAnnotations().ValidateOnStart();
services.AddOptions<KafkaOptions>().Bind(config.GetSection("Kafka")).ValidateDataAnnotations().ValidateOnStart();
services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
services.AddOptions<JwtOptions>().Bind(config.GetSection("Jwt")).ValidateDataAnnotations().ValidateOnStart();
services.AddOptions<SignalROptions>().Bind(config.GetSection("SignalR")).ValidateDataAnnotations().ValidateOnStart();
services.AddOptions<RateLimitOptions>().Bind(config.GetSection("RateLimiting")).ValidateDataAnnotations().ValidateOnStart();
services.AddOptions<OpenTelemetryOptions>().Bind(config.GetSection("OpenTelemetry")).ValidateDataAnnotations().ValidateOnStart();
services.AddOptions<FeaturesOptions>().Bind(config.GetSection(FeaturesOptions.SectionName)).ValidateOnStart();
services.AddFeatureManagement(config.GetSection(FeaturesOptions.SectionName));
services.AddGraphifyServices();

// -----------------------------------------------------------------------------
//  JSON
// -----------------------------------------------------------------------------
services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy    = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition  = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// -----------------------------------------------------------------------------
//  Redis  (lazy start, never fatal)
// -----------------------------------------------------------------------------
services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisOptions>>().Value;
    var configuration = new ConfigurationOptions
    {
        AbortOnConnectFail = false,                // critical for lazy start
        ConnectTimeout     = opts.ConnectTimeoutMs,
        SyncTimeout        = opts.SyncTimeoutMs,
        ClientName         = "fleetstream-bff",
    };
    foreach (var ep in opts.Endpoints)
        configuration.EndPoints.Add(ep.Host, ep.Port);
    return ConnectionMultiplexer.Connect(configuration);
});

// -----------------------------------------------------------------------------
//  Infrastructure adapters
// -----------------------------------------------------------------------------
services.AddScoped<ICacheService,        RedisCacheService>();
services.AddScoped<ITruckStateStore,     RedisTruckStateStore>();
services.AddScoped<ITruckRepository,     InMemoryTruckRepository>();
services.AddScoped<IAlertService,           RedisAlertService>();
services.AddScoped<ITelemetryHistoryStore,  RedisTelemetryHistoryStore>();
services.AddScoped<INotificationService,      SignalRNotificationService>();

// Kafka consumers (M2 per the roadmap). Soft-start: log a warning if the
// broker is unreachable instead of crashing the host.
services.AddHostedService<KafkaTelemetryConsumer>();
services.AddHostedService<KafkaAlertConsumer>();

// -----------------------------------------------------------------------------
//  Vertical-slice handlers  (pure DI + Decorator pattern)
//
//  Every ICommandHandler / IQueryHandler found in FleetStream.Application is
//  registered as the inner handler. The decorators are then layered on top,
//  in this order (outermost first): Validation -> Logging.
//  No MediatR, no IPipelineBehavior, no assembly scanning at runtime.
// -----------------------------------------------------------------------------
var handlerAssembly   = typeof(ICommandHandler<,>).Assembly;
var commandDecorators = new DecoratorRegistration
    { typeof(CommandValidationDecorator<,>), typeof(CommandLoggingDecorator<,>) };
var queryDecorators   = new DecoratorRegistration
    { typeof(QueryValidationDecorator<,>), typeof(QueryLoggingDecorator<,>) };
services.AddVerticalSliceHandlers(handlerAssembly, commandDecorators, queryDecorators);

// FluentValidation: scan the Application assembly for every IValidator<T>.
// Validator types themselves are still useful (controllers can inject
// IValidator<T> directly) and may be wired into the decorator chain once
// DecoratorApplicationExtensions supports multi-decorator composition.
services.AddValidatorsFromAssemblyContaining<AcknowledgeAlertCommandValidator>(includeInternalTypes: true);

// -----------------------------------------------------------------------------
//  SignalR  (with Redis backplane)
// -----------------------------------------------------------------------------
services.AddSignalR(opts =>
{
    var sr = config.GetSection("SignalR").Get<SignalROptions>() ?? new SignalROptions();
    opts.MaximumReceiveMessageSize = sr.MaximumReceiveMessageSize;
    opts.KeepAliveInterval         = TimeSpan.FromSeconds(sr.KeepAliveSeconds);
    opts.ClientTimeoutInterval     = TimeSpan.FromSeconds(sr.ClientTimeoutSeconds);
})
.AddStackExchangeRedis(opts =>
{
    var redisCfg = config.GetConnectionString("Redis") ?? "localhost:6379";
    opts.Configuration = ConfigurationOptions.Parse(redisCfg);
    opts.Configuration.ChannelPrefix = RedisChannel.Literal("FleetStream");
    opts.Configuration.AbortOnConnectFail = false;
});

// -----------------------------------------------------------------------------
//  CORS
// -----------------------------------------------------------------------------
var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
services.AddCors(o => o.AddPolicy("AllowFrontend", p =>
    p.WithOrigins(allowedOrigins)
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

// -----------------------------------------------------------------------------
//  Authentication / Authorization  (JWT)
// -----------------------------------------------------------------------------
var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        o.SaveToken            = true;
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = jwt.Issuer,
            ValidateAudience         = true,
            ValidAudience            = jwt.Audience,
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
            ValidateIssuerSigningKey = true,
            NameClaimType            = "sub",
        };

        if (builder.Environment.IsDevelopment())
        {
            o.TokenValidationParameters.IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));
        }
        else
        {
            var jwksProvider = new JwksSigningKeyProvider(jwt.JwksUri, new HttpClient());
            o.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) =>
                jwksProvider.Resolve(kid);
        }
    });

if (builder.Environment.IsDevelopment())
    services.AddSingleton<DevTokenIssuer>();

services.AddAuthorization(o =>
{
    o.AddPolicy("FleetReader", p => p.RequireRole("fleet:reader", "fleet:admin"));
    o.AddPolicy("FleetAdmin",  p => p.RequireRole("fleet:admin"));
    o.AddPolicy("AlertsAck",   p => p.RequireRole("alerts:ack",  "fleet:admin"));
});

var controllers = services.AddControllers();
if (!builder.Environment.IsDevelopment())
{
    controllers.ConfigureApplicationPartManager(manager =>
        manager.FeatureProviders.Add(
            new DevelopmentOnlyControllerFeatureProvider(builder.Environment, typeof(AuthController))));
}

// -----------------------------------------------------------------------------
//  API versioning
// -----------------------------------------------------------------------------
services.AddApiVersioning(o =>
{
    o.DefaultApiVersion               = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions               = true;
}).AddApiExplorer(o =>
{
    o.GroupNameFormat           = "v" + "VVV";
    o.SubstituteApiVersionInUrl = true;
});

// -----------------------------------------------------------------------------
//  Health checks
// -----------------------------------------------------------------------------
var kafkaOpts = config.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>() ?? new KafkaOptions();
var kafkaBrokers = kafkaOpts.Brokers
    ?? config.GetConnectionString("Kafka")
    ?? "localhost:9092";
kafkaOpts.Brokers = kafkaBrokers;

var kafkaHealthConfig = new ProducerConfig { BootstrapServers = kafkaBrokers };
KafkaClientConfig.ApplySecurity(kafkaHealthConfig, kafkaOpts);

services.AddHealthChecks()
    .AddRedis(config.GetConnectionString("Redis") ?? "localhost:6379", name: "redis", tags: new[] { "ready" })
    .AddKafka(kafkaHealthConfig, name: "kafka", tags: new[] { "ready" })
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("self"), tags: new[] { "ready" });

// -----------------------------------------------------------------------------
//  Rate limiting
// -----------------------------------------------------------------------------
var rateLimitOpts = config.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();

services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (RateLimitExemptions.IsExempt(context))
            return RateLimitPartition.GetNoLimiter("exempt");

        var partition = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOpts.GlobalPermitLimit,
            Window      = TimeSpan.FromSeconds(rateLimitOpts.GlobalWindowSeconds),
            QueueLimit  = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    });
});

// -----------------------------------------------------------------------------
//  OpenAPI / Swagger
// -----------------------------------------------------------------------------
services.AddEndpointsApiExplorer();
services.AddSwaggerGen(o =>
{
    // With GroupNameFormat = "v" + "VVV", Asp.Versioning emits the group "v10"
    // for version 1.0, "v20" for 2.0, etc. Register both forms defensively.
    o.SwaggerDoc("v10", new OpenApiInfo
    {
        Title       = "FleetStream BFF API",
        Version     = "v1.0",
        Description = "Backend-for-Frontend API for the FleetStream IoT Fleet Telemetry Platform"
    });
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "FleetStream BFF API",
        Version     = "v1",
        Description = "Backend-for-Frontend API for the FleetStream IoT Fleet Telemetry Platform"
    });
    o.DocInclusionPredicate((docName, apiDesc) =>
    {
        if (!apiDesc.ActionDescriptor.EndpointMetadata.OfType<ApiVersionAttribute>().Any())
            return true;
        var versions = apiDesc.ActionDescriptor.EndpointMetadata
            .OfType<ApiVersionAttribute>()
            .SelectMany(a => a.Versions);
        return versions.Any(v => $"v{v.MajorVersion}{v.MinorVersion}" == docName);
    });
});

// -----------------------------------------------------------------------------
//  YARP
// -----------------------------------------------------------------------------
services.ConfigureHttpClientDefaults(http =>
{
    http.AddStandardResilienceHandler();
});

services.AddReverseProxy()
    .LoadFromConfig(config.GetSection("ReverseProxy"))
    .AddTransforms(builder =>
    {
        builder.AddRequestTransform(transformContext =>
        {
            var correlationId = transformContext.HttpContext.Items
                .TryGetValue(CorrelationIdMiddleware.ItemKey, out var value)
                ? value as string
                : null;

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                transformContext.ProxyRequest.Headers.Remove("X-Correlation-Id");
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
            }

            return ValueTask.CompletedTask;
        });
    });

// -----------------------------------------------------------------------------
//  OpenTelemetry
// -----------------------------------------------------------------------------
var useOtlpExporter = !builder.Environment.IsDevelopment()
    && !string.IsNullOrWhiteSpace(otel.OtlpEndpoint);

services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(serviceName: otel.ServiceName, serviceVersion: otel.ServiceVersion)
        .AddAttributes(new KeyValuePair<string, object>[]
        {
            new("deployment.environment", builder.Environment.EnvironmentName),
        }))
    .WithTracing(t =>
    {
        t.AddSource("FleetStream.*");
        t.AddAspNetCoreInstrumentation();
        t.AddHttpClientInstrumentation();
        if (useOtlpExporter)
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otel.OtlpEndpoint));
    })
    .WithMetrics(m =>
    {
        m.AddMeter(BffMetrics.MeterName);
        m.AddRuntimeInstrumentation();
        m.AddAspNetCoreInstrumentation();
        m.AddHttpClientInstrumentation();
        if (otel.PrometheusEnabled)
            m.AddPrometheusExporter();
        if (useOtlpExporter)
            m.AddOtlpExporter(o => o.Endpoint = new Uri(otel.OtlpEndpoint));
    });

if (useOtlpExporter)
{
    builder.Logging.AddOpenTelemetry(logging =>
    {
        logging.AddOtlpExporter(o => o.Endpoint = new Uri(otel.OtlpEndpoint));
    });
}

services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

StartupConfigurationLogger.LogEffectiveConfiguration(
    config,
    app.Logger,
    app.Environment,
    config.GetSection(FeaturesOptions.SectionName).Get<FeaturesOptions>() ?? new FeaturesOptions());

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/swagger/v10/swagger.json", "FleetStream BFF API v1.0");
        o.SwaggerEndpoint("/swagger/v1/swagger.json",  "FleetStream BFF API v1 (legacy)");
        o.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuditLoggingMiddleware>();

app.MapControllers();
app.MapHub<FleetHub>("/hubs/v1/fleet");
app.MapHealthChecks("/api/v1/health/live",  new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/api/v1/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapReverseProxy();
app.MapPrometheusScrapingEndpoint();

app.Logger.LogInformation("FleetStream BFF API starting on {Urls}", string.Join(", ", app.Urls.DefaultIfEmpty("http://localhost:8080")));

app.Run();

public partial class Program { }