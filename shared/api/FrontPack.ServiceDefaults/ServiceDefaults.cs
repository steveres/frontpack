using System.Security.Claims;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;

namespace FrontPack.ServiceDefaults;

/// <summary>The set-up every FrontPack service API shares. The host registers a TokenCredential (for Table Storage).</summary>
public static class ServiceDefaults
{
    public static IServiceCollection AddServiceDefaults(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>().BindConfiguration("Auth");
        services.AddOptions<CorsOptions>().BindConfiguration("Cors");
        services.AddOptions<RateLimitOptions>().BindConfiguration("Limits");
        services.AddOptions<InternalOptions>().BindConfiguration("Internal");

        services.AddUserStore();
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
        services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
        {
            // Lets the API explorer send "Authorization: Bearer <token>".
            var scheme = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
            doc.Components ??= new OpenApiComponents();
            doc.Components.SecuritySchemes["Bearer"] = scheme;
            doc.SecurityRequirements.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
            });
            return Task.CompletedTask;
        }));
        if (!string.IsNullOrEmpty(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            services.AddOpenTelemetry().UseAzureMonitor();
        return services;
    }

    /// <summary>
    /// Error responses, security headers, CORS, sign-in and rate limiting, in that order; then /healthz and,
    /// in Development, the API explorer titled <paramref name="title"/>.
    /// </summary>
    public static WebApplication UseServiceDefaults(this WebApplication app, string title)
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
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();                                       // /openapi/v1.json
            app.MapScalarApiReference(o => o.WithTitle(title));     // /scalar: browse and try the API
            app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
        }
        return app;
    }
}
