using PortalOrdemMss.Web.Services;

namespace PortalOrdemMss.Web.Tests;

public class OrdemPlannerTests
{
    private static List<ArtigoOrdem> Lista(params (string Codigo, string Ordem)[] itens) =>
        itens.Select(i => new ArtigoOrdem(i.Codigo, i.Ordem)).ToList();

    [Fact]
    public void Exemplo_do_Bruno_arrastado_fica_com_anterior_mais_a()
    {
        var original = Lista(("X", "a55023"), ("Y", "b45223"), ("Z", "c10000"));
        var r = OrdemPlanner.Planear(original, ["Y", "X", "Z"], ["X"]);
        Assert.Equal(new AlteracaoOrdem("X", "a55023", "b45223a"), Assert.Single(r));
    }

    [Fact]
    public void Mantem_maiusculas()
    {
        var original = Lista(("X", "AB0005A5"), ("Y", "AB0007B1"), ("Z", "AB0009"));
        var r = OrdemPlanner.Planear(original, ["Y", "X", "Z"], ["X"]);
        Assert.Equal("AB0007B1A", Assert.Single(r).Novo);
    }

    [Fact]
    public void Se_ja_existe_anterior_mais_a_fica_antes_dele()
    {
        var original = Lista(("X", "a1"), ("Y", "b45223"), ("W", "b45223a"));
        var r = OrdemPlanner.Planear(original, ["Y", "X", "W"], ["X"]);
        var novo = Assert.Single(r).Novo;
        Assert.Equal("b452230", novo);
        Assert.True(StringComparer.OrdinalIgnoreCase.Compare("b45223", novo) < 0);
        Assert.True(StringComparer.OrdinalIgnoreCase.Compare(novo, "b45223a") < 0);
    }

    [Fact]
    public void Para_o_primeiro_lugar_fica_logo_antes_do_primeiro()
    {
        var original = Lista(("Y", "a55023"), ("Z", "b10000"), ("X", "c2"));
        var r = OrdemPlanner.Planear(original, ["X", "Y", "Z"], ["X"]);
        Assert.Equal("a55022z", Assert.Single(r).Novo);
    }

    [Fact]
    public void Para_o_primeiro_lugar_quando_o_primeiro_acaba_em_zero()
    {
        var original = Lista(("Y", "0020"), ("Z", "0030"), ("X", "0040"));
        var r = OrdemPlanner.Planear(original, ["X", "Y", "Z"], ["X"]);
        Assert.Equal("001z", Assert.Single(r).Novo);
        Assert.True(StringComparer.OrdinalIgnoreCase.Compare("001z", "0020") < 0);
    }

    [Fact]
    public void Dois_seguidos_depois_do_mesmo()
    {
        var original = Lista(("A", "10"), ("B", "20"), ("C", "30"), ("D", "40"));
        var r = OrdemPlanner.Planear(original, ["A", "C", "D", "B"], ["C", "D"]);
        Assert.Equal(new[] { "10a", "10aa" }, r.Select(a => a.Novo));
        Assert.Equal(new[] { "C", "D" }, r.Select(a => a.Codigo));
    }

    [Fact]
    public void Sem_arrastados_usa_quem_ficou_no_sitio()
    {
        var original = Lista(("A", "10"), ("B", "20"), ("C", "30"), ("D", "40"));
        var r = OrdemPlanner.Planear(original, ["A", "C", "D", "B"]);
        Assert.Equal(new AlteracaoOrdem("B", "20", "40a"), Assert.Single(r));
    }

    [Fact]
    public void Recusa_listas_diferentes() =>
        Assert.Throws<ArgumentException>(() => OrdemPlanner.Planear(Lista(("A", "1"), ("B", "2")), ["A", "C"], ["C"]));

    [Fact]
    public void Sem_espaco_fica_igual_ao_anterior()
    {
        var original = Lista(("A", "CA0000A"), ("B", "CA0000A"), ("X", "CB0001"));
        var r = OrdemPlanner.Planear(original, ["A", "X", "B"], ["X"]);
        Assert.Equal(new AlteracaoOrdem("X", "CB0001", "CA0000A"), Assert.Single(r));
    }

    [Fact]
    public void Desempatar_exemplo_do_Bruno_dez_iguais_ficam_a_a_j()
    {
        var codigos = Enumerable.Range(1, 10).Select(i => $"P{i:00}").ToList();
        var original = Lista([("A", "ab010"), .. codigos.Select(c => (c, "ab011")), ("Z", "ab012")]);
        var nova = original.Select(a => a.Codigo).ToList();
        var r = OrdemPlanner.Planear(original, nova, [], desempatar: true);
        Assert.Equal(codigos, r.Select(a => a.Codigo));
        Assert.Equal("abcdefghij".Select(c => "ab011" + c), r.Select(a => a.Novo));
    }

    [Fact]
    public void Desempatar_segue_a_ordem_do_ecra_e_as_maiusculas()
    {
        var original = Lista(("A", "AB011"), ("B", "AB011"), ("C", "AB011"));
        var r = OrdemPlanner.Planear(original, ["C", "A", "B"], ["C"], desempatar: true);
        Assert.Equal(new[] { "C:AB011A", "A:AB011B", "B:AB011C" }, r.Select(a => $"{a.Codigo}:{a.Novo}"));
    }

    [Fact]
    public void Desempatar_so_os_grupos_da_selecao()
    {
        var original = Lista(("A", "10"), ("B", "10"), ("C", "20"), ("D", "20"));
        var r = OrdemPlanner.Planear(original, ["A", "B", "C", "D"], [], desempatar: true, selecao: ["D"]);
        Assert.Equal(new[] { "C:20a", "D:20b" }, r.Select(a => $"{a.Codigo}:{a.Novo}"));
    }

    [Fact]
    public void Desempatar_recusa_se_passar_a_frente_do_seguinte()
    {
        var original = Lista(("A", "ab011"), ("B", "ab011"), ("C", "ab011a"));
        Assert.Throws<ArgumentException>(() => OrdemPlanner.Planear(original, ["A", "B", "C"], [], desempatar: true));
    }

    [Fact]
    public void Sufixo_depois_do_z_continua_por_ordem()
    {
        var sufixos = Enumerable.Range(0, 60).Select(n => "x" + OrdemPlanner.Sufixo(n, "x")).ToList();
        Assert.Equal(sufixos.OrderBy(v => v, StringComparer.OrdinalIgnoreCase), sufixos);
        Assert.Equal(60, sufixos.Distinct().Count());
    }
}
