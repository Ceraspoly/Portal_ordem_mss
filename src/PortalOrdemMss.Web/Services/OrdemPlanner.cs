namespace PortalOrdemMss.Web.Services;

public sealed class OrdemConflitoException(string message) : Exception(message);

/// <summary>
/// Calcula que valores de CDU_MSS_ORDEM gravar depois de arrastar artigos.
/// Regra (pedido do Bruno, 2026-09-30): só o artigo arrastado muda, e fica
/// com o valor do artigo que ficou antes dele mais um "a". Ex.: arrastar
/// A55023 para depois de B45223 grava B45223A. Os outros artigos ficam como
/// estão.
/// </summary>
public static class OrdemPlanner
{
    // Mesma comparação que o SQL Server faz com uma collation _CI_ (sem
    // distinguir maiúsculas): dígitos antes de letras.
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    /// <param name="desempatar">
    /// Também desempata artigos seguidos com o mesmo valor (pedido do Bruno,
    /// 2026-10-03): 10 artigos com "ab011" ficam "ab011a", "ab011b"… pela
    /// ordem do ecrã. Com <paramref name="selecao"/>, só os grupos com algum
    /// artigo selecionado; sem seleção, todos os grupos.
    /// </param>
    public static IReadOnlyList<AlteracaoOrdem> Planear(IReadOnlyList<ArtigoOrdem> original, IReadOnlyList<string> nova,
        IReadOnlyCollection<string>? arrastados = null, bool desempatar = false, IReadOnlyCollection<string>? selecao = null)
    {
        if (original.Count == 0)
        {
            throw new ArgumentException("Não há artigos para reordenar.");
        }

        var codigosOriginais = original.Select(a => a.Codigo).ToList();
        if (codigosOriginais.Distinct(StringComparer.OrdinalIgnoreCase).Count() != codigosOriginais.Count
            || nova.Distinct(StringComparer.OrdinalIgnoreCase).Count() != nova.Count
            || nova.Count != original.Count
            || !new HashSet<string>(codigosOriginais, StringComparer.OrdinalIgnoreCase).SetEquals(nova))
        {
            throw new ArgumentException("A nova ordem tem de ter exatamente os mesmos artigos que a original, sem repetidos.");
        }

        var indiceOriginal = original.Select((a, i) => (a.Codigo, i)).ToDictionary(x => x.Codigo, x => x.i, StringComparer.OrdinalIgnoreCase);
        var porCodigo = original.ToDictionary(a => a.Codigo, StringComparer.OrdinalIgnoreCase);
        var sequencia = nova.Select(c => indiceOriginal[c]).ToArray();

        // Ficam com o valor que têm os artigos que o utilizador não arrastou,
        // desde que continuem pela ordem original. Se não vier essa
        // informação (ou não bater certo), os que "ficaram no sítio" são a
        // maior subsequência crescente das posições originais.
        var arrastadosSet = new HashSet<string>(arrastados ?? [], StringComparer.OrdinalIgnoreCase);
        var fixos = Enumerable.Range(0, nova.Count).Where(i => !arrastadosSet.Contains(nova[i])).ToHashSet();
        var fixosOrdenados = fixos.Order().Select(i => sequencia[i]).ToList();
        if (arrastadosSet.Count == 0 || !fixosOrdenados.Zip(fixosOrdenados.Skip(1)).All(p => p.First < p.Second))
        {
            fixos = MaiorSubsequenciaCrescente(sequencia);
        }

        var valores = new string?[nova.Count];
        for (var i = 0; i < nova.Count; i++)
        {
            if (fixos.Contains(i))
            {
                valores[i] = porCodigo[nova[i]].Ordem;
            }
        }

        // Ao desempatar, um artigo mudado de sítio dentro do próprio grupo de
        // iguais (ex. trocar dois "ab011") fica no grupo com o valor que tem,
        // para ser desempatado pela ordem do ecrã em vez de sair do grupo.
        if (desempatar)
        {
            var fixosValores = (string?[])valores.Clone();
            for (var i = 0; i < nova.Count; i++)
            {
                if (fixosValores[i] is not null)
                {
                    continue;
                }

                var proprio = porCodigo[nova[i]].Ordem;
                var antes = fixosValores.Take(i).LastOrDefault(v => v is not null);
                var depois = fixosValores.Skip(i + 1).FirstOrDefault(v => v is not null);
                var noGrupo = Cmp.Equals(proprio, antes) || Cmp.Equals(proprio, depois);
                if (noGrupo && (antes is null || Cmp.Compare(antes, proprio) <= 0) && (depois is null || Cmp.Compare(proprio, depois) <= 0))
                {
                    valores[i] = proprio;
                }
            }
        }

        for (var i = 0; i < nova.Count; i++)
        {
            if (valores[i] is not null)
            {
                continue;
            }

            var artigo = porCodigo[nova[i]];
            var anterior = i > 0 ? valores[i - 1] : null;
            string? seguinte = null;
            for (var j = i + 1; j < nova.Count; j++)
            {
                if (valores[j] is not null)
                {
                    seguinte = valores[j];
                    break;
                }
            }

            // Sem espaço entre os dois (ex. anterior e seguinte ambos
            // "CA0000A"): fica igual ao anterior (pedido do Bruno,
            // 2026-10-01); no primeiro lugar, igual ao seguinte.
            var novo = ValorEntre(anterior, seguinte)
                ?? anterior
                ?? seguinte
                ?? throw new ArgumentException($"Não foi possível calcular a ordem do artigo {artigo.Codigo}.");

            valores[i] = novo;
        }

        if (desempatar)
        {
            Desempatar(nova, valores!, new HashSet<string>(selecao ?? [], StringComparer.OrdinalIgnoreCase));
        }

        var alteracoes = new List<AlteracaoOrdem>();
        for (var i = 0; i < nova.Count; i++)
        {
            var artigo = porCodigo[nova[i]];
            if (!string.Equals(valores[i], artigo.Ordem, StringComparison.Ordinal))
            {
                alteracoes.Add(new AlteracaoOrdem(artigo.Codigo, artigo.Ordem, valores[i]!));
            }
        }

        return alteracoes;
    }

    // Grupos de artigos seguidos com o mesmo valor: cada um fica com o valor
    // mais uma letra (a, b, … z, depois za, zb, …), pela ordem em que estão.
    private static void Desempatar(IReadOnlyList<string> nova, string[] valores, HashSet<string> selecao)
    {
        var i = 0;
        while (i < valores.Length)
        {
            var j = i;
            while (j + 1 < valores.Length && Cmp.Equals(valores[j + 1], valores[i]))
            {
                j++;
            }

            var tocado = selecao.Count == 0 || Enumerable.Range(i, j - i + 1).Any(k => selecao.Contains(nova[k]));
            if (j > i && tocado)
            {
                var baseValor = valores[i];
                for (var k = i; k <= j; k++)
                {
                    valores[k] = baseValor + Sufixo(k - i, baseValor);
                }

                // Tem de continuar antes do artigo seguinte (ex. se a seguir
                // houver "ab0110", "ab011k" já passava à frente dele).
                if (j + 1 < valores.Length && Cmp.Compare(valores[j], valores[j + 1]) >= 0)
                {
                    throw new ArgumentException(
                        $"Não dá para desempatar os {j - i + 1} artigos com \"{baseValor}\": o valor {valores[j]} passava à frente do artigo {nova[j + 1]} ({valores[j + 1]}).");
                }
            }

            i = j + 1;
        }
    }

    internal static string Sufixo(int n, string referencia) =>
        new string(Letra('z', referencia), n / 26) + Letra((char)('a' + n % 26), referencia);

    /// <summary>
    /// Um valor estritamente entre <paramref name="anterior"/> e
    /// <paramref name="seguinte"/> (qualquer um pode faltar), ou null se não
    /// houver nenhum simples.
    /// </summary>
    internal static string? ValorEntre(string? anterior, string? seguinte)
    {
        if (anterior is not null)
        {
            // Regra normal: valor do anterior + "a".
            var candidato = anterior + Letra('a', anterior);
            if (seguinte is null || Cmp.Compare(candidato, seguinte) < 0)
            {
                return candidato;
            }

            // O seguinte já é "anterior + a..." (ex. já existe B45223A):
            // procura o primeiro sítio onde cabe um "0" antes dele.
            if (Cmp.Compare(anterior, seguinte) >= 0 || !seguinte.StartsWith(anterior, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var prefixo = anterior;
            foreach (var ch in seguinte[anterior.Length..])
            {
                if (Cmp.Compare(ch.ToString(), "0") > 0)
                {
                    return prefixo + "0";
                }

                prefixo += ch;
            }

            return null;
        }

        // Primeiro lugar: baixa o último carácter que se pode baixar e
        // acrescenta "z" (ex. A55023 -> A55022Z; 0020 -> 001z), para ficar
        // logo antes dele.
        if (string.IsNullOrEmpty(seguinte))
        {
            return null;
        }

        for (var i = seguinte.Length - 1; i >= 0; i--)
        {
            var c = seguinte[i];
            if (c is > '0' and <= '9' or > 'a' and <= 'z' or > 'A' and <= 'Z')
            {
                return seguinte[..i] + (char)(c - 1) + Letra('z', seguinte);
            }
        }

        return null;
    }

    // Mantém o estilo do valor: se só tem maiúsculas (ex. AB0005A5), usa maiúscula.
    private static char Letra(char c, string referencia) =>
        referencia.Any(char.IsLetter) && !referencia.Any(char.IsLower) ? char.ToUpperInvariant(c) : c;

    private static HashSet<int> MaiorSubsequenciaCrescente(int[] seq)
    {
        var comprimento = new int[seq.Length];
        var anterior = new int[seq.Length];
        var melhor = -1;
        for (var i = 0; i < seq.Length; i++)
        {
            comprimento[i] = 1;
            anterior[i] = -1;
            for (var j = 0; j < i; j++)
            {
                if (seq[j] < seq[i] && comprimento[j] + 1 > comprimento[i])
                {
                    comprimento[i] = comprimento[j] + 1;
                    anterior[i] = j;
                }
            }

            if (melhor < 0 || comprimento[i] > comprimento[melhor])
            {
                melhor = i;
            }
        }

        var resultado = new HashSet<int>();
        for (var i = melhor; i >= 0; i = anterior[i])
        {
            resultado.Add(i);
        }

        return resultado;
    }
}
