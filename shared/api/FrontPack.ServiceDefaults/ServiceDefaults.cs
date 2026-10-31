using System.Security.Claims;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FrontPack.ServiceDefaults;

/// <summary>The set-up every FrontPack service API shares. The service registers its IUserStore.</summary>
public static class ServiceDefaults
{
    public static IServiceCollection AddServiceDefaults(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>().BindConfiguration("Auth");
        services.AddOptions<CorsOptions>().BindConfiguration("Cors");
        services.AddOptions<RateLimitOptions>().BindConfiguration("Limits");

        services.AddFrontPackAuth();
        services.AddProblemDetails();
        services.AddCors();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var perMinute = http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.RequestsPerMinute;
                var key = http.User.FindFirstValue("oid") ?? "ip:" + http.Connection.RemoteIpAddress;
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) });
            });
        });
        if (!string.IsNullOrEmpty(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            services.AddOpenTelemetry().UseAzureMonitor();
        return services;
    }

    /// <summary>Error responses, security headers, CORS, sign-in and rate limiting, in that order.</summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.Use((http, next) =>
        {
            var h = http.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h["Referrer-Policy"] = "no-referrer";
            // API responses are JSON or media bytes; neither should ever be interpreted as a page.
            // (The development-only API explorer under /scalar is a page, so it's left out.)
            if (!http.Request.Path.StartsWithSegments("/scalar"))
                h.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; sandbox";
            return next();
        });
        var cors = app.Services.GetRequiredService<IOptions<CorsOptions>>().Value;
        app.UseCors(p => p.WithOrigins(cors.AllowedOrigins).AllowAnyHeader().AllowAnyMethod()
            .WithExposedHeaders("Location", "Content-Disposition", "Content-Range", "Accept-Ranges", "ETag")
            .SetPreflightMaxAge(TimeSpan.FromHours(1)));
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous().DisableRateLimiting().ExcludeFromDescription();
        return app;
    }
}
