# Mentor

Osobní AI kouč v češtině. ASP.NET Core 10, HTML/JavaScript, SQLite přes Entity Framework Core a jedna trvalá fronta úloh. Aplikace nemá přihlášení a běží pouze na `http://127.0.0.1:5178`.

## Spuštění na Windows

Potřebujete .NET SDK podle `global.json` a nativní `codex.exe` v PATH. CLI přihlaste pod stejným Windows účtem, který spouští Mentor:

```powershell
codex login
codex login status
./start-mentor.ps1
```

Otevřete http://127.0.0.1:5178. Alternativně spusťte `dotnet run --project src/Mentor --no-launch-profile`. Middleware kontroluje lokální přístup, Host a Origin; aplikaci nevystavujte veřejnou proxy ani tunelem. Rozbor posílá obsah přihlášenému Codex CLI a službě OpenAI, nejde o offline zpracování.

## Použití

1. V **Přehledu** vložte veřejný textový odkaz ChatGPT (`https://chatgpt.com/share/<id>`) nebo Codexu (`https://chatgpt.com/s/cx_<id>`). Import začne na pozadí. Po stavu „Uloženo“ lze kliknutím na název otevřít uložené zprávy.
2. Spusťte **Analyzovat**, nebo nechte aplikaci a počítač běžet do času nastaveného v **Nastavení** (výchozí 22:00). Jeden rozbor zahrne všechny úspěšně importované konverzace daného dne. Celá konverzace patří ke dni vložení odkazu v systémovém časovém pásmu; původní časy zpráv se nepoužívají.
3. **Instrukce kouče** upravují pravidla rozboru. Instrukce i nastavení se ukládají automaticky po sekundě bez psaní nebo při opuštění pole; chyba ukládání zůstane viditelná. Před rozborem se ukládání instrukcí dokončí. JSON konverzací doplňuje backend. Přesný odeslaný prompt je dostupný v podrobnostech běhu.
4. Pod dokončeným rozborem můžete položit otázku. Každá odpověď je samostatná úloha s původním zadáním, podklady, výsledkem a dokončenou diskusí. Otevření diskuse samo CLI nespouští. Neúspěšnou odpověď lze zopakovat.
5. V **Archivu** otevřete starší den a případně spusťte jeho rozbor ručně. Aplikace po startu doplní dnešní rozbor, pokud jeho čas již uplynul; starší vynechané dny automaticky nedoplňuje.

Dnešní odkazy lze obnovit jednotlivě nebo hromadně. Před dnešním rozborem se obnoví automaticky. Nedostupný odkaz ponechá poslední uloženou kopii a zobrazí příčinu. U Codexu se ověřuje také veřejná stránka, protože datový endpoint může držet starý snímek. Archivované odkazy se neobnovují. Smazání dnešní konverzace vyžaduje potvrzení.

Duplicitní odkaz po normalizaci URL nevytvoří další záznam ani nepřepíše uložený obsah. Neúspěšný import lze opakovat. Import zahrnuje pouze text uživatele a viditelné odpovědi AI s pořadovými čísly pro citace; vynechává interní myšlenky, průběžné komentáře a výstupy nástrojů. Claude a další zdroje nejsou podporované. Importní formáty ChatGPT/Codexu nejsou stabilní veřejné API; neočekávaný formát skončí chybou.

Opakovaný rozbor zahrne celý den a vyžaduje potvrzení nahrazení. Předchozí úspěšný výsledek zůstává čitelný i při selhání nového pokusu. Úspěšné dokončení jej nahradí a odstraní jeho diskusi v jedné transakci; historie nahrazených výsledků se neuchovává. Pro jeden den může čekat nebo běžet jen jeden rozbor či odpověď. Den bez analyzovatelných zpráv rozbor nespustí.

Navigace zachovává rozepsané formuláře a podporuje historii i obnovení stránky. Nastavení zahrnuje interval pollování 1–3600 sekund (výchozí 5) a čas denního rozboru. Změny se projeví bez restartu a zůstávají v databázi.

## Data a limity

Databáze je v `src/Mentor/Data/mentor.db`, za běhu mohou existovat i soubory WAL/SHM. Data nejsou aplikací šifrovaná. Pro zálohu Mentor zastavte a zkopírujte celý adresář `Data`. Smazání zdrojového sdílení neodstraní kopii v Mentoru.

| Omezení | Výchozí hodnota |
| --- | --- |
| Importní odpověď | 8 MB |
| Šablona instrukcí včetně bloku `{{data}}` | 20 000 znaků |
| Celý vstup CLI včetně diskuse | 240 000 znaků |
| Jeden běh CLI | 10 minut |

Nadlimitní vstup se odmítne bez zkrácení. `prompts/defaultMentorPrompt.txt` plní jen novou databázi; existující instrukce se při restartu nepřepisují. Konfiguraci cest, timeoutů, logů a OTLP popisuje [provozní návod](docs/operations.md).

Mentor je pilot: migrace ani zpětná kompatibilita se neimplementují bez výslovného požadavku. Pokud změna schématu vyžaduje čistou databázi, zastavte aplikaci a odstraňte její adresář `Data` (nebo nastavený `Mentor__DataDirectory`). **Běžný restart data zachovává.**

## Vývoj a ověření

Strukturu projektu, pravidla fronty a životnost CLI popisuje [architektura](docs/architecture.md). Závazná pravidla organizace kódu a Gitu jsou v [AGENTS.md](AGENTS.md).

```powershell
dotnet restore Mentor.slnx --locked-mode
dotnet build Mentor.slnx --no-restore --configuration Release
dotnet run --project tests/Mentor.Checks --no-build --configuration Release
dotnet format whitespace Mentor.slnx --no-restore --verify-no-changes
npm ci
npm run check:ui
npm run test:ui
```

`npm run format:ui` upraví formát UI. Backendové kontroly používají dočasné SQLite databáze a testovací CLI; nevolají model. Pokrývají import, denní dávky, souběh, restart, nahrazení rozboru, diskusi, procesy a soukromí telemetrie. CI provádí backendové kontroly na Windows i Linuxu a kontroly UI.

Pro ověření skutečného HTTP hostu a OTLP při zastaveném Mentoru:

```powershell
$env:MENTOR_BUILD_CONFIGURATION = 'Release'
node scripts/observability-smoke.mjs
```

Parsery lze volitelně ověřit na vlastních lokálních souborech. Osobní konverzace ani databáze nepatří do Gitu:

```powershell
dotnet run --project tests/Mentor.Checks -- C:/cesta/ke/share.html
dotnet run --project tests/Mentor.Checks -- --codex-share C:/cesta/ke/snapshot.json
```
