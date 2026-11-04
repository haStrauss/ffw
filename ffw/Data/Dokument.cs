namespace ffw.Data;

/// <summary>Oberpunkt, unter dem Dokumente zusammengefasst werden (z. B. "Satzung", "Formulare").</summary>
public class DokumentKategorie
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public List<Dokument> Dokumente { get; set; } = new();
}

/// <summary>Ein hochgeladenes Dokument. Die Datei liegt im Dateisystem, nicht in der Datenbank.</summary>
public class Dokument
{
    public int Id { get; set; }

    public int KategorieId { get; set; }

    public DokumentKategorie? Kategorie { get; set; }

    /// <summary>Dateiname im Dokumenten-Verzeichnis (zufällig, nicht erratbar).</summary>
    public Guid DateiId { get; set; }

    /// <summary>Ursprünglicher Dateiname, so wie er Besuchern angezeigt und beim Download vorgeschlagen wird.</summary>
    public string Name { get; set; } = "";

    public long Groesse { get; set; }

    public DateTime HochgeladenAm { get; set; } = DateTime.UtcNow;
}
