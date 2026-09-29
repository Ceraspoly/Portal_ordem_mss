using Microsoft.Extensions.Options;
using PortalOrdemMss.Web;
using PortalOrdemMss.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "MssPortalOrdem");

// Configuração em duas camadas: appsettings.json só dá os valores por
// omissão; o settings.json em ProgramData (fora da pasta de instalação)
// manda e é relido sem reiniciar.
var rootDirectory = AppPaths.ResolveRoot(builder.Configuration);
var configDirectory = Path.Combine(rootDirectory, "config");
Directory.CreateDirectory(configDirectory);
builder.Configuration.AddJsonFile(Path.Combine(configDirectory, "settings.json"), optional: true, reloadOnChange: true);

builder.Services.AddOptions<PortalOptions>()
    .Bind(builder.Configuration.GetSection("Portal"))
    .Validate(o => o.PageSize is > 0 and <= 200, "PageSize tem de estar entre 1 e 200.")
    .ValidateOnStart();
builder.Services.AddOptions<SqlOptions>()
    .Bind(builder.Configuration.GetSection("Sql"))
    .ValidateOnStart();

builder.Services.AddSingleton<ProductImageService>();
builder.Services.AddSingleton<DemoArtigoProvider>();
builder.Services.AddSingleton<SqlArtigoProvider>();
builder.Services.AddSingleton<IArtigoProvider, DynamicArtigoProvider>();
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
builder.Services.AddHealthChecks();

var listenUrl = builder.Configuration["Portal:ListenUrl"];
if (!string.IsNullOrWhiteSpace(listenUrl) && string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls(listenUrl);
}

var app = builder.Build();

app.UseResponseCompression();

// Erros em /api sempre em JSON (o fetch do front-end não sabe ler uma
// página de erro HTML).
app.Use(async (ctx, next) =>
{
    if (!ctx.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    try
    {
        await next();
    }
    catch (Exception ex) when (!ctx.Response.HasStarted && ex is not OperationCanceledException)
    {
        app.Logger.LogError(ex, "Erro em {Path}", ctx.Request.Path);
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = app.Environment.IsDevelopment() ? ex.Message : "Ocorreu um erro ao ler os artigos. Ver o registo do servidor."
        });
    }
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});

// Fotos servidas da pasta configurada (muitas vezes UNC). Uma falha aqui
// (rede ainda em baixo depois de um reboot) não pode matar o arranque:
// as fotos passam a placeholder e o resto funciona.
try
{
    var imagesFolder = Path.GetFullPath(app.Services.GetRequiredService<IOptionsMonitor<PortalOptions>>().CurrentValue.ProductImageFolder);
    Directory.CreateDirectory(imagesFolder);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(imagesFolder),
        RequestPath = ProductImageService.RequestPath,
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public, max-age=604800"
    });
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Pasta de imagens indisponível no arranque; as fotos vão aparecer como placeholder.");
}

app.MapHealthChecks("/health");

app.MapGet("/api/config", (IOptionsMonitor<PortalOptions> opts) =>
    Results.Ok(new { titulo = opts.CurrentValue.Titulo, demo = opts.CurrentValue.DemoMode, pageSize = opts.CurrentValue.PageSize }));

app.MapGet("/api/familias", async (IArtigoProvider data, CancellationToken ct) =>
    Results.Ok(await data.GetFamiliasAsync(ct)));

app.MapGet("/api/artigos", async (string? q, string? familia, int? offset, IArtigoProvider data,
    IOptionsMonitor<PortalOptions> opts, CancellationToken ct) =>
{
    var pesquisa = new PesquisaArtigos(
        (q ?? string.Empty).Trim(),
        (familia ?? string.Empty).Trim(),
        Math.Max(0, offset ?? 0),
        opts.CurrentValue.PageSize);
    return Results.Ok(await data.SearchAsync(pesquisa, ct));
});

app.Run();

public partial class Program;
