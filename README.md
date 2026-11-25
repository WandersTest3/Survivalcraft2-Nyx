# Survivalcraft2-Nyx

API própria para Survivalcraft 2, com **multiplayer integrado ao jogo** (não é um mod) e **tradução em português do Brasil**.

Base: código-fonte do Survivalcraft API 1.8 (projeto SC-SPM, versão 1.8.2.0), mantido em `Engine/`, `EntitySystem/`, `Survivalcraft.Windows/` (código do jogo, compartilhado com a versão Android), `Survivalcraft.Android/`, `Survivalcraft.Linux/` e `*.IOS/`. O projeto original não declara licença.

## O que foi adicionado

| Caminho | Descrição |
|---|---|
| `Survivalcraft.Windows/Multiplayer/` | Multiplayer TCP (host/cliente), compilado dentro do assembly `Survivalcraft` |
| `Survivalcraft.Windows/Content/Assets/Lang/pt-BR.json` | Tradução completa (2814 chaves, mesma estrutura do `en-US.json`) |
| `tests/` | Teste de rede em loopback (sem o jogo) |
| `tools/` | Extração das DLLs de um APK e verificação de compilação |
| `.github/workflows/build-apk.yml` | Compila o APK no GitHub Actions |

## Idioma

O jogo lista os idiomas pelos arquivos de `Lang/`. O `pt-BR` entra na lista automaticamente. Sem idioma salvo, o jogo usa o idioma do sistema e, se ele não existir, `en-US`.

## Multiplayer

O `NyxLoader` é uma subclasse de `ModLoader` dentro do próprio assembly do jogo, e o `SurvivalCraftModEntity` a registra sozinho ao carregar. Não precisa instalar nenhum mod.

Modelo: um jogador é o **host** (servidor dentro do jogo) e os demais são **clientes**, via TCP. Configuração em `nyx_multiplayer.json`, criado na pasta de dados do usuário (`ModsManager.UserDataPath`) ao abrir um mundo:

```json
{
  "Mode": "Off",
  "Address": "127.0.0.1",
  "Port": 25565,
  "Name": ""
}
```

- `Mode`: `Off`, `Host` ou `Client`.
- Host: `Mode: "Host"` e abra o mundo; libere a porta no roteador para jogar pela internet.
- Cliente: `Mode: "Client"`, `Address` = IP do host, e abra um mundo com a **mesma semente**.

Sincroniza posição e rotação dos jogadores (caixa roxa com nome), alterações de blocos (nos dois sentidos, retransmitidas pelo host) e avisos de entrada e saída. A conexão é recusada se a semente ou a versão do protocolo forem diferentes.

### Limitações

- Alterações de blocos anteriores à conexão não são enviadas.
- Criaturas, itens no chão, hora do dia, clima e inventários não são sincronizados.
- Sem chat, autenticação nem criptografia.
- Jogadores remotos aparecem como caixas.

## Compilar

Pelo GitHub Actions: rode o workflow **Build APK**; o APK sai como artefato. O workflow cria o `Content.zip` a partir de `Survivalcraft.Windows/Content` e compila `Survivalcraft.Android` (net10.0-android).

Localmente é necessário o SDK .NET 10 com o workload `android`, além de gerar `Survivalcraft.Windows/Content.zip`.

## Verificação feita

- Todo o código de `Survivalcraft.Windows` (incluindo `Multiplayer/`) foi compilado com o Roslyn contra as DLLs de um APK do API 1.80: os únicos erros são os mesmos 12 que já existem sem o multiplayer (pacote NuGet.Versioning e um tipo do Engine que o APK antigo não tem).
- A camada de rede passou no teste de loopback em `tests/`.
- **Não testado:** build completo do APK, execução no Android e sincronização dentro do jogo.

## Licença

A definir pelo autor.
