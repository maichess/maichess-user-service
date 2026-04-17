using System.Text;
using MaichessUserService.Data;
using MaichessUserService.Grpc;
using MaichessUserService.Rest;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured");

builder.Services.AddDbContext<UserDbContext>(options =>
    options.UseNpgsql(ToNpgsqlConnectionString(connectionString)));

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
    });

builder.Services.AddAuthorization();
builder.Services.AddGrpc();
builder.Services.AddOpenApi();

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

// Npgsql requires key=value format; convert postgresql:// URIs transparently.
static string ToNpgsqlConnectionString(string cs)
{
    if (!cs.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) &&
        !cs.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
    {
        return cs;
    }

    Uri uri = new(cs);
    string[] userInfo = uri.UserInfo.Split(':');
    string username = Uri.UnescapeDataString(userInfo[0]);
    string password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
    int port = uri.Port > 0 ? uri.Port : 5432;

    return $"Host={uri.Host};Port={port};Database={uri.AbsolutePath.TrimStart('/')};Username={username};Password={password}";
}
