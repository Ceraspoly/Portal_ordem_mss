namespace PortalOrdemMss.Web.Services;

public interface IArtigoProvider
{
    Task<IReadOnlyList<Artigo>> SearchAsync(PesquisaArtigos pesquisa, CancellationToken ct);

    Task<IReadOnlyList<Familia>> GetFamiliasAsync(CancellationToken ct);

    /// <summary>
    /// Grava todas as alterações ou nenhuma. Lança <see cref="OrdemConflitoException"/>
    /// se algum artigo já não tiver o CDU_MSS_ORDEM anterior.
    /// </summary>
    Task GravarOrdemAsync(IReadOnlyList<AlteracaoOrdem> alteracoes, CancellationToken ct);
}
