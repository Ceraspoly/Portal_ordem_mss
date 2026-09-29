namespace PortalOrdemMss.Web.Services;

public interface IArtigoProvider
{
    Task<IReadOnlyList<Artigo>> SearchAsync(PesquisaArtigos pesquisa, CancellationToken ct);

    Task<IReadOnlyList<Familia>> GetFamiliasAsync(CancellationToken ct);
}
