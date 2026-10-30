# Avhengigheter

Versjoner kontrollert mot nuget.org og npmjs.com 2026-10-30. .NET-pakker er låst med sentral versjonsstyring
(`Directory.Packages.props`) og `packages.lock.json` per prosjekt (`dotnet restore --locked-mode`). npm-pakker er låst
med eksakte versjoner i `web/package.json` og `web/package-lock.json` (`npm ci`).

## Kjøretid

| Pakke | Versjon | Lisens | Bruk |
|---|---|---|---|
| .NET / ASP.NET Core | 10.0 (LTS) | MIT | Backend |
| DocumentFormat.OpenXml | 3.5.1 | MIT | Navigering og validering av OOXML |
| Microsoft.Data.Sqlite | 10.0.14 | MIT | Jobbdata |
| SkiaSharp | 4.154.0 | MIT | Beskjæring, skalering, bildesammenligning |
| ZXing.Net | 0.16.11 | Apache-2.0 | Lokal QR-dekoding |
| Anthropic (C#-SDK) | 13.3.0 | MIT | Valgfri skyleverandør |
| react, react-dom | 19.3.1 | MIT | Grensesnitt |
| zod | 4.6.7 | MIT | Validering av API-kontrakter i frontend |

Windows-komponenter (ingen pakke): Office COM (Word/PowerPoint), `Windows.Data.Pdf`, `Windows.Media.Ocr`.

## Utvikling og test

| Pakke | Versjon | Lisens |
|---|---|---|
| vite | 8.3.5 | MIT |
| @vitejs/plugin-react | 6.1.2 | MIT |
| typescript | 7.0.2 | Apache-2.0 |
| vitest | 5.0.5 | MIT |
| xunit.v3 | 4.0.2 | Apache-2.0 |
| xunit.runner.visualstudio | 4.0.1 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 18.11.0 | MIT |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | MIT |
| Microsoft.Playwright | 1.64.1 | Apache-2.0 |
| Deque.AxeCore.Playwright (axe-core) | 4.14.0 | MPL-2.0 (bare i tester, ikke distribuert med appen) |

Node.js 26.13.0 (LTS) og .NET SDK 10.0.403 ble installert med winget på utviklingsmaskinen.
