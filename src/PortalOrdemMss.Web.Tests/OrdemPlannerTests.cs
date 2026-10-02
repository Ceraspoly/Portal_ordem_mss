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
}
