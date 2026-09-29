namespace PortalOrdemMss.Web.Services;

/// <summary>
/// Pasta persistente fora da pasta de instalação (mesmo padrão do Portal de
/// Encomendas Rápidas): publicar uma versão nova nunca mexe na configuração.
/// </summary>
public static class AppPaths
{
    public static string ResolveRoot(IConfiguration configuration)
    {
        var configured = configuration["Storage:BasePath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MSS", "PortalOrdemMss");
        }

        // Só para desenvolvimento fora de Windows.
        return Path.Combine(AppContext.BaseDirectory, "ProgramData-dev");
    }
}
