namespace PortalOrdemMss.Web.Services;

/// <summary>Dados fixos para trabalhar sem acesso ao SQL Server do Primavera.</summary>
public sealed class DemoArtigoProvider(ProductImageService images) : IArtigoProvider
{
    private static readonly Familia[] Familias =
    [
        new("VEL", "Velas"),
        new("CER", "Ceras"),
        new("ACE", "Acessórios"),
    ];

    private static readonly (string Codigo, string Nome, string Familia, int Ordem)[] Dados =
    [
        ("VEL001", "Vela lisa branca 20 cm", "VEL", 10),
        ("VEL002", "Vela lisa marfim 20 cm", "VEL", 20),
        ("VEL003", "Vela torcida vermelha 25 cm", "VEL", 30),
        ("VEL004", "Vela aromática baunilha", "VEL", 40),
        ("VEL005", "Vela de pilar 7x15 cm", "VEL", 50),
        ("CER001", "Cera de abelha 1 kg", "CER", 10),
        ("CER002", "Cera de soja 1 kg", "CER", 20),
        ("CER003", "Parafina em pastilhas 5 kg", "CER", 30),
        ("ACE001", "Pavio de algodão 50 m", "ACE", 10),
        ("ACE002", "Molde de silicone redondo", "ACE", 20),
        ("ACE003", "Castiçal em vidro", "ACE", 30),
        ("ACE004", "Corante para cera (6 cores)", "ACE", 40),
    ];

    public Task<IReadOnlyList<Artigo>> SearchAsync(PesquisaArtigos pesquisa, CancellationToken ct)
    {
        var nomes = Familias.ToDictionary(f => f.Codigo, f => f.Nome);
        IReadOnlyList<Artigo> result = Dados
            .Where(d => pesquisa.Familia.Length == 0 || d.Familia == pesquisa.Familia)
            .Where(d => pesquisa.Search.Length == 0
                        || d.Codigo.Contains(pesquisa.Search, StringComparison.OrdinalIgnoreCase)
                        || d.Nome.Contains(pesquisa.Search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => nomes[d.Familia]).ThenBy(d => d.Ordem).ThenBy(d => d.Codigo)
            .Skip(pesquisa.Offset).Take(pesquisa.Limit)
            .Select(d => new Artigo(d.Codigo, d.Nome, d.Familia, nomes[d.Familia], images.ResolveImageUrl(string.Empty, d.Codigo), d.Ordem))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Familia>> GetFamiliasAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Familia>>(Familias.OrderBy(f => f.Nome).ToList());
}
