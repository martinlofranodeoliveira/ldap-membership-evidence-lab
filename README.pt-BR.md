# LDAP Membership Evidence Lab

**Versão de desenvolvimento 0.1.0.** Software original da [Lofrano Tecnologia](https://lofranotecnologia.com.br), licença MIT.

Observa grupos sintéticos aninhados em um OpenLDAP privado e explica caminhos mínimos, rotas duplicadas, componentes cíclicos, referências ausentes e limites atingidos. Não autentica pessoas, não autoriza operações e não concede acesso. Não permite configurar diretórios externos nem credenciais.

[English](README.md) · [Contrato detalhado](docs/contract.md) · [Projeto](docs/design.md)

## Instalação e preparação

Ambiente suportado: Linux x64, Ubuntu 24.04, SDK .NET 10.0.403, OpenLDAP 2.6 e ferramentas `slapd`, `slapadd`, `ldapmodify` e `ldapsearch`. O compilador C é necessário somente durante a compilação. A biblioteca utiliza Novell.Directory.Ldap.NETStandard 4.0.0; os pacotes de teste são apenas de desenvolvimento. Não existe dependência de modelos ou APIs em execução.

O Dockerfile prepara dependências sem iniciar um serviço LDAP do sistema e compila o supervisor nativo original. Apenas essa preparação precisa de rede externa:

```sh
docker build --tag ldap-membership-local .
docker run --rm --network none --cap-drop ALL --security-opt no-new-privileges --pids-limit 256 --memory 1g --cpus 2 --user 1000:1000 ldap-membership-local dotnet test --solution MembershipEvidence.sln --no-restore --configuration Release
docker run --rm --network none --cap-drop ALL --security-opt no-new-privileges --pids-limit 256 --memory 1g --cpus 2 --user 1000:1000 ldap-membership-local dotnet run --no-restore --project src/MembershipEvidence.Cli -- demo --assert
```

Em um ambiente já preparado, execute como usuário sem privilégios:

```sh
dotnet restore MembershipEvidence.sln --locked-mode
dotnet build MembershipEvidence.sln --no-restore --configuration Release
dotnet test --solution MembershipEvidence.sln --no-restore --configuration Release
dotnet run --no-restore --project src/MembershipEvidence.Cli -- demo --assert
dotnet run --no-restore --project src/MembershipEvidence.Cli -- demo --assert --json
```

Os arquivos `packages.lock.json` versionados foram gerados por resolução real do NuGet. Use restore bloqueado em builds normais; regenere e revise os locks somente ao atualizar dependências de forma intencional. Os testes usam Microsoft.Testing.Platform nativo do .NET 10, sem adaptador VSTest. Precisam de UID diferente de zero e rede loopback; `--network none` mantém loopback no contêiner.

## Saída de exemplo

O resumo legível é emitido em inglês:

```text
LDAP Membership Evidence Lab — development 0.1.0
Original: A, B, C; 3 groups; 6 edges. B: root → G1 → B (2 shortest paths).
Cycle: {G2, root}. Missing member: incomplete, no export.
Replacement: A, B, D; +1 / -1. Group cap: incomplete, no export.
ldapsearch: 3 groups, 6 total links, 4 below G1/G2. Stable exports: 2.
JSON:
```

Em seguida aparece o relatório JSON. `--json` produz somente JSON. Os campos incluem `principals: ["A", "B", "C"]`, `bPath: ["root", "G1", "B"]`, `bShortestPathCount: "2"` e `stableExports: 2`. O resumo determinístico omite UUIDs atribuídos pelo servidor e a época aleatória; as evidências reais preservam ambos. As gerações do demo existem apenas durante a vida dos fixtures.

## Exemplo de biblioteca

Adicione uma referência ao projeto `src/MembershipEvidence/MembershipEvidence.csproj`:

```csharp
using MembershipEvidence;

await using var fixture = await SyntheticFixture.Create();
var resultado = await fixture.ResolveGroup(SyntheticFixture.RootDn);
foreach (var principal in resultado.Principals)
    Console.WriteLine($"{principal.DisplayName}: {string.Join(" -> ", principal.Path)}");

var exportado = await fixture.ResolveAndExport(SyntheticFixture.RootDn);
if (exportado.Publication is { } geracao)
{
    byte[] json = await fixture.ReadEvidence(geracao.FileName);
    Console.WriteLine($"Bytes da evidência: {json.Length}");
}

var limitado = await fixture.ResolveAndExport(SyntheticFixture.RootDn, new Limits(Groups: 2));
Console.WriteLine(string.Join(", ", limitado.Errors)); // GROUP_CAP, sem exportação
```

`Apply` aceita apenas mutações sintéticas enumeradas. `ReplaceG2WithD` substitui todos os membros de G2 em uma única operação LDAP. `MembershipDifference.Compare` compara observações completas da mesma época por entryUUID, não pelo nome exibido.

## Decisões e limites

O servidor faz a comparação de DNs; não há normalização caseira nem conversão para minúsculas. Cada referência usa uma busca base-object. O cache por texto exato é só uma otimização; a identidade é entryUUID. DNs retornados e atributos Unicode são preservados. A análise do grafo escolhe caminhos mínimos com desempate ordinal pelos identificadores do fixture e relata componentes fortemente conexos, sem enumerar todos os ciclos.

Buscas LDAP comuns **não são snapshots**. A exportação estável exige época e revisão par iguais antes e depois da travessia, sob o protocolo cooperativo. A conferência final e a publicação sem substituição mantêm o mesmo `flock` usado pelo escritor. Todos os escritores precisam cooperar. Revisão ímpar após falha impede exportação até descartar o fixture e criar outro a partir da semente declarada com nova época. Não se presume que um lote incompleto foi confirmado.

- Tetos: 256 grupos, 1.024 principais, 4.096 referências de membros, **32 requisições LDAP sequenciais no total** e dois segundos monotônicos por observação. Só é permitido reduzir limites. O teto de requisições pode vencer primeiro; não se promete visitar 256 grupos em 32 buscas.
- As duas leituras da revisão, falhas de cache e referências ausentes consomem requisições. Não há referrals, aliases, URLs externas, filtros dinâmicos inseguros ou paralelismo ilimitado.
- Referências ausentes, limites e erros preservam resultados parciais conhecidos, marcam `complete=false` e impedem a geração de evidência.
- Apenas o esquema sintético documentado é suportado. Não há alegação de conformidade geral com todos os padrões LDAP.
- Os limites da aplicação não limitam todas as alocações nativas do servidor. O mapa MDB tem 16 MiB; utilize também limites do sistema operacional.
- Leituras TCP anônimas são exclusivas do laboratório sintético. A escrita local usa identidade de processo via LDAPI, sem senha e sem funcionalidade de login de usuário.
- O supervisor nativo retém o filho direto, termina a sinalização antes de coletá-lo e nunca tenta matar um PID depois da coleta. Processos bloqueados de forma ininterruptível pelo kernel e adulteração por outro processo com o mesmo UID estão fora da garantia.
- A publicação usa flush e rename sem substituição no mesmo sistema de arquivos. Não é um protocolo de durabilidade contra falta de energia nem uma assinatura. A limpeza remove somente a árvore privada após parar o servidor retido.

Consulte [ambiente](docs/environment.md), [dados sintéticos](docs/fixtures.md) e [segurança](SECURITY.md). Não há telemetria nem dados pessoais reais.
