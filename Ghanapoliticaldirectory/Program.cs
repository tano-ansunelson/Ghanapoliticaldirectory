using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Data.Seeding;
using Ghanapoliticaldirectory.Business;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ghanapoliticaldirectory.Services;
using Ghanapoliticaldirectory.Service.Business;
using Ghanapoliticaldirectory.Service.Business.Export;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
// Add services to the container.
//builder.Services.AddControllersWithViews();

//builder.Services.AddDbContext<>();
// Every POST/PUT/DELETE must carry an anti-forgery token, without relying on each action remembering the attribute.
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
//builder.Services.AddSingleton<IDirectoryReader, SampleDirectoryReader>();

builder.Services.Configure<Microsoft.AspNetCore.Mvc.Razor.RazorViewEngineOptions>(o =>
    o.ViewLocationFormats.Add("/Views/Admin/{0}.cshtml"));

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(
    config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Set connectionString:DefaultConnection(see README).")));


builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
{
    o.Password.RequiredLength = 6;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.User.RequireUniqueEmail = true;
}).AddEntityFrameworkStores<AppDbContext>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/account/login";
    o.AccessDeniedPath = "/account/denied";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});
builder.Services.AddAuthorization(o =>
{
    foreach (var p in Permissions.All)
        o.AddPolicy(Policies.For(p), policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(ctx => RolePermissions.For(ctx.User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value)).Contains(p)));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton(new WorkflowOptions { RequireDistinctUsers = config.GetValue("Workflow:RequireDistinctUsers", true) });
//builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

var uploads = Path.Combine(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"), "uploads", "people");
builder.Services.AddSingleton<IImageStore>(new FileSystemImageStore(uploads, "/uploads/people"));

builder.Services.AddScoped<PersonService>();
builder.Services.AddScoped<CandidateService>();
builder.Services.AddScoped<ResultService>();
builder.Services.AddScoped<WorkflowService>();
builder.Services.AddScoped<GovernmentService>();
builder.Services.AddScoped<PhotoService>();
builder.Services.AddScoped<ReferenceDataService>();
builder.Services.AddScoped<AdminQueries>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<IDirectoryReader, EfDirectoryReader>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}



// Startup seeding. Reference data (regions, parties, positions, roles, the 275 constituencies, and the first admin
// from user-secrets) is added whenever Seed:OnStartup is true, in any environment. It is safe to repeat.
// Migrations are applied automatically only in Development; elsewhere run `dotnet ef database update` yourself.
if (config.GetValue("Seed:OnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;
    var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Seeding");
    var db = sp.GetRequiredService<AppDbContext>();
    if (app.Environment.IsDevelopment())
        await db.Database.MigrateAsync();

    await ReferenceSeeder.SeedAsync(db);
    if (config.GetValue("Seed:Constituencies", true))
        log.LogInformation("Constituencies added this start: {Count}", await ConstituencySeeder.SeedAsync(db));
    await IdentitySeeder.SeedAsync(sp.GetRequiredService<RoleManager<IdentityRole>>(),
        sp.GetRequiredService<UserManager<ApplicationUser>>(), config["Seed:AdminEmail"], config["Seed:AdminPassword"]);
   //if (app.Environment.IsDevelopment() && config.GetValue("Seed:Sample", false))
     //   await SampleSeeder.SeedAsync(db);
}


app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
