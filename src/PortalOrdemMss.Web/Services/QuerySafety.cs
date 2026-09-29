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
