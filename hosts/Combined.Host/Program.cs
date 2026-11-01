// FrontPack and Slideshow in one API, deployed as today's slideshow-api container app. Each service also
// gets a host of its own, and this one goes once traffic has moved to them (see the microservices plan).
using Azure.Core;
using Azure.Identity;
using FrontPack.Api;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using Slideshow.Api;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// Signing in to Table, Blob and Queue Storage in Azure: the API's managed identity, straight away (no credential
// chain), and one credential for all of them so they share one storage token. AZURE_CLIENT_ID picks the identity.
services.AddSingleton<TokenCredential>(_ => builder.Configuration["AZURE_CLIENT_ID"] is { Length: > 0 } clientId
    ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
    : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));

// Sign-in, the FrontPack accounts, CORS, rate limiting, security headers, /healthz and telemetry.
services.AddServiceDefaults(builder.Configuration);
services.AddFrontPack();
services.AddSlideshow();

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

var app = builder.Build();

await app.Services.InitializeUserStoreAsync();
await app.Services.InitializeSlideshowStorageAsync();

app.UseServiceDefaults();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                                                      // /openapi/v1.json
    app.MapScalarApiReference(o => o.WithTitle("FrontPack API"));         // /scalar: browse and try the API
    app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
}

var api = app.MapGroup("/api/v1").RequireAuthorization(Auth.ApiPolicy);
app.MapFrontPack(api);
app.MapSlideshow(api);

app.Run();

public partial class Program; // for WebApplicationFactory in tests
