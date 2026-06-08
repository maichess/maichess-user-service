using System.Text;
using Grpc.Net.Client;
using Maichess.Database.V1;
using MaichessUserService;
using MaichessUserService.Grpc;
using MaichessUserService.Kafka;
using MaichessUserService.Rest;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

DotNetEnv.Env.Load();
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string dbServiceUrl = builder.Configuration["Services:DatabaseService"]
    ?? throw new InvalidOperationException("Services:DatabaseService is not configured");

builder.Services.AddSingleton(
    new Database.DatabaseClient(GrpcChannel.ForAddress(dbServiceUrl)));

builder.Services.AddSingleton<UsersService>();

// CDC relay: curates user.events.v1 from the Debezium user.cdc.v1 stream so the write
// path stays Postgres-only (see change-data-capture.md). Off by default; enabled per
// environment (Cdc__Enabled=true) once Kafka Connect is deployed.
if (builder.Configuration.GetValue<bool>("Cdc:Enabled"))
{
    builder.Services.AddHostedService<UserCdcRelay>();
}

string jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("access_token", out string? token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddGrpc();
builder.Services.AddOpenApi();

string otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
    ?? "http://otel-collector:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("user-service"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<UsersGrpcService>();
app.MapUsersEndpoints();

app.Run();
