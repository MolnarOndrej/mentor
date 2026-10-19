# Architektura a údržba Mentoru

## Výsledek rozboru původního kódu

Původní aplikace měla DI kontejner, ale HTTP handlery a worker pracovaly přímo s konkrétním `Store` a importérem. `Program.cs` obsahoval konfiguraci hostu, bezpečnostní middleware, projekci stavů, validaci i příkazy nad databází. `Store` současně určoval pravidla denních dávek. Importér spojoval síťovou komunikaci s parsováním nestabilního HTML; adaptér Codexu spojoval běh procesu s interpretací JSONL. JavaScript měl v jednom souboru komunikaci s API, vykreslování i stav editoru. Provozní diagnostika končila u několika zpráv workeru.

Tato místa jsou nyní oddělená. Rozhraní jsou definovaná podle potřeb konzumentů; neexistuje univerzální repository rozhraní pro všechny endpointy. SQLite implementace sdílí jednu synchronizaci a transakce, protože claim fronty musí atomicky pracovat s konverzacemi, nastavením a rozbory.

## Vrstvy a závislosti

Každý typ má vlastní stejně pojmenovaný soubor a namespace podle složky.
V Application jsou typy seskupené do `Conversations`, `Analyses`, `Prompts`,
`Configuration`, `Processing`, `Diagnostics` a `Dashboard`. Infrastructure
obsahuje implementace importu v `Conversations`, CLI v `Analyses` a databázové
entity v doménových podsložkách `Persistence`. HTTP požadavky jsou oddělené
pod `Endpoints` podle příslušné domény, health checky pod
`Observability/HealthChecks`. Testovací dvojníci mají samostatné soubory v
`TestDoubles`.

```text
Mentor (HTTP, UI, host, monitoring)
  ├─ Mentor.Application (pravidla, případy použití, modely, rozhraní)
  └─ Mentor.Infrastructure (SQLite, ChatGPT HTTP/parser, Codex proces/JSONL)
       └─ Mentor.Application
```

`Application` neodkazuje na ASP.NET Core, SQLite ani Infrastructure; kontrola závislostí je součástí testů. Samostatný Domain projekt zatím nepřináší užitek: model je malý a neobsahuje rozsáhlé doménové agregáty. UI zná pouze JSON kontrakt endpointů. Infrastructure je ve webovém projektu dostupná pro composition root a provozní integraci.

| Část | Odpovědnost |
| --- | --- |
| `MentorService` | Případy použití, validace vstupů, čtecí model pro UI |
| `DailyAnalysisPolicy`, `DailySchedule` | Den podle systémového časového pásma, výběr zpráv a jejich stabilní klíče |
| `AnalysisPrompt` | Dosazení dat do DB šablony; žádný skrytý text instrukcí |
| `WorkProcessor` | Jedna testovatelná iterace zpracování s injektovanými poskytovateli |
| `MentorDbContext` | EF Core model, mapování tabulek a JSON kolekcí |
| `EfMentorRepository` | LINQ, trvalá fronta, atomické claimy a neměnné snímky |
| `ChatGptImporter`, `ChatGptShareParser` | Odděleně přenos HTTP a čtení obsahu |
| `CodexCliAnalyzer`, `CodexResponseParser` | Odděleně proces a kontrola výsledku protokolu |
| `MentorWorker` | Životní cyklus BackgroundService, interval a shutdown |
| `MentorEndpoints` | Pouze mapování HTTP na business službu |
| `LocalAccessMiddleware`, `ApiErrorMiddleware` | Lokální přístup a jednotné chyby s trace ID |
| `api.js`, `dom.js`, `prompt-editor.js`, `app.js` | HTTP klient, bezpečné DOM, editor a řízení stránky |

DI kontroluje scope i úplnost registrací při startu. Repository, zámek pracovního adresáře, telemetrie a adaptér CLI jsou singletony; business služby a `HttpClient` importér vznikají ve scope požadavku nebo iterace workeru. Worker vytváří nový scope pro každou iteraci, takže nedrží typed HTTP klienta po celou dobu života aplikace. `TimeProvider` dovoluje testovat čas bez čekání.

## Databázový přístup

Persistence používá Entity Framework Core 10 se SQLite providerem. Veškeré čtení a zápisy procházejí přes `MentorDbContext`, LINQ a `SaveChanges`; aplikace nesestavuje SQL ani nepoužívá přímé SQLite příkazy. Unikátní index chrání URL proti duplicitám. Zprávy a klíče snímků se ukládají jako JSON kolekce s EF konvertorem a porovnáváním hodnot.

DI registruje `IDbContextFactory<MentorDbContext>`. Repository je singleton kvůli společnému zámku fronty, ale **DbContext se nesdílí**: každá operace jej vytvoří a dispose uvolní. Čtecí dotazy používají `AsNoTracking`; zápisy a převzetí úloh mají explicitní transakci. Konflikt při kontrole promptu vrátí zpět i případnou úpravu nastavení. Inicializace používá `EnsureCreated`, bez migrací a podpory starších schémat podle pravidel pilotu. Testy používají skutečný SQLite provider nad dočasnými soubory.

Životnost kontextů odpovídá [doporučení EF Core](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#use-a-dbcontext-factory). Citlivé hodnoty parametrů se nelogují a informační SQL logy jsou potlačené.

## Zachované invarianty

- Jeden den spojuje N celých konverzací podle dne vložení odkazu v systémovém časovém pásmu. Časy jednotlivých zpráv se neukládají ani nepoužívají.
- Úprava promptu neovlivní rozpracované ani historické běhy.
- Opakované spuštění nevyžaduje změny konverzací. Backend ověřuje dostupnost zpráv a odmítá souběžný rozbor stejného dne.
- Původní rozbor zůstává dostupný až do transakčního uložení ověřeného výsledku a odstranění předchozího rozboru. Historie nahrazených rozborů se neuchovává; chyba posledního pokusu je samostatná.
- Selhání nerozšíří úspěšné pokrytí a nesmaže předchozí výsledek.
- CLI má read-only sandbox, vypnuté akční nástroje a omezenou dobu běhu.
- Pilot nemá migrace ani legacy větve. Běžný restart zachovává data.

## Co záměrně zatím nepřidáváme

Více samostatných procesů, CQRS/MediatR, message broker ani obecné CRUD repository nyní nepřidáváme. Další rozdělení persistence má smysl, až se jednotlivé části začnou měnit nezávisle nebo bude potřeba více procesů.

## Další praktiky podle skutečné potřeby

| Priorita / spouštěč | Praktika nebo technologie | Přínos |
| --- | --- | --- |
| Chci procházet traces a metriky graficky | Samostatný Aspire Dashboard nebo OTel Collector + Grafana | Existující OTLP export se připojí bez změny business kódu. |
| Roste počet testovacích scénářů | xUnit a `WebApplicationFactory` nad stávajícími testovacími porty | Samostatné případy, standardní reporty a izolované HTTP testy; současný CLI test executable může zůstat jako fixture. |
| Archiv zpomaluje načtení | Stránkované EF Core dotazy a serverové seskupení | Nejprve omezit načítání všech textů, měřit latenci a paměť. |
| Síťové importy často selhávají přechodně | `Microsoft.Extensions.Http.Resilience` s omezeným retry jen pro čtecí HTTP | Odolnější import bez nekonečné fronty. Analýzu přes CLI automaticky neopakovat kvůli nákladům a nejistému dokončení. |
| Aplikace má běžet i bez otevřeného terminálu | Windows Service / spravované spuštění při startu systému | Spolehlivé plánování při běžícím PC; CLI musí být přihlášené pod účtem služby. |
| Více uživatelů nebo hostů | Autentizace, autorizace, PostgreSQL provider EF Core | Teprve potom řešit souběh více instancí a na explicitní pokyn i migrace dat. |
| Worker se oddělí od webu | Trvalý W3C trace context ve frontě a externí scheduler | Návaznost trace přes procesy a nezávislý provoz workeru. Nyní slouží pro vazbu HTTP a úloh ID rozboru. |

Podkladem je [doporučení Microsoftu k aplikačním vrstvám](https://learn.microsoft.com/en-us/dotnet/standard/modern-web-apps-azure-architecture/common-web-application-architectures) a [observabilitě .NET přes OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel). Vrstvení je přizpůsobené velikosti tohoto pilotu.
