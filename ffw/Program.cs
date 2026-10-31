using ffw.Data;
using ffw.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Die Verbindungszeichenfolge 'DefaultConnection' ist nicht konfiguriert.");

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<FotoSpeicher>();

// Anmeldung per E-Mail und Passwort. Es gibt bewusst keine Registrierung:
// Benutzer werden nur über Admin:Email / Admin:Password angelegt (siehe DatenbankInitialisierer).
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

// Die Schlüssel für Login-Cookies und Antiforgery-Token müssen einen Neustart/Deploy des Containers überleben,
// sonst wären nach jedem Deploy alle angemeldeten Benutzer abgemeldet. Der Pfad liegt in einem Docker-Volume.
builder.Services.AddDataProtection()
    .SetApplicationName("ffw")
    .PersistKeysToFileSystem(new DirectoryInfo(
        builder.Configuration["DataProtection:Pfad"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "data", "keys")));

// Hinter dem Reverse Proxy sieht die Anwendung nur HTTP. Deshalb wird das Secure-Flag
// des Login-Cookies in Produktion immer gesetzt statt vom Request abgeleitet. Für das Antiforgery-Cookie
// ist das nicht möglich (es wirft bei Always eine Exception, wenn die Anfrage selbst kein HTTPS ist).
var cookieSecurePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/anmelden";
    options.AccessDeniedPath = "/anmelden";
    options.Cookie.Name = "ffw.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);

var app = builder.Build();

await DatenbankInitialisierer.InitialisierenAsync(app.Services, app.Configuration, app.Logger);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Fotos sind öffentlich. Die Dateinamen sind zufällige GUIDs und ändern sich nie,
// daher dürfen Browser und Proxys sie dauerhaft zwischenspeichern.
var fotoSpeicher = app.Services.GetRequiredService<FotoSpeicher>();
app.MapGet("/fotos/bild/{id:guid}", (Guid id, HttpContext context) => FotoDatei(context, fotoSpeicher.PfadGross(id)));
app.MapGet("/fotos/vorschau/{id:guid}", (Guid id, HttpContext context) => FotoDatei(context, fotoSpeicher.PfadVorschau(id)));

app.Run();

static IResult FotoDatei(HttpContext context, string pfad)
{
    if (!File.Exists(pfad))
    {
        return Results.NotFound();
    }

    context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    return Results.File(pfad, "image/jpeg");
}
