using PortalOrdemMss.Web.Services;

namespace PortalOrdemMss.Web.Tests;

public sealed class ProductImageServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "portal-ordem-img-" + Guid.NewGuid().ToString("N"));
    private readonly ProductImageService _service;

    public ProductImageServiceTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(_folder, "FOTO123.jpg"), [0xFF, 0xD8]);
        File.WriteAllBytes(Path.Combine(_folder, "ART9.png"), [0x89, 0x50]);
        _service = new ProductImageService(new TestOptionsMonitor<PortalOptions>(new PortalOptions { ProductImageFolder = _folder }));
    }

    [Fact]
    public void Usa_a_coluna_imagem_primeiro() =>
        Assert.Equal("/imagens-artigos/FOTO123.jpg", _service.ResolveImageUrl("FOTO123", "ART9"));

    [Fact]
    public void Sem_ficheiro_da_coluna_usa_o_codigo() =>
        Assert.Equal("/imagens-artigos/ART9.png", _service.ResolveImageUrl("NAOEXISTE", "ART9"));

    [Fact]
    public void Sem_nada_usa_placeholder() =>
        Assert.Equal(ProductImageService.Placeholder, _service.ResolveImageUrl("", "XPTO"));

    [Fact]
    public void Url_http_passa_direto() =>
        Assert.Equal("https://exemplo.pt/a.jpg", _service.ResolveImageUrl("https://exemplo.pt/a.jpg", "ART9"));

    [Fact]
    public void Nao_sai_da_pasta() =>
        Assert.Equal("FOTO123", ProductImageService.SanitizeFileName("..\\..\\FOTO123"));

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}

internal sealed class TestOptionsMonitor<T>(T value) : Microsoft.Extensions.Options.IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
