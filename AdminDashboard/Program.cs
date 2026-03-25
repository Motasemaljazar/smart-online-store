using AdminDashboard.Data;
using AdminDashboard.Hubs;
using AdminDashboard.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseWebRoot("wwwroot");

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services
    .AddRazorPages(opts =>
    {
        opts.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
        opts.Conventions.AllowAnonymousToPage("/Admin/Login");
    });

builder.Services
    .AddControllers(opts =>
    {
        // رفع حد حجم الطلب إلى 100MB لدعم رفع الصور الكبيرة
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
builder.Services.AddMemoryCache();

// رفع حد حجم الملفات المرفوعة إلى 100MB
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 104857600; // 100 MB
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 104857600; // 100 MB
});

builder.Services.Configure<AppSecurityOptions>(builder.Configuration.GetSection("Security"));

builder.Services.AddScoped<NotificationService>();
builder.Services.AddHostedService<AdminDashboard.Services.AgentOrderAutoAcceptService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opts =>
    {
        opts.LoginPath = "/Admin/Login";
        opts.LogoutPath = "/Logout";
        opts.AccessDeniedPath = "/Admin/Login";
        opts.Cookie.Name = "store_admin";
        opts.SlidingExpiration = true;
        opts.ExpireTimeSpan = TimeSpan.FromHours(12);
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

// HttpClient مخصص لـ Groq بإعدادات محسّنة
builder.Services.AddHttpClient("groq", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    UseProxy = false,
    AllowAutoRedirect = true,
});
builder.Services.AddSingleton<IAuthorizationHandler, AdminApiKeyAuthorizationHandler>();
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("AdminOnly", policy =>
    {
        policy.Requirements.Add(new AdminOnlyRequirement());
    });
});

var connStr = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=app.db";
var recreate = builder.Configuration.GetValue<bool>("Database:RecreateOnStart");

if (connStr.Contains("Server=") || connStr.Contains("Host="))
{
    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseMySql(connStr, ServerVersion.AutoDetect(connStr)));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseSqlite(connStr));
}

var app = builder.Build();

if (app.Environment.IsProduction())
{
    var sec = app.Configuration.GetSection("Security").Get<AppSecurityOptions>() ?? new AppSecurityOptions();
    if (string.Equals(sec.AdminApiKey, "CHANGE_ME", StringComparison.OrdinalIgnoreCase)
        || string.Equals(sec.DriverTokenSecret, "DEV_SECRET_CHANGE_ME", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Production misconfiguration: set Security:AdminApiKey and Security:DriverTokenSecret to secure values.");
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (recreate) await db.Database.EnsureDeletedAsync();

    if (db.Database.IsSqlite())
    {
        await db.Database.EnsureCreatedAsync();
        await SchemaUpgrader.EnsureAsync(db);
    }
    else
    {
        // نحاول تطبيق الـ migrations، وإذا فشل بعضها نكمل
        // لأن MySqlSchemaUpgrader يضمن وجود الجداول المهمة
        try { await db.Database.MigrateAsync(); } catch { /* نكمل دائماً */ }
        await MySqlSchemaUpgrader.EnsureAsync(db);
    }

    await DbSeeder.SeedAsync(db, app.Environment.IsDevelopment(), app.Configuration);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();

// ── تحويل كل الاستثناءات إلى JSON بدل HTML ──
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            var err = System.Text.Json.JsonSerializer.Serialize(new
            {
                error = "server_exception",
                message = ex.Message,
                inner = ex.InnerException?.Message ?? "",
                exType = ex.GetType().Name
            });
            await ctx.Response.WriteAsync(err);
        }
    }
});

if (app.Environment.IsProduction())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/uploads", StringComparison.OrdinalIgnoreCase) ||
        ctx.Request.Path.StartsWithSegments("/assets", StringComparison.OrdinalIgnoreCase) ||
        ctx.Request.Path.StartsWithSegments("/images", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
        ctx.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
    }
    await next();
});

var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".webp"] = "image/webp";
contentTypeProvider.Mappings[".avif"] = "image/avif";
contentTypeProvider.Mappings[".heic"] = "image/heic";
contentTypeProvider.Mappings[".heif"] = "image/heif";
contentTypeProvider.Mappings[".svg"] = "image/svg+xml";
contentTypeProvider.Mappings[".jfif"] = "image/jpeg";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider,
    ServeUnknownFileTypes = true
});

app.UseRouting();

if (app.Environment.IsDevelopment())
{
    app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials().SetIsOriginAllowed(_ => true));
}
else
{
    var allowed = app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (allowed.Length > 0)
        app.UseCors(policy => policy.WithOrigins(allowed).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
    else
        app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials().SetIsOriginAllowed(_ => true));
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Json(new { status = "ok", utc = DateTime.UtcNow })).AllowAnonymous();

// ── endpoint تشخيصي لفحص قاعدة البيانات ──
app.MapGet("/api/admin/db-check", async (AppDbContext db) =>
{
    var results = new Dictionary<string, object>();
    bool isMySql = db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true
                || db.Database.ProviderName?.Contains("Pomelo", StringComparison.OrdinalIgnoreCase) == true;
    results["provider"] = db.Database.ProviderName ?? "unknown";
    results["isMySql"] = isMySql;

    var conn = db.Database.GetDbConnection();
    try
    {
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        var tables = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = isMySql
            ? "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE()"
            : "SELECT name FROM sqlite_master WHERE type='table'";
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) tables.Add(rdr.GetString(0));
        results["tables"] = tables;

        // فحص وجود الجداول المهمة
        results["hasOrderRatings"] = tables.Any(t => t.Equals("OrderRatings", StringComparison.OrdinalIgnoreCase));
        results["hasProductRatings"] = tables.Any(t => t.Equals("ProductRatings", StringComparison.OrdinalIgnoreCase));
        results["dbStatus"] = "connected";
    }
    catch (Exception ex)
    {
        results["dbStatus"] = "error";
        results["dbError"] = ex.Message;
    }
    return Results.Json(results);
}).AllowAnonymous();

app.MapControllers();
app.MapRazorPages();
app.MapHub<TrackingHub>("/hubs/tracking");
app.MapHub<NotifyHub>("/hubs/notify");

app.Run();
