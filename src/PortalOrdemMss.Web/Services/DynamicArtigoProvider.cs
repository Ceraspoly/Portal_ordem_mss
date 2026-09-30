using Microsoft.Extensions.Options;

namespace PortalOrdemMss.Web.Services;

/// <summary>
/// Decide em cada pedido entre dados de demonstração e SQL, lendo
/// Portal:DemoMode. Não guardar a escolha em cache: mudar DemoMode no
/// settings.json tem de ter efeito sem reiniciar.
/// </summary>
public sealed class DynamicArtigoProvider(
    IOptionsMonitor<PortalOptions> options,
    DemoArtigoProvider demo,
    SqlArtigoProvider sql) : IArtigoProvider
{
    private IArtigoProvider Current => options.CurrentValue.DemoMode ? demo : sql;

    public Task<IReadOnlyList<Artigo>> SearchAsync(PesquisaArtigos pesquisa, CancellationToken ct) =>
        Current.SearchAsync(pesquisa, ct);

    public Task<IReadOnlyList<Familia>> GetFamiliasAsync(CancellationToken ct) =>
        Current.GetFamiliasAsync(ct);

    public Task GravarOrdemAsync(IReadOnlyList<AlteracaoOrdem> alteracoes, CancellationToken ct) =>
        Current.GravarOrdemAsync(alteracoes, ct);
}
