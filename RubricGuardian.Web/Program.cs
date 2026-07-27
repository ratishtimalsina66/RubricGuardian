using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using RubricGuardian.Web.Data;
using RubricGuardian.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/login";
        o.Cookie.Name = "RubricGuardian.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<AssignmentWorkflowService>();
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<IAiServiceClient, AiServiceClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(config["AiService:BaseUrl"] ?? "http://localhost:8000");
    client.Timeout = TimeSpan.FromMinutes(5); // LLM calls can be slow
    var apiKey = config["AiService:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
});

var app = builder.Build();

// Azure App Service (and any reverse proxy) terminates TLS at the edge and forwards plain
// HTTP internally - without this, UseHttpsRedirection() below can't tell the original
// request was HTTPS and would redirect-loop. Must be the first middleware in the pipeline.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// SecurePolicy left at default (SameAsRequest): safe now that ForwardedHeaders lets the
// app correctly see HTTPS requests forwarded from Azure/a reverse proxy as HTTPS.
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Create database and seed demo data on first run
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    if (app.Configuration.GetValue("Database:SeedOnStartup", true))
        await DbSeeder.SeedAsync(db, hasher);
    else
        await db.Database.EnsureCreatedAsync();
}

app.Run();
