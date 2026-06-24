using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using ModelContextProtocol.Protocol;
using SvnHub.App.Configuration;
using SvnHub.App.Services;
using SvnHub.App.Storage;
using SvnHub.App.System;
using SvnHub.Infrastructure.Storage;
using SvnHub.Infrastructure.System;
using SvnHub.Domain;
using SvnHub.Web.Mcp;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();

var options = builder.Configuration.GetSection("SvnHub").Get<SvnHubOptions>() ?? new SvnHubOptions();
if (!Path.IsPathRooted(options.DataDirectory))
{
    options.DataDirectory = Path.Combine(builder.Environment.ContentRootPath, options.DataDirectory);
}

options.DataDirectory = Path.GetFullPath(options.DataDirectory);
Directory.CreateDirectory(options.DataDirectory);

builder.Services.AddSingleton(options);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Login";
        o.AccessDeniedPath = "/Login";
        o.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/mcp"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/mcp"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new Implementation
        {
            Name = "SvnHub",
            Title = "SvnHub",
            Version = "1.0.0",
            Description = "Read-only access to repositories visible to the authenticated SvnHub user.",
        };
        o.ServerInstructions =
            "Use these tools for read-only SVN repository inspection. Do not assume binary or oversized files have previewable contents.";
    })
    .WithHttpTransport(o =>
    {
        o.Stateless = true;
    })
    .WithTools<SvnHubMcpTools>()
    .AddAuthorizationFilters();

builder.Services.AddSingleton<IPortalStore, MultiFilePortalStore>();
builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
builder.Services.AddSingleton<IHtpasswdService, HtpasswdService>();
builder.Services.AddSingleton<IAuthFilesWriter, AuthFilesWriter>();
builder.Services.AddSingleton<ISvnRepositoryProvisioner, SvnadminRepositoryProvisioner>();
builder.Services.AddSingleton<ISvnLookClient, SvnLookClient>();
builder.Services.AddSingleton<ISvnRepositoryWriter, SvnCliRepositoryWriter>();
builder.Services.AddSingleton<SetupService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<RepositoryService>();
builder.Services.AddSingleton<GroupService>();
builder.Services.AddSingleton<PermissionService>();
builder.Services.AddSingleton<AccessService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<ApiTokenService>();

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

app.Use(async (context, next) =>
{
    // Allow larger uploads for the Tree Upload handler (before antiforgery/form parsing happens).
    if (HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Value?.EndsWith("/tree", StringComparison.OrdinalIgnoreCase) == true
        && string.Equals(context.Request.Query["handler"], "Upload", StringComparison.OrdinalIgnoreCase))
    {
        var settings = context.RequestServices.GetRequiredService<SettingsService>();
        var limit = settings.GetEffectiveMaxUploadBytes();

        // Add some overhead for multipart boundaries/headers.
        const long overhead = 5L * 1024 * 1024;
        var maxBody = SettingsService.MaxAllowedUploadBytes + overhead;

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is not null && !feature.IsReadOnly)
        {
            feature.MaxRequestBodySize = maxBody;
        }

        // If request is obviously too large, return a friendly response instead of a connection reset.
        // We can only do this when Content-Length is known.
        if (context.Request.ContentLength is long contentLength && contentLength > (limit + overhead))
        {
            var repoName = (context.Request.RouteValues.TryGetValue("repoName", out var rv) ? rv?.ToString() : null) ?? "";
            var path = context.Request.Query["path"].ToString();
            var backUrl = string.IsNullOrWhiteSpace(repoName)
                ? "/Repos/Index"
                : $"/repos/{Uri.EscapeDataString(repoName)}/tree" +
                  (string.IsNullOrWhiteSpace(path) ? "" : $"?path={Uri.EscapeDataString(path)}");

            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";

            var mb = (limit / (1024d * 1024d)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

            var prefix = context.Request.PathBase.Value ?? "";
            var backHref = System.Net.WebUtility.HtmlEncode(backUrl);

            await context.Response.WriteAsync($$"""
                <!doctype html>
                <html lang="en" data-bs-theme="dark">
                <head>
                  <meta charset="utf-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                  <title>Upload too large - SvnHub</title>
                  <link rel="stylesheet" href="{{prefix}}/lib/bootstrap/dist/css/bootstrap.min.css" />
                  <link rel="stylesheet" href="{{prefix}}/css/site.css" />
                  <link rel="stylesheet" href="{{prefix}}/SvnHub.Web.styles.css" />
                  <link rel="stylesheet" href="{{prefix}}/css/theme.css" />
                  <link rel="stylesheet" href="{{prefix}}/css/code.css" />
                </head>
                <body>
                  <nav class="navbar navbar-expand-sm navbar-dark bg-body-tertiary border-bottom mb-3">
                    <div class="container">
                      <a class="navbar-brand" href="{{prefix}}/Repos/Index">SvnHub</a>
                    </div>
                  </nav>

                  <div class="container">
                    <div class="card file-viewer mt-3">
                      <div class="card-header d-flex align-items-center justify-content-between flex-wrap gap-2">
                        <div class="text-muted small">Upload</div>
                      </div>
                      <div class="card-body">
                        <div class="alert alert-danger mb-3">
                          Upload is too large. Max upload size is {{mb}} MB.
                        </div>
                        <a class="btn btn-outline-secondary" href="{{backHref}}">Back</a>
                      </div>
                    </div>
                  </div>
                </body>
                </html>
                """);
            return;
        }
    }

    await next();
});

app.UseAuthentication();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/mcp")
        && !(context.User.Identity?.IsAuthenticated ?? false)
        && TryAuthenticateMcpApiToken(context, out var principal))
    {
        context.User = principal;
    }

    await next();
});

app.UseAuthorization();

app.MapStaticAssets();
app.MapMcp("/mcp").RequireAuthorization();
app.MapRazorPages().WithStaticAssets();

app.MapGet("/health", () => Results.Ok("ok"));

app.Run();

static bool TryAuthenticateMcpApiToken(HttpContext context, out ClaimsPrincipal principal)
{
    principal = new ClaimsPrincipal(new ClaimsIdentity());

    var header = context.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var token = header[prefix.Length..].Trim();

    var apiTokens = context.RequestServices.GetRequiredService<ApiTokenService>();
    var apiTokenUser = apiTokens.AuthenticateBearerToken(token);
    if (apiTokenUser is not null)
    {
        principal = BuildPrincipal(apiTokenUser);
        return true;
    }

    return false;
}

static ClaimsPrincipal BuildPrincipal(PortalUser user)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
        new(ClaimTypes.Name, user.UserName),
    };

    if (user.Roles.HasFlag(PortalUserRoles.AdminRepo))
    {
        claims.Add(new Claim(ClaimTypes.Role, nameof(PortalUserRoles.AdminRepo)));
    }

    if (user.Roles.HasFlag(PortalUserRoles.AdminSystem))
    {
        claims.Add(new Claim(ClaimTypes.Role, nameof(PortalUserRoles.AdminSystem)));
    }

    if (user.Roles.HasFlag(PortalUserRoles.AdminHooks))
    {
        claims.Add(new Claim(ClaimTypes.Role, nameof(PortalUserRoles.AdminHooks)));
    }

    return new ClaimsPrincipal(new ClaimsIdentity(claims, "McpApiToken"));
}
