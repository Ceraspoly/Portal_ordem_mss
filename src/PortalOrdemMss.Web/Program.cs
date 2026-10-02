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
builder.Services.AddSingleton(new OrdemAuditLog(Path.Combine(rootDirectory, "data", "alteracoes-ordem.csv")));
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

app.MapGet("/api/config", (HttpContext ctx, IOptionsMonitor<PortalOptions> opts) =>
    Results.Ok(new
    {
        titulo = opts.CurrentValue.Titulo,
        demo = opts.CurrentValue.DemoMode,
        pageSize = opts.CurrentValue.PageSize,
        podeReordenar = opts.CurrentValue.PermitirReordenar && IsWriteAllowed(ctx, opts.CurrentValue)
    }));

app.MapGet("/api/familias", async (IArtigoProvider data, CancellationToken ct) =>
    Results.Ok(await data.GetFamiliasAsync(ct)));

app.MapGet("/api/artigos", async (string? q, string? familia, int? offset, bool? todos, IArtigoProvider data,
    IOptionsMonitor<PortalOptions> opts, CancellationToken ct) =>
{
    // todos=true: todos os artigos de uma vez (da família escolhida, ou do
    // catálogo inteiro), para o modo de ordenação. Com limite de segurança:
    // se houver mais, recusa em vez de devolver só uma parte.
    if (todos == true)
    {
        var tudo = await data.SearchAsync(new PesquisaArtigos(string.Empty, (familia ?? string.Empty).Trim(), 0, MaxArtigosOrdenar + 1), ct);
        return tudo.Count > MaxArtigosOrdenar
            ? Results.BadRequest(new { error = $"São mais de {MaxArtigosOrdenar} artigos para ordenar de uma vez. Escolhe uma família." })
            : Results.Ok(tudo);
    }

    var pesquisa = new PesquisaArtigos(
        (q ?? string.Empty).Trim(),
        (familia ?? string.Empty).Trim(),
        Math.Max(0, offset ?? 0),
        opts.CurrentValue.PageSize);
    return Results.Ok(await data.SearchAsync(pesquisa, ct));
});

// Mostra, sem gravar nada, que CDU_MSS_ORDEM cada artigo arrastado vai ter.
app.MapPost("/api/ordem/previsao", (NovaOrdemRequest req) =>
{
    try
    {
        var alteracoes = OrdemPlanner.Planear(req.Original ?? [], req.Nova ?? [], req.Arrastados);
        return Results.Ok(alteracoes);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Única escrita do portal: grava o CDU_MSS_ORDEM depois de arrastar artigos.
app.MapPost("/api/ordem", async (HttpContext ctx, NovaOrdemRequest req, IArtigoProvider data,
    IOptionsMonitor<PortalOptions> opts, OrdemAuditLog audit, CancellationToken ct) =>
{
    if (!opts.CurrentValue.PermitirReordenar || !IsWriteAllowed(ctx, opts.CurrentValue))
    {
        return Results.Json(new { error = "Não tens permissão para mudar a ordem a partir deste computador." }, statusCode: StatusCodes.Status403Forbidden);
    }

    IReadOnlyList<AlteracaoOrdem> alteracoes;
    try
    {
        alteracoes = OrdemPlanner.Planear(req.Original ?? [], req.Nova ?? [], req.Arrastados);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    if (alteracoes.Count == 0)
    {
        return Results.Ok(new { gravados = 0 });
    }

    var origem = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";

    // Antes de gravar: cópia em texto de todos os CDU_MSS_ORDEM da família,
    // lidos agora do Primavera. Sem cópia não se grava nada.
    try
    {
        // Família vazia = catálogo inteiro (ordenação sem família).
        var familia = (req.Familia ?? "").Trim();
        var atuais = await data.SearchAsync(new PesquisaArtigos("", familia, 0, MaxArtigosOrdenar + 1), ct);
        var copia = audit.GuardarCopia(familia, atuais, alteracoes, opts.CurrentValue.DemoMode ? $"demo {origem}" : origem);
        app.Logger.LogInformation("Cópia da ordem antes de gravar: {Ficheiro}", copia);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        app.Logger.LogError(ex, "Não foi possível guardar a cópia antes de gravar a ordem.");
        return Results.Json(new { error = "Não foi possível guardar a cópia de segurança antes de gravar, por isso nada foi gravado." },
            statusCode: StatusCodes.Status500InternalServerError);
    }

    try
    {
        await data.GravarOrdemAsync(alteracoes, ct);
    }
    catch (OrdemConflitoException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }

    audit.Registar(alteracoes, opts.CurrentValue.DemoMode ? $"demo {origem}" : origem);
    app.Logger.LogInformation("Ordem gravada para {Count} artigos a partir de {Origem}.", alteracoes.Count, origem);
    return Results.Ok(new { gravados = alteracoes.Count });
});

app.Run();

// Escrever no Primavera só a partir do próprio servidor (loopback), de um
// PC da rede local se Portal:PermitirEscritaNaRede estiver ligado, ou com a
// chave da variável de ambiente MSS_PORTAL_ORDEM_ADMIN_KEY no cabeçalho
// X-Admin-Key. Sem nada disto, pedidos remotos ficam bloqueados.
static bool IsWriteAllowed(HttpContext ctx, PortalOptions opts)
{
    var remote = ctx.Connection.RemoteIpAddress;
    if (remote is null || System.Net.IPAddress.IsLoopback(remote))
    {
        return true;
    }

    if (opts.PermitirEscritaNaRede && IsRedeLocal(remote))
    {
        return true;
    }

    var key = Environment.GetEnvironmentVariable("MSS_PORTAL_ORDEM_ADMIN_KEY");
    return !string.IsNullOrWhiteSpace(key)
        && ctx.Request.Headers.TryGetValue("X-Admin-Key", out var provided)
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(key), System.Text.Encoding.UTF8.GetBytes(provided.ToString()));
}

// Endereços privados IPv4 (10/8, 172.16/12, 192.168/16) e locais IPv6.
static bool IsRedeLocal(System.Net.IPAddress ip)
{
    if (ip.IsIPv4MappedToIPv6)
    {
        ip = ip.MapToIPv4();
    }

    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
    {
        return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;
    }

    var b = ip.GetAddressBytes();
    return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
}

public partial class Program
{
    // Limite de segurança para carregar tudo de uma vez no modo de ordenação.
    private const int MaxArtigosOrdenar = 5000;
}
