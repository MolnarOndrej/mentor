# Pilot

Mentor je ve fázi pilotu. Zpětnou kompatibilitu, migrace starých schémat ani obnovu
starých importů neimplementujte, dokud si je uživatel výslovně nevyžádá.
Při změně datového modelu lze pilotní databázi zahodit a vytvořit znovu.
Data se při běžném restartu aplikace zachovávají.

# Organizace kódu

Každá třída, interface, entita, record, struct nebo enum má vlastní soubor
pojmenovaný podle typu. Platí to i pro pomocné testovací typy; nevkládejte je
do souboru jiného typu jako vnořené deklarace.
Soubory seskupujte podle domény nebo funkce (například Conversations, Analyses,
Prompts, Configuration, Processing, Diagnostics), ne do společných souborů
Models.cs nebo Ports.cs. Namespace odpovídá projektu a složce.

Při přejmenování zachovejte jednotné názvosloví souvisejících prvků. Vyhledejte
stejné i podobné názvy v celém projektu a upravte všechny související výskyty,
včetně názvů souborů a typů, proměnných, HTML ID, atributů aria-labelledby,
selektorů, importů a testů. Odkazy musí odpovídat přejmenovaným cílům.

# Git workflow

Změny integrujte do `master` pomocí squash a přímého push bez PR, i když ochrana
větve v GitHubu vyžaduje PR. Změny připravené přímo na `master` uložte jako jeden
commit. Uživatel dne 2026-10-18 uvedl, že jeho účet má dočasnou výjimku pro force
push; tato výjimka sama o sobě neznamená, že je nutné přepisovat historii.
