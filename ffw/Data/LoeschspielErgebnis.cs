namespace ffw.Data;

/// <summary>Ein Eintrag der Bestenliste des Löschspiels (ein beendeter Einsatz mit dem Namen des Spielers).</summary>
public class LoeschspielErgebnis
{
    public int Id { get; set; }

    /// <summary>Kennung des Einsatzes. Sie ist eindeutig, damit sich ein Einsatz nur einmal eintragen lässt.</summary>
    public Guid SpielId { get; set; }

    public string Name { get; set; } = "";

    public int Punkte { get; set; }

    /// <summary>Einsatzdauer in Sekunden (ohne Pausen).</summary>
    public int Sekunden { get; set; }

    public DateTime Datum { get; set; } = DateTime.UtcNow;
}
