using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace PortalOrdemMss.Web.Services;

/// <summary>
/// Resolve o URL da foto de um artigo (padrão do Portal de Encomendas
/// Rápidas): 1) valor da coluna Imagem se for um URL http(s); 2) valor da
/// coluna Imagem (ex. CDU_MTImagem) como nome de ficheiro na pasta
/// configurada; 3) código do artigo como nome de ficheiro; 4) placeholder.
/// Nunca falha.
/// </summary>
public sealed class ProductImageService(IOptionsMonitor<PortalOptions> options)
{
    public const string Placeholder = "/img/placeholder.svg";
    public const string RequestPath = "/imagens-artigos";

    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    // A pasta é muitas vezes uma partilha de rede: sem cache, cada pesquisa
    // faria várias chamadas SMB síncronas por artigo. Uma foto nova demora
    // no máximo o TTL a aparecer.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (DateTime FetchedAtUtc, string Url)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string ResolveImageUrl(string imagem, string codigo)
    {
        imagem = imagem.Trim();
        if (imagem.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || imagem.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return imagem;
        }

        var url = imagem.Length > 0 ? ResolveStem(imagem) : Placeholder;
        return url != Placeholder ? url : ResolveStem(codigo);
    }

    private string ResolveStem(string stem)
    {
        var safe = SanitizeFileName(stem);
        if (safe.Length == 0)
        {
            return Placeholder;
        }

        if (_cache.TryGetValue(safe, out var cached) && DateTime.UtcNow - cached.FetchedAtUtc < Ttl)
        {
            return cached.Url;
        }

        var url = ResolveUncached(safe);
        _cache[safe] = (DateTime.UtcNow, url);
        return url;
    }

    private string ResolveUncached(string safe)
    {
        try
        {
            var folder = Path.GetFullPath(options.CurrentValue.ProductImageFolder);
            if (!Directory.Exists(folder))
            {
                return Placeholder;
            }

            foreach (var ext in AllowedExtensions)
            {
                var full = Path.GetFullPath(Path.Combine(folder, safe + ext));
                if (full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                {
                    return $"{RequestPath}/{Uri.EscapeDataString(safe + ext)}";
                }
            }
        }
        catch (Exception)
        {
            // Partilha de rede indisponível: mostra o placeholder, não parte a pesquisa.
        }

        return Placeholder;
    }

    // Nome vindo do Primavera nunca é confiável: tira separadores e pontos
    // para não sair da pasta configurada.
    internal static string SanitizeFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = input.Where(c => !invalid.Contains(c) && c != '.' && c != '/' && c != '\\');
        return new string(chars.ToArray()).Trim();
    }
}
