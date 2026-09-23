using Admin.Components;
using Admin.Interfaces;
using Admin.ServiceRegistration;
using Admin.Services;
using Application.AutoMapper;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Asn;
using Application.Interfaces.V1.User;
using Application.Services;
using Application.Services.Asn;
using Application.Services.User;
using Application.Services.V1;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using Domain.Entities.AppSettings;
using Domain.Entities.Common;
using Domain.Entities.Modals;
using Domain.Entities.Response;
using Domain.Enums;
using EmailService;
using Infrastructure.Services.V1;
using Infrastructure.Services.V1.User;
using Infrastructure.ServicesConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;
using MudExtensions.Services;
using MyNewEncDec;
using PdfSharp.Charting;
using Serilog;
using Serilog.Core;
using Serilog.Events;


Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger(); // catches startup errors


var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
builder.Services.AddSingleton(levelSwitch);

// Bind AppSettings once from appsettings.json
ConfigurationInitializer.InitializeApiSettings(builder.Configuration);
// Read appsettings section value for local use
// MaxSessionTime = idle timeout in minutes. Drives the auth cookie, HttpContext.Session,
// disconnected-circuit retention and the in-app idle logout (IdleTimeoutMonitor).
int MaxSessionTime = Math.Max(builder.Configuration.GetValue<int>("AppConfigurationSettings:MaxSessionTime"), 1);
int useProxyValue = builder.Configuration.GetValue<int>("AppConfigurationSettings:ProxyValue");
string logFilePath = builder.Configuration.GetValue<string>("ErrorLoggingSettings:LogFilePath")!;

builder.Services.Configure<AppConfigurationSettings>(builder.Configuration.GetSection("AppConfigurationSettings"));
builder.Services.Configure<EmailServerSettings>(builder.Configuration.GetSection("EmailServerSettings"));
builder.Services.Configure<EmailSchedulerOptions>(builder.Configuration.GetSection("EmailScheduler"));// injected as IOptions<EmailSchedulerOptions> options

builder.Services.Configure<BSRSettings>(builder.Configuration.GetSection("BSRSettings"));

builder.Services.AddInfrastructure(useProxyValue, logFilePath ?? "", levelSwitch);
builder.Services.AddDependencies();

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AppConfigurationSettings>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<EmailServerSettings>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<BSRSettings>>().Value);






builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddServerSideBlazor(options =>
{
    options.DisconnectedCircuitMaxRetained = 100;
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(MaxSessionTime);
    options.MaxBufferedUnacknowledgedRenderBatches = 20;
})
.AddHubOptions(options =>
{
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    options.MaximumReceiveMessageSize = 50 * 1024 * 1024;
    // These detect dead connections; they are NOT the idle timeout. The Blazor client expects
    // a server message within 30 s (serverTimeoutInMilliseconds), so KeepAliveInterval must stay
    // well below that — a larger value makes idle pages show "Attempting to reconnect...".
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30); // must be >= 2x the client keep-alive (15 s)
});




// Add MudBlazor services
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopCenter;

    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = false;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 1000;
    config.SnackbarConfiguration.HideTransitionDuration = 200;
    config.SnackbarConfiguration.ShowTransitionDuration = 300;
    config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
});
builder.Services.AddControllers();
builder.Services.AddMudExtensions();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(MaxSessionTime); // kept in sync with the auth cookie
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddCascadingAuthenticationState();
//builder.Services.AddAuthorizationCore();
builder.Services.AddAuthorizationCore(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddScoped<Application.Services.MenuService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<SharedStateService>();
builder.Services.AddSingleton<NavMenuService>();
builder.Services.AddSingleton<New_Enc_Dec>();
builder.Services.AddSingleton<EncryptedQueryString>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IUserValidationService, UserValidationService>();
builder.Services.AddScoped<IUserActivityState, UserActivityState>(); // per-circuit last activity (idle timeout)
builder.Services.AddScoped<AuthenticationStateProvider, CustomRevalidatingAuthenticationStateProvider>();
builder.Services.AddScoped<ISessionInvalidationState, SessionInvalidationState>();

builder.Services.AddAuthentication(Cons.AuthScheme)
            .AddCookie(Cons.AuthScheme, Options =>
            {
                Options.Cookie.Name = Cons.AuthCookie;
                Options.LoginPath = "/Account/Login";
                Options.LogoutPath = "/Account/User-Logout";
                Options.AccessDeniedPath = "/access-denied";


                Options.Cookie.HttpOnly = true;
                Options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
                Options.Cookie.SameSite = SameSiteMode.Strict;

                // Renewed by api/session/keepalive while the user is active (see idle-timeout.js).
                Options.ExpireTimeSpan = TimeSpan.FromMinutes(MaxSessionTime);
                Options.SlidingExpiration = true;
            });

//ADD THIS BLOCK
builder.Services.AddAuthorization(options =>
{
    // QAAdmin 
    options.AddPolicy("PPSAccess", policy => policy.RequireRole(Roles.Buyer, Roles.QC, Roles.View, Roles.Admin, Roles.QAAdmin, Roles.MERCHANDISER));
    options.AddPolicy("InspectionRequestAccess", policy => policy.RequireRole(Roles.QAM, Roles.QA, Roles.QAAdmin, Roles.Vendor, Roles.Buyer, Roles.ACH, Roles.CH, Roles.MERCHANDISER));
    options.AddPolicy("VendorMgtAccess", policy => policy.RequireRole(Roles.QAM, Roles.Administrator, Roles.Admin, Roles.QAAdmin));
    options.AddPolicy("ASNAccess", policy => policy.RequireRole(Roles.QAAdmin, Roles.QAM, Roles.BFT, Roles.Buyer, Roles.ASN, Roles.Vendor));

});


//



builder.Services.AddAutoMapper(typeof(ServerSettingsProfile));
builder.Services.AddAutoMapper(typeof(ApplicationProfile));
builder.Services.AddScoped<PasswordEncryptConverter>();
builder.Services.AddTransient<MailSender>();
builder.Services.AddScoped<IEmailService, VMMEmailService>();
builder.Services.AddHostedService<DailyEmailHostedService>();
builder.Services.AddHostedService<OrphanFileCleanupHostedService>();
// WEB — Wizard State Service (Blazor Server only)
builder.Services.AddScoped<IAuditStateService, AuditStateService>();

builder.Services.AddScoped<IInspectionDetailLoader, InspectionDetailLoader>();
builder.Services.AddScoped<IFetchMasterList, FetchMasterListService>();
builder.Services.AddKeyedScoped<IInspectionDialogLauncher, QaInspectionDialogLauncher>(InspectionDialogType.QaInspection);
builder.Services.AddKeyedScoped<IInspectionDialogLauncher, VendorInspectionDialogLauncher>(InspectionDialogType.VendorInspectionRequest);
builder.Services.AddKeyedScoped<IAsnDialogLauncher, ASNDialogLauncher>(InspectionDialogType.AsnInspection);
builder.Services.AddKeyedScoped<IAsnDialogLauncher, BFTDialogLauncher>(InspectionDialogType.BftAsnRequest);
builder.Services.AddScoped<InspectionDialogLauncherFactory>();

builder.Services.AddScoped<IVendorLookupService, VendorLookupService>();
builder.Services.AddScoped<IVendorSource, LocalVendorSource>();
builder.Services.AddScoped<IVendorSource, SapVendorSource>();
builder.Services.AddScoped<VendorResolver>();

builder.Services.AddScoped<ISapPoFetcher, SapPoFetcher>();
builder.Services.AddScoped<IPoSource, LocalPoSource>();
builder.Services.AddScoped<IPoSource, SapPoSource>();
builder.Services.AddScoped<PoResolver>();
builder.Services.AddScoped<IPoSyncService, PoSyncService>();

builder.Services.AddKeyedScoped<IGetAsn, SAPAsnSource>(InstanceOrigin.Sap);
builder.Services.AddKeyedScoped<IGetAsn, LocalAsnSource>(InstanceOrigin.Local);
builder.Services.AddScoped<GetAsnListResolver>();

builder.Services.AddScoped<PODownloadService>();

//Set Data
var ApiMasterService = new APICallService(useProxyValue);
var apiMasterResponse = await ApiMasterService.CallingAPI<ApiMasterRes, string>(AllApiNames.MasterApi, "");
if (apiMasterResponse.responseCode == 0 && apiMasterResponse.responseMessage!.ToUpper() == "SUCCESS" && apiMasterResponse.ApiData?.Count > 0)
{
    AllApi.ApiDetails = apiMasterResponse.ApiData;
}

//builder.Services.AddDataProtection()
//    .PersistKeysToFileSystem(new DirectoryInfo(@"D:\QPS\DataProtectionKeys"))
//    .SetApplicationName("QPSPortal");
// Add services to the container.

var app = builder.Build();


app.Use(async (context, next) =>
{
    // Block /robots.txt for all bots
    if (context.Request.Path == "/robots.txt")
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("Access Denied");
        return;
    }
    // Security headers
    var headers = context.Response.Headers;
    headers.Remove("X-Powered-By");
    headers.Add("X-Powered-By", "VMM");
    headers["Server"] = "VMM";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["X-XSS-Protection"] = "1; mode=block";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' blob:; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self' wss:; " +
        "frame-src 'self'; " +
        "media-src 'self';";
    headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    headers["Feature-Policy"] = "vibrate 'none'";
    headers["Referrer-Policy"] = "strict-origin";
    await next();
});
// Instead of a synchronous exception handler that disposes scope synchronously,
// ensure your UseExceptionHandler uses async pipeline properly.

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            // your error handling logic
            await context.Response.WriteAsync("An error occurred.");
        });
    });
    //app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);

app.Run();
