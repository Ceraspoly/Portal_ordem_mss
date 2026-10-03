using System.Text.Json;
using System.Text.RegularExpressions;

namespace PortalOrdemMss.Web.Services;

/// <summary>Uma gravação da ordem, para se poder ver e reverter.</summary>
public sealed record Gravacao(
    string Id,
    DateTime Data,
    string Origem,
    string Familia,
    string Descricao,
    IReadOnlyList<AlteracaoOrdem> Alteracoes,
    string? Reverte = null,
    string? RevertidaPor = null);

/// <summary>
/// Guarda cada gravação num JSON em data\gravacoes (um ficheiro por
/// gravação, nunca apagado), para o portal mostrar as últimas e as poder
/// reverter (pedido do Bruno, 2026-10-03: até às últimas 15).
/// </summary>
public sealed partial class HistoricoGravacoes(string pasta)
{
    public const int Mostrar = 15;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();

    public string Pasta { get; } = pasta;

    public static bool IdValido(string? id) => id is not null && IdRegex().IsMatch(id);

    public Gravacao Registar(string familia, string origem, string descricao, IReadOnlyList<AlteracaoOrdem> alteracoes, string? reverte = null)
    {
        var agora = DateTime.Now;
        var g = new Gravacao($"{agora:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}", agora, origem, familia, descricao, alteracoes, reverte);
        lock (_gate)
        {
            Directory.CreateDirectory(Pasta);
            Escrever(g);
        }

        return g;
    }

    public IReadOnlyList<Gravacao> Ultimas(int quantas = Mostrar)
    {
        lock (_gate)
        {
            if (!Directory.Exists(Pasta))
            {
                return [];
            }

            return Directory.GetFiles(Pasta, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(IdValido)
                .OrderByDescending(id => id, StringComparer.Ordinal)
                .Take(quantas)
                .Select(id => Ler(id!))
                .OfType<Gravacao>()
                .ToList();
        }
    }

    public Gravacao? Obter(string id)
    {
        if (!IdValido(id))
        {
            return null;
        }

        lock (_gate)
        {
            return Ler(id);
        }
    }

    public void MarcarRevertida(string id, string revertidaPor)
    {
        lock (_gate)
        {
            if (Ler(id) is { } g)
            {
                Escrever(g with { RevertidaPor = revertidaPor });
            }
        }
    }

    private Gravacao? Ler(string id)
    {
        var ficheiro = Path.Combine(Pasta, id + ".json");
        return File.Exists(ficheiro) ? JsonSerializer.Deserialize<Gravacao>(File.ReadAllText(ficheiro), Json) : null;
    }

    private void Escrever(Gravacao g)
    {
        var ficheiro = Path.Combine(Pasta, g.Id + ".json");
        var temp = ficheiro + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(g, Json));
        File.Move(temp, ficheiro, overwrite: true);
    }

    [GeneratedRegex(@"^\d{17}_[0-9a-f]{32}$")]
    private static partial Regex IdRegex();
}
