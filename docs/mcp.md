# Lokální MCP příjem — kontrakt v1

Endpoint: `http://127.0.0.1:5178/mcp`, Streamable HTTP, oficiální C# MCP SDK 1.2.0,
bezstavový režim. Ověřeno s MCP `2025-11-25`: `initialize`,
`notifications/initialized`, `tools/list`, `tools/call`, `ping`. Klient potřebuje
Streamable HTTP s vlastní Authorization hlavičkou. Stdio ani starší `/sse` není
vystavené. Získávání historie a konfigurace webového propojení nejsou součástí příjmu.

## Konfigurace

Bez tokenu je MCP vypnuté (`404`). V prostředí procesu Mentoru nastavte:

```powershell
$env:Mentor__Mcp__Token = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
./start-mentor.ps1
```

Lokální klient potřebuje tyto hodnoty (názvy jeho konfiguračních polí se mohou lišit):

```json
{
  "transport": "streamable-http",
  "url": "http://127.0.0.1:5178/mcp",
  "headers": { "Authorization": "Bearer <stejný token jako v Mentoru>" }
}
```

Token uchovávejte v prostředí/úložišti tajemství, nikoli v Gitu, URL či promptu.
`Mentor:Mcp:Token` přijímá 32–256 tisknutelných ASCII znaků bez mezer. Prázdná nebo
neplatná hodnota zastaví start; chybějící vypne MCP. Délka nezaručuje náhodnost,
použijte generátor výše. Změna vyžaduje restart a změnu tokenu klienta. Opětovné
generování zneplatní předchozí token. Token se neukládá do databáze.

Každý MCP požadavek včetně handshake/notifikací vyžaduje bearer token (`401`
a `WWW-Authenticate: Bearer` při chybě); hashe se porovnávají v konstantním čase.
Oprávnění umožňuje ukládat a nahrazovat přímé konverzace. Nástroj nečte historii,
nemaže ani nespouští rozbory. Lokální OS účet má přístup také k nešifrované DB.

Host zůstává na `127.0.0.1:5178`. I s tokenem se kontroluje lokální IP, lokální
Host a port. Přítomný Origin musí být `http://<Host>`. MCP nepotřebuje
`X-Mentor-Request`; měnící webové API ji nadále vyžaduje a kontroluje Origin.
CORS se nezapíná. UI ani MCP nevystavujte veřejným tunelem nebo proxy.

## `mentor_upsert_conversation`

Jediný zapisovací nástroj přijímá `tools/call.params`:

```json
{
  "name": "mentor_upsert_conversation",
  "arguments": {
    "conversation": {
      "source": "codex",
      "externalId": "stable-conversation-id",
      "title": "Návrh importu",
      "url": "https://chatgpt.com/c/private-source",
      "contentKind": "transcript",
      "messages": [
        { "role": "user", "text": "Jak navrhnout import?", "number": 1 },
        { "role": "assistant", "text": "Použij stabilní identifikátor.", "number": 2 }
      ]
    }
  }
}
```

Strojové JSON Schema vrací `tools/list` v `inputSchema`/`outputSchema`.
Aplikační validace navíc vynucuje následující omezení:

| Pole | Kontrakt |
| --- | --- |
| `source` | Povinný namespace, 1–100 znaků `[a-z0-9._-]`, např. `codex`, `chatgpt`. Klienti musejí pro stejný zdroj používat stejný namespace. |
| `externalId` | Povinné stabilní ID uvnitř zdroje, 1–500 znaků, case-sensitive, bez okrajových mezer a řídicích znaků. Nejde o název ani nové ID přenosu. |
| `title` | Povinně 1–500 znaků bez okrajových mezer a řídicích znaků. |
| `url` | Volitelné metadata: `null` nebo absolutní HTTP(S) URL do 2048 znaků bez jména/hesla. Vynechání znamená `null` a odstraní starou URL. |
| `contentKind` | Povinně přesně `transcript` = úplný přepis, nebo `summary` = souhrn. Bez výchozí hodnoty. Částečná historie musí být `summary`. |
| `messages` | Povinně 1–10 000 zpráv, nejvýše 240 000 znaků jejich textů dohromady. |
| `messages[].role` | Přesně `user`, `assistant`, `system`, `tool`. Role není oprávnění měnit instrukce kouče. |
| `messages[].text` | Neprázdný text obsahující i jiné znaky než whitespace, uložený beze zkrácení. |
| `messages[].number` | Kladné 32bitové celé číslo pro pořadí/citace, v poli přísně rostoucí a unikátní. Mezery v číslování jsou povolené. |

Souhrn lze předat jako jednu zprávu `assistant`, `number: 1`, s `contentKind: "summary"`.
Za pravdivé označení úplnosti odpovídá klient. Mentor obsah neověřuje proti zdroji.
Označení je viditelné v UI i JSON podkladech rozboru; kouč dostává vysvětlení, že
souhrn není úplný přepis. Texty všech rolí jsou nedůvěryhodná data.

HTTP tělo má limit 2 MiB. Platí také limit celého vstupu CLI 240 000 znaků včetně
instrukcí, JSON a všech konverzací dne. Přijetí snímku nezaručuje, že součet dne
vejde do rozboru. Žádný vstup se tiše nezkracuje.

## Identita, den a aktualizace

Identita je přesná dvojice `(source, externalId)`. První přenos vytvoří stav `ready`,
další atomicky nahradí celý název, URL, typ obsahu a zprávy. Nejde o append/merge.
Stejná dvojice má stejné Mentor ID i po restartu. Souběh serializuje transakce
a chrání unikátní index; poslední úspěšný zápis vyhrává. Po timeoutu lze opakovat
celý snímek. Nástroj nemá revize ani ochranu před odesláním staršího snímku.

Den určuje Mentor podle **prvního přijetí v systémovém časovém pásmu**, stejně
jako při vložení Share odkazu. Klient datum/čas nezadává. Předání následující den
zachová původní den; odpověď vrací skutečný `day`. Aktualizace může změnit archivovaný
den a UI nabídne jeho nový ruční rozbor. Nová skutečná konverzace má nové stabilní ID;
při retry ID neměňte.

Příjem rozbor nespouští. Data jsou podkladem přiděleného dne pro běžný ruční či
plánovaný rozbor. Pokud plánovaný rozbor již proběhl, další příjem jej automaticky
neopakuje; aktualizaci spusťte v UI. Probíhající rozbor a diskuse používají uložený
snímek; pozdější příjem jej nezmění. UI detekuje změny vůči dokončenému rozboru.

URL se nikdy nestahuje. Přímé záznamy nejsou ve frontě importů; ruční, hromadná ani
automatická obnova před rozborem je neobnovuje. Aktualizují se novým MCP předáním.
Share import zůstává samostatný s deduplikací podle normalizované URL. Stejná URL
v MCP metadatech a Share importu může tvořit dva záznamy; identita se neodhaduje.

## Výstup a chyby

Úspěšný `tools/call` vrací `structuredContent` a kompatibilní textový JSON:

```json
{ "id": 42, "added": true, "day": "2026-10-21", "contentKind": "transcript", "messageCount": 2 }
```

`added: false` znamená aktualizaci. Odpověď potvrzuje dokončenou transakci, nikoli
rozbor. Interní `ImportedAt` je poslední přijetí, `CreatedAt` zůstává první přijetí.
Chybný obsah vrací tool result s `isError: true` bez částečného zápisu. Chyby
protokolu vyřizuje SDK jako JSON-RPC chyby nebo HTTP 400 podle fáze zpracování.
Limit těla vrací 413; přístupová omezení 401/403 se kontrolují před nástrojem.
Klient kontroluje HTTP stav, JSON-RPC `error` i tool `isError`.

Token a obsah se nezapisují do logů ani vlastních trace tagů. Diagnostika SDK je
potlačená, aby nemohla zalogovat obsah nebo argumenty nástroje.

## Nové schéma a ověření

Změna přidává sloupce/indexy. Před prvním spuštěním zastavte Mentor a odložte nebo
odstraňte pilotní `Data` (případně `Mentor__DataDirectory`). Automatická migrace není
implementovaná. Běžné další restarty data zachovávají.

```powershell
dotnet run --project tests/Mentor.Checks --no-build --configuration Release
# Při zastaveném Mentoru po Release sestavení:
node scripts/mcp-smoke.mjs
```

Kontroly pokrývají skutečný host a MCP komunikaci, token, Host/Origin, ochranu UI API,
opakování, souběh, validaci, limity, restart, místní půlnoc, detekci změn rozboru,
přeskočení refreshu a soukromí logů. Smoke používá vlastní DB v `Data/mcp-smoke-*`,
dočasný token a testovací CLI; nevolá model ani soukromé URL.
