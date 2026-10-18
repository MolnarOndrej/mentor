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
