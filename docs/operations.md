# Monitoring a provoz

Mentor stále běží jen na `127.0.0.1:5178`. Provozní endpointy mají stejné omezení Host a lokálního přístupu jako aplikace. Není potřeba externí služba k běžnému spuštění.

## Rychlá kontrola

| Adresa | Význam |
| --- | --- |
| `/health/live` | Host odpovídá. Neověřuje závislosti. |
| `/health/ready` | Databáze má očekávané tabulky a nastavení, worker je živý a executable Codexu existuje. Při problému HTTP 503. |
| `/api/operations` | Poslední heartbeat, začátek zpracování a počty čekajících, běžících a neúspěšných importů a rozborů. Stavy diskuse jsou v `/api/state`. |

Kontrola Codexu nespouští model ani neověřuje účet. Přihlášení, quota a dostupnost modelu jsou potvrzené až skutečným rozborem. Neúspěšná jednotlivá analýza neznamená automaticky nedostupnost celé aplikace; její chyba je uložená u rozboru a v počítadlech fronty.

```powershell
Invoke-RestMethod http://127.0.0.1:5178/health/ready
Invoke-RestMethod http://127.0.0.1:5178/api/operations
```

## Strukturované logy

Serilog zapisuje JSON do konzole a `Data/logs/mentor-*.jsonl`. Soubor se rotuje po dni nebo 10 MB; uchovává se nejvýše 14 souborů. Každá úloha má `JobKind`, `JobId`, výsledek, délku a při aktivním tracingu i trace/span ID. API chyby vracejí `traceId`.

Logy ani vlastní trace tagy neobsahují prompt, zprávy, surový stdout/stderr CLI nebo celé sdílené URL. Podrobná chyba poskytovatele zůstává v osobní databázi a je dostupná v UI. Automatické logování HTTP klienta je potlačené; URL a query tagy se před exportem HTTP traces odstraní. HTTP payloady se nezaznamenávají. Polling stavu a health checky nezaplňují informační request log ani traces.

## OpenTelemetry

Instrumentace používá `ActivitySource`, `Meter`, ASP.NET Core, HttpClient a runtime .NET. Bez nastaveného OTLP endpointu se nic neposílá do externího backendu. Logy zůstávají lokální; OTLP v této verzi exportuje **traces a metriky**, nikoli obsah souborových logů.

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://127.0.0.1:4317'
# Výchozí transport je gRPC. Pro HTTP/protobuf typicky:
# $env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://127.0.0.1:4318'
# $env:OTEL_EXPORTER_OTLP_PROTOCOL = 'http/protobuf'
./start-mentor.ps1
```

Připojte vlastní OTLP backend, například [samostatný Aspire Dashboard](https://aspire.dev/dashboard/standalone/). Dashboard zde není automaticky nainstalovaný ani spuštěný. Způsob konfigurace transportu a endpointu popisuje [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/exporters/).

| Signál | Jednotka / tagy |
| --- | --- |
| `mentor.jobs` | Počet dokončených úloh; `job.kind`, `job.outcome` (`completed`, `failed`, `cancelled`) |
| `mentor.job.duration` | Sekundy; stejné nízkokardinalitní tagy |
| `mentor.queue.depth` | Počet položek podle `job.kind` a `job.status`; `failed` je počet uložených selhání |
| `mentor.worker.running` | 0/1 |
| `mentor.worker.heartbeat.age` | Sekundy od posledního heartbeat |
| `mentor.import`, `mentor.analysis`, `mentor.answer`, `mentor.codex` | Spany aplikace; Codex je podřízený span rozboru nebo odpovědi |

ID úloh jsou pouze v logu/trace, nikoli v metric labels. Metriky dokončených úloh zahrnují i `job.kind=answer`; gauge hloubky fronty zatím zahrnuje pouze importy a rozbory. U běžící analýzy je delší heartbeat normální; readiness počítá s timeoutem úlohy.

## Konfigurace a incidenty

Databázi spravuje EF Core přes `EnsureCreated`. Prázdný adresář při startu vytvoří nové schéma; běžný restart data zachová. Pilot nepoužívá migrace. Po budoucí změně modelu lze při zastavené aplikaci odstranit pilotní `mentor.db` a případné soubory `mentor.db-wal` a `mentor.db-shm`, aby vznikla nová databáze. EF sensitive-data logging je vypnutý.

`Mentor__DataDirectory`, `Mentor__CodexExecutable`, `Mentor__AnalysisTimeout` (např. `00:10:00`), `Mentor__ImportTimeout` a `Mentor__MaxImportBytes` se bindují do validovaných options. Neplatné klíčové hodnoty zastaví start. Limit celého vstupu CLI je jednotně 240 000 znaků v business pravidlech. Přístup zůstává pevně lokální na portu 5178.

1. **HTTP 503 readiness:** podle `checks` zjistit databázi/worker/CLI. Databázi nesdílet mezi více procesy. Zkontrolovat zapisovatelný adresář a cestu k CLI.
2. **Narůstá fronta:** porovnat běžící úlohu a heartbeat s timeoutem; delší rozbor je očekávaný, úloha přes limit ne. Zkontrolovat strukturovaný log podle `JobId`.
3. **Rozbor selhal:** přesnou chybu přečíst v UI, ověřit `codex login status` pod stejným Windows účtem, opravit příčinu a spustit nový pokus.
4. **Chybí denní výsledek:** aplikace i PC musejí běžet; čas je uložený v databázi a upravitelný tlačítkem Nastavení (výchozí 22:00 v systémovém časovém pásmu). Starší den lze otevřít v archivu.

## Ověření transportu a provozních stavů

Po sestavení řešení a při zastaveném Mentoru (výchozí konfigurace skriptu je Debug):

```powershell
$env:MENTOR_BUILD_CONFIGURATION = 'Release' # odpovídá konfiguraci sestavení
node scripts/observability-smoke.mjs
```

Skript používá oddělenou databázi v `Data/smoke-*`, dvě syntetické konverzace a testovací executable místo modelu. Spustí skutečný web, SQLite frontu a proces CLI, přijme protobuf traces/metriky v lokálním testovacím OTLP přijímači a ověří shodu uloženého promptu se skutečným stdin, diskusi i nepřítomnost soukromých markerů v telemetrii. Po skončení server zastaví a ponechá adresář s diagnostikou. Původní databázi nepoužije.
