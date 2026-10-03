namespace PortalOrdemMss.Web;

/// <summary>Um artigo tal como aparece no catálogo: só foto, nome, família e ordem.</summary>
// Loja: o artigo tem o visto no CDU de loja (coluna opcional "Loja" na query).
public sealed record Artigo(string Codigo, string Nome, string Familia, string FamiliaNome, string ImagemUrl, string Ordem, bool Loja = false);

public sealed record Familia(string Codigo, string Nome);

public sealed record PesquisaArtigos(string Search, string Familia, int Offset, int Limit);

/// <summary>Um artigo cujo CDU_MSS_ORDEM vai mudar de Anterior para Novo.</summary>
public sealed record AlteracaoOrdem(string Codigo, string Anterior, string Novo);

/// <summary>
/// Pedido de reordenação: os artigos na ordem em que estavam (com o
/// CDU_MSS_ORDEM que tinham), os códigos na nova ordem e os códigos que o
/// utilizador arrastou (só esses mudam de valor).
/// </summary>
public sealed record NovaOrdemRequest(List<ArtigoOrdem>? Original, List<string>? Nova, List<string>? Arrastados, string? Familia = null,
    bool Desempatar = false, List<string>? Selecao = null);

public sealed record ArtigoOrdem(string Codigo, string Ordem);

public sealed class PortalOptions
{
    public bool DemoMode { get; set; } = true;
    public string Titulo { get; set; } = "Catálogo de artigos";
    public string ListenUrl { get; set; } = "http://127.0.0.1:5090";
    public int PageSize { get; set; } = 60;

    /// <summary>Mostra o botão "Ordenar" e aceita gravar a nova ordem.</summary>
    public bool PermitirReordenar { get; set; } = true;

    /// <summary>
    /// Deixa ordenar a partir de outros PCs da rede local (IPs privados),
    /// não só do próprio servidor. Só faz sentido com ListenUrl em 0.0.0.0.
    /// </summary>
    public bool PermitirEscritaNaRede { get; set; }

    /// <summary>
    /// Hash do código pedido ao gravar ("pbkdf2-sha256$..."), criado pelo
    /// script tools\ATUALIZAR-PORTAL.ps1 -DefinirCodigo. Com ele definido,
    /// todas as gravações pedem o código; sem ele, os PCs da rede não gravam.
    /// </summary>
    public string CodigoEscritaHash { get; set; } = string.Empty;

    /// <summary>
    /// Nomes extra (além de IPs, localhost e o nome do servidor) pelos quais
    /// se pode abrir o portal para gravar, ex. "portal.ceraspoly.local".
    /// Protege contra DNS rebinding.
    /// </summary>
    public string[] HostsPermitidos { get; set; } = [];

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

    /// <summary>
    /// Opcional: devolve a coluna Codigo dos artigos com o visto no CDU de
    /// loja (rebordo vermelho). Se falhar, o portal continua sem rebordos.
    /// </summary>
    public string LojaQuery { get; set; } = string.Empty;

    /// <summary>Tem de devolver as colunas Codigo e Nome.</summary>
    public string FamiliasQuery { get; set; } = string.Empty;

    /// <summary>
    /// Único comando de escrita do portal: um UPDATE ao CDU_MSS_ORDEM de um
    /// artigo, com os parâmetros @Ordem, @Codigo e @OrdemAnterior. O
    /// @OrdemAnterior garante que só grava se o valor não mudou entretanto
    /// (ver QuerySafety.IsOrdemUpdate).
    /// </summary>
    public string AtualizarOrdemQuery { get; set; } = string.Empty;
}
