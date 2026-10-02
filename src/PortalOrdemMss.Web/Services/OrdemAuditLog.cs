using System.Text;

namespace PortalOrdemMss.Web.Services;

/// <summary>
/// Regista cada alteração gravada (data;codigo;anterior;novo) num CSV em
/// ProgramData, para se poder ver ou desfazer à mão o que o portal mudou no
/// Primavera. Antes de cada gravação guarda também uma cópia em texto de
/// todos os CDU_MSS_ORDEM da família (pedido do Bruno, 2026-10-02).
/// </summary>
public sealed class OrdemAuditLog(string path)
{
    private static readonly object Gate = new();

    public string Path { get; } = path;

    public string PastaHistorico => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "historico");

    /// <summary>
    /// Escreve um .txt (abre no Bloco de Notas) com os valores atuais de toda
    /// a família, lidos do Primavera, e as alterações pedidas. Se falhar,
    /// lança exceção e não se grava nada.
    /// </summary>
    public string GuardarCopia(string familia, IReadOnlyList<Artigo> atuais, IReadOnlyList<AlteracaoOrdem> alteracoes, string origem)
    {
        var agora = DateTime.Now;
        var nomeFamilia = new string((familia.Length == 0 ? "sem-familia" : familia)
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        var ficheiro = System.IO.Path.Combine(PastaHistorico, $"{agora:yyyy-MM-dd_HHmmss_fff}_{nomeFamilia}.txt");

        var sb = new StringBuilder();
        sb.AppendLine("Cópia de segurança do CDU_MSS_ORDEM, feita antes de gravar");
        sb.AppendLine($"Data: {agora:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Origem: {origem}");
        sb.AppendLine($"Família: {(familia.Length == 0 ? "(não indicada)" : familia)}");
        sb.AppendLine();
        sb.AppendLine($"Alterações a gravar ({alteracoes.Count}): Artigo;OrdemAnterior;OrdemNova");
        foreach (var a in alteracoes)
        {
            sb.Append(Csv(a.Codigo)).Append(';').Append(Csv(a.Anterior)).Append(';').Append(Csv(a.Novo)).AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine($"Valores atuais de toda a família no Primavera ({atuais.Count} artigos): Artigo;CDU_MSS_ORDEM;Nome");
        foreach (var a in atuais)
        {
            sb.Append(Csv(a.Codigo)).Append(';').Append(Csv(a.Ordem)).Append(';').Append(Csv(a.Nome)).AppendLine();
        }

        lock (Gate)
        {
            Directory.CreateDirectory(PastaHistorico);
            File.WriteAllText(ficheiro, sb.ToString(), new UTF8Encoding(true));
        }

        return ficheiro;
    }

    public void Registar(IReadOnlyList<AlteracaoOrdem> alteracoes, string origem)
    {
        var agora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var sb = new StringBuilder();
        foreach (var a in alteracoes)
        {
            sb.Append(agora).Append(';').Append(Csv(a.Codigo)).Append(';').Append(Csv(a.Anterior))
              .Append(';').Append(Csv(a.Novo)).Append(';').Append(Csv(origem)).AppendLine();
        }

        lock (Gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (!File.Exists(Path))
            {
                File.WriteAllText(Path, "Data;Artigo;OrdemAnterior;OrdemNova;Origem" + Environment.NewLine, new UTF8Encoding(true));
            }

            File.AppendAllText(Path, sb.ToString(), Encoding.UTF8);
        }
    }

    private static string Csv(string s) =>
        s.Contains(';') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
