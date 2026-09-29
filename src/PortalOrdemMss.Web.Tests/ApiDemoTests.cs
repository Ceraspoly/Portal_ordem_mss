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
            todos.OrderBy(a => a.FamiliaNome).ThenBy(a => a.Ordem).Select(a => a.Codigo),
            todos.Select(a => a.Codigo));
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
}
