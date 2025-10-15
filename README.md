SMHI‑projektet – Dokumentation från start till mål

Sammanfattning: Vi har byggt ett litet .NET‑verktyg som hämtar observationer från SMHI:s öppna meteorologiska API, beräknar aggregerade mått (t.ex. medeltemperatur i Sverige för senaste timmen och total nederbörd i Lund för utvalda månader) och streamar stationsvisa temperaturer till konsolen. På vägen löste vi bl.a. JSON‑serialisering där tal ibland levereras som strängar, och vi lade till robust felhantering.

1. Mål & omfattning

Mål 1: Hämta senaste timmens temperaturer för alla relevanta stationer och beräkna ett riksmedel.

Mål 2: Hämta nederbördsdata för Lund för senaste månaderna och summera per vald period.

Mål 3: Streama stationsvärden (temperatur) till konsolen i realtid/sekvens, med tydlig loggning och möjlighet att avbryta.

I/O:

Input: SMHI:s REST‑endpoints (JSON). Inga egna filer eller databaser.

Output: Text i konsolen (sammanfattningar + radvisa stationer), samt fel/varningar till Console.Error.

2. Datakällor (SMHI API)

Vi använder SMHI:s Observations API ("metobs"). Relevanta endpoints:

Stationsuppsättning och metadata

GET /api/version/1.0/parameter/{parameterId}.json

Returnerar parameter → stationer (ID, namn, koordinater, m.m.).

Stationsuppsättning – observationsdata

GET /api/version/1.0/parameter/{parameterId}/station-set/all/period/latest-hour/data.json

GET /api/version/1.0/parameter/{parameterId}/station-set/all/period/latest-day/data.json

Enskild station – observationsdata

GET /api/version/1.0/parameter/{parameterId}/station/{stationId}/period/latest-day/data.json

GET /api/version/1.0/parameter/{parameterId}/station/{stationId}/period/latest-months/data.json

Parameter‑ID:n hämtas från SMHI:s dokumentation. I vår kod använde vi en konstant för temperatur (MetObs.TemperatureParam). För nederbörd valde vi motsvarande parameter för ackumulerad nederbörd (1h/dygn beroende på dataset), samt filtrerade på station Lund.

Obs: SMHI kan leverera observationer där vissa tidssteg saknas eller är fördröjda. Tomma/tillfälligt otillgängliga värden hanteras som n/a i loggen.

3. Arkitektur & design
image

Nyckelkomponenter

ISmhiClient: Tunn service‑abstraktion för testbarhet och separation av ansvar.

SmhiClient (sealed): Implementerar HTTP‑anrop, parse och felhantering.

FlexibleDoubleConverter: System.Text.Json‑konverter som tillåter tal som antingen siffra eller sträng.

Models: DTO‑klasser för SMHI:s svar (stationslistor, data‑serier, m.m.).

Varför sealed + DI via HttpClient?

sealed för att undvika oavsiktligt arv.

HttpClient injiceras (ex. via IHttpClientFactory) för connection pooling och enklare testning.

4. SmhiClient – implementation & felhantering

4.1 JSON‑inställningar
image

Problem: SMHI returnerar ibland numeriska fält som strängar (t.ex. "11.7") och ibland som tal (11.7).

Lösning: FlexibleDoubleConverter försöker parse:a båda varianter (double?), och returnerar null vid tomma/icke‑numeriska.

4.2 HTTP‑mönster

using var req = new HttpRequestMessage(HttpMethod.Get, url);
using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
if (!resp.IsSuccessStatusCode) { Console.Error.WriteLine($"[http] {resp.StatusCode} for {url}"); return null; }
try { return await resp.Content.ReadFromJsonAsync(_jsonOptions, ct); }
catch (Exception ex) { Console.Error.WriteLine($"[http] JSON parse failed for {url}: {ex.Message}"); return null; }

ResponseHeadersRead minskar latency innan hela kroppen är nedladdad.

Loggning: HTTP‑fel och JSON‑parse‑fel loggas till stderr och anropet returnerar null → uppströms kod kan besluta att hoppa över stationen.

4.3 Offentliga metoder

GetLatestHourTemperatureAllAsync() → station‑set/all, senaste timmen.

GetStationsAsync(parameterId) → metadata för valda parametern.

GetLatestMonthsForStationAsync(parameterId, stationId) → tidsserie för senaste månader för vald station.

GetLatestDayTemperatureAllAsync() och GetLatestDayTemperatureForStationAsync(stationId) → dagsfönster.

5. Konsolappen – logik & metoder

5.1 Medeltemperatur för Sverige (senaste timmen)

Algoritm:

Hämta alla stationer som rapporterat temperatur den senaste timmen.

Släng bort trasiga värden (n/a, saknas, konstiga strängar). Vår FlexibleDoubleConverter gör att både "7.4" och 7.4 tolkas korrekt som tal.

Räkna ett enkelt medelvärde (alla stationer väger lika):

Medel = (summa av alla giltiga temperaturer) / (antal stationer som hade ett giltigt tal)

Avrunda till en decimal för att bli lättläst (t.ex. 7,73 → 7,7 °C).

Viktigt: Det här är ett enkelt medel. Vi väger inte stationer efter geografi eller befolkning – varje station räknas lika. Det gör talet lätt att förstå och snabbt att räkna, men det är inte en perfekt “yta-viktad” bild av hela landet.

5.2 Total nederbörd i Lund (senaste månader)

Algoritm:

Hämta tidsserien för nederbörd (mm) för stationen i Lund för perioden “latest-months”.

Hoppa över saknade värden (n/a).

Summera alla millimeter i perioden:

Total nederbörd = mm₁ + mm₂ + … + mmₙ

Avrunda till en decimal: t.ex. 167,62 → 167,6 mm.

5.3 Streaming av stationsvisa temperaturer

Mål: Ge en "live"‑känsla genom att skriva ut en rad per station:

[stationId] Namn: 10,7 °C
[stationId] Namn: n/a
...

Saknade värden markeras n/a (vanligt förekommande p.g.a. fördröjningar/underhåll).

Värden är rimliga i körningen (ex. Västerås 9,8 °C, Hoburg ~12 °C, mm.), vilket stärker datakvaliteten.

6. Matematik & databehandling – detaljer

6.1 Urval av senaste datapunkt per station
Varje stationsserie kan innehålla flera punkter. Vi väljer senaste (störst observedAt/date i Unix ms). Om flera punkter har samma tidsstämpel används första eller den med bäst datakvalitetsflagga (om tillgängligt i modellen).

6.2 Tidsstämplar & ålder
SMHI anger tid som Unix millisekunder.

6.3 Fel & saknade värden
null, tomma strängar eller ej parsebara tal → hoppa över.

Stationer med 404/timeout loggas en gång i klienten och generatorn fortsätter.

7. Problem jag stötte på – och hur jag löste dem
JSON‑tal som strängar - SMHI blandar datatyper(tal/sträng). Lösning: Implementerade filerna FlexibleDoubleConverter och registrerade JsonSerializerOptions.
Många n/a i console log - Senaste timmen saknas ofta för många stationer. Lösning: Accepterade som normalfall, markerade tydligt som n/a. Kan dämpa resutlatet i slutet genom att inte visa stationer med resultatet N/A.
Format - Punkt & komma i decimaler, Lösning: Använde standardformat och lät systemet styra utskrift. exempelvis 7,6 °C.
HTTP/JSON-fel - Nätverk och API-fel. Konsekvent felhantering med IsSuccessStatuscode + ReadFromJsonAsync, returnera null.

8. Kvalitetskontroller

Sanity check: Stickprov i loggen visar realistiska värden (ex. kuststationer ~11–12 °C, norra inlandet några få grader, fjäll nära 0 °C).

Nollskydd: Alla aggregeringar skyddar mot tomma listor.

Fel‑tolerans: Enstaka fel (HTTP/JSON) stoppar inte hela körningen.

9. Körning & användning

Kör konsolappen ->

Skriver ut medeltemperatur för Sverige.

Skriver ut total nederbörd i Lund för de senaste månaderna (exemplet: 167,6 mm för 2025‑06..08).

Startar streaming: "Streaming station temperatures (press any key to cancel)…" och raddumpar [id] namn: värde/n-a tills avbrutet.

10. Förslag på vidareutveckling

Bättre täckning: Filtrera stationer (typ "A"), viktat medel (t.ex. latitud‑vikt eller rutnätsmedel).

UI: Lägg till TUI (t.ex. Spectre.Console) med tabeller, progress och färgkodning.

Cache: Memoisera 404 per station (TTL) för att minska brus.

Export: Skriv CSV/JSON för senare analys.

Larm: Tröskelvärden med notifiering (extrema värden, saknad data).

Slutsats
Projektet uppnår målen: vi hämtar aktuell observationsdata från SMHI, gör begripliga aggregeringar, och kan strömma stationsvisa temperaturer. De största praktiska utmaningarna var att få rätt API anrop för samtliga stationer, minska console log 404 errors för stationer utan värden.
