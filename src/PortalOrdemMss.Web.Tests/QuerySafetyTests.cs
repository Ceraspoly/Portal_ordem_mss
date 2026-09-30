using PortalOrdemMss.Web.Services;

namespace PortalOrdemMss.Web.Tests;

public class QuerySafetyTests
{
    [Theory]
    [InlineData("SELECT a.Artigo AS Codigo FROM Artigo a")]
    [InlineData("  select 1 AS Codigo")]
    public void Aceita_select(string sql) => Assert.True(QuerySafety.IsReadOnlySelect(sql, out _));

    [Theory]
    [InlineData("")]
    [InlineData("UPDATE Artigo SET Descricao = 'x'")]
    [InlineData("SELECT 1; DROP TABLE Artigo")]
    [InlineData("SELECT * FROM Artigo WHERE 1=1 DELETE FROM Artigo")]
    [InlineData("WITH x AS (SELECT 1) SELECT * FROM x")]
    public void Recusa_o_resto(string sql) => Assert.False(QuerySafety.IsReadOnlySelect(sql, out _));

    [Fact]
    public void Aceita_o_update_da_ordem() =>
        Assert.True(QuerySafety.IsOrdemUpdate("UPDATE PRIMSS2CLO.dbo.Artigo SET CDU_MSS_ORDEM = @Ordem WHERE Artigo = @Codigo AND ISNULL(CDU_MSS_ORDEM, '') = @OrdemAnterior", out _));

    [Theory]
    [InlineData("")]
    [InlineData("UPDATE Artigo SET Descricao = @Ordem WHERE Artigo = @Codigo AND x = @OrdemAnterior")]
    [InlineData("UPDATE Artigo SET CDU_MSS_ORDEM = @Ordem WHERE Artigo = @Codigo")]
    [InlineData("UPDATE Artigo SET CDU_MSS_ORDEM = @Ordem WHERE Artigo = @Codigo AND CDU_MSS_ORDEM = @OrdemAnterior; DELETE FROM Artigo")]
    [InlineData("DELETE FROM Artigo WHERE CDU_MSS_ORDEM = @Ordem AND Artigo = @Codigo AND x = @OrdemAnterior")]
    public void Recusa_outras_escritas(string sql) => Assert.False(QuerySafety.IsOrdemUpdate(sql, out _));
}
