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
}
