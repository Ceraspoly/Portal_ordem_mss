using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using PortalOrdemMss.Web.Services;

namespace PortalOrdemMss.Web.Tests;

public sealed class CodigoEscritaTests : IClassFixture<CodigoEscritaTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        public static readonly string Hash = CodigoEscrita.CriarHash("1234-certo");
        private readonly string _root = Path.Combine(Path.GetTempPath(), "portal-ordem-codigo-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:BasePath", _root);
            builder.UseSetting("Portal:DemoMode", "true");
            builder.UseSetting("Portal:CodigoEscritaHash", Hash);
        }
    }

    private readonly Factory _factory;

    public CodigoEscritaTests(Factory factory) => _factory = factory;

    [Fact]
    public void Hash_confere_so_com_o_codigo_certo()
    {
        var hash = CodigoEscrita.CriarHash("segredo");
        Assert.StartsWith("pbkdf2-sha256$210000$", hash);
        Assert.DoesNotContain("segredo", hash);
        Assert.True(CodigoEscrita.Confere(hash, "segredo"));
        Assert.False(CodigoEscrita.Confere(hash, "Segredo"));
        Assert.False(CodigoEscrita.HashValido("texto-qualquer"));
    }

    [Fact]
    public void Bloqueia_depois_de_5_erradas()
    {
        var relogio = new RelogioFixo();
        var c = new CodigoEscrita(relogio);
        var hash = CodigoEscrita.CriarHash("certo");
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(ResultadoCodigo.Errado, c.Verificar(hash, "errado", "10.0.0.1"));
        }

        Assert.Equal(ResultadoCodigo.Bloqueado, c.Verificar(hash, "errado", "10.0.0.1"));
        Assert.Equal(ResultadoCodigo.Bloqueado, c.Verificar(hash, "certo", "10.0.0.1"));
        Assert.Equal(ResultadoCodigo.Certo, c.Verificar(hash, "certo", "10.0.0.2"));

        relogio.Agora += CodigoEscrita.Bloqueio + TimeSpan.FromSeconds(1);
        Assert.Equal(ResultadoCodigo.Certo, c.Verificar(hash, "certo", "10.0.0.1"));
    }

    [Fact]
    public async Task Gravar_pede_o_codigo()
    {
        var client = _factory.CreateClient();
        var config = await client.GetFromJsonAsync<Dictionary<string, object>>("/api/config");
        Assert.Equal("True", config!["pedeCodigo"].ToString(), ignoreCase: true);

        var velas = (await client.GetFromJsonAsync<List<Artigo>>("/api/artigos?familia=VEL"))!;
        var original = velas.Select(a => new ArtigoOrdem(a.Codigo, a.Ordem)).ToList();
        var nova = new List<string> { velas[1].Codigo, velas[0].Codigo };
        nova.AddRange(velas.Skip(2).Select(a => a.Codigo));
        var corpo = new NovaOrdemRequest(original, nova, [velas[0].Codigo], "VEL");

        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(client, corpo, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(client, corpo, "errado")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(client, corpo, "1234-certo")).StatusCode);
    }

    private static Task<HttpResponseMessage> Enviar(HttpClient client, NovaOrdemRequest corpo, string? codigo)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/ordem") { Content = JsonContent.Create(corpo) };
        if (codigo is not null)
        {
            pedido.Headers.Add("X-Codigo-Escrita", codigo);
        }

        return client.SendAsync(pedido);
    }

    private sealed class RelogioFixo : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Agora;
    }
}
