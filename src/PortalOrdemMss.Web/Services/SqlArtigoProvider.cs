using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PortalOrdemMss.Web.Services;

/// <summary>
/// Lê artigos e famílias do Primavera com as queries do settings.json.
/// Só SELECT (QuerySafety) e valores sempre parametrizados.
/// </summary>
public sealed class SqlArtigoProvider(IOptionsMonitor<SqlOptions> options, ProductImageService images) : IArtigoProvider
{
    public async Task<IReadOnlyList<Artigo>> SearchAsync(PesquisaArtigos pesquisa, CancellationToken ct)
    {
        var opts = options.CurrentValue;
        await using var connection = await OpenAsync(opts, ct);
        await using var command = CreateCommand(connection, opts, opts.ArtigosQuery);
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 200).Value = pesquisa.Search;
        command.Parameters.Add("@LikeSearch", SqlDbType.NVarChar, 210).Value = "%" + EscapeLike(pesquisa.Search) + "%";
        command.Parameters.Add("@Familia", SqlDbType.NVarChar, 50).Value = pesquisa.Familia;
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = pesquisa.Offset;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = pesquisa.Limit;

        await using var reader = await command.ExecuteReaderAsync(ct);
        QuerySafety.RequireColumns(reader, "Codigo", "Nome", "Familia", "FamiliaNome", "Imagem", "Ordem");
        int iCodigo = reader.GetOrdinal("Codigo"), iNome = reader.GetOrdinal("Nome"),
            iFamilia = reader.GetOrdinal("Familia"), iFamiliaNome = reader.GetOrdinal("FamiliaNome"),
            iImagem = reader.GetOrdinal("Imagem"), iOrdem = reader.GetOrdinal("Ordem");
        // Coluna opcional: se a query devolver "Loja", os artigos marcados
        // ficam com rebordo vermelho. Sem ela, nada muda.
        var iLoja = Ordinal(reader, "Loja");

        var result = new List<Artigo>();
        while (await reader.ReadAsync(ct))
        {
            var codigo = Text(reader, iCodigo);
            result.Add(new Artigo(
                codigo,
                Text(reader, iNome),
                Text(reader, iFamilia),
                Text(reader, iFamiliaNome),
                images.ResolveImageUrl(Text(reader, iImagem), codigo),
                // CDU_MSS_ORDEM é texto (ex. "AB0005A5"), não número: a ordenação fica no SQL.
                Text(reader, iOrdem),
                iLoja >= 0 && Marcado(reader.GetValue(iLoja))));
        }

        return result;
    }

    private static int Ordinal(System.Data.Common.DbDataReader reader, string nome)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), nome, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    // Visto do Primavera: bit 1/true, ou texto "1"/"S"/"Sim"/"true".
    internal static bool Marcado(object valor) => valor switch
    {
        null or DBNull => false,
        bool b => b,
        byte or short or int or long or decimal => Convert.ToDecimal(valor) != 0,
        _ => valor.ToString()!.Trim().ToUpperInvariant() is "1" or "S" or "SIM" or "TRUE" or "Y",
    };

    public async Task<IReadOnlyList<Familia>> GetFamiliasAsync(CancellationToken ct)
    {
        var opts = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(opts.FamiliasQuery))
        {
            return Array.Empty<Familia>();
        }

        await using var connection = await OpenAsync(opts, ct);
        await using var command = CreateCommand(connection, opts, opts.FamiliasQuery);
        await using var reader = await command.ExecuteReaderAsync(ct);
        QuerySafety.RequireColumns(reader, "Codigo", "Nome");
        int iCodigo = reader.GetOrdinal("Codigo"), iNome = reader.GetOrdinal("Nome");

        var result = new List<Familia>();
        while (await reader.ReadAsync(ct))
        {
            var codigo = Text(reader, iCodigo);
            var nome = Text(reader, iNome);
            result.Add(new Familia(codigo, nome.Length > 0 ? nome : codigo));
        }

        return result;
    }

    public async Task GravarOrdemAsync(IReadOnlyList<AlteracaoOrdem> alteracoes, CancellationToken ct)
    {
        var opts = options.CurrentValue;
        if (!QuerySafety.IsOrdemUpdate(opts.AtualizarOrdemQuery, out var reason))
        {
            throw new InvalidOperationException($"Gravação recusada: {reason}");
        }

        await using var connection = await OpenAsync(opts, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            foreach (var a in alteracoes)
            {
                await using var command = new SqlCommand(opts.AtualizarOrdemQuery, connection, transaction)
                {
                    CommandTimeout = opts.CommandTimeoutSeconds
                };
                command.Parameters.Add("@Ordem", SqlDbType.NVarChar, 100).Value = a.Novo;
                command.Parameters.Add("@Codigo", SqlDbType.NVarChar, 100).Value = a.Codigo;
                command.Parameters.Add("@OrdemAnterior", SqlDbType.NVarChar, 100).Value = a.Anterior;

                // Tem de mexer em exatamente 1 linha: 0 quer dizer que alguém
                // mudou a ordem entretanto; mais de 1 quer dizer que a query
                // configurada não filtra bem pelo artigo. Em ambos os casos
                // não se grava nada.
                var linhas = await command.ExecuteNonQueryAsync(ct);
                if (linhas != 1)
                {
                    throw new OrdemConflitoException(linhas == 0
                        ? $"A ordem do artigo {a.Codigo} mudou entretanto no Primavera. Atualiza a página e tenta de novo."
                        : $"A gravação do artigo {a.Codigo} afetaria {linhas} linhas. Nada foi gravado; verifica Sql:AtualizarOrdemQuery.");
                }
            }

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<SqlConnection> OpenAsync(SqlOptions opts, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opts.ConnectionString))
        {
            throw new InvalidOperationException("Sql:ConnectionString não está configurada (ou ativa Portal:DemoMode).");
        }

        var connection = new SqlConnection(opts.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static SqlCommand CreateCommand(SqlConnection connection, SqlOptions opts, string sql)
    {
        if (!QuerySafety.IsReadOnlySelect(sql, out var reason))
        {
            throw new InvalidOperationException($"Query recusada: {reason}");
        }

        return new SqlCommand(sql, connection) { CommandTimeout = opts.CommandTimeoutSeconds };
    }

    private static string Text(IDataRecord r, int i) =>
        r.IsDBNull(i) ? string.Empty : Convert.ToString(r.GetValue(i))?.Trim() ?? string.Empty;

    private static string EscapeLike(string s) =>
        s.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
