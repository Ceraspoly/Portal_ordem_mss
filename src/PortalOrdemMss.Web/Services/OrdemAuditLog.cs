using System.Text;

namespace PortalOrdemMss.Web.Services;

/// <summary>
/// Regista cada alteração gravada (data;codigo;anterior;novo) num CSV em
/// ProgramData, para se poder ver ou desfazer à mão o que o portal mudou no
/// Primavera.
/// </summary>
public sealed class OrdemAuditLog(string path)
{
    private static readonly object Gate = new();

    public string Path { get; } = path;

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
