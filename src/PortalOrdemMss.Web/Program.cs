using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PortalOrdemMss.Web;
using PortalOrdemMss.Web.Services;

// Usado pelo script de instalação para criar o hash do código de gravação
// (lido do stdin, nunca da linha de comandos). Não arranca o portal.
if (args.Contains("--hash-codigo"))
{
    Console.InputEncoding = System.Text.Encoding.UTF8; // o script manda em UTF-8 (acentos incluídos)
    var lido = Console.In.ReadLine()?.Trim() ?? string.Empty;
    if (lido.Length < 6)
    {
        Console.Error.WriteLine("O código tem de ter pelo menos 6 caracteres.");
        return 1;
    }

    Console.WriteLine(CodigoEscrita.CriarHash(lido));
    return 0;
}

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

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CodigoEscrita>();
builder.Services.AddSingleton<ProductImageService>();
builder.Services.AddSingleton<DemoArtigoProvider>();
builder.Services.AddSingleton<SqlArtigoProvider>();
builder.Services.AddSingleton<IArtigoProvider, DynamicArtigoProvider>();
builder.Services.AddSingleton(new OrdemAuditLog(Path.Combine(rootDirectory, "data", "alteracoes-ordem.csv")));
builder.Services.AddSingleton(new HistoricoGravacoes(Path.Combine(rootDirectory, "data", "gravacoes")));
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
builder.Services.AddHealthChecks();

// Sem cabeçalho "Server: Kestrel" e com limite ao tamanho dos pedidos (o
// maior é a ordem de 5000 artigos, bem abaixo de 4 MB).
builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    k.Limits.MaxRequestBodySize = 4 * 1024 * 1024;
});

// Limite de pedidos por IP: geral na API e mais apertado para gravar.
var gravacoesPorMinuto = builder.Configuration.GetValue("Portal:GravacoesPorMinuto", 20);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
        await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Demasiados pedidos. Espera um minuto e tenta de novo." }, ct);
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        ctx.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetFixedWindowLimiter(IpDe(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = 1200, Window = TimeSpan.FromMinutes(1) })
            : RateLimitPartition.GetNoLimiter("estaticos"));
    o.AddPolicy("gravar", ctx => RateLimitPartition.GetFixedWindowLimiter(IpDe(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = gravacoesPorMinuto, Window = TimeSpan.FromMinutes(1) }));
});

var listenUrl = builder.Configuration["Portal:ListenUrl"];
if (!string.IsNullOrWhiteSpace(listenUrl) && string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls(listenUrl);
}

var app = builder.Build();

// Cabeçalhos de segurança: sem ser metido noutra página (clickjacking),
// sem adivinhar tipos de ficheiro e só scripts/estilos do próprio portal.
// As fotos podem vir de um URL http(s) do Primavera, por isso img-src aceita-os.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: http: https:; script-src 'self'; style-src 'self'; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'";
    await next();
});

app.UseResponseCompression();
app.UseRateLimiter();

// Pedidos que escrevem (POST em /api) só vindos do próprio portal: recusa
// pedidos de outros sites (CSRF) e nomes de servidor desconhecidos (DNS
// rebinding), antes de qualquer outra verificação.
app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsPost(ctx.Request.Method) && ctx.Request.Path.StartsWithSegments("/api")
        && !IsMesmaOrigem(ctx, ctx.RequestServices.GetRequiredService<IOptionsMonitor<PortalOptions>>().CurrentValue))
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new { error = "Pedido recusado: não veio do próprio portal." });
        return;
    }

    await next();
});

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
        podeReordenar = opts.CurrentValue.PermitirReordenar && IsWriteAllowed(ctx, opts.CurrentValue),
        pedeCodigo = CodigoEscrita.HashValido(opts.CurrentValue.CodigoEscritaHash)
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
    if (ValidarPedido(req) is { } invalido)
    {
        return Results.BadRequest(new { error = invalido });
    }

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
    IOptionsMonitor<PortalOptions> opts, OrdemAuditLog audit, HistoricoGravacoes historico, CodigoEscrita codigo, CancellationToken ct) =>
{
    if (Autorizar(ctx, opts.CurrentValue, codigo) is { } recusado)
    {
        return recusado;
    }

    if (ValidarPedido(req) is { } invalido)
    {
        return Results.BadRequest(new { error = invalido });
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

    // O parâmetro SQL tem tamanho fixo: um valor maior seria cortado sem
    // aviso, por isso recusa antes de gravar.
    if (alteracoes.FirstOrDefault(a => a.Novo.Length > MaxTamanhoTexto) is { } longo)
    {
        return Results.BadRequest(new { error = $"A nova ordem do artigo {longo.Codigo} ficaria demasiado comprida. Corrige a ordem desses artigos no Primavera." });
    }

    if (alteracoes.Count == 0)
    {
        return Results.Ok(new { gravados = 0 });
    }

    var familia = (req.Familia ?? "").Trim();
    var descricao = $"{alteracoes.Count} artigo(s) mudado(s) de sítio";
    return await GravarComCopia(ctx, data, opts.CurrentValue, audit, historico, familia, alteracoes, descricao, null, ct);
}).RequireRateLimiting("gravar");

// Últimas gravações (as mais recentes primeiro), para o ecrã "Histórico".
app.MapGet("/api/historico", (HttpContext ctx, IOptionsMonitor<PortalOptions> opts, HistoricoGravacoes historico) =>
    Results.Ok(historico.Ultimas().Select(g => new
    {
        g.Id,
        g.Data,
        g.Origem,
        familia = g.Familia.Length == 0 ? "Todas as famílias" : g.Familia,
        g.Descricao,
        g.Alteracoes,
        g.Reverte,
        g.RevertidaPor
    })));

// Reverter uma gravação: cada artigo volta ao valor que tinha antes dela.
// Só se nenhum desses artigos tiver mudado depois (senão, 409 a pedir para
// reverter primeiro as gravações mais recentes). A reversão é ela própria
// uma gravação nova no histórico, com cópia antes, como as outras.
app.MapPost("/api/historico/{id}/reverter", async (string id, HttpContext ctx, IArtigoProvider data,
    IOptionsMonitor<PortalOptions> opts, OrdemAuditLog audit, HistoricoGravacoes historico, CodigoEscrita codigo, CancellationToken ct) =>
{
    if (Autorizar(ctx, opts.CurrentValue, codigo) is { } recusado)
    {
        return recusado;
    }

    if (historico.Obter(id) is not { } g)
    {
        return Results.NotFound(new { error = "Essa gravação não existe." });
    }

    if (g.RevertidaPor is not null)
    {
        return Results.Conflict(new { error = "Essa gravação já foi revertida." });
    }

    var inverso = g.Alteracoes.Select(a => new AlteracaoOrdem(a.Codigo, a.Novo, a.Anterior)).ToList();
    var descricao = $"Reversão da gravação de {g.Data:dd/MM/yyyy HH:mm} ({inverso.Count} artigo(s))";
    var resultado = await GravarComCopia(ctx, data, opts.CurrentValue, audit, historico, g.Familia, inverso, descricao, g.Id, ct,
        conflito: "Há artigos desta gravação que foram mudados depois. Reverte primeiro as gravações mais recentes.");
    return resultado;
}).RequireRateLimiting("gravar");

app.Run();
return 0;

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

    // Da rede só com código definido: sem ele, ficaria qualquer PC a gravar.
    if (opts.PermitirEscritaNaRede && IsRedeLocal(remote) && CodigoEscrita.HashValido(opts.CodigoEscritaHash))
    {
        return true;
    }

    var key = Environment.GetEnvironmentVariable("MSS_PORTAL_ORDEM_ADMIN_KEY");
    return !string.IsNullOrWhiteSpace(key)
        && ctx.Request.Headers.TryGetValue("X-Admin-Key", out var provided)
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(key), System.Text.Encoding.UTF8.GetBytes(provided.ToString()));
}

// Permissão para gravar: computador autorizado e, se houver código
// definido, o código certo (também no servidor). null = pode gravar.
IResult? Autorizar(HttpContext ctx, PortalOptions o, CodigoEscrita codigo)
{
    if (!o.PermitirReordenar || !IsWriteAllowed(ctx, o))
    {
        return Results.Json(new { error = "Não tens permissão para mudar a ordem a partir deste computador." }, statusCode: StatusCodes.Status403Forbidden);
    }

    if (!CodigoEscrita.HashValido(o.CodigoEscritaHash))
    {
        return null;
    }

    var fornecido = ctx.Request.Headers["X-Codigo-Escrita"].ToString();
    switch (codigo.Verificar(o.CodigoEscritaHash, fornecido, IpDe(ctx)))
    {
        case ResultadoCodigo.Bloqueado:
            app.Logger.LogWarning("Gravação bloqueada por tentativas erradas do código a partir de {Ip}.", IpDe(ctx));
            return Results.Json(new { error = "Demasiadas tentativas erradas. Espera 15 minutos.", pedeCodigo = true }, statusCode: StatusCodes.Status429TooManyRequests);
        case ResultadoCodigo.Errado:
            app.Logger.LogWarning("Código de gravação errado a partir de {Ip}.", IpDe(ctx));
            return Results.Json(new { error = fornecido.Length == 0 ? "Indica o código para gravar." : "Código errado.", pedeCodigo = true }, statusCode: StatusCodes.Status401Unauthorized);
        default:
            return null;
    }
}

// Grava no Primavera com cópia antes (texto com todos os CDU_MSS_ORDEM da
// família, lidos agora; sem cópia não se grava nada), registo CSV depois e
// entrada no histórico para poder reverter.
async Task<IResult> GravarComCopia(HttpContext ctx, IArtigoProvider data, PortalOptions o, OrdemAuditLog audit,
    HistoricoGravacoes historico, string familia, IReadOnlyList<AlteracaoOrdem> alteracoes, string descricao,
    string? reverte, CancellationToken ct, string? conflito = null)
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
    var origem = o.DemoMode ? $"demo {ip}" : ip;
    try
    {
        // Família vazia = catálogo inteiro.
        var atuais = await data.SearchAsync(new PesquisaArtigos("", familia, 0, MaxArtigosOrdenar + 1), ct);
        var copia = audit.GuardarCopia(familia, atuais, alteracoes, origem);
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
        return Results.Conflict(new { error = conflito ?? ex.Message });
    }

    audit.Registar(alteracoes, reverte is null ? origem : $"{origem} (reversão)");
    try
    {
        var g = historico.Registar(familia, origem, descricao, alteracoes, reverte);
        if (reverte is not null)
        {
            historico.MarcarRevertida(reverte, g.Id);
        }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        // Já está gravado no Primavera e no CSV; só não aparece no histórico do portal.
        app.Logger.LogError(ex, "Gravado, mas não foi possível escrever no histórico de gravações.");
    }

    app.Logger.LogInformation("Ordem gravada para {Count} artigos a partir de {Origem}: {Descricao}.", alteracoes.Count, origem, descricao);
    return Results.Ok(new { gravados = alteracoes.Count });
}

static string IpDe(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

// Limites ao pedido de ordem: número de artigos e tamanho dos textos (iguais
// aos parâmetros SQL), para não aceitar pedidos enormes nem valores cortados.
static string? ValidarPedido(NovaOrdemRequest req)
{
    var original = req.Original ?? [];
    var nova = req.Nova ?? [];
    var arrastados = req.Arrastados ?? [];
    if (original.Count > MaxArtigosOrdenar || nova.Count > MaxArtigosOrdenar || arrastados.Count > MaxArtigosOrdenar)
    {
        return $"Demasiados artigos num só pedido (máximo {MaxArtigosOrdenar}).";
    }

    if (original.Any(a => a is null || string.IsNullOrWhiteSpace(a.Codigo) || a.Codigo.Length > MaxTamanhoTexto
            || (a.Ordem?.Length ?? 0) > MaxTamanhoTexto)
        || nova.Concat(arrastados).Any(c => string.IsNullOrWhiteSpace(c) || c.Length > MaxTamanhoTexto)
        || (req.Familia?.Length ?? 0) > 50)
    {
        return "Pedido inválido: código ou ordem vazios ou demasiado compridos.";
    }

    return null;
}

// Um POST só é aceite se o cabeçalho Origin (quando o browser o manda) for
// o próprio portal e o nome usado para o abrir for um IP, localhost, o nome
// deste servidor ou um dos Portal:HostsPermitidos.
static bool IsMesmaOrigem(HttpContext ctx, PortalOptions opts)
{
    var host = ctx.Request.Host;
    if (!host.HasValue)
    {
        return false;
    }

    var nome = host.Host.Trim('[', ']');
    var maquina = Environment.MachineName;
    var hostConhecido = System.Net.IPAddress.TryParse(nome, out _)
        || nome.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || nome.Equals(maquina, StringComparison.OrdinalIgnoreCase)
        || nome.StartsWith(maquina + ".", StringComparison.OrdinalIgnoreCase)
        || opts.HostsPermitidos.Any(h => nome.Equals(h, StringComparison.OrdinalIgnoreCase));
    if (!hostConhecido)
    {
        return false;
    }

    var origin = ctx.Request.Headers.Origin.ToString();
    if (origin.Length == 0)
    {
        return true; // sem Origin: não é um pedido de outro site feito pelo browser
    }

    return Uri.TryCreate(origin, UriKind.Absolute, out var o)
        && string.Equals(o.Authority, host.Value, StringComparison.OrdinalIgnoreCase);
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

    // Igual ao tamanho dos parâmetros @Codigo/@Ordem/@OrdemAnterior.
    private const int MaxTamanhoTexto = 100;
}
