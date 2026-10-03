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

        public string Root => _root;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:BasePath", _root);
            builder.UseSetting("Portal:DemoMode", "true");
            builder.UseSetting("Portal:PageSize", "5");
            builder.UseSetting("Portal:ProductImageFolder", Path.Combine(_root, "imagens"));
        }
    }

    private readonly HttpClient _client;
    private readonly Factory _factory;

    public ApiDemoTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_responde()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Artigos_vem_pela_ordem_independentemente_da_familia_com_paginas()
    {
        var pagina1 = await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos");
        var pagina2 = await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?offset=5");

        Assert.Equal(5, pagina1!.Count);
        Assert.Equal(5, pagina2!.Count);
        var todos = pagina1.Concat(pagina2).ToList();
        Assert.Equal(todos.Count, todos.Select(a => a.Codigo).Distinct().Count());
        Assert.All(todos, a => Assert.Equal(ProductImageService.Placeholder, a.ImagemUrl));
        Assert.Equal(
            todos.OrderBy(a => a.Ordem, StringComparer.Ordinal).ThenBy(a => a.Codigo, StringComparer.Ordinal).Select(a => a.Codigo),
            todos.Select(a => a.Codigo));
    }

    [Fact]
    public async Task Todos_devolve_tudo_de_uma_vez_ignorando_a_pagina()
    {
        // Página de 5: com offset=3 só viriam 2 velas, com todos=true vêm as 5.
        var velas = await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?familia=VEL&offset=3&todos=true");
        Assert.Equal(new[] { "VEL001", "VEL002", "VEL003", "VEL004", "VEL005" }, velas!.Select(a => a.Codigo));

        // Sem família, todos=true traz o catálogo inteiro (12 artigos na demo).
        var semFamilia = await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?todos=true");
        Assert.Equal(12, semFamilia!.Count);
    }

    [Fact]
    public async Task Artigos_com_cdu_loja_vem_marcados()
    {
        var todos = (await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?todos=true"))!;
        Assert.Equal(new[] { "ACE003", "CER001", "VEL002" }, todos.Where(a => a.Loja).Select(a => a.Codigo).Order());
    }

    [Fact]
    public async Task Recusa_gravar_vindo_de_outro_site()
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/ordem")
        {
            Content = JsonContent.Create(new NovaOrdemRequest([new ArtigoOrdem("VEL001", "0010")], ["VEL001"], []))
        };
        pedido.Headers.Add("Origin", "http://site-malicioso.example");
        var r = await _client.SendAsync(pedido);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Recusa_gravar_com_nome_de_servidor_desconhecido()
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/ordem/previsao")
        {
            Content = JsonContent.Create(new NovaOrdemRequest([new ArtigoOrdem("VEL001", "0010")], ["VEL001"], []))
        };
        pedido.Headers.Host = "atacante.example:5090";
        var r = await _client.SendAsync(pedido);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Recusa_pedidos_demasiado_grandes_ou_com_textos_compridos()
    {
        var muitos = Enumerable.Range(0, 5001).Select(i => new ArtigoOrdem($"X{i}", "1")).ToList();
        var r1 = await _client.PostAsJsonAsync("/api/ordem/previsao", new NovaOrdemRequest(muitos, muitos.Select(a => a.Codigo).ToList(), []));
        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);

        var longo = new string('A', 101);
        var r2 = await _client.PostAsJsonAsync("/api/ordem", new NovaOrdemRequest([new ArtigoOrdem(longo, "1")], [longo], []));
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
    }

    [Fact]
    public async Task Respostas_tem_cabecalhos_de_seguranca()
    {
        var r = await _client.GetAsync("/");
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
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

        var r = await _client.PostAsJsonAsync("/api/ordem", new NovaOrdemRequest(original, nova, [ceras[0].Codigo], "CER"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        // Antes de gravar ficou uma cópia em texto com os valores antigos de toda a família.
        var copia = Assert.Single(Directory.GetFiles(Path.Combine(_factory.Root, "data", "historico"), "*_CER.txt"));
        var texto = File.ReadAllText(copia);
        Assert.Contains($"{ceras[0].Codigo};{ceras[0].Ordem};{ceras[1].Ordem}a", texto);
        Assert.All(ceras, a => Assert.Contains($"{a.Codigo};{a.Ordem};{a.Nome}", texto));

        var depois = (await _client.GetFromJsonAsync<List<Artigo>>("/api/artigos?familia=CER"))!;
        Assert.Equal(nova, depois.Select(a => a.Codigo));
        Assert.Equal(ceras[1].Ordem + "a", depois[1].Ordem);

        // Mesmo pedido outra vez: o valor anterior já não bate certo.
        var repetido = await _client.PostAsJsonAsync("/api/ordem", new NovaOrdemRequest(original, nova, [ceras[0].Codigo]));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
    }
}
