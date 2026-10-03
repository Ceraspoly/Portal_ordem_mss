# Portal Ordem MSS — catálogo de artigos

Catálogo com o que foi pedido: **foto do artigo (100×100), nome, família e CDU_MSS_Ordem**. Permite arrastar artigos para mudar a ordem, gravando o CDU_MSS_ORDEM no Primavera (única escrita). Feito a partir dos padrões do Portal de Encomendas Rápidas (`Ceraspoly/portal-encomendas-rapidas`), sem preços, clientes, carrinho nem login.

## Estado

- Compila com o SDK .NET 10 (`dotnet build -c Release`, 0 avisos, 0 erros).
- 33 testes a passar (`dotnet test`), incluindo a API em modo demonstração e a regra de reordenação.
- Testado em modo demonstração no browser (desktop e telemóvel), incluindo o modo de ordenação em lista.
- Já corre no servidor da CERASPOLY com os dados reais do Primavera (leitura). O script de serviço `tools\ATUALIZAR-PORTAL.ps1` ainda não foi executado em Windows.

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
| `Sql:LojaQuery` | Códigos (`Codigo`) dos artigos com rebordo vermelho. Por omissão: os que têm o visto em `CDU_ArtigoLoja`. Se a query falhar, o portal continua a funcionar, só sem rebordos (aviso no log). |
| `Sql:ArtigosQuery` (opcional) | Se devolver também uma coluna `Loja` (ex. `a.CDU_xxx AS Loja`), os artigos com o visto ficam com rebordo vermelho. Os artigos mudados e ainda por gravar ficam a azul. |
| `Sql:FamiliasQuery` | Tem de devolver `Codigo, Nome` |

As queries por omissão leem `PRIMSS2CLO.dbo.Artigo` + `Familias`, excluem artigos anulados (`ArtigoAnulado`, `CDU_PS_ANULAR`) e ordenam só por `CDU_MSS_ORDEM` (depois pelo código), independentemente da família. Só são aceites queries `SELECT` (`QuerySafety`) e os valores vão sempre como parâmetros. A única escrita é a da ordem (ver abaixo).

## Mudar a ordem

1. Sem texto na pesquisa, carregar em **Ordenar**. Com uma família escolhida ordena-se só essa família; em "Todas as famílias" ordena-se o catálogo inteiro pelo CDU_MSS_ORDEM.
2. Os artigos aparecem todos de uma vez (até 5000; acima disso pede para escolher uma família), já sem "Carregar mais". Para mover um artigo:
   - arrastar a linha (a página desliza sozinha quando se chega ao topo ou ao fundo do ecrã);
   - botões ⤒ (topo), ↑ (subir), ↓ (descer), ⤓ (fim);
   - clicar na linha e usar as setas ↑ ↓, `Home` e `End`.
   - **↪ A seguir a…**: escrever o código ou parte do nome de outro artigo e o artigo vai logo para a seguir a esse (ou "Para o início").
   - **Vários de uma vez**: Ctrl+clique para escolher vários, Shift+clique para escolher um intervalo (Esc limpa). Arrastar, os botões, as setas e "A seguir a…" movem os selecionados todos juntos, pela ordem em que estavam. Cada um fica com o valor do anterior + `a` (`0040a`, `0040aa`…).
   - **Ordenar seleção por nome**: põe os selecionados por ordem alfabética nos lugares que já ocupavam (números pela ordem natural: "7 cm" antes de "20 cm"). Aparece em pré-visualização; só grava com "Guardar ordem".
   - **Desempatar iguais**: artigos seguidos com o mesmo valor (ex. 10 com `ab011`) ficam `ab011a`, `ab011b`, … `ab011j` pela ordem do ecrã (depois do `z` continua `za`, `zb`…). Primeiro põem-se pela ordem certa (um artigo mudado dentro do grupo continua no grupo) e depois carrega-se no botão. Com artigos selecionados, só desempata os grupos desses; sem seleção, todos. Recusa se o último valor passasse à frente do artigo seguinte.
   No topo há **Quadrados / Lista** para mudar a vista (quadrados pequenos para arrastar, ou linhas com todos os botões); a escolha fica lembrada no browser.
   Cada artigo mudado fica com contorno e mostra logo a ordem nova (`Ordem 0010 → 0030a`), sem gravar.
3. **Guardar ordem**, confirmar.

Cada artigo mostra o número da posição na lista (1, 2, 3…). A caixa **Só loja** mostra só os artigos com o visto no CDU Loja (também no modo de ordenação, para ordenar só esses entre si).

Regra de gravação (`Services/OrdemPlanner.cs`): **só o artigo arrastado muda**, e fica com o CDU_MSS_ORDEM do artigo que ficou antes dele mais um `a` (ex. arrastar `A55023` para depois de `B45223` grava `B45223A`; mantém maiúsculas se o valor só tiver maiúsculas).
- Se esse valor já existir no seguinte (ex. já há `B45223A`), usa `B452230`, que fica entre os dois.
- Arrastado para o primeiro lugar: baixa o último carácter do primeiro artigo que se possa baixar e acrescenta `z` (`A55023` → `A55022Z`, `0020` → `001z`).
- Sem espaço possível (ex. anterior e seguinte ambos `CA0000A`): fica com o mesmo valor do anterior (no primeiro lugar, igual ao seguinte).

Segurança da escrita no Primavera:
- A única escrita é `Sql:AtualizarOrdemQuery` (por omissão `UPDATE PRIMSS2CLO.dbo.Artigo SET CDU_MSS_ORDEM = @Ordem WHERE Artigo = @Codigo AND ISNULL(CDU_MSS_ORDEM, '') = @OrdemAnterior`). Só é aceite um único UPDATE que contenha `CDU_MSS_ORDEM`, `@Ordem`, `@Codigo` e `@OrdemAnterior`.
- Tudo numa transação: cada UPDATE tem de afetar exatamente 1 linha; se o valor mudou entretanto (0 linhas) ou a query apanha mais de uma, nada é gravado.
- **Antes de cada gravação** é criada uma cópia em texto (abre no Bloco de Notas) em `%ProgramData%\MSS\PortalOrdemMss\data\historico\<data>_<família ou "todas">.txt`, com as alterações a gravar e o CDU_MSS_ORDEM atual de **todos** os artigos da família (ou do catálogo inteiro), lido do Primavera nesse momento. Se a cópia não puder ser escrita, nada é gravado. Estes ficheiros nunca são apagados pelo portal.
- **Histórico (botão no topo):** mostra as últimas 15 gravações (data, família, de que PC, artigos mudados) com **Reverter**: os artigos dessa gravação voltam à ordem que tinham antes. Só reverte se nenhum desses artigos tiver mudado depois (senão pede para reverter primeiro as mais recentes); pede o código como gravar e faz cópia antes. Cada gravação fica em `data\gravacoes\*.json` (nunca apagados).
- Depois de gravar, cada alteração fica também em `%ProgramData%\MSS\PortalOrdemMss\data\alteracoes-ordem.csv` (data; artigo; ordem anterior; ordem nova; origem), para poder desfazer à mão.
- Só aceita gravar a partir do próprio servidor, ou com a chave da variável de ambiente `MSS_PORTAL_ORDEM_ADMIN_KEY` no cabeçalho `X-Admin-Key`. `Portal:PermitirReordenar: false` desliga a função.
- Para abrir e ordenar a partir de outro PC da rede: correr o script com `-Rede`. Põe `Portal:ListenUrl` = `http://0.0.0.0:5090` e `Portal:PermitirEscritaNaRede` = `true` no `settings.json` (com cópia `settings.json.bak-<data>` antes) e abre a porta na firewall. Só aceita gravar de IPs privados (10.x, 172.16-31.x, 192.168.x); qualquer pessoa nessa rede passa a poder gravar a ordem. Para voltar a só ver na rede, pôr `PermitirEscritaNaRede` a `false` (relido sem reiniciar).
- O utilizador SQL da connection string precisa de permissão de UPDATE na tabela `Artigo`.
- Proteções da API (ver `docs/AUDITORIA_SEGURANCA.md`): pedidos POST de outros sites (CSRF) ou com um nome de servidor desconhecido (DNS rebinding) são recusados; para usar um nome DNS próprio, acrescentá-lo a `Portal:HostsPermitidos`. Limite de 20 gravações e 1200 pedidos à API por minuto e por IP, pedidos até 4 MB, cabeçalhos CSP/X-Frame-Options/nosniff.
- O script de instalação deixa a pasta `config` (que tem a password do SQL) legível só pelo sistema e pelos Administradores.
- **Código para gravar**: com `Portal:CodigoEscritaHash` definido, "Guardar ordem" pede um código (5 erradas bloqueiam o IP 15 minutos). No `settings.json` só fica o hash. Definir/mudar com `tools\ATUALIZAR-PORTAL.ps1 -DefinirCodigo` (com `-Rede` é pedido automaticamente se ainda não existir). Sem código, os PCs da rede não conseguem gravar.
- `tools/SQL-UTILIZADOR-PORTAL.sql`: utilizador SQL só com leitura de `Artigo`/`Familias` e escrita na coluna `CDU_MSS_ORDEM` (correr à mão no SSMS).
- O Portal de Encomendas Rápidas também ordena por CDU_MSS_ORDEM, por isso a nova ordem aparece lá também.

## Fotos

Ordem de procura (`ProductImageService`):
1. Coluna `Imagem` (`CDU_MTImagem`) se for um URL `http(s)`.
2. Coluna `Imagem` como nome de ficheiro na pasta configurada (`.jpg`, `.jpeg`, `.png`, `.webp`).
3. Código do artigo como nome de ficheiro.
4. `/img/placeholder.svg`.

O tamanho 100×100 é feito no browser (`object-fit: contain`, sem deformar), tal como no portal de encomendas. Se as fotos originais forem muito pesadas, o passo seguinte é gerar miniaturas no servidor.

## Pôr em produção (serviço Windows)

Uma vez, e depois sempre que houver uma versão nova, num PowerShell **como Administrador**:

```powershell
cd C:\_dev\ordem
powershell -ExecutionPolicy Bypass -File tools\ATUALIZAR-PORTAL.ps1 -Rede
```
(sem `-Rede` o portal só abre no próprio servidor.)

O script (`tools\ATUALIZAR-PORTAL.ps1`) faz `git pull`, publica para `C:\Program Files\MSS\PortalOrdemMss\versions\<data-commit>`, pára o serviço `MssPortalOrdem`, aponta a junção `current` para a versão nova, cria o serviço se não existir e espera que `/health` responda. Depois disso o portal fica sempre ligado (também depois de reiniciar o servidor) e já não é preciso deixar nenhuma janela aberta.
- Antes da primeira vez, fechar a janela com `dotnet run` (Ctrl+C): o script recusa se a porta estiver ocupada.
- Precisa do runtime ASP.NET Core 10 no servidor (vem com o SDK que já lá está).
- O serviço arranca em `delayed-auto` (a pasta das fotos em rede pode não estar pronta logo após um reboot) e reinicia sozinho se falhar depois de arrancar.
- O serviço corre como LocalSystem: se as fotos estão numa partilha de rede (UNC), confirmar que a conta do computador tem leitura nessa partilha; senão as fotos aparecem como "sem foto".
- O `settings.json` em `%ProgramData%\MSS\PortalOrdemMss\config` é o mesmo que o `dotnet run` já usava; o script só lhe mexe com `-Rede`, e faz cópia antes.
- Voltar à versão anterior: `Stop-Service MssPortalOrdem`, `cmd /c rmdir "C:\Program Files\MSS\PortalOrdemMss\current"`, `New-Item -ItemType Junction -Path "C:\Program Files\MSS\PortalOrdemMss\current" -Target "<pasta em versions>"`, `Start-Service MssPortalOrdem`.
- `-Rede` (ou só `-AbrirFirewall`) cria a regra de entrada para a porta 5090 (perfis domínio/privado). No fim o script mostra o endereço a usar nos outros PCs (`http://<IP do servidor>:5090`).
- O serviço é independente do `MssPortalEncomendas` (nome, porta e pastas diferentes).

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
tools/ATUALIZAR-PORTAL.ps1   instalar/atualizar como serviço Windows
```
