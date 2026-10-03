# Auditoria de segurança — Portal Ordem MSS

Data: 2026-10-03. Âmbito: todo o repositório `Ceraspoly/Portal_ordem_mss` (API ASP.NET Core, front-end em JS simples, script `tools/ATUALIZAR-PORTAL.ps1`), na configuração em uso: serviço Windows no servidor 192.168.1.169, aberto à rede local com `-Rede` e com permissão para gravar a partir de IPs privados.

## 1. Resumo de risco

**Antes das correções: Médio. Depois: Baixo** (2.ª ronda, 2026-10-03: código obrigatório para gravar).

O ponto mais forte do portal é a escrita no Primavera. Só existe um UPDATE, parametrizado e dentro de uma transação, e cada linha tem de bater com o valor anterior (senão nada é gravado). Antes de cada gravação fica uma cópia em texto e, depois, um registo CSV.

O que impedia o risco baixo era não haver login: qualquer pessoa na rede local podia alterar o CDU_MSS_ORDEM. Agora gravar pede um código (A1). Ver o portal continua aberto a toda a rede, porque são só nomes, famílias e fotos de artigos.

## 2. Vulnerabilidades encontradas (por gravidade)

| # | Gravidade | Ponto | O que era | Impacto | Estado |
|---|---|---|---|---|---|
| A1 | Média | 5, 7, 8 | Sem autenticação para gravar: com `PermitirEscritaNaRede`, qualquer IP privado grava (`IsWriteAllowed`, Program.cs). | Qualquer pessoa ou PC infetado na rede pode baralhar a ordem do catálogo (reversível pelas cópias). | **Corrigido**: código obrigatório (hash PBKDF2), bloqueio após 5 erradas. |
| A2 | Média | 10, 19 | Proteção contra pedidos de outros sites dependia só do browser exigir `Content-Type: application/json`. Sem verificação de `Origin` nem do nome do servidor: com **DNS rebinding**, uma página maliciosa aberta num PC da rede podia gravar como se fosse do próprio portal. | Alteração da ordem sem o utilizador saber. | **Corrigido** |
| A3 | Média | 7, 1 | A connection string (com password do SQL) fica em `C:\ProgramData\MSS\PortalOrdemMss\config\settings.json`, que por omissão qualquer utilizador do servidor consegue ler (também nas cópias `.bak`). | Quem tiver conta no servidor lê a password do SQL e acede ao Primavera diretamente. | **Corrigido** no script (permissões); ver R2. |
| A4 | Baixa | 3, 4 | Os parâmetros SQL têm tamanho fixo (`NVarChar 100`); um código ou ordem maior era **cortado em silêncio** pelo SqlClient. Listas sem limite de tamanho no pedido de ordem (o cálculo de fallback é O(n²)). | Gravar um valor diferente do mostrado; pedido enorme a ocupar o CPU. | **Corrigido** |
| A5 | Baixa | 15, 8 | Sem limite de pedidos; a chave `X-Admin-Key` podia ser tentada sem limite. | Força bruta da chave, sobrecarga do SQL. | **Corrigido** |
| A6 | Baixa | 19 | Sem cabeçalhos de segurança (CSP, `X-Frame-Options`, `nosniff`). | Clickjacking (portal metido noutra página). XSS só se o código mudar no futuro: hoje todo o texto vai para a página com `textContent` e não há `innerHTML`. | **Corrigido** |
| A7 | Informativo | 12 | Cabeçalho `Server: Kestrel`. Os erros 500 já eram genéricos em produção (o detalhe vai só para o log). | Revela a tecnologia do servidor. | **Corrigido** |
| A8 | Informativo | 13 | `dotnet list package --vulnerable`: **sem pacotes vulneráveis**. `Hosting.WindowsServices` 8.0.1 estava desatualizado; `Microsoft.Data.SqlClient` 5.2.2 tem versões mais recentes (7.x) mas sem vulnerabilidade conhecida. | — | WindowsServices atualizado para 10.0.12; SqlClient ver R4. |

Sem problemas nos restantes pontos:
- **1 (segredos no repositório):** nenhum. `appsettings.json` não tem password e o histórico do git foi verificado.
- **2 (validação só no front-end):** o servidor recalcula sempre a ordem e volta a validar tudo; o que vem do browser nunca é gravado tal e qual.
- **4 (SQL injection):** todos os valores vão como parâmetros. As queries configuráveis só aceitam `SELECT` (`QuerySafety`), e a de gravação só aceita um UPDATE com os parâmetros certos.
- **6 (IDOR):** não há utilizadores nem recursos de outros utilizadores.
- **7 (passwords guardadas):** o portal não guarda passwords de utilizadores. Só existe a password do SQL (ver A3).
- **9 (envio duplicado):** o botão desativa-se durante o pedido. No servidor, o `WHERE ... = @OrdemAnterior` faz um segundo envio igual dar 409, sem gravar duas vezes.
- **11 (upload):** não existe upload.
- **14 (tokens):** não há tokens.
- **16 (dados pessoais em logs):** os logs só têm o IP e os códigos de artigo, sem dados pessoais.
- **17 (SSRF):** o servidor nunca faz pedidos a URLs. O URL de uma foto vindo do Primavera é aberto pelo browser, não pelo servidor. Na pasta de fotos, o nome do ficheiro é limpo e confirma-se que fica dentro da pasta.
- **18 (cookies):** o portal não usa cookies nem sessão.
- **19 (CORS):** não há CORS configurado, por isso outros sites não podem ler as respostas.

## 3. Código corrigido (commit no `main`)

**A2: CSRF e DNS rebinding** (`Program.cs`, middleware antes dos endpoints). Todos os POST em `/api` são recusados com 403 se:
- o nome usado para abrir o portal não for um IP, `localhost`, o nome deste servidor ou um dos `Portal:HostsPermitidos`; ou
- o browser mandar um `Origin` diferente do próprio portal.

```csharp
var hostConhecido = IPAddress.TryParse(nome, out _) || nome == "localhost"
    || nome == Environment.MachineName || opts.HostsPermitidos.Contains(nome);
...
return Uri.TryCreate(origin, UriKind.Absolute, out var o) && o.Authority == host.Value;
```

Porquê: no DNS rebinding o browser julga estar no "mesmo site", mas o nome do servidor no pedido é o domínio do atacante. Validar o nome fecha essa porta.

**A3: password do SQL** (`tools/ATUALIZAR-PORTAL.ps1`). A pasta `config` passa a ser só do sistema e dos Administradores:

```powershell
icacls $pastaConfigAcl /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" /T /Q
```

Usa SIDs em vez de nomes, para funcionar com o Windows em português.

**A4: validação no servidor** (`ValidarPedido` em `Program.cs`). Máximo de 5000 artigos por pedido, códigos não vazios e textos até 100 caracteres (o tamanho dos parâmetros SQL). Se a nova ordem calculada passar de 100 caracteres, recusa em vez de a cortar.

**A5: limites de pedidos** (`AddRateLimiter`), por IP: 1200 pedidos/minuto à API e 20 gravações/minuto. Acima disso, responde 429 em JSON.

**A6/A7: cabeçalhos e Kestrel.** Todas as respostas levam:
- `Content-Security-Policy: default-src 'self'; img-src 'self' data: http: https:; script-src 'self'; ... frame-ancestors 'none'`
- `X-Frame-Options: DENY`
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: no-referrer`

Além disso, `AddServerHeader = false` e os pedidos estão limitados a 4 MB.

Testes: 39 a passar, incluindo novos para Origin de outro site (403), nome de servidor desconhecido (403), pedido com 5001 artigos e texto com 101 caracteres (400) e presença dos cabeçalhos. Testado no browser em modo demonstração, sem erros de CSP.

**A1: código para gravar** (`Services/CodigoEscrita.cs`, `/api/ordem`, `app.js`):
- Com `Portal:CodigoEscritaHash` definido, todas as gravações pedem o código (cabeçalho `X-Codigo-Escrita`).
- No `settings.json` só fica `pbkdf2-sha256$210000$<salt>$<hash>` (PBKDF2-SHA256, salt aleatório de 16 bytes). A comparação é feita em tempo constante.
- 5 tentativas erradas bloqueiam esse IP 15 minutos e ficam no log.
- Os PCs da rede **só** podem gravar se houver código definido; sem código, só o próprio servidor grava.
- O código é criado com `tools\ATUALIZAR-PORTAL.ps1 -DefinirCodigo`, ou pedido automaticamente com `-Rede` se ainda não existir. O script passa-o ao portal pelo stdin, nunca pela linha de comandos.
- No browser o código fica só em memória enquanto a página está aberta.

**R2: utilizador SQL mínimo**: `tools/SQL-UTILIZADOR-PORTAL.sql` cria um login só com `SELECT` em `Artigo`/`Familias` e `UPDATE` só na coluna `CDU_MSS_ORDEM`. É para correr à mão no SSMS, por quem administra o SQL Server.

Testes: 42 a passar (novos: hash, bloqueio após 5 erradas, gravar sem/errado/certo).

## 4. Recomendações finais pré-deploy

1. **Atualizar o servidor:** `git pull` e `tools\ATUALIZAR-PORTAL.ps1 -Rede`. Isto aplica as correções e as novas permissões da pasta `config`.
2. **R1: código para gravar.** Feito (A1). Partilhar o código só com quem deve ordenar; para mudar, `-DefinirCodigo`.
3. **R2: utilizador SQL com o mínimo de permissões.** Correr `tools/SQL-UTILIZADOR-PORTAL.sql` e pôr esse utilizador na connection string. Enquanto o portal usar um utilizador com acesso total, quem conseguir a password consegue mexer em todo o Primavera. **Este é o passo que falta para o risco ficar baixo também do lado da base de dados.**
4. **R3: sem HTTP público.** Risco residual aceite: o código vai em HTTP simples dentro da rede local (alguém a escutar a rede podia apanhá-lo). O portal é HTTP simples. Manter a regra de firewall só em perfis Domínio/Privado (é o que o script faz) e nunca o publicar para a internet. Se um dia for preciso, pôr HTTPS à frente (IIS/reverse proxy).
5. **R4: dependências.** Correr `dotnet list package --vulnerable` de vez em quando. Atualizar o `Microsoft.Data.SqlClient` para 6.x/7.x numa próxima versão, com testes contra o Primavera.
6. **Cópias de segurança:** incluir `C:\ProgramData\MSS\PortalOrdemMss\data\historico` no backup do servidor.
