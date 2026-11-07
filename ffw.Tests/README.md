# Tests für ffw

```bash
dotnet test ffw.Tests/ffw.Tests.csproj
```

- **Unit-Tests** (`Unit/`): Rechte und Gruppen, Foto- und Dokumentspeicher (Dateityp-Prüfung, Verkleinern, Pixel-Limit).
  Brauchen nichts außer .NET.
- **Integrationstests** (`Integration/`): starten die ganze Anwendung gegen eine echte PostgreSQL-Datenbank
  (`postgres:16` wie in `docker-compose.yml`, per Testcontainers). **Docker muss dafür laufen.**
  Geprüft werden Anmeldung und Sperre, wer welche Seite und Datei sehen darf, und der `DatenbankInitialisierer`
  (der bei jedem Start bzw. Deploy läuft).

Nur ein Teil ausführen: `dotnet test ffw.Tests/ffw.Tests.csproj --filter "FullyQualifiedName~Unit"`.

## Wann läuft das automatisch?

Siehe `.github/workflows/tests.yml`: bei jedem Pull Request, jede Nacht gegen 03:00 Uhr (zusammen mit Paket-Schwachstellen-Check,
Docker-Build und Live-Prüfung von ffw-walting.de) und vor jedem Deployment (`deploy.yml`).
Die Live-Prüfung lässt sich auch von Hand ausführen: `bash scripts/smoke-test.sh https://ffw-walting.de`.
