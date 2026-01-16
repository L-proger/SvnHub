using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using SvnHub.App.Configuration;
using SvnHub.App.Services;
using SvnHub.App.Storage;
using SvnHub.App.System;
using SvnHub.Infrastructure.Storage;
using SvnHub.Infrastructure.System;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

var options = builder.Configuration.GetSection("SvnHub").Get<SvnHubOptions>() ?? new SvnHubOptions();
if (!Path.IsPathRooted(options.DataFilePath))
{
    options.DataFilePath = Path.Combine(builder.Environment.ContentRootPath, options.DataFilePath);
}

builder.Services.AddSingleton(options);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Login";
        o.AccessDeniedPath = "/Login";
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<IPortalStore, JsonPortalStore>();
builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
builder.Services.AddSingleton<IHtpasswdService, HtpasswdService>();
builder.Services.AddSingleton<IAuthFilesWriter, AuthFilesWriter>();
builder.Services.AddSingleton<ISvnRepositoryProvisioner, SvnadminRepositoryProvisioner>();
builder.Services.AddSingleton<ISvnLookClient, SvnLookClient>();
builder.Services.AddSingleton<SetupService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<RepositoryService>();
builder.Services.AddSingleton<GroupService>();
builder.Services.AddSingleton<PermissionService>();
builder.Services.AddSingleton<AccessService>();
builder.Services.AddSingleton<SettingsService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownNetworks = { },
    KnownProxies = { },
});

app.UseHttpsRedirection();

app.UseRouting();

app.Use(async (context, next) =>
{
    var setup = context.RequestServices.GetRequiredService<SetupService>();
    if (!setup.IsSetupRequired())
    {
        await next();
        return;
    }

    var p = context.Request.Path;
    if (p.StartsWithSegments("/Setup")
        || p.StartsWithSegments("/health")
        || p.StartsWithSegments("/Error")
        || p.StartsWithSegments("/css")
        || p.StartsWithSegments("/js")
        || p.StartsWithSegments("/lib")
        || p.Equals("/favicon.ico"))
    {
        await next();
        return;
    }

    context.Response.Redirect("/Setup");
});

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.MapGet("/health", () => Results.Ok("ok"));

app.Run();
