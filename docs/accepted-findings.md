# Aanvaarde bevindingen

Eerste volledige pipeline-run op de **ongewijzigde** starter app: [run #3](https://github.com/NIKAT0/websec-unbreakable-g18/actions/runs/38239999731).
(Run #1 en #2 tellen niet: door een configuratiefout scanden Trivy en CodeQL toen niet volledig.)
De app bevat bewust zwakke plekken; die lossen we op in latere opdrachten.
Deze bevindingen laten de pipeline niet meer falen. Elke **nieuwe** kritieke bevinding doet dat wel.

## Wat is kritiek?

| Scan | Kritiek = pipeline faalt | Waarom deze grens |
|---|---|---|
| SCA (Trivy, retire.js) | HIGH of CRITICAL | CVSS 7 of hoger: aanvallen zijn realistisch en de schade is groot |
| SAST (Semgrep) | severity ERROR | WARNING-regels geven te veel ruis om op te blokkeren |
| SAST (CodeQL) | security-severity 7.0 of hoger | dezelfde grens als SCA |
| Secrets (Gitleaks) | elk gevonden geheim | een gelekt geheim staat voor altijd in de git-geschiedenis |

## Aanvaard

| # | Scan | Bevinding | Echt of false positive? | Waarom kan het nu? | Opgelost in | Lijst |
|---|---|---|---|---|---|---|
| 1 | Gitleaks | API-sleutel `ubk_live_…` in `app/appsettings.json:15` | echt patroon (geheim in code), maar de sleutel zelf is nep | het endpoint `push.unbreakable.example` bestaat niet (`.example` is gereserveerd), dus de sleutel geeft nergens toegang | opdracht 6 (geheimenbeheer) | `.gitleaksignore` |
| 2 | Trivy | `Newtonsoft.Json 12.0.1`, CVE-2024-21907 (HIGH) | echt lek in de bibliotheek, niet bereikbaar in de app | het lek zit in het **inlezen** van diep geneste JSON; de app gebruikt Newtonsoft enkel om eigen data te **schrijven** (export) | update naar 13.0.1 of hoger; aanvaard tot 31/12/2026 | `.trivyignore` |
| 3 | Semgrep | container draait als root (`app/Dockerfile`) | echt | de app staat niet open naar buiten (enkel 127.0.0.1) en bevat enkel testdata | threat model, dreiging 14 (non-root) | `.semgrep-accepted` |
| 4 | CodeQL | SQL-injectie in de zoekfunctie (`Program.cs`, `FromSqlRaw`) | echt | bewuste zwakke plek van de starter app; nodig als testdoel voor de WAF (stap 5) | geparametriseerde query (threat model) | `.codeql-accepted` |
| 5 | CodeQL | XSS (`js/xss`): zoekterm via jQuery `.html()` (`app.js`) | echt | bewuste zwakke plek; nodig als testdoel voor de WAF (stap 5) | `.text()` + Content-Security-Policy (opdracht 3) | `.codeql-accepted` |

## Gemeld, maar niet kritiek (blokkeert niet)

| Scan | Bevinding | Severity |
|---|---|---|
| retire.js | jQuery 3.3.1 (CVE-2019-11358, CVE-2020-11022, CVE-2020-11023) | MEDIUM |
| retire.js | Bootstrap 4.1.3 (CVE-2019-8331) | MEDIUM |
| Semgrep (enkel lokaal; de pipeline toont enkel ERROR) | `UseDeveloperExceptionPage()`: stacktrace zichtbaar voor iedereen (`Program.cs`) | WARNING |

## Wat de scans missen

- **Semgrep** vindt de SQL-injectie en de XSS niet, met geen enkele ruleset. **CodeQL** vindt ze wel (run #3); daarom zijn er twee SAST-tools.
- **Gitleaks** vindt het databankwachtwoord in `appsettings.json` en `docker-compose.yml` niet (geen herkenbaar patroon).

Een groene pipeline betekent dus niet dat de code veilig is.
