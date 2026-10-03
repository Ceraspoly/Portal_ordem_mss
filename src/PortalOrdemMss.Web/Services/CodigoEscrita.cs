using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PortalOrdemMss.Web.Services;

public enum ResultadoCodigo { Certo, Errado, Bloqueado }

/// <summary>
/// Código pedido ao gravar a ordem. No settings.json só fica o hash
/// (Portal:CodigoEscritaHash = "pbkdf2-sha256$iteracoes$salt$hash", em
/// base64), nunca o código. Depois de 5 tentativas erradas o IP fica
/// bloqueado 15 minutos.
/// </summary>
public sealed class CodigoEscrita(TimeProvider relogio)
{
    public const int MaxFalhas = 5;
    public static readonly TimeSpan Bloqueio = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (int Falhas, DateTimeOffset Ate)> _falhas = new();

    public ResultadoCodigo Verificar(string hashConfigurado, string? codigo, string ip)
    {
        var agora = relogio.GetUtcNow();
        if (_falhas.TryGetValue(ip, out var f) && f.Falhas >= MaxFalhas && f.Ate > agora)
        {
            return ResultadoCodigo.Bloqueado;
        }

        if (!string.IsNullOrEmpty(codigo) && Confere(hashConfigurado, codigo))
        {
            _falhas.TryRemove(ip, out _);
            return ResultadoCodigo.Certo;
        }

        _falhas.AddOrUpdate(ip,
            _ => (1, agora + Bloqueio),
            (_, atual) => (atual.Ate <= agora ? 1 : atual.Falhas + 1, agora + Bloqueio));
        return _falhas[ip].Falhas >= MaxFalhas ? ResultadoCodigo.Bloqueado : ResultadoCodigo.Errado;
    }

    public static bool HashValido(string? hash) => hash is not null && Partes(hash) is not null;

    public static string CriarHash(string codigo, int iteracoes = 210_000)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(codigo), salt, iteracoes, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${iteracoes}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    internal static bool Confere(string hashConfigurado, string codigo)
    {
        if (Partes(hashConfigurado) is not var (iteracoes, salt, esperado))
        {
            return false;
        }

        var calculado = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(codigo), salt, iteracoes, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    private static (int Iteracoes, byte[] Salt, byte[] Hash)? Partes(string hash)
    {
        var p = hash.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2-sha256" || !int.TryParse(p[1], out var it) || it < 100_000)
        {
            return null;
        }

        try
        {
            var salt = Convert.FromBase64String(p[2]);
            var h = Convert.FromBase64String(p[3]);
            return salt.Length >= 16 && h.Length >= 32 ? (it, salt, h) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
