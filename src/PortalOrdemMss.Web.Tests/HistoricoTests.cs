using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PortalOrdemMss.Web.Tests;

public sealed class HistoricoTests : IClassFixture<HistoricoTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "portal-ordem-hist-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:BasePath", _root);
            builder.UseSetting("Portal:DemoMode", "true");
            builder.UseSetting("Portal:GravacoesPorMinuto", "1000");
        }
    }

    private readonly HttpClient _client;

    public HistoricoTests(Factory factory) => _client = factory.CreateClient();

    private sealed record Entrada(string Id, string Descricao, List<AlteracaoOrdem> Alteracoes, string? Reverte, string? RevertidaPor);

    private async Task<List<Artigo>> Familia(string f) => (await _client.GetFromJsonAsync<List<Artigo>>($"/api/artigos?familia={f}"))!;

    private async Task<List<Entrada>> Historico() =>
        (await _client.GetFromJsonAsync<List<Entrada>>("/api/historico", new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

    // Põe o primeiro artigo da família logo a seguir ao segundo.
    private async Task Trocar(string f)
    {
        var a = await Familia(f);
        var nova = new List<string> { a[1].Codigo, a[0].Codigo };
        nova.AddRange(a.Skip(2).Select(x => x.Codigo));
        var r = await _client.PostAsJsonAsync("/api/ordem",
            new NovaOrdemRequest(a.Select(x => new ArtigoOrdem(x.Codigo, x.Ordem)).ToList(), nova, [a[0].Codigo], f));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Reverter_volta_a_ordem_anterior_e_fica_no_historico()
    {
        var antes = await Familia("ACE");
        await Trocar("ACE");
        Assert.NotEqual(antes.Select(a => a.Ordem), (await Familia("ACE")).Select(a => a.Ordem));

        var gravacao = (await Historico()).First(g => g.Alteracoes.Any(a => a.Codigo.StartsWith("ACE")));
        var r = await _client.PostAsync($"/api/historico/{gravacao.Id}/reverter", JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        Assert.Equal(antes.Select(a => (a.Codigo, a.Ordem)), (await Familia("ACE")).Select(a => (a.Codigo, a.Ordem)));
        var hist = await Historico();
        var reversao = hist.First();
        Assert.Equal(gravacao.Id, reversao.Reverte);
        Assert.Equal(reversao.Id, hist.Single(g => g.Id == gravacao.Id).RevertidaPor);

        var outraVez = await _client.PostAsync($"/api/historico/{gravacao.Id}/reverter", JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.Conflict, outraVez.StatusCode);
    }

    [Fact]
    public async Task Nao_reverte_se_os_artigos_mudaram_depois()
    {
        await Trocar("CER");
        var primeira = (await Historico()).First();
        var mudado = Assert.Single(primeira.Alteracoes).Codigo;

        // Depois, o mesmo artigo vai para o fim da família.
        var a = await Familia("CER");
        var nova = a.Select(x => x.Codigo).Where(c => c != mudado).Append(mudado).ToList();
        var r2 = await _client.PostAsJsonAsync("/api/ordem",
            new NovaOrdemRequest(a.Select(x => new ArtigoOrdem(x.Codigo, x.Ordem)).ToList(), nova, [mudado], "CER"));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        var r = await _client.PostAsync($"/api/historico/{primeira.Id}/reverter", JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Id_invalido_ou_inexistente_da_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/historico/..%2Fsettings/reverter", JsonContent.Create(new { }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/historico/20261003092242123_00000000000000000000000000000000/reverter", JsonContent.Create(new { }))).StatusCode);
    }

    [Fact]
    public async Task Mostra_no_maximo_15()
    {
        for (var i = 0; i < 17; i++)
        {
            await Trocar("VEL");
        }

        Assert.Equal(15, (await Historico()).Count);
    }
}
