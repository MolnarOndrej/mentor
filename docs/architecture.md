# Architektura a údržba Mentoru

## Vrstvy a odpovědnosti

```text
Mentor (HTTP, UI, host, monitoring)
  ├─ Mentor.Application (pravidla, případy použití, modely, rozhraní)
  └─ Mentor.Infrastructure (SQLite, import, CLI)
       └─ Mentor.Application
```

Application neodkazuje na ASP.NET Core, SQLite ani Infrastructure; testy ověřují směr závislostí. Každý typ má vlastní stejně pojmenovaný soubor a namespace odpovídá složce. Soubory se seskupují podle domény; testovací dvojníci jsou v `TestDoubles`.

| Oblast | Odpovědnost |
| --- | --- |
| `Dashboard/MentorService` | Příkazy UI a čtecí model, pokrytí zpráv a detekce změn podkladů |
| `Analyses/DailyAnalysisPolicy`, `DailySchedule` | Výběr celých konverzací podle dne vložení, klíče zpráv a plánování |
| `Prompts/AnalysisPrompt`, `DiscussionPrompt` | Sestavení vstupu rozboru a diskuse, validace šablony a limitu diskuse |
| `Conversations/ConversationRefresher` | Obnovení dnešních zdrojů s ponecháním dat při nedostupnosti |
| `Processing/WorkProcessor` | Jedna iterace plánování a zpracování importu, odpovědi nebo rozboru |
| `Persistence/MentorDbContext`, `EfMentorRepository` | EF mapování, transakce, trvalá fronta a obnova přerušených úloh |
| `Conversations/ChatGptImporter`, `ChatGptShareParser`, `CodexShareParser` v Infrastructure | HTTP přenos oddělený od parsování jednotlivých formátů |
| `Analyses/CodexCliAnalyzer`, `CodexResponseParser` | Konfigurace Codexu a validace jeho JSONL výsledku |
| `Processes/ICliProcessRunner`, `CliProcessRunner` | Obecné CLI bez znalosti poskytovatele: UTF-8 I/O, timeout, limity a rušení |
| `Hosting/MentorWorker` | Smyčka background služby, DI scope, pollování a shutdown |
| `Endpoints` | HTTP mapování rozdělené na konverzace, rozbory, diskusi a prompt |
| `Middleware`, `Observability` | Lokální přístup, jednotné chyby, health checky a telemetrie |

UI má moduly pro API a bezpečné DOM, navigaci, automatické ukládání, editory, archiv a diskusi. `app.js` je propojuje a řídí obnovování stránky. Formátovaný text se vykresluje přes DOM, nikoli jako důvěryhodné HTML z modelu.

## Persistence a životnost služeb

DI při startu kontroluje scope a registrace. Repository, workspace lease, telemetrie a CLI adaptér jsou singletony. Business služby a typed HTTP importér vznikají ve scope požadavku nebo jedné iterace workeru. `TimeProvider` umožňuje testovat plánování bez čekání.

Repository implementuje rozhraní podle potřeb konzumentů a sdílí jeden zámek, aby atomické operace zasahující konverzace, nastavení, rozbory a diskusi nemohly soupeřit. `IDbContextFactory<MentorDbContext>` vytváří nový kontext pro každou operaci; DbContext se mezi požadavky nesdílí. Čtení používá `AsNoTracking`, zápisy explicitní transakci. Testy pracují se skutečným SQLite providerem v dočasných souborech.

Unikátní index chrání normalizované URL. Zprávy a klíče snímků jsou JSON kolekce s EF konvertorem a porovnáváním hodnot. Diskuse má unikátní pořadí v rozboru a kaskádové odstranění přes cizí klíč. Inicializace používá `EnsureCreated`; pravidlo pilotu nepovoluje automatické migrace. Zámek adresáře brání dvěma instancím nad jednou databází.

## Průchod úlohy a invarianty

1. Zařazení rozboru rezervuje den, ověří dostupnost zpráv a uloží neměnné instrukce. Souběžný ruční požadavek na rozbor nebo odpověď stejného dne dostane HTTP 409. Odmítnutí požadavku vrátí zpět i případnou změnu instrukcí.
2. Worker obslouží import, poté odpověď, poté rozbor. Rozbor počká na dokončení importů. Při převzetí vytvoří snímek aktuálních zpráv a přesný úplný prompt. U dnešního dne ještě obnoví zdroje a přepíše snímek skutečně použitým vstupem. Archiv používá uložené konverzace.
3. Úspěch rozboru uloží výsledek, klíče pokrytí a odstraní předchozí výsledek včetně jeho diskuse v jedné transakci. Selhání uchová poslední úspěšný výsledek a pouze poslední neúspěšný pokus. Opakování vždy analyzuje celý den.
4. Odpověď vychází z přesného uloženého promptu dokončeného rozboru, jeho výsledku, dokončené historie a otázky. Celkový limit se kontroluje před zařazením. Chyba uchová otázku a předchozí diskusi; další otázka počká na zopakování neúspěšné odpovědi.
5. Čekající úlohy přežijí restart. Rozpracovaný import se vrátí do fronty; rozpracovaný rozbor a odpověď se označí za přerušené selhání. Při běžném shutdownu se přerušení generování zapíše také. Pozdní výsledek po zrušení se nepřijme.

Klíč pokrytí zprávy tvoří ID konverzace a pořadové číslo zprávy. Změnu textu se stejným číslem zachytí porovnání nově sestaveného promptu s posledním úspěšným vstupem. Úprava instrukcí neovlivní již zařazené úlohy ani historický výsledek. Backend nepodporuje starý režim editoru celého promptu s vloženým JSONem; UI ukládá šablonu a data doplňuje až zpracování fronty.

## Izolace CLI

`CodexCliAnalyzer` používá nový `--ephemeral` běh s read-only sandboxem, bez osobní konfigurace a projektových instrukcí, se zakázaným shellem, patchem, webem a pluginy. Importovaný obsah je v zadání nedůvěryhodnými daty. Úspěch vyžaduje nulový exit kód, `turn.completed` a neprázdnou finální odpověď; akční nástroj ve výstupu je chyba. Runner omezuje vstup/výstup i dobu běhu a spouští proces bez shellu a viditelného okna.

Na Windows `WindowsCliProcessLauncher` vytváří pro každý běh vlastní Job Object s `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. `CreateProcessW` s `PROC_THREAD_ATTRIBUTE_JOB_LIST` zařadí proces do jobu už při vytvoření; potomci členství dědí. Zrušení, timeout, limit výstupu, dokončení kořenového procesu i násilné ukončení Mentoru uklidí potomky daného běhu. Jiné běhy se neovlivní.

Mimo Windows `PortableCliProcessLauncher` používá `Process.Start` a při chybě nebo zrušení `Kill(entireProcessTree: true)`. Tato cesta nezaručuje úklid při násilném ukončení hostu ani potomků, kteří přežili kořenový proces. Nasazení na Linuxu by vyžadovalo správu životnosti hostitelským prostředím nebo další platformní implementaci.

## Pravidla dalších změn

Zachovejte transakční rezervaci dne, nahrazení výsledku a mazání diskuse. Sdílený zámek persistence nerozdělujte bez náhrady těchto záruk. Nové importní formáty oddělte od HTTP přenosu a ověřte role i odmítnutí neúplného obsahu. Změny CLI ověřujte přes testovací proces včetně rušení a životnosti potomků.

Před commitem spusťte kontroly uvedené v [README](../README.md); provozní ověření a známé limity diagnostiky jsou v [operations.md](operations.md). Další infrastrukturu přidávejte až podle konkrétní potřeby. Aktuální model počítá s jedním uživatelem, jedním hostem a jednou frontou.
