namespace ffw.Data;

/// <summary>Oberpunkt, unter dem Fotos zusammengefasst werden (z. B. "Einsätze", "Übungen").</summary>
public class FotoKategorie
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public List<Foto> Fotos { get; set; } = new();
}

/// <summary>Ein hochgeladenes Foto. Die Bilddateien liegen im Dateisystem, nicht in der Datenbank.</summary>
public class Foto
{
    public int Id { get; set; }

    public int KategorieId { get; set; }

    public FotoKategorie? Kategorie { get; set; }

    /// <summary>Basis des Dateinamens im Foto-Verzeichnis (zufällig, nicht erratbar, kein Originalname).</summary>
    public Guid DateiId { get; set; }

    public DateTime HochgeladenAm { get; set; } = DateTime.UtcNow;
}
