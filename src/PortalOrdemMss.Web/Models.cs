namespace PortalOrdemMss.Web;

/// <summary>Um artigo tal como aparece no catálogo: só foto, nome, família e ordem.</summary>
public sealed record Artigo(string Codigo, string Nome, string Familia, string FamiliaNome, string ImagemUrl, int? Ordem);

public sealed record Familia(string Codigo, string Nome);

public sealed record PesquisaArtigos(string Search, string Familia, int Offset, int Limit);

public sealed class PortalOptions
{
    public bool DemoMode { get; set; } = true;
    public string Titulo { get; set; } = "Catálogo de artigos";
    public string ListenUrl { get; set; } = "http://127.0.0.1:5090";
    public int PageSize { get; set; } = 60;

    /// <summary>
    /// Pasta das fotos dos artigos (pode ser um caminho UNC, ex.
    /// \\servidor\partilha\imagens). O ficheiro procurado é o valor da
    /// coluna Imagem (CDU_MTImagem) ou, sem ele, o código do artigo.
    /// </summary>
    public string ProductImageFolder { get; set; } = "data/imagens";
}

public sealed class SqlOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Tem de devolver as colunas Codigo, Nome, Familia, FamiliaNome, Imagem
    /// e Ordem, e pode usar os parâmetros @Search, @LikeSearch, @Familia,
    /// @Offset e @Limit.
    /// </summary>
    public string ArtigosQuery { get; set; } = string.Empty;

    /// <summary>Tem de devolver as colunas Codigo e Nome.</summary>
    public string FamiliasQuery { get; set; } = string.Empty;
}
