using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using PortalOrdemMss.Web.Services;

namespace PortalOrdemMss.Web.Tests;

public sealed class ApiDemoTests : IClassFixture<ApiDemoTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "portal-ordem-test-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:BasePath", _root);
            builder.UseSetting("Portal:DemoMode", "true");
            builder.UseSetting("Portal:PageSize", "5");
            builder.UseSetting("Portal:ProductImageFolder", Path.Combine(_root, "imagens"));
        }
    }

    private readonly HttpClient _client;

    public ApiDemoTests(Factory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_responde()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Artigos_vem_por_familia_e_ordem_com_paginas()
    {
        var pagina1 = await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos");
        var pagina2 = await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?offset=5");

        Assert.Equal(5, pagina1!.Count);
        Assert.Equal(5, pagina2!.Count);
        var todos = pagina1.Concat(pagina2).ToList();
        Assert.Equal(todos.Count, todos.Select(a => a.Codigo).Distinct().Count());
        Assert.All(todos, a => Assert.Equal(ProductImageService.Placeholder, a.ImagemUrl));
        Assert.Equal(
            todos.OrderBy(a => a.FamiliaNome).ThenBy(a => a.Ordem, StringComparer.Ordinal).Select(a => a.Codigo),
            todos.Select(a => a.Codigo));
    }

    [Fact]
    public async Task Todos_devolve_a_familia_inteira_ignorando_a_pagina()
    {
        // Página de 5: com offset=3 só viriam 2 velas, com todos=true vêm as 5.
        var velas = await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?familia=VEL&offset=3&todos=true");
        Assert.Equal(new[] { "VEL001", "VEL002", "VEL003", "VEL004", "VEL005" }, velas!.Select(a => a.Codigo));

        // Sem família, todos=true é ignorado (não carrega o catálogo inteiro).
        var semFamilia = await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?todos=true");
        Assert.Equal(5, semFamilia!.Count);
    }

    [Fact]
    public async Task Filtra_por_familia_e_pesquisa()
    {
        var ceras = await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?familia=CER");
        Assert.NotEmpty(ceras!);
        Assert.All(ceras!, a => Assert.Equal("CER", a.Familia));

        var soja = await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?q=soja");
        Assert.Equal("CER002", Assert.Single(soja!).Codigo);
    }

    [Fact]
    public async Task Familias_listadas()
    {
        var familias = await _client.GetFromJsonAsync<List<Familia>>("/api/familias");
        Assert.Equal(3, familias!.Count);
    }

    [Fact]
    public async Task Gravar_ordem_em_demo_e_detetar_conflito()
    {
        var ceras = (await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?familia=CER"))!;
        Assert.True(ceras.Count >= 3);
        var original = ceras.Select(a => new ArtigoOrdem(a.Codigo, a.Ordem)).ToList();
        var nova = new List<string> { ceras[1].Codigo, ceras[0].Codigo };
        nova.AddRange(ceras.Skip(2).Select(a => a.Codigo));

        var previsao = await _client.PostAsJsonAsync("/api/ordem/previsao", new NovaOrdemRequest(original, nova, [ceras[0].Codigo]));
        var alteracoes = await previsao.Content.ReadFromJsonAsync<List<AlteracaoOrdem>>();
        Assert.Equal(new AlteracaoOrdem(ceras[0].Codigo, ceras[0].Ordem, ceras[1].Ordem + "a"), Assert.Single(alteracoes!));
        var semMudar = (await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?familia=CER"))!;
        Assert.Equal(ceras.Select(a => a.Ordem), semMudar.Select(a => a.Ordem));

        var r = await _client.PostAsJsonAsync("/api/ordem", new NovaOrdemRequest(original, nova, [ceras[0].Codigo]));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var depois = (await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?familia=CER"))!;
        Assert.Equal(nova, depois.Select(a => a.Codigo));
        Assert.Equal(ceras[1].Ordem + "a", depois[1].Ordem);

        // Mesmo pedido outra vez: o valor anterior já não bate certo.
        var repetido = await _client.PostAsJsonAsync("/api/ordem", new NovaOrdemRequest(original, nova, [ceras[0].Codigo]));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
    }
}
