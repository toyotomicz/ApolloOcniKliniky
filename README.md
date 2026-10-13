# ApolloData – registrační portál očních klinik

Demonstrační full-stack aplikace pro technický pohovor: registrace **očních klinik** – veřejná stránka se třemi plány, registrace kliniky
s údaji podle plánu, přihlašování klinik jednorázovým kódem z e-mailu (bez hesla), portál kliniky se stavem
schválení a administrace se statistikami, seznamem klinik, schvalováním/zamítáním a interními poznámkami.

> **Nezávislý demonstrační projekt.** Nejde o oficiální službu ani integraci VZP či jiné instituce a nevyjadřuje
> žádnou podporu z jejich strany. „ApolloData“ je pracovní název (změna v `frontend/src/config/branding.ts`
> a `APP_NAME`). Názvy a obsah plánů jsou provizorní, bez cen. Aplikace nezpracovává žádné údaje o pacientech.

---

## Obsah

1. [Architektura a technologie](#architektura-a-technologie)
2. [Rychlý start (Docker)](#rychlý-start-docker)
3. [Konfigurace (.env)](#konfigurace-env)
4. [E-mail: Gmail nebo Mailpit](#e-mail-gmail-nebo-mailpit)
5. [Administrátor](#administrátor)
6. [Ukázkový scénář (demo)](#ukázkový-scénář-demo)
7. [Spuštění bez Dockeru](#spuštění-bez-dockeru)
8. [Databáze a migrace](#databáze-a-migrace)
9. [Testy](#testy)
10. [API](#api)
11. [Bezpečnost](#bezpečnost)
12. [Předpoklady, omezení a další kroky](#předpoklady-omezení-a-další-kroky)
13. [Řešení problémů](#řešení-problémů)

---

## Architektura a technologie

```
Prohlížeč ──► web (Nginx: SPA + reverse proxy /api) ──► api (ASP.NET Core 10) ──► db (PostgreSQL 17)
                                                           └──► SMTP (Gmail / Mailpit)
```

| Vrstva | Technologie |
|---|---|
| Frontend | React 19, TypeScript (strict), Vite 8, React Router 8, React Hook Form + Zod, Lucide, vlastní CSS design systém (bez UI knihoven) sladěný s apollodata.cz – barvy `#313336`/`#c93a32`, písma Open Sans, Saira a Nunito (self-hosted), tokeny v `frontend/src/styles/tokens.css` |
| Backend | ASP.NET Core Minimal API (.NET 10), EF Core 10 + Npgsql, MailKit, ASP.NET Core Identity `PasswordHasher`, ProblemDetails, rate limiting, antiforgery |
| Databáze | PostgreSQL 17, migrace EF Core (commitnuté v `backend/src/ApolloData.Api/Data/Migrations`) |
| Testy | xUnit v3 + Testcontainers (skutečný PostgreSQL), Vitest + React Testing Library |
| Provoz | Docker Compose (db, api, web, volitelně mailpit), Nginx pro produkční build SPA |

```
apollo/
├─ backend/
│  ├─ src/ApolloData.Api/
│  │  ├─ Domain/          entity (Clinic, ClinicLoginChallenge, Administrator, UserSession, …)
│  │  ├─ Data/            DbContext, Fluent API konfigurace, migrace
│  │  ├─ Plans/           PlanCatalog – jediný zdroj definic plánů
│  │  ├─ Clinics/         registrace, portál kliniky, validace, DTO
│  │  ├─ Auth/            OTP, relace, cookie schémata, administrátorské účty
│  │  ├─ Admin/           dashboard, seznam a detail klinik, změny stavu
│  │  ├─ Email/           MailKit sender, šablona e-mailu
│  │  ├─ Common/          validace, ProblemDetails, globální zpracování výjimek
│  │  └─ Hosting/         registrace služeb, pipeline, CSRF middleware, údržbové příkazy
│  └─ tests/ApolloData.Api.Tests/   integrační a jednotkové testy
├─ frontend/
│  ├─ src/{api,auth,components,layouts,pages,lib,styles,config}
│  └─ nginx/              konfigurace Nginx (SPA fallback, proxy, bezpečnostní hlavičky)
├─ scripts/               init-env.ps1, reset-admin.ps1, run-api-local.ps1
├─ docker-compose.yml     hlavní stack
├─ docker-compose.dev.yml publikuje DB/Mailpit na localhost pro vývoj bez Dockeru
└─ .env.example           šablona konfigurace (jen zástupné hodnoty)
```

**Klíčová rozhodnutí**

- **Plány** jsou definovány na jednom místě – `backend/src/ApolloData.Api/Plans/PlanCatalog.cs`. Frontend z
  `GET /api/plans` vykresluje karty plánů i kroky registrace; backend podle stejného katalogu validuje.
  Basic = základní údaje; Standard = + provozní kontakt; Premium = + provozní kontakt + konfigurace služby.
- **Relace** jsou serverové: HttpOnly cookie nese jen ID relace, která se při každém požadavku ověřuje v tabulce
  `UserSessions` (odhlášení/odvolání platí okamžitě). Klinika a administrátor mají oddělená cookie schémata,
  takže lze mít v jednom prohlížeči otevřený portál kliniky i administraci.
- **Vlastnictví dat**: endpointy kliniky nemají v URL žádné ID – klinika se vždy určuje ze serverové relace.

---

## Rychlý start (Docker)

Předpoklady: Windows 10/11 s **Docker Desktop** (WSL 2), ~4 GB volného místa na disku pro image.

```bash
powershell -ExecutionPolicy Bypass -File scripts/init-env.ps1 -Mailpit
```

Skript vytvoří `.env` z `.env.example` a vygeneruje náhodná hesla (`POSTGRES_PASSWORD`, `ADMIN_PASSWORD`,
`OTP_HASH_KEY`). Přepínač `-Mailpit` nastaví lokální zachytávání e-mailů (nic neodchází ven). Bez něj se použije
šablona pro Gmail – viz níže.

```bash
docker compose up --build
```

Při prvním startu API automaticky aplikuje migrace a vytvoří administrátora. Data se ukládají do pojmenovaného
volume `db-data` a restart kontejnerů je nesmaže.

| Adresa | Co tam je |
|---|---|
| http://localhost:8080 | veřejná stránka, registrace, přihlášení kliniky (`/prihlaseni`), portál (`/portal`) |
| http://localhost:8080/admin | administrace (přihlášení `/admin/prihlaseni`) |
| http://localhost:8025 | Mailpit – doručené kódy (jen s profilem `mailpit`) |
| http://localhost:8080/swagger | Swagger UI (jen při `ENABLE_SWAGGER=true` nebo `ASPNETCORE_ENVIRONMENT=Development`) |

Databáze nemá ve výchozí konfiguraci publikovaný port; API je dostupné jen přes Nginx.

Zastavení: `docker compose down` (data zůstanou). Úplné smazání včetně dat: `docker compose down -v`.

---

## Konfigurace (.env)

Soubor `.env` se **nikdy necommituje** (je v `.gitignore`). Všechny proměnné:

| Proměnná | Výchozí | Popis |
|---|---|---|
| `WEB_PORT` | `8080` | port, na kterém Nginx publikuje aplikaci |
| `APP_NAME` | `ApolloData` | název v e-mailech |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Development` zapne Swagger a detail výjimek v odpovědích |
| `ENABLE_SWAGGER` | `false` | Swagger UI i mimo Development |
| `COOKIE_SECURE` | `true` | příznak `Secure` u relačních cookies; prohlížeče jej akceptují i na `http://localhost`. `false` jen pro přístup přes HTTP z jiného počítače |
| `REPORTING_TIME_ZONE` | `Europe/Prague` | časové pásmo pro „dnes“ a denní sloupce grafu |
| `POSTGRES_DB` / `POSTGRES_USER` | `apollodata` / `apollo` | databáze a uživatel |
| `POSTGRES_PASSWORD` | – (povinné) | heslo DB; pouze písmena a číslice |
| `ADMIN_USERNAME` | `admin` | uživatelské jméno počátečního administrátora |
| `ADMIN_PASSWORD` | – (povinné při 1. startu) | min. 12 znaků; bez něj API při prázdné databázi odmítne nastartovat |
| `OTP_HASH_KEY` | náhodný za běhu | klíč HMAC pro otisky jednorázových kódů |
| `SMTP_HOST` / `SMTP_PORT` | `smtp.gmail.com` / `587` | SMTP server |
| `SMTP_USERNAME` / `SMTP_PASSWORD` | – | přihlašovací údaje SMTP (Gmail: adresa + App Password) |
| `SMTP_FROM_EMAIL` / `SMTP_FROM_NAME` | = username / `ApolloData` | odesílatel |
| `SMTP_USE_STARTTLS` | `true` | `true` = STARTTLS (Gmail 587); `false` = bez TLS (Mailpit), na portu 465 implicitní TLS |
| `COMPOSE_PROFILES` | – | `mailpit` spustí i kontejner Mailpit |
| `MAILPIT_UI_PORT` | `8025` | port webového rozhraní Mailpitu |

Pokročilé (appsettings / proměnné prostředí): `Auth__OtpLifetimeMinutes` (10), `Auth__OtpMaxAttempts` (5),
`Auth__OtpResendCooldownSeconds` (60), `Auth__OtpMaxCodesPerHour` (5), `Auth__AdminMaxFailedAttempts` (5),
`Auth__AdminLockoutMinutes` (15), `Auth__ClinicSessionHours`/`AdminSessionHours` (8),
`RateLimiting__*Permits` a `RateLimiting__WindowSeconds` (limity na IP adresu), `CORS_ALLOWED_ORIGINS`
(jen pokud SPA běží na jiném originu), `DATABASE_MIGRATE_ON_STARTUP` (`true`).

---

## E-mail: Gmail nebo Mailpit

### Gmail (skutečné doručení)

1. Na účtu Google zapněte **dvoufázové ověření** (Účet Google → Zabezpečení).
2. Vytvořte **heslo aplikace**: https://myaccount.google.com/apppasswords → název např. „ApolloData“ → zkopírujte
   16znakové heslo.
3. V **lokálním** `.env` nastavte:
   ```
   SMTP_HOST=smtp.gmail.com
   SMTP_PORT=587
   SMTP_USE_STARTTLS=true
   SMTP_USERNAME=vase.adresa@gmail.com
   SMTP_PASSWORD=<heslo aplikace – zadejte jen do .env>
   SMTP_FROM_EMAIL=vase.adresa@gmail.com
   ```
   a zakomentujte/odstraňte `COMPOSE_PROFILES=mailpit` a SMTP řádky pro Mailpit.
4. `docker compose up -d --build api` (API načte novou konfiguraci).
5. Test: zaregistrujte kliniku se svou skutečnou adresou nebo na `/prihlaseni` požádejte o kód pro již
   registrovaný e-mail. Kód přijde do schránky (zkontrolujte i spam).

Heslo nikdy nevkládejte do zdrojových souborů, dokumentace ani do chatu. Při chybě SMTP API zapíše do logu
bezpečnou diagnostiku (host, port, typ chyby, SMTP kód) – nikdy heslo ani kód: `docker compose logs api`.

### Mailpit (lokálně, bez odesílání)

`scripts/init-env.ps1 -Mailpit` vše nastaví. Ručně: v `.env` odkomentujte blok „Option B“
(`COMPOSE_PROFILES=mailpit`, `SMTP_HOST=mailpit`, `SMTP_PORT=1025`, `SMTP_USE_STARTTLS=false`).
Kódy si přečtete na http://localhost:8025.

---

## Administrátor

- Veřejná registrace administrátorů neexistuje.
- Při startu API se podle `ADMIN_USERNAME`/`ADMIN_PASSWORD` **vytvoří** administrátor, pokud ještě neexistuje.
  Seedování je idempotentní – existující účet se neduplikuje a jeho heslo se **nikdy** automaticky nepřepíše.
- Chybí-li `ADMIN_PASSWORD` a v databázi není žádný administrátor, API skončí s jasnou chybou konfigurace
  (žádné výchozí heslo).
- **Změna/obnova hesla** (lokální údržbový příkaz, není dostupný přes HTTP): změňte `ADMIN_PASSWORD` v `.env` a spusťte
  ```bash
  powershell -File scripts/reset-admin.ps1
  ```
  (= `docker compose run --rm api reset-admin-password`). Účet se vytvoří nebo resetuje, zruší se zámek a odhlásí
  se jeho existující relace.
- Po 5 neúspěšných pokusech se účet na 15 minut zamkne; přihlášení je navíc omezeno na IP adresu.

---

## Ukázkový scénář (demo)

Doporučeno s Mailpitem (http://localhost:8025 v druhé záložce).

1. Otevřete http://localhost:8080 – úvod a tři plány.
2. U plánu **Standard** klikněte na „Zvolit Standard“.
3. Vyplňte údaje kliniky (např. IČO `25596641` má platný kontrolní součet), pokračujte – plán Standard přidá krok
   **Provozní kontakt**; Premium navíc **Konfiguraci služby**. Prázdné kroky ukážou validaci.
4. V souhrnu potvrďte zásady zpracování a odešlete. Registrace se uloží ve stavu **Čeká na posouzení**.
5. V Mailpitu otevřete e-mail s kódem a zadejte jej → e-mail se ověří a otevře se **portál kliniky**.
6. Portál ukazuje plán, údaje, datum registrace, ověření e-mailu a stav **Čeká na posouzení** s dalšími kroky.
7. Odhlaste se (vpravo nahoře).
8. Otevřete http://localhost:8080/admin a přihlaste se (`admin` + heslo z `.env`).
9. **Přehled**: metriky (celkem, nové dnes, čeká, schválené, zamítnuté, přihlášené kliniky dnes), graf 7 dní,
   poslední aktivita.
10. **Kliniky**: vyhledejte název/e-mail/IČO, filtrujte stavem a plánem, řaďte, stránkujte.
11. Otevřete detail kliniky → **Schválit registraci** (nebo Zamítnout – důvod je povinný a uvidí jej klinika),
    volitelně interní poznámka. Akce vyžaduje potvrzení.
12. Znovu se přihlaste jako klinika (`/prihlaseni`, nový kód z Mailpitu) – portál ukazuje nový stav a zprávu;
    interní poznámka se klinice nezobrazí.
13. V administraci zkontrolujte změněné statistiky a **Historii stavů** v detailu (kdo, kdy, z jakého stavu, důvod).

Žádná demo data se nenahrávají automaticky – vše v ukázce vzniká skutečnou registrací a je uloženo v PostgreSQL.

---

## Spuštění bez Dockeru

Pro vývoj (hot reload). Potřebujete .NET SDK 10, Node.js 22.12+ a PostgreSQL (nejjednodušeji z kontejneru):

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml --profile mailpit up -d db mailpit
```

```bash
powershell -File scripts/run-api-local.ps1
```

```bash
cd frontend && npm install && npm run dev
```

API běží na http://localhost:5080 (Swagger http://localhost:5080/swagger), SPA na http://localhost:5173 –
Vite proxyuje `/api` na API, takže frontend volá vždy relativní `/api`. Skript `run-api-local.ps1` načte `.env`,
přesměruje DB i Mailpit na `localhost` a spustí API v režimu Development (necertifikované HTTP → `COOKIE_SECURE=false`).

---

## Databáze a migrace

- Schéma je spravováno migracemi EF Core (`backend/src/ApolloData.Api/Data/Migrations`, commitnuté).
- API je při startu aplikuje (`DATABASE_MIGRATE_ON_STARTUP=true`); existující data se nemažou.
- Nová migrace (z adresáře `backend`):
  ```bash
  dotnet tool restore
  ```
  ```bash
  dotnet ef migrations add NazevZmeny --project src/ApolloData.Api --output-dir Data/Migrations
  ```
- Hlavní tabulky: `Clinics` (plánové údaje jako owned types, `xmin` jako token souběžnosti), `ClinicLoginChallenges`
  (pouze HMAC kódu), `Administrators`, `UserSessions`, `ClinicStatusChanges` (audit), `AdminNotes`, `ActivityEvents`.
- Časy se ukládají v UTC (`timestamptz`).

---

## Testy

```bash
cd backend && dotnet test
```

Integrační testy spouštějí skutečný PostgreSQL přes **Testcontainers** (vyžaduje běžící Docker). E-mail je
nahrazen falešným odesílatelem, čas `FakeTimeProvider` – testy nikdy nepoužívají Gmail.

```bash
cd frontend && npm test
```

Pokrytí (výběr): validace registrace a plánových polí, IČO, uložení do DB (UTC, normalizace), duplicitní e-mail,
over-posting, OTP (hash, expirace, znovupoužití, nahrazení novým kódem, limit pokusů, cooldown, neznámý e-mail
se stejnou odpovědí, výpadek SMTP), odvolání relace po odhlášení, CSRF, vlastnictví dat mezi klinikami,
autorizace administrátorských endpointů, zámek účtu, idempotentní seeding, schválení/zamítnutí s auditem
a soukromou poznámkou, optimistická souběžnost, vyhledávání/filtry/stránkování, statistiky a denní řada,
rate limiting; na frontendu průchod průvodcem registrace, mapování serverových chyb, přihlášení kódem,
přesměrování nepřihlášených, dashboard a dialog zamítnutí.

Výsledky posledního běhu jsou v [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md).

---

## API

Všechny odpovědi jsou JSON; chyby ve formátu RFC 7807 (`application/problem+json`, validační chyby v `errors`
podle názvů polí). Nebezpečné metody vyžadují hlavičku `X-CSRF-TOKEN` (token z `GET /api/security/csrf`).

| Metoda | Cesta | Přístup | Popis |
|---|---|---|---|
| GET | `/api/plans` | veřejné | definice plánů |
| GET | `/api/security/csrf` | veřejné | antiforgery token |
| POST | `/api/registrations` | veřejné, rate limit | registrace kliniky (+ odeslání ověřovacího kódu) |
| POST | `/api/clinic-auth/request-code` | veřejné, rate limit | vyžádání kódu (vždy stejná odpověď 202) |
| POST | `/api/clinic-auth/verify-code` | veřejné, rate limit | ověření kódu → relace kliniky |
| POST | `/api/clinic-auth/logout` | – | odhlášení kliniky |
| GET/PUT | `/api/clinic/me` | klinika | vlastní údaje / úprava povolených polí |
| POST | `/api/admin-auth/login` | veřejné, rate limit | přihlášení administrátora |
| POST | `/api/admin-auth/logout` | – | odhlášení |
| GET | `/api/admin-auth/me` | admin | přihlášený administrátor |
| GET | `/api/admin/dashboard` | admin | metriky, graf 7 dní, aktivita |
| GET | `/api/admin/clinics` | admin | `search`, `status`, `plan`, `sort` (`createdAt`/`name`/`status`/`plan`), `direction`, `page`, `pageSize` |
| GET | `/api/admin/clinics/{id}` | admin | detail, historie stavů, poznámky, verze |
| POST | `/api/admin/clinics/{id}/status` | admin | `{ status, message?, internalNote?, version }` |
| POST | `/api/admin/clinics/{id}/notes` | admin | `{ text }` |
| GET | `/health/live`, `/health/ready` | interní | health checky (ready ověřuje DB) |

**Definice metrik dashboardu** (`DashboardService.cs`): *Registrované kliniky* = všechny záznamy; *Nové dnes* =
`CreatedAt` v aktuálním dni dle `REPORTING_TIME_ZONE`; *Čeká/Schválené/Zamítnuté* = aktuální stav; *Přihlášené
kliniky dnes* = počet **různých** klinik s úspěšným přihlášením kódem dnes (z tabulky aktivit – nejde o registrace);
graf = registrace po lokálních dnech za posledních 7 dní včetně dneška, chybějící dny = 0.

---

## Bezpečnost

Implementováno:

- Jednorázové kódy: 6 číslic z `RandomNumberGenerator`, uložen jen **HMAC-SHA256** (vázaný na ID výzvy),
  platnost 10 min, max. 5 pokusů, atomická spotřeba podmíněným `UPDATE`, nový kód ruší starší, cooldown 60 s
  a max. 5 kódů/h na účet, rate limit na IP. Kód se neloguje.
- Neznámý e-mail dostane stejnou odpověď jako registrovaný (bez úniku existence účtu).
- Hesla administrátorů: ASP.NET Core Identity `PasswordHasher` (PBKDF2), zámek účtu, rate limit, konstantní
  chování pro neznámé jméno.
- Relace: HttpOnly, `SameSite=Strict`, `Secure` (konfigurovatelné), serverová validace a odvolání, 8 h, odhlášení.
- CSRF: antiforgery token v hlavičce u všech nebezpečných požadavků + SameSite=Strict; CORS jen pro výslovně
  nastavené originy (ve výchozím stavu same-origin přes Nginx).
- Autorizace na serveru: oddělené politiky Clinic/Admin; klinika nemá v API žádné ID → nemůže číst cizí data.
- Explicitní DTO (žádné entity v API, žádný over-posting), validace na serveru, EF Core parametrizované dotazy
  (LIKE wildcardy escapované).
- Audit změn stavu v transakci (jeden `SaveChanges`) s optimistickou souběžností (`xmin`).
- Produkční chybové odpovědi bez detailů výjimek; strukturované JSON logy; `Cache-Control: no-store` u API.
- Nginx: CSP, `X-Frame-Options`, `nosniff`, `Referrer-Policy`, `Permissions-Policy`; DB bez publikovaného portu;
  API kontejner běží jako ne-root uživatel.

Před produkčním nasazením navíc: TLS (reverzní proxy / load balancer) a HSTS; správa tajemství mimo `.env`
(Key Vault, Docker secrets); šifrování klíčů Data Protection (log varuje „No XML encryptor configured“ – klíče
jsou ve volume `api-keys` nešifrované) a sdílené úložiště klíčů při více instancích; nastavit `OTP_HASH_KEY`;
distribuovaný rate limiting při více instancích; zálohování DB; dedikovaný e-mailový provider (SPF/DKIM) místo
Gmailu; právně posouzené zásady zpracování údajů; monitoring a centralizace logů.

---

## Předpoklady, omezení a další kroky

Zadání bylo rekonstruováno z ústního pohovoru; tyto body jsou zvolené výchozí hodnoty (snadno změnitelné):

- **Plány** Basic/Standard/Premium, jejich texty a dodatečná pole jsou provizorní (`PlanCatalog.cs`). Žádné ceny.
- **Stavový model**: Pending / Approved / Rejected. Administrátor může nastavit kterýkoli jiný stav (včetně „Vrátit
  k posouzení“); zamítnutí vyžaduje důvod viditelný klinice, zpráva klinice je i u jiných stavů volitelná.
- **Ověření e-mailu ≠ schválení**: ověření nastane prvním úspěšným zadáním kódu; schválení je vždy ruční.
  Schválit lze i kliniku bez ověřeného e-mailu (UI na to upozorní).
- Klinika může upravit kontaktní osobu, telefon, adresu a plánová pole; **název, IČO, e-mail a plán** mění jen
  administrace (zatím bez UI – změna e-mailu by vyžadovala nové ověření). Úpravy se zaznamenávají do aktivit.
- Registrace hned odešle ověřovací kód. Při výpadku SMTP se registrace uloží a uživatel může kód poslat znovu.
- Registrace s již použitým e-mailem vrací 409 (registrace ze své podstaty existenci účtu prozrazuje; přihlášení ne).
  Odezva `request-code` se u existujícího e-mailu může časově lišit (odesílá se e-mail) a při výpadku SMTP vrací
  503 – vědomý kompromis ve prospěch srozumitelné chyby.
- Časové pásmo statistik: Europe/Prague.
- Bez demo seedu – data vznikají v ukázce reálnou registrací.

Možná rozšíření (podle priority): e-mailové notifikace klinice při změně stavu; administrace změny e-mailu/plánu
kliniky; export seznamu (CSV); správa více administrátorů a rolí; trvalé/sdílené úložiště rate limitů a Data
Protection klíčů pro více instancí; E2E testy (Playwright); lokalizace do angličtiny.

---

## Řešení problémů

| Problém | Řešení |
|---|---|
| `POSTGRES_PASSWORD must be set` | chybí `.env` → `scripts/init-env.ps1` |
| API se restartuje, log „ADMIN_PASSWORD is not configured“ | doplňte `ADMIN_PASSWORD` (min. 12 znaků) do `.env`, `docker compose up -d` |
| Kód nepřichází (Gmail) | `docker compose logs api` – hledejte „SMTP delivery failed“; ověřte App Password, 2FA, `SMTP_USE_STARTTLS=true`, port 587, spam |
| Kód nepřichází (Mailpit) | běží kontejner `mailpit`? (`COMPOSE_PROFILES=mailpit`), `SMTP_HOST=mailpit` |
| „Platnost formuláře vypršela“ | obnovte stránku (nový CSRF token) |
| Po přihlášení zpět na login při přístupu z jiného PC přes HTTP | `COOKIE_SECURE=false` (jen lokálně) nebo nasadit HTTPS |
| Port 8080/8025 je obsazený | změňte `WEB_PORT` / `MAILPIT_UI_PORT` |
| Testy backendu selžou na Dockeru | Testcontainers potřebuje běžící Docker Desktop |
| Docker Desktop „unable to start“ / `read-only file system` | zkontrolujte volné místo na disku (image a build cache potřebují několik GB), poté `wsl --shutdown` a znovu spustit Docker Desktop; úklid: `docker builder prune` |
| Změna hesla administrátora | `scripts/reset-admin.ps1` |
| Začít s prázdnou DB | `docker compose down -v` (smaže všechna data) |
