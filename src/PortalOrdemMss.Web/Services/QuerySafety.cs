using System.Data.Common;
using System.Text.RegularExpressions;

namespace PortalOrdemMss.Web.Services;

/// <summary>
/// Recusa queries configuradas que não sejam um único SELECT. Não substitui
/// a parametrização: os valores vão sempre em SqlCommand.Parameters.
/// </summary>
public static partial class QuerySafety
{
    [GeneratedRegex(
        @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|EXEC|EXECUTE|MERGE|GRANT|REVOKE|CREATE)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex DangerousKeywords();

    public static bool IsReadOnlySelect(string sql, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            reason = "A query está vazia.";
            return false;
        }

        if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            reason = "A query tem de começar por SELECT (este portal é só de leitura).";
            return false;
        }

        if (DangerousKeywords().IsMatch(sql))
        {
            reason = "A query contém uma palavra-chave que não é permitida em modo de leitura.";
            return false;
        }

        if (sql.Contains(';'))
        {
            reason = "A query não pode conter vários comandos separados por ';'.";
            return false;
        }

        reason = null;
        return true;
    }

    [GeneratedRegex(
        @"\b(INSERT|DELETE|DROP|ALTER|TRUNCATE|EXEC|EXECUTE|MERGE|GRANT|REVOKE|CREATE|SELECT)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenInOrdemUpdate();

    /// <summary>
    /// A única escrita permitida: um UPDATE de um só comando que mexe no
    /// CDU_MSS_ORDEM e usa @Ordem, @Codigo e @OrdemAnterior.
    /// </summary>
    public static bool IsOrdemUpdate(string sql, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            reason = "Sql:AtualizarOrdemQuery não está configurada.";
            return false;
        }

        if (!sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Sql:AtualizarOrdemQuery tem de começar por UPDATE.";
            return false;
        }

        if (sql.Contains(';') || ForbiddenInOrdemUpdate().IsMatch(sql))
        {
            reason = "Sql:AtualizarOrdemQuery só pode ser um único UPDATE, sem outros comandos.";
            return false;
        }

        foreach (var required in new[] { "CDU_MSS_ORDEM", "@Ordem", "@Codigo", "@OrdemAnterior" })
        {
            if (!sql.Contains(required, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Sql:AtualizarOrdemQuery tem de conter {required}.";
                return false;
            }
        }

        reason = null;
        return true;
    }

    public static void RequireColumns(DbDataReader reader, params string[] expected)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            present.Add(reader.GetName(i));
        }

        var missing = expected.Where(e => !present.Contains(e)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"A query devolveu colunas em falta: {string.Join(", ", missing)}. " +
                "Confirma os aliases (AS Codigo, AS Nome, ...) no settings.json.");
        }
    }
}
