namespace ffw.Data;

/// <summary>
/// Die einzelnen Rechte der Anwendung. Ein Recht ist ein Claim vom Typ <see cref="ClaimTyp"/> an einer Rolle
/// (Tabelle AspNetRoleClaims). Jedes Recht ist zugleich der Name einer Autorisierungs-Policy.
/// </summary>
public static class Rechte
{
    public const string ClaimTyp = "ffw:recht";

    public const string FotosLesen = "Fotos.Lesen";
    public const string FotosBearbeiten = "Fotos.Bearbeiten";
    public const string DokumenteLesen = "Dokumente.Lesen";
    public const string DokumenteBearbeiten = "Dokumente.Bearbeiten";
    public const string BenutzerVerwalten = "Benutzer.Verwalten";

    public static readonly IReadOnlyList<string> Alle =
    [
        FotosLesen, FotosBearbeiten, DokumenteLesen, DokumenteBearbeiten, BenutzerVerwalten
    ];
}

/// <summary>Die Rechtegruppen (Rollen) und welche Rechte sie beim erstmaligen Anlegen bekommen.</summary>
public static class Rollen
{
    public const string Admin = "Admin";
    public const string Redakteur = "Redakteur";
    public const string Mitglied = "Mitglied";

    /// <summary>Reihenfolge, in der die Rollen in der Oberfläche angeboten werden.</summary>
    public static readonly IReadOnlyList<string> Alle = [Admin, Redakteur, Mitglied];

    public static IReadOnlyList<string> StandardRechte(string rolle) => rolle switch
    {
        Admin => Rechte.Alle,
        Redakteur => [Rechte.FotosLesen, Rechte.FotosBearbeiten, Rechte.DokumenteLesen, Rechte.DokumenteBearbeiten],
        Mitglied => [Rechte.FotosLesen, Rechte.DokumenteLesen],
        _ => []
    };

    public static string Beschreibung(string rolle) => rolle switch
    {
        Admin => "darf alles, auch Benutzer verwalten",
        Redakteur => "darf Fotos und Dokumente hochladen und löschen",
        Mitglied => "darf lesen",
        _ => ""
    };
}
