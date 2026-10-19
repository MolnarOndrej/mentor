# Mentor

Osobní AI kouč v češtině. ASP.NET Core 10, jedna stránka v HTML/JS, Entity Framework Core se SQLite a jeden background worker. Bez přihlášení, pouze `http://127.0.0.1:5178`.

Kód má tři vrstvy: `Mentor.Application` (pravidla a rozhraní), `Mentor.Infrastructure` (SQLite, import, Codex CLI) a `Mentor` (web, UI, host a monitoring). Rozbor kódu, rozhodnutí a další doporučení jsou v [architektuře](docs/architecture.md); provozní endpointy, logy a OTLP v [provozním návodu](docs/operations.md).

## Spuštění na Windows

Potřebujete .NET SDK 10 a Codex CLI dostupný jako `codex.exe`. Přihlaste CLI pod stejným Windows účtem, který spouští Mentor:

```powershell
codex login
codex login status
./start-mentor.ps1
```

Otevřete http://127.0.0.1:5178. Alternativně: `dotnet run --project src/Mentor --no-launch-profile`. Port je záměrně pevný a middleware odmítá ne-lokální přístup i cizí Host/Origin. Nevystavujte aplikaci přes veřejný proxy server ani tunel.

## Použití

Pod nadpisem Mentor je stálá navigace **Přehled, Nastavení, Instrukce kouče a Archiv**. Každá sekce má vlastní ikonu a adresu (`#/overview`, `#/settings`, `#/prompt`, `#/archive`); aktivní sekce je zvýrazněná. Na přehled se vrátíte i kliknutím na nadpis Mentor. Funguje historie prohlížeče i obnovení otevřené sekce. Přepínání zachová rozepsané formuláře, vybraný den a pozici v jednotlivých sekcích; instrukce kouče i nastavení se automaticky ukládají po sekundě bez psaní nebo při opuštění pole. Neplatné hodnoty se neuloží a chyba ukládání zůstane viditelná. Při odchodu s neuloženými změnami prohlížeč zobrazí upozornění.

1. Vložte veřejný sdílený odkaz na konverzaci ChatGPT (`https://chatgpt.com/share/<id>`) nebo Codexu (`https://chatgpt.com/s/cx_<id>`). Import začne na pozadí hned po uložení.
2. Po stavu „Uloženo“ je v databázi kopie zpráv i zdrojový odkaz. Před každým dnešním rozborem se všechny dnešní odkazy znovu načtou. Nedostupný nebo smazaný odkaz ponechá poslední uloženou kopii. Kliknutím na název zobrazíte uložené zprávy s autory a pořadím.
3. Pod vložením odkazu je dnešní rozbor. Klikněte na „Analyzovat“ nebo nechte aplikaci běžet do nastaveného času (výchozí 22:00 v systémovém časovém pásmu). Starší den otevřete tlačítkem „Archiv“ nahoře; tlačítko „Dnes“ vás vrátí zpět.
4. Celá konverzace ChatGPT i Codexu patří ke **dni vložení odkazu** v systémovém časovém pásmu. Datum se uloží automaticky při přidání odkazu a nemění se při dokončení nebo opakování importu. Původní časy jednotlivých zpráv se nenačítají, neukládají ani neposílají do rozboru. Konverzace se mezi dny nedělí; zachovává se pořadí a čísla zpráv pro citace.
5. Každý běh vytvoří **jeden společný rozbor všech zpráv daného dne ze všech konverzací**. Opakované spuštění zahrne i již analyzované zprávy: po přidání další konverzace vznikne aktualizovaná souhrnná verze, ne izolovaný dodatek.
6. Počty zpráv ukazují pokrytí posledním úspěšným rozborem a počet čekajících zpráv. Archiv po dnech je rozkliknutelný a řazený od nejnovějších. Konverzace v jednotlivých dnech se zobrazují bez číslování, od nejstarší po nejnovější podle vložení odkazu. Z archivu lze otevřít denní rozbor. Při selhání zůstává předchozí úspěšný výsledek viditelný.
7. U dnešních konverzací šipka aktualizuje jednotlivý odkaz a koš odstraní konverzaci z aplikace i databáze. Pokud odkaz nefunguje, zobrazí se zpráva s příčinou (včetně HTTP chyby) a uložená data zůstanou beze změny. U Codexu se ověřuje i veřejný odkaz, protože samotný datový endpoint může stále nabízet starší snímek. Stejný obsah zobrazí „Konverzace je beze změn“; úspěšná aktualizace se hlásí pouze při změně názvu či zpráv. Archiv tato tlačítka nemá a jeho odkazy se znovu nenačítají. Vedle analýzy je hromadná aktualizace všech dnešních konverzací. Tlačítko „Analyzovat“ umožňuje opakované spuštění i beze změny podkladů. UI i backend spuštění zakážou, pokud den nemá zprávy v úspěšně importovaných konverzacích nebo už jeho rozbor čeká či běží. Odstranění konverzace vyžaduje potvrzení v dialogu; výchozí volba je Zrušit.

Přegenerování existujícího rozboru vyžaduje potvrzení s poznámkou „Po úspěšném dokončení nový rozbor nahradí stávající rozbor.“ Původní rozbor zůstává během generování čitelný. Až po úspěšném dokončení a ověření výsledku se nový výsledek uloží a původní odstraní v jedné databázové transakci. Historie nahrazených rozborů se neuchovává. Chyba posledního pokusu se zobrazuje samostatně a původní rozbor zachovává i po restartu.

Tlačítko „Instrukce kouče“ nahoře otevře editor instrukcí, které určují chování kouče. Editor neobsahuje JSON konverzací ani zástupný blok dat. Instrukce se automaticky ukládají; před spuštěním analýzy se jejich ukládání dokončí. Konverzace se připojí automaticky při rozboru. Všechna pravidla se čtou z databáze; aplikace pouze doplní blok vstupních dat. Dnešní běh znovu načte zdrojové odkazy a uloží skutečný vstup s aktualizovanými konverzacemi. Přesnou kopii odeslaného promptu včetně JSONu lze otevřít v podrobnostech rozboru. Úprava instrukcí nemění rozpracované ani historické běhy.

Denní rozbor funguje jen za běhu aplikace a počítače. Při spuštění po nastaveném čase se doplní dnešní rozbor; starší vynechané dny lze spustit ručně v archivu. Letní/zimní čas řeší systémová časová zóna. Zahrnou se všechny zprávy z konverzací vložených daný den a dostupných při zahájení běhu. Později importované zprávy jsou označené jako čekající a celý den lze aktualizovat ručně. Den bez zpráv automatický plán přeskočí bez vytvoření rozboru a bez volání CLI. Současná kliknutí na stejný den nespuštějí dva souběžné rozbory; různé dny zpracovává jedna fronta postupně.

Ověřené zdroje jsou **veřejné textové sdílené konverzace ChatGPT a Codexu**. Dodaný ChatGPT odkaz při vývoji obsahoval 10 zpráv (5 uživatelských a 5 odpovědí AI), Codex odkaz jeden uživatelský dotaz a jednu finální odpověď. Codex se načítá z veřejného JSON endpointu `/backend-api/wham/shared_threads/<id>` ve formátu verze 1. Tyto formáty nejsou veřejné stabilní importní API; při změně import skončí skutečnou chybou. Claude a další zdroje nejsou v této verzi implementované. Přihlášení, HTTP blokace, chybějící obsah, netextové zprávy a neočekávaný formát se nepovažují za úspěšný import. Interní myšlenky, průběžné komentáře Codexu a výstupy nástrojů včetně změn souborů se neimportují. Uživatelům nejsou přisuzovány výroky AI.

Zdrojové datum zprávy nemá vliv na import. Zpráva obsahuje pouze autora, text a pořadové číslo; datum pro rozbor vychází z vložení odkazu na konverzaci.

Stejný odkaz po odstranění query/fragmentu a sjednocení ID znovu nevytvoří záznam. Neúspěšný import lze opakovat. Opětovné vložení uložené konverzace ji nepřepíše novější verzí; dnešní obsah obnoví šipka nebo spuštění dnešního rozboru.

Tlačítko „Nastavení“ nahoře umožňuje měnit interval pollování (1–3600 sekund, výchozí 5 sekund) a čas automatického rozboru (výchozí 22:00 v systémovém časovém pásmu). Obě hodnoty se ukládají do databáze a zachovají se po restartu. Interval řídí obnovování stránky i kontrolu fronty na pozadí; změny se načtou při dalším pollování bez restartu. Posun času na již uplynulou hodinu spustí dnešní automatický rozbor při další kontrole, pokud dnes ještě neběžel.

## Data a konfigurace

Databáze: `src/Mentor/Data/mentor.db` (včetně WAL/SHM za běhu), vynechaná z Gitu. Obsah není šifrovaný aplikací; chraňte Windows účet a disk. Pro zálohu aplikaci zastavte a zkopírujte celý adresář `Data`. Smazání zdrojového vlákna nesmaže kopii v Mentoru.

Volitelné proměnné prostředí:

| Proměnná | Význam |
| --- | --- |
| `Mentor__CodexExecutable` | Úplná cesta k nativnímu `codex.exe`; výchozí `codex.exe` z PATH. Skript ji zjistí automaticky. |
| `Mentor__DataDirectory` | Absolutní cesta k adresáři databáze; výchozí `Data` uvnitř projektu. |

Analýza předává vybrané zprávy dne a upravené zadání přihlášenému Codex CLI, které komunikuje se službou OpenAI; nejde o offline model. Aplikace nevolá modelové API přímo. Limit importní stránky je 8 MB, zadání 20 000 znaků, celého vstupu CLI 240 000 znaků a běhu CLI 10 minut. Nadlimitní dávka skončí chybou bez tichého zkrácení či označení zpráv za zpracované.

Soubor `prompts/defaultMentorPrompt.txt` slouží pouze k prvnímu naplnění nové databáze. Existující zadání se z něj při restartu neobnovuje. `AnalysisPrompt.cs` jen dosazuje data do databázové šablony a kontroluje její formát; neobsahuje textové konstanty instrukcí.

## Codex CLI a spolehlivost

Jediný integrační bod pro analýzu je `IConversationAnalyzer`, implementovaný `CodexCliAnalyzer`. Ten připravuje argumenty a ověřuje JSONL odpověď Codexu; obecný `ICliProcessRunner` / `CliProcessRunner` v `Mentor.Infrastructure.Processes` řeší spuštění, pracovní adresář, prostředí, UTF-8 stdin/stdout/stderr, limity výstupu, timeout a ukončení procesů. Vrací exit kód a oba výstupy bez interpretace protokolu poskytovatele, takže jej může využít také jiný CLI analyzátor. Runner je bez sdíleného stavu běhů a umožňuje jejich nezávislé rušení. CLI se spouští bez shellu/skrytě. Rozbor uspěje až při nulovém exit kódu, události `turn.completed` a neprázdném finálním textu. Selhání, timeout nebo chybějící CLI jsou trvale zaznamenané a konverzace zůstanou k dalšímu pokusu.

Používá `--sandbox read-only`, zákaz schvalování, vypnutý shell/patch/web, `--ignore-user-config`, `--ignore-rules`, `--ephemeral` a prázdný pracovní adresář. Nenačítá projektové instrukce. Importované zprávy jsou v promptu výslovně nedůvěryhodná data. Výstup s událostí akčního nástroje se nepřijme. Účelem CLI je pouze textový rozbor; nepřebírá osobní konfiguraci nástrojů, hooků či MCP. Viz [oficiální dokumentace neinteraktivního Codexu](https://developers.openai.com/codex/noninteractive/).

Na Windows má každý běh vlastní systémový Job Object s příznakem `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Nativní `CreateProcessW` s `PROC_THREAD_ATTRIBUTE_JOB_LIST` zařadí CLI do jobu už při vytvoření, bez mezery mezi spuštěním a přiřazením; viz [postup Microsoftu](https://devblogs.microsoft.com/oldnewthing/20230209-00/?p=107812/). Potomci CLI členství zdědí. Mentor sám do těchto jobů nepatří a drží jejich nezděditelné handly. Zrušení, timeout, limit výstupu i dokončení kořenového procesu ukončí potomky daného běhu; jiné běhy a ostatní procesy Mentoru neovlivní. Při násilném ukončení Mentoru Windows zavře handly a ukončí všechny jeho CLI joby.

Mimo Windows používá samostatná přenositelná implementace `Process.Start` a při zrušení nebo chybě `Kill(entireProcessTree: true)`. Výchozí název programu je na Windows `codex.exe`, jinde `codex`; lze jej přepsat konfigurací. Tato cesta neposkytuje ochranu při násilném ukončení Mentoru ani záruku úklidu potomků, kteří přežili již ukončený kořenový proces. Pro budoucí linuxový hosting je potřeba doplnit správu životnosti pomocí prostředí kontejneru nebo linuxové implementace. Docker image a síťový režim aplikace tato změna nezavádí.

Fronta i stavy žijí v SQLite, ne jen v paměti. Po restartu se importy obnoví a běžící nedokončené rozbory se označí za selhání se zprávou „Rozbor byl přerušen ukončením aplikace. Spusťte nový rozbor.“ Čekající rozbory zůstávají ve frontě. Přerušený rozbor lze spustit znovu tlačítkem „Analyzovat“; poslední úspěšný rozbor i jeho pokrytí zůstávají dostupné. Stejně se zaznamená přerušení při běžném zastavení aplikace. Běh má neměnný snímek zadání a seznam přesných klíčů zahrnutých zpráv; pokrytí se počítá pouze z úspěšných výsledků. Výsledek se zapisuje transakčně. Zámek adresáře zabraňuje dvěma instancím nad stejnou databází. Každý pokus používá nový dočasný běh `--ephemeral`, bez ukládání session Codexu, evidence ID vláken nebo navazování pomocí `resume`.

## Pravidlo pilotu

Zpětnou kompatibilitu řešíme až na výslovný pokyn uživatele. Při změně schématu se pilotní databáze zahodí a založí znovu; aplikace neobsahuje migrační ani legacy větve. Pro čistý start zastavte Mentor a odstraňte jeho adresář `Data` (nebo adresář nastavený v `Mentor__DataDirectory`). Běžný restart data nemaže.

## Ověření

```powershell
dotnet restore Mentor.slnx --locked-mode
dotnet build Mentor.slnx --no-restore
dotnet run --project tests/Mentor.Checks --no-build
npm ci
npm run check:ui
npm run test:ui
# Formát C# (nebo formátování bez --verify-no-changes):
dotnet format whitespace Mentor.slnx --no-restore --verify-no-changes
# Volitelně ověřit parser na lokálně uloženém skutečném HTML:
dotnet run --project tests/Mentor.Checks -- C:/cesta/ke/share.html
# Volitelně ověřit parser na lokálně uloženém JSON veřejné konverzace Codexu:
dotnet run --project tests/Mentor.Checks -- --codex-share C:/cesta/ke/snapshot.json
```

Kontroly pokrývají parser/role, ignorování zdrojových časů, chybějící data, společný rozbor více konverzací podle dne vložení odkazu, přiřazení celého vlákna k jednomu dni, opakovaný rozbor celého dne, neměnnost promptu a pokrytí, duplicity, restart, chyby CLI a shodu skutečného stdin s uloženým promptem. Skutečná konverzace ani osobní databáze nejsou testovací fixture v Gitu.

Navíc ověřují izolované business služby, výstup job metrik a traces, anonymitu provozních dat, health checky a směr závislostí mezi vrstvami. Workflow `.github/workflows/ci.yml` provádí build a kontroly na Windows/Linux a ověřuje formát UI. Skript `node scripts/observability-smoke.mjs` otestuje skutečný HTTP host, SQLite, proces a export OTLP s testovacími daty, bez volání modelu; před jeho spuštěním zastavte běžící Mentor. `npm run format:ui` upraví formát UI, `npm run check:ui` jej pouze zkontroluje.
