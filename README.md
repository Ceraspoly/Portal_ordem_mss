# Portal Ordem MSS — catálogo de artigos

Catálogo só de leitura com o que foi pedido: **foto do artigo (200×200), nome, família e CDU_MSS_Ordem**. Feito a partir dos padrões do Portal de Encomendas Rápidas (`Ceraspoly/portal-encomendas-rapidas`), sem preços, clientes, carrinho, login nem escrita no ERP.

## Estado

- Compila com o SDK .NET 10 (`dotnet build -c Release`, 0 avisos, 0 erros).
- 16 testes a passar (`dotnet test`), incluindo a API em modo demonstração.
- Testado em modo demonstração no browser (desktop e telemóvel): a foto fica sempre em 200×200.
- **Ainda não testado contra o SQL Server do Primavera** nem publicado em Windows.

## Correr em desenvolvimento

```powershell
cd src/PortalOrdemMss.Web
dotnet run -c Release
```
Abre `http://127.0.0.1:5090`. Arranca em modo demonstração (12 artigos fictícios). Saúde: `/health`.

## Configuração (duas camadas, como no portal de encomendas)

- `src/PortalOrdemMss.Web/appsettings.json` só tem os valores por omissão.
- A configuração real fica em `%ProgramData%\MSS\PortalOrdemMss\config\settings.json` e é relida sem reiniciar. Exemplo:

```json
{
  "Portal": {
    "DemoMode": false,
    "Titulo": "Catálogo Ceraspoly",
    "ListenUrl": "http://0.0.0.0:5090",
    "ProductImageFolder": "\\\\servidor\\partilha\\imagens"
  },
  "Sql": {
    "ConnectionString": "Server=192.168.1.170\\PRIMAVERA;Database=PRIMSS2CLO;User Id=...;Password=...;TrustServerCertificate=True"
  }
}
```

| Chave | Para quê |
|---|---|
| `Portal:DemoMode` | `true` = dados fictícios; `false` = Primavera |
| `Portal:ProductImageFolder` | Pasta das fotos (pode ser UNC) |
| `Portal:PageSize` | Artigos por página ("Carregar mais") |
| `Sql:ArtigosQuery` | Tem de devolver `Codigo, Nome, Familia, FamiliaNome, Imagem, Ordem`; pode usar `@Search, @LikeSearch, @Familia, @Offset, @Limit` |
| `Sql:FamiliasQuery` | Tem de devolver `Codigo, Nome` |

As queries por omissão leem `PRIMSS2CLO.dbo.Artigo` + `Familias`, excluem artigos anulados (`ArtigoAnulado`, `CDU_PS_ANULAR`) e ordenam por família e `CDU_MSS_ORDEM`. Só são aceites queries `SELECT` (`QuerySafety`) e os valores vão sempre como parâmetros. Usar um utilizador SQL só de leitura.

## Fotos

Ordem de procura (`ProductImageService`):
1. Coluna `Imagem` (`CDU_MTImagem`) se for um URL `http(s)`.
2. Coluna `Imagem` como nome de ficheiro na pasta configurada (`.jpg`, `.jpeg`, `.png`, `.webp`).
3. Código do artigo como nome de ficheiro.
4. `/img/placeholder.svg`.

O tamanho 200×200 é feito no browser (`object-fit: contain`, sem deformar), tal como no portal de encomendas. Se as fotos originais forem muito pesadas, o passo seguinte é gerar miniaturas no servidor.

## Pôr em produção (Windows, ainda por validar)

```powershell
dotnet publish src/PortalOrdemMss.Web -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "C:\Program Files\MSS\PortalOrdemMss\versions\1.0.0"
sc.exe create MssPortalOrdem binPath= "C:\Program Files\MSS\PortalOrdemMss\versions\1.0.0\PortalOrdemMss.Web.exe" start= delayed-auto
sc.exe failure MssPortalOrdem reset= 86400 actions= restart/5000/restart/10000/restart/60000
Start-Service MssPortalOrdem
```
- `delayed-auto` porque a pasta de fotos em rede pode ainda não estar disponível logo após um reboot (problema real no portal de encomendas). O arranque também não falha se a pasta não existir.
- O serviço corre como LocalSystem: confirmar que essa conta consegue ler a partilha das fotos.
- Os scripts `PUBLICAR-PRODUCAO.ps1` e `WATCHDOG-PORTAL.ps1` do portal de encomendas podem ser adaptados quando for para produção (guardar os `.ps1` em UTF-8 com BOM).

## Estrutura

```
src/PortalOrdemMss.Web/
  Program.cs                  endpoints /api/config, /api/familias, /api/artigos, /health
  Models.cs                   Artigo, Familia, PortalOptions, SqlOptions
  Services/
    DynamicArtigoProvider.cs  escolhe demo ou SQL em cada pedido
    DemoArtigoProvider.cs     dados fictícios
    SqlArtigoProvider.cs      Primavera (só SELECT, parametrizado)
    ProductImageService.cs    resolução da foto
    QuerySafety.cs            recusa tudo o que não seja SELECT
    AppPaths.cs               pasta persistente em ProgramData
  wwwroot/                    index.html, css/site.css, js/app.js (sem build)
src/PortalOrdemMss.Web.Tests/ xUnit
```
