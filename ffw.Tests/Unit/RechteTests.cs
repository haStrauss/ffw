using ffw.Data;

namespace ffw.Tests.Unit;

public class RechteTests
{
    [Fact]
    public void Admin_hat_alle_Rechte()
    {
        Assert.Equal(Rechte.Alle.OrderBy(r => r), Rollen.StandardRechte(Rollen.Admin).OrderBy(r => r));
    }

    [Fact]
    public void Mitglied_darf_nur_lesen()
    {
        var rechte = Rollen.StandardRechte(Rollen.Mitglied);

        Assert.Equal(new[] { Rechte.DokumenteLesen, Rechte.FotosLesen }, rechte.OrderBy(r => r));
    }

    [Fact]
    public void Redakteur_darf_Fotos_und_Dokumente_bearbeiten_aber_keine_Benutzer_verwalten()
    {
        var rechte = Rollen.StandardRechte(Rollen.Redakteur);

        Assert.Contains(Rechte.FotosBearbeiten, rechte);
        Assert.Contains(Rechte.DokumenteBearbeiten, rechte);
        Assert.DoesNotContain(Rechte.BenutzerVerwalten, rechte);
    }

    [Fact]
    public void Unbekannte_Gruppe_hat_keine_Rechte()
    {
        Assert.Empty(Rollen.StandardRechte("Gast"));
    }

    [Fact]
    public void Kein_Mitglied_und_kein_Redakteur_hat_ein_Recht_das_es_nicht_gibt()
    {
        foreach (var rolle in Rollen.Alle)
        {
            Assert.All(Rollen.StandardRechte(rolle), recht => Assert.Contains(recht, Rechte.Alle));
        }
    }

    [Fact]
    public void Rechte_sind_eindeutig()
    {
        Assert.Equal(Rechte.Alle.Count, Rechte.Alle.Distinct().Count());
    }

    [Fact]
    public void Gruppen_erscheinen_in_der_vorgesehenen_Reihenfolge()
    {
        Assert.Equal(new[] { Rollen.Admin, Rollen.Redakteur, Rollen.Mitglied }, Rollen.Alle);
    }

    [Fact]
    public void Jede_Gruppe_hat_eine_Beschreibung()
    {
        Assert.All(Rollen.Alle, rolle => Assert.False(string.IsNullOrWhiteSpace(Rollen.Beschreibung(rolle))));
    }
}
