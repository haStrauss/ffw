namespace ffw.Services;

/// <summary>
/// Die Schnittstellen unter /loeschspiel/api, die die Seite des Löschspiels für die Bestenliste braucht.
/// Sie sind öffentlich (wie die Seite selbst), die Absicherung gegen erfundene Ergebnisse steckt in <see cref="LoeschspielBestenliste"/>.
/// </summary>
public static class LoeschspielEndpunkte
{
    public sealed record ErgebnisAnfrage(string? Marke, string? Name, int Punkte, int Sekunden);

    public static void MapLoeschspielBestenliste(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/loeschspiel/api");

        // Die Top 10.
        api.MapGet("/bestenliste", async (LoeschspielBestenliste liste, HttpContext context, CancellationToken ct) =>
            Antwort(context, new { eintraege = await liste.Top10Async(ct) }));

        // Beim Start eines Einsatzes: Spielmarke, mit der sich das Ergebnis später eintragen lässt.
        api.MapPost("/start", (LoeschspielBestenliste liste, HttpContext context) =>
            Antwort(context, new { marke = liste.NeueSpielmarke() }));

        // Nach dem Einsatz: Würde diese Punktzahl unter die Top 10 kommen? (null = nein)
        api.MapGet("/platz", async (int punkte, LoeschspielBestenliste liste, HttpContext context, CancellationToken ct) =>
            Antwort(context, new { platz = await liste.PlatzFuerAsync(punkte, ct) }));

        // Name eintragen.
        api.MapPost("/ergebnis", async (ErgebnisAnfrage anfrage, LoeschspielBestenliste liste, HttpContext context, CancellationToken ct) =>
        {
            var ergebnis = await liste.EintragenAsync(anfrage.Marke, anfrage.Name, anfrage.Punkte, anfrage.Sekunden, ct);
            switch (ergebnis.Ausgang)
            {
                case LoeschspielBestenliste.Ausgang.Eingetragen:
                    return Antwort(context, new { platz = ergebnis.Platz, eintraege = await liste.Top10Async(ct) });
                case LoeschspielBestenliste.Ausgang.NameUngueltig:
                    return Antwort(context, new
                    {
                        fehler = $"Bitte einen Namen mit {LoeschspielBestenliste.MinNameLaenge} bis {LoeschspielBestenliste.MaxNameLaenge} Zeichen eingeben " +
                                 $"(Buchstaben, Ziffern, Leerzeichen und {LoeschspielBestenliste.ErlaubteSonderzeichen}).",
                    }, StatusCodes.Status400BadRequest);
                case LoeschspielBestenliste.Ausgang.MarkeUngueltig:
                    return Antwort(context, new { fehler = "Dieser Einsatz kann nicht mehr eingetragen werden. Starte einen neuen Einsatz." },
                        StatusCodes.Status400BadRequest);
                case LoeschspielBestenliste.Ausgang.Unplausibel:
                    return Antwort(context, new { fehler = "Dieses Ergebnis wurde nicht angenommen." }, StatusCodes.Status400BadRequest);
                case LoeschspielBestenliste.Ausgang.BereitsEingetragen:
                    return Antwort(context, new { fehler = "Dieser Einsatz ist schon eingetragen." }, StatusCodes.Status409Conflict);
                default:
                    return Antwort(context, new { fehler = "Die Punkte reichen inzwischen nicht mehr für die Top 10." }, StatusCodes.Status409Conflict);
            }
        });
    }

    // Antworten sind immer aktuell und dürfen nicht zwischengespeichert werden.
    private static IResult Antwort(HttpContext context, object daten, int statusCode = StatusCodes.Status200OK)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(daten, statusCode: statusCode);
    }
}
