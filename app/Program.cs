using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using OpenFga.Sdk.Client;
using OpenFga.Sdk.Client.Model;
using System.Security.Claims;
using Unbreakable;
using Unbreakable.Data;
using Unbreakable.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<UnbreakableContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Unbreakable")));

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Authenticatie: de API vertrouwt enkel een geldig JWT van onze Keycloak-realm.
// De metadata en de sleutels worden INTERN opgehaald (keycloak:8080), niet via de WAF.
// Keycloak draait met hostname-backchannel-dynamic, dus de jwks_uri is intern terwijl
// de issuer de publieke URL blijft (die in het token staat).
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MetadataAddress = "http://keycloak:8080/auth/realms/unbreakable/.well-known/openid-configuration";
        options.RequireHttpsMetadata = false; // interne backchannel over http binnen het Docker-netwerk
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://g18.ipv6.thelastgym.be/auth/realms/unbreakable",
            ValidateAudience = true,
            ValidAudience = "unbreakable-api",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "preferred_username",
        };
    });
builder.Services.AddAuthorization();

// De PDP (OpenFGA). De API is de PEP: ze stelt enkel de vraag, OpenFGA beslist.
// Adres, store en model komen uit de omgeving (docker-compose), niet hardcoded.
builder.Services.AddSingleton(new OpenFgaClient(new ClientConfiguration
{
    ApiUrl = builder.Configuration["OpenFga:ApiUrl"] ?? "http://openfga:8080",
    StoreId = builder.Configuration["OpenFga:StoreId"],
    AuthorizationModelId = builder.Configuration["OpenFga:ModelId"],
}));

var app = builder.Build();

// Databank aanmaken en vullen met voorbeeldberichten.
// De databank kan iets later opstarten dan de app, dus we proberen een paar keer.
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<UnbreakableContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    for (var attempt = 1; attempt <= 30; attempt++)
    {
        try
        {
            context.Database.EnsureCreated();
            break;
        }
        catch (Exception ex) when (attempt < 30)
        {
            logger.LogWarning("Databank nog niet bereikbaar ({Error}), poging {Attempt}", ex.Message, attempt);
            Thread.Sleep(2000);
        }
    }

    // Relaties voor bestaande berichten eenmalig naar de PDP schrijven, zodat ook
    // de voorbeeldberichten onder de nieuwe autorisatie leesbaar zijn. Idempotent:
    // bestaat de tuple al (bij een herstart), dan negeren we de fout.
    var fga = scope.ServiceProvider.GetRequiredService<OpenFgaClient>();
    foreach (var message in await context.Messages.ToListAsync())
    {
        try
        {
            await Authz.SchrijfTuples(fga, message);
        }
        catch (Exception ex)
        {
            logger.LogDebug("Tuple voor bericht {Id} niet geschreven ({Error})", message.Id, ex.Message);
        }
    }
}

app.UseDeveloperExceptionPage();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// De SPA: index.html, JavaScript en CSS uit wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => "ok");

// De API. Wie je bent, komt uit het geverifieerde token (claim preferred_username),
// niet langer uit een header. Elke endpoint vereist een geldig token.
var api = app.MapGroup("/api").RequireAuthorization();

api.MapGet("/users", async (UnbreakableContext db) =>
{
    var senders = await db.Messages.Select(m => m.Sender).Distinct().ToListAsync();
    var recipients = await db.Messages.Select(m => m.Recipient).Distinct().ToListAsync();
    return Results.Ok(senders.Union(recipients).OrderBy(name => name).ToList());
});

api.MapGet("/messages", async (HttpRequest request, ClaimsPrincipal principal, OpenFgaClient fga, UnbreakableContext db) =>
{
    // De PDP bepaalt welke berichten je mag zien (ListObjects op can_read);
    // de code filtert niet meer zelf op ontvanger.
    var leesbaar = await Authz.LeesbareBerichtIds(fga, CurrentUser(principal));
    var inbox = db.Messages.Where(m => leesbaar.Contains(m.Id));

    // Filter op afzender is een weergavekeuze (businessregel), geen autorisatie.
    var from = request.Query["from"].ToString();
    if (!string.IsNullOrEmpty(from))
    {
        inbox = inbox.Where(m => m.Sender == from);
    }

    return Results.Ok(await inbox.OrderByDescending(m => m.SentAt).ToListAsync());
});

api.MapGet("/messages/search", async (HttpRequest request, ClaimsPrincipal principal, OpenFgaClient fga, UnbreakableContext db) =>
{
    var q = request.Query["q"].ToString();

    // Welke berichten je mag zien, komt van de PDP (geen "Recipient = user" meer in de query).
    var leesbaar = await Authz.LeesbareBerichtIds(fga, CurrentUser(principal));
    var inClause = leesbaar.Count > 0 ? string.Join(",", leesbaar) : "-1";

    var sql = "SELECT * FROM \"Messages\" WHERE \"Id\" IN (" + inClause + ") " +
              "AND (\"Subject\" ILIKE '%" + q + "%' OR \"Body\" ILIKE '%" + q + "%')";

    var messages = await db.Messages.FromSqlRaw(sql).OrderByDescending(m => m.SentAt).ToListAsync();
    return Results.Ok(messages);
});

api.MapGet("/messages/export", async (ClaimsPrincipal principal, OpenFgaClient fga, UnbreakableContext db) =>
{
    // Ook de export toont enkel wat de PDP toelaat.
    var leesbaar = await Authz.LeesbareBerichtIds(fga, CurrentUser(principal));

    var messages = await db.Messages
        .Where(m => leesbaar.Contains(m.Id))
        .OrderBy(m => m.SentAt)
        .ToListAsync();

    return Results.Text(JsonConvert.SerializeObject(messages, Formatting.Indented), "application/json");
});

// Verzonden berichten: enkel die je mag verwijderen (can_delete, dus de afzender).
// Zonder de inhoud, want lezen mag enkel de ontvanger. Zo kan de afzender zijn bericht terugvinden en verwijderen.
api.MapGet("/messages/sent", async (ClaimsPrincipal principal, OpenFgaClient fga, UnbreakableContext db) =>
{
    var verwijderbaar = await Authz.VerwijderbareBerichtIds(fga, CurrentUser(principal));

    var sent = await db.Messages
        .Where(m => verwijderbaar.Contains(m.Id))
        .OrderByDescending(m => m.SentAt)
        .Select(m => new { m.Id, m.Recipient, m.Subject, m.SentAt })
        .ToListAsync();

    return Results.Ok(sent);
});

api.MapGet("/messages/{id:int}", async (int id, ClaimsPrincipal principal, OpenFgaClient fga, UnbreakableContext db) =>
{
    var message = await db.Messages.FindAsync(id);
    if (message == null)
    {
        return Results.NotFound();
    }

    // De PDP beslist of deze gebruiker dit bericht mag lezen (can_read).
    if (!await Authz.Mag(fga, CurrentUser(principal), "can_read", id))
    {
        return Results.Forbid();
    }

    return Results.Ok(message);
});

api.MapPost("/messages", async (Message message, ClaimsPrincipal principal, OpenFgaClient fga, UnbreakableContext db) =>
{
    // De afzender komt altijd uit het token, niet uit de invoer: je kan je niet voordoen als iemand anders.
    message.Sender = CurrentUser(principal);

    message.Id = 0;
    message.SentAt = DateTime.UtcNow;

    db.Messages.Add(message);
    await db.SaveChangesAsync();

    // De relatie afzender/ontvanger ↔ bericht naar de PDP schrijven, zodat
    // de autorisatie (lezen/verwijderen) op dit bericht klopt.
    await Authz.SchrijfTuples(fga, message);

    return Results.Created($"/api/messages/{message.Id}", message);
});

api.MapDelete("/messages/{id:int}", async (int id, ClaimsPrincipal principal, OpenFgaClient fga, UnbreakableContext db) =>
{
    var message = await db.Messages.FindAsync(id);
    if (message == null)
    {
        return Results.NotFound();
    }

    // De PDP beslist of deze gebruiker dit bericht mag verwijderen (can_delete).
    if (!await Authz.Mag(fga, CurrentUser(principal), "can_delete", id))
    {
        return Results.Forbid();
    }

    db.Messages.Remove(message);
    await db.SaveChangesAsync();

    // De tuples van dit bericht opruimen: de relatie bestaat niet meer.
    await Authz.VerwijderTuples(fga, message);

    return Results.NoContent();
});

app.Run();

// De naam van de ingelogde gebruiker, uit het geverifieerde token.
static string CurrentUser(ClaimsPrincipal principal)
{
    return principal.FindFirst("preferred_username")?.Value ?? principal.Identity?.Name;
}
