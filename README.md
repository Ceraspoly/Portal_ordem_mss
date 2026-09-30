# Portal Ordem MSS — catálogo de artigos

Catálogo com o que foi pedido: **foto do artigo (200×200), nome, família e CDU_MSS_Ordem**. Permite arrastar artigos para mudar a ordem, gravando o CDU_MSS_ORDEM no Primavera (única escrita). Feito a partir dos padrões do Portal de Encomendas Rápidas (`Ceraspoly/portal-encomendas-rapidas`), sem preços, clientes, carrinho nem login.

## Estado

- Compila com o SDK .NET 10 (`dotnet build -c Release`, 0 avisos, 0 erros).
- 31 testes a passar (`dotnet test`), incluindo a API em modo demonstração e a regra de reordenação.
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

As queries por omissão leem `PRIMSS2CLO.dbo.Artigo` + `Familias`, excluem artigos anulados (`ArtigoAnulado`, `CDU_PS_ANULAR`) e ordenam por família e `CDU_MSS_ORDEM`. Só são aceites queries `SELECT` (`QuerySafety`) e os valores vão sempre como parâmetros. A única escrita é a da ordem (ver abaixo).

## Mudar a ordem (arrastar e largar)

1. Escolher uma família (sem texto na pesquisa) e carregar em **Ordenar**.
2. Arrastar os artigos para a nova posição. Os que mudaram ficam com contorno.
3. **Guardar ordem**, confirmar.

Regra de gravação (`Services/OrdemPlanner.cs`): **só o artigo arrastado muda**, e fica com o CDU_MSS_ORDEM do artigo que ficou antes dele mais um `a` (ex. arrastar `A55023` para depois de `B45223` grava `B45223A`; mantém maiúsculas se o valor só tiver maiúsculas).
- Se esse valor já existir no seguinte (ex. já há `B45223A`), usa `B452230`, que fica entre os dois.
- Arrastado para o primeiro lugar: baixa o último carácter do primeiro artigo e acrescenta `z` (`A55023` → `A55022Z`).
- Sem espaço possível (ex. dois artigos com o mesmo valor à volta): recusa e explica.

Segurança da escrita no Primavera:
- A única escrita é `Sql:AtualizarOrdemQuery` (por omissão `UPDATE PRIMSS2CLO.dbo.Artigo SET CDU_MSS_ORDEM = @Ordem WHERE Artigo = @Codigo AND ISNULL(CDU_MSS_ORDEM, '') = @OrdemAnterior`). Só é aceite um único UPDATE que contenha `CDU_MSS_ORDEM`, `@Ordem`, `@Codigo` e `@OrdemAnterior`.
- Tudo numa transação: cada UPDATE tem de afetar exatamente 1 linha; se o valor mudou entretanto (0 linhas) ou a query apanha mais de uma, nada é gravado.
- Cada alteração fica registada em `%ProgramData%\MSS\PortalOrdemMss\data\alteracoes-ordem.csv` (data; artigo; ordem anterior; ordem nova), para poder desfazer à mão.
- Só aceita gravar a partir do próprio servidor, ou com a chave da variável de ambiente `MSS_PORTAL_ORDEM_ADMIN_KEY` no cabeçalho `X-Admin-Key`. `Portal:PermitirReordenar: false` desliga a função.
- O utilizador SQL da connection string precisa de permissão de UPDATE na tabela `Artigo`.
- O Portal de Encomendas Rápidas também ordena por CDU_MSS_ORDEM, por isso a nova ordem aparece lá também.

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
