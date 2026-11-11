(function () {
    'use strict';

    // =====================================================================
    //  SPIELMODELL
    //  Enthält alle Spielregeln und kommt ohne Browser (DOM) aus. Dadurch lässt
    //  es sich auch außerhalb des Browsers testen (siehe Ende der Datei).
    //  Alle Koordinaten liegen im Pixelraster des Hintergrundbilds (2200 x 949).
    // =====================================================================

    var WELT = { b: 2200, h: 949 };
    var DUESE = { x: 1100, y: 895 };   // Hier steht der Spieler mit dem Strahlrohr.
    var MAX_HP = 320;                  // Größter möglicher Brand ("Vollbrand").
    var MAX_FEUER = 10;                // So viele Brände können gleichzeitig brennen.

    // Die Ausrüstung. "rate" ist die Wassermenge bei 100 % in Spieleinheiten pro Sekunde,
    // "tank" der Wasservorrat, "zufluss" das, was pro Sekunde nachläuft.
    var GERAETE = [
        { kurz: 'Gartenschlauch', name: 'Gartenschlauch', literProMin: 25, rate: 10, reichweite: 520, tank: 60, zufluss: 3, dicke: 7, farbe: '#2f9e44', rohr: '#3d4650', kosten: 0 },
        { kurz: 'D-Rohr', name: 'D-Schlauch mit D-Strahlrohr', literProMin: 140, rate: 28, reichweite: 760, tank: 150, zufluss: 8, dicke: 11, farbe: '#e8b400', rohr: '#c61b21', kosten: 60 },
        { kurz: 'C-Rohr', name: 'C-Schlauch mit C-Strahlrohr', literProMin: 235, rate: 55, reichweite: 1050, tank: 300, zufluss: 16, dicke: 15, farbe: '#d1242f', rohr: '#9c1117', kosten: 220 },
        { kurz: 'B-Rohr', name: 'B-Schlauch mit B-Strahlrohr', literProMin: 400, rate: 110, reichweite: 1450, tank: 600, zufluss: 32, dicke: 20, farbe: '#e6ebef', rohr: '#c61b21', kosten: 600 }
    ];

    // Hier kann es brennen. Welche Stellen vorkommen, hängt von der Reichweite der Ausrüstung ab.
    var BRANDSTELLEN = [
        { x: 968, y: 682, name: 'Tor links' },
        { x: 1210, y: 682, name: 'Tor rechts' },
        { x: 1089, y: 616, name: 'Tormitte' },
        { x: 913, y: 814, name: 'Lautsprecher am Tor' },
        { x: 858, y: 860, name: 'Pflaster links' },
        { x: 1375, y: 860, name: 'Pflaster rechts' },
        { x: 902, y: 561, name: 'Scheinwerfer links' },
        { x: 1279, y: 563, name: 'Scheinwerfer rechts' },
        { x: 1386, y: 704, name: 'Wand rechts vom Tor' },
        { x: 740, y: 704, name: 'Tür am Anbau' },
        { x: 616, y: 671, name: 'Wand am Anbau' },
        { x: 935, y: 418, name: 'Wappen' },
        { x: 1133, y: 440, name: 'Schriftzug' },
        { x: 770, y: 363, name: 'Giebelwand links' },
        { x: 400, y: 810, name: 'Holzlager' },
        { x: 1617, y: 836, name: 'Blumenbeet' },
        { x: 1562, y: 704, name: 'Pavillon' },
        { x: 1595, y: 550, name: 'Pavillondach' },
        { x: 1375, y: 258, name: 'Dach rechts' },
        { x: 880, y: 209, name: 'Dach links' },
        { x: 1100, y: 110, name: 'Dachfirst' },
        { x: 440, y: 500, name: 'Dach des Anbaus' },
        { x: 286, y: 563, name: 'Fenster am Anbau' },
        { x: 1716, y: 363, name: 'Baum' },
        { x: 1749, y: 220, name: 'Baumkrone' },
        { x: 1705, y: 418, name: 'Wald rechts' },
        { x: 440, y: 396, name: 'Wald links' },
        { x: 330, y: 363, name: 'Waldrand links' },
        { x: 2035, y: 660, name: 'Nachbarscheune' },
        { x: 1980, y: 374, name: 'Wald hinten rechts' },
        { x: 110, y: 330, name: 'Solardach' },
        { x: 55, y: 270, name: 'Schornstein' },
        { x: 33, y: 572, name: 'Sträucher links' }
    ];

    function abstand(ax, ay, bx, by) {
        var dx = ax - bx, dy = ay - by;
        return Math.sqrt(dx * dx + dy * dy);
    }

    // Wie weit der Strahl kommt: Bei wenig Wasser ist er kurz, bei vollem Durchfluss erreicht er die volle Reichweite.
    // "fluss" ist der Anteil (0 bis 1) der Wassermenge, die tatsächlich ankommt.
    function reichweite(geraet, fluss) {
        return geraet.reichweite * (0.35 + 0.65 * Math.sqrt(Math.max(0, Math.min(1, fluss))));
    }

    // Wie viel Wasser pro Sekunde nötig ist, damit ein Brand überhaupt Schaden nimmt. Darunter verdampft das Wasser.
    function mindestrate(hp) {
        return 0.34 * Math.pow(Math.max(hp, 1), 0.75);
    }

    // So schnell wächst ein Brand von selbst.
    function wachstum(hp) {
        return 0.9 + 0.025 * hp;
    }

    function radius(hp) {
        return 24 + 4.2 * Math.sqrt(Math.max(hp, 0));
    }

    function groesse(hp) {
        if (hp < 30) return 'klein';
        if (hp < 80) return 'mittel';
        if (hp < 180) return 'groß';
        return 'Vollbrand';
    }

    // Mehr Wasser als dieser Wert bringt nichts mehr, es wird nur verschwendet (Überspritzen).
    // Die Untergrenze sorgt dafür, dass sich auch ein fast gelöschter Brand noch richtig löschen lässt.
    function wirkrate(hp) {
        return Math.max(3 * mindestrate(hp), 14);
    }

    // Wirkung des Wassers: Unterhalb der Mindestrate passiert nichts (das Wasser verdampft),
    // darüber steigt die Wirkung bis zur Wirkrate.
    function loeschwirkung(rate, hp) {
        return Math.max(0, Math.min(rate, wirkrate(hp)) - mindestrate(hp));
    }

    function intervall(zeit) {
        return Math.max(3.2, 9 - zeit * 0.014);
    }

    function neuesSpiel() {
        return {
            zeit: 0,
            punkte: 0,
            gesamt: 0,
            geloescht: 0,
            stufe: 0,
            tank: GERAETE[0].tank,
            schaden: 0,
            feuer: [],
            naechsterBrand: 1.5,
            naechsteId: 1,
            vorbei: false,
            ereignisse: [],
            strahl: null
        };
    }

    function stellenInReichweite(sp, mitte, maxAbstand, minAbstand) {
        var g = GERAETE[sp.stufe];
        return BRANDSTELLEN.filter(function (s) {
            if (abstand(DUESE.x, DUESE.y, s.x, s.y) > g.reichweite * 0.97) return false;
            if (mitte && abstand(mitte.x, mitte.y, s.x, s.y) > maxAbstand) return false;
            return sp.feuer.every(function (f) { return abstand(f.x, f.y, s.x, s.y) > (minAbstand || 110); });
        });
    }

    function entzuenden(sp, stelle, hp) {
        var brand = { id: sp.naechsteId++, x: stelle.x, y: stelle.y, name: stelle.name, hp: hp, spitze: hp, alter: 0, zufall: Math.random() * 1000 };
        sp.feuer.push(brand);
        sp.ereignisse.push({ typ: 'neu', brand: brand });
        return brand;
    }

    function neuerBrand(sp) {
        var g = GERAETE[sp.stufe];
        var vorher = sp.stufe > 0 ? GERAETE[sp.stufe - 1].reichweite : 0;
        var frei = stellenInReichweite(sp);
        if (!frei.length) return;

        // Stellen, die erst mit der neuen Ausrüstung erreichbar sind, kommen öfter vor, damit sich das Upgrade lohnt.
        var gewichtet = [];
        frei.forEach(function (s) {
            var gewicht = abstand(DUESE.x, DUESE.y, s.x, s.y) > vorher * 0.97 ? 3 : 1;
            for (var i = 0; i < gewicht; i++) gewichtet.push(s);
        });
        var stelle = gewichtet[Math.floor(Math.random() * gewichtet.length)];
        var hp = 12 + Math.random() * 8 + Math.min(12, sp.zeit * 0.03);
        entzuenden(sp, stelle, hp);
    }

    // Zielhilfe: Liegt der Zielpunkt innerhalb von "toleranz" (Spielfeld-Einheiten) um einen Brand, wird dieser Brand angepeilt.
    // Auf dem Smartphone ist der Finger viel größer als ein Brand und verdeckt ihn, ohne Zielhilfe träfe man kaum.
    // Der Brand selbst gilt mindestens mit seiner eigenen Größe als getroffen.
    function zielhilfe(sp, x, y, toleranz) {
        var nah = null, best = Infinity;
        sp.feuer.forEach(function (f) {
            var a = abstand(x, y, f.x, f.y);
            if (a <= Math.max(toleranz || 0, radius(f.hp) * 0.85 + 10) && a < best) { best = a; nah = f; }
        });
        return nah ? { x: nah.x, y: nah.y } : { x: x, y: y };
    }

    // Ein Spielschritt. "ein" enthält: x, y (Zielpunkt), spritzt (Taste gedrückt), fluss (Regler 0 bis 1)
    // und optional zielhilfe (Toleranz um den Zielpunkt in Spielfeld-Einheiten).
    function schritt(sp, ein, dt) {
        if (sp.vorbei) return;
        sp.ereignisse = [];
        sp.zeit += dt;
        var g = GERAETE[sp.stufe];

        // Neue Brände
        sp.naechsterBrand -= dt;
        if (sp.naechsterBrand <= 0) {
            if (sp.feuer.length < MAX_FEUER) neuerBrand(sp);
            sp.naechsterBrand = intervall(sp.zeit) * (0.7 + 0.6 * Math.random());
        }

        // Brände wachsen und greifen auf die Umgebung über
        sp.feuer.forEach(function (f) {
            f.alter += dt;
            f.hp = Math.min(MAX_HP, f.hp + wachstum(f.hp) * dt);
            if (f.hp > f.spitze) f.spitze = f.hp;
            if (f.hp > 200 && sp.feuer.length < MAX_FEUER && Math.random() < 0.04 * dt) {
                var nah = stellenInReichweite(sp, f, 450, 110);
                if (nah.length) entzuenden(sp, nah[Math.floor(Math.random() * nah.length)], 14);
            }
        });

        // Wasservorrat läuft nach
        sp.tank = Math.min(g.tank, sp.tank + g.zufluss * dt);

        // Wasser
        sp.strahl = null;
        if (ein.spritzt && ein.fluss > 0.001) {
            var gewollt = ein.fluss * g.rate * dt;
            var verbraucht = Math.min(gewollt, sp.tank);
            sp.tank -= verbraucht;
            var rate = verbraucht / dt;
            if (rate > 0.001) {
                var anteil = rate / g.rate;
                var weite = reichweite(g, anteil);
                var ziel = zielhilfe(sp, ein.x, ein.y, ein.zielhilfe);
                var dx = ziel.x - DUESE.x, dy = ziel.y - DUESE.y;
                var d = Math.sqrt(dx * dx + dy * dy) || 1;
                var kurz = d > weite;
                var lx = kurz ? DUESE.x + dx / d * weite : ziel.x;
                var ly = kurz ? DUESE.y + dy / d * weite : ziel.y;

                var treffer = null, besser = Infinity;
                sp.feuer.forEach(function (f) {
                    var a = abstand(lx, ly, f.x, f.y);
                    if (a <= radius(f.hp) * 0.85 + 10 && a < besser) { besser = a; treffer = f; }
                });

                var verdampft = false;
                if (treffer) {
                    verdampft = rate <= mindestrate(treffer.hp);
                    treffer.hp -= loeschwirkung(rate, treffer.hp) * dt;
                }

                // Gibt es einen Brand in Zielnähe, den der Strahl nicht erreicht?
                var zuKurz = false;
                if (kurz && !treffer) {
                    zuKurz = sp.feuer.some(function (f) { return abstand(ziel.x, ziel.y, f.x, f.y) <= radius(f.hp) * 1.4 + 20; });
                }

                sp.strahl = {
                    aktiv: true, fluss: anteil, rate: rate, weite: weite,
                    zielX: ziel.x, zielY: ziel.y,
                    landeX: lx, landeY: ly, kurz: kurz, zuKurz: zuKurz,
                    treffer: treffer, verdampft: verdampft,
                    leer: verbraucht < gewollt - 1e-9
                };
            }
        }

        // Gelöschte Brände
        sp.feuer = sp.feuer.filter(function (f) {
            if (f.hp > 0) return true;
            var punkte = Math.round(8 + f.spitze * 0.35);
            sp.punkte += punkte;
            sp.gesamt += punkte;
            sp.geloescht++;
            sp.ereignisse.push({ typ: 'geloescht', x: f.x, y: f.y, punkte: punkte, brand: f });
            return false;
        });

        // Schaden am Gebäude
        var last = 0;
        sp.feuer.forEach(function (f) { last += Math.pow(f.hp / 100, 1.3); });
        sp.schaden += 0.2 * last * dt;
        if (sp.schaden >= 100) {
            sp.schaden = 100;
            sp.vorbei = true;
        }
    }

    function naechstesGeraet(sp) {
        return GERAETE[sp.stufe + 1] || null;
    }

    function upgradeMoeglich(sp) {
        var n = naechstesGeraet(sp);
        return !!n && sp.punkte >= n.kosten;
    }

    function upgraden(sp) {
        if (!upgradeMoeglich(sp)) return false;
        var n = naechstesGeraet(sp);
        sp.punkte -= n.kosten;
        sp.stufe++;
        sp.tank = n.tank;
        return true;
    }

    var Modell = {
        WELT: WELT, DUESE: DUESE, MAX_HP: MAX_HP, MAX_FEUER: MAX_FEUER, GERAETE: GERAETE, BRANDSTELLEN: BRANDSTELLEN,
        abstand: abstand, zielhilfe: zielhilfe, reichweite: reichweite, mindestrate: mindestrate, wirkrate: wirkrate, wachstum: wachstum, radius: radius,
        groesse: groesse, loeschwirkung: loeschwirkung, intervall: intervall,
        neuesSpiel: neuesSpiel, schritt: schritt, naechstesGeraet: naechstesGeraet, upgradeMoeglich: upgradeMoeglich, upgraden: upgraden
    };

    // Außerhalb des Browsers (Node) wird nur das Modell bereitgestellt.
    if (typeof document === 'undefined') {
        if (typeof module === 'object' && module.exports) module.exports = Modell;
        return;
    }

    // =====================================================================
    //  DARSTELLUNG UND BEDIENUNG
    // =====================================================================

    var REKORD_SCHLUESSEL = 'loeschspiel.rekord';
    var HILFE_SCHLUESSEL = 'loeschspiel.hilfe';

    var $ = function (id) { return document.getElementById(id); };
    var buehneEl = $('buehne'), flaeche = $('flaeche'), ctx = flaeche.getContext('2d');
    var reglerEl = $('durchfluss'), reglerWertEl = $('durchfluss-wert');
    var punkteEl = $('punkte'), zeitEl = $('zeit'), rekordEl = $('rekord');
    var tankEl = $('tank-fuellung'), tankTextEl = $('tank-text');
    var schadenEl = $('schaden-fuellung'), schadenTextEl = $('schaden-text');
    var geraetEl = $('geraet-name'), geraetDatenEl = $('geraet-daten');
    var upgradeEl = $('btn-upgrade'), pauseEl = $('btn-pause'), hilfeEl = $('hilfe-an');
    var hinweisEl = $('hinweis');
    var overlayEl = $('overlay'), overlayTitelEl = $('overlay-titel'), overlayTextEl = $('overlay-text'), overlayStartEl = $('overlay-start');

    var sp = Modell.neuesSpiel();
    var phase = 'start';                   // start | laeuft | pause | ende
    var eingabe = { x: DUESE.x, y: 420, spritzt: false, fluss: 0.5 };
    var zeigerDruck = false, tasteDruck = false, zeigerId = null, zeigerDa = false, zeigerArt = 'mouse';
    // So großzügig (in Bildschirm-Pixeln) wird ein Brand um den Zielpunkt herum angepeilt. Ein Finger deckt viel mehr ab als ein Mauszeiger.
    var ZIELHILFE_PX = { mouse: 10, pen: 22, touch: 44 };
    var teilchen = [];
    var upgradeWarBereit = false;          // damit der Hinweiston nur einmal kommt, wenn das Upgrade bezahlbar wird
    var tropfenZaehler = 0;
    var meldungText = '', meldungBis = 0;
    var dpr = 1, sk = 1, schriftKlein = 12, schriftGross = 22;
    var rekord = ladeRekord();
    var zeigeHilfe = ladeHilfe();
    var hudCache = {};

    function ladeRekord() {
        try { return parseInt(localStorage.getItem(REKORD_SCHLUESSEL), 10) || 0; } catch (e) { return 0; }
    }

    function speichereRekord() {
        try { localStorage.setItem(REKORD_SCHLUESSEL, String(rekord)); } catch (e) { }
    }

    function ladeHilfe() {
        try { return localStorage.getItem(HILFE_SCHLUESSEL) !== '0'; } catch (e) { return true; }
    }

    // ---------- Ton ----------
    // Alle Geräusche werden live mit der Web-Audio-API erzeugt, es gibt keine Audiodateien.
    // Browser erlauben Ton erst nach einer Bedienung durch den Spieler (Klick, Tippen, Taste). Bis dahin bleibt es stumm.
    var TON_SCHLUESSEL = 'loeschspiel.ton';
    var tonEl = $('btn-ton');

    var Ton = (function () {
        var AC = window.AudioContext || window.webkitAudioContext;
        var ac = null, master = null, rauschen = null, dauer = null;
        var an = true;
        var knisterRest = 1, pieps = { tank: 0, schaden: 0 };
        try { an = localStorage.getItem(TON_SCHLUESSEL) !== '0'; } catch (e) { }

        function nichts() { }
        function bereit() { return !!ac && an && ac.state === 'running'; }

        // Rosa Rauschen klingt nach Wasser und Feuer und ist weicher als weißes Rauschen.
        function rauschenBauen() {
            var laenge = Math.round(ac.sampleRate * 2);
            var puffer = ac.createBuffer(1, laenge, ac.sampleRate);
            var d = puffer.getChannelData(0);
            var b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
            for (var i = 0; i < laenge; i++) {
                var w = Math.random() * 2 - 1;
                b0 = 0.99886 * b0 + w * 0.0555179;
                b1 = 0.99332 * b1 + w * 0.0750759;
                b2 = 0.96900 * b2 + w * 0.1538520;
                b3 = 0.86650 * b3 + w * 0.3104856;
                b4 = 0.55000 * b4 + w * 0.5329522;
                b5 = -0.7616 * b5 - w * 0.0168980;
                d[i] = (b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362) * 0.11;
                b6 = w * 0.115926;
            }
            return puffer;
        }

        // Eine dauerhaft laufende Rauschquelle, deren Lautstärke das Spiel laufend nachführt.
        function dauerQuelle(typ, freq, q) {
            var quelle = ac.createBufferSource();
            quelle.buffer = rauschen;
            quelle.loop = true;
            var filter = ac.createBiquadFilter();
            filter.type = typ;
            filter.frequency.value = freq;
            filter.Q.value = q;
            var g = ac.createGain();
            g.gain.value = 0;
            quelle.connect(filter);
            filter.connect(g);
            g.connect(master);
            quelle.start(0, Math.random() * 1.5);
            return { filter: filter, g: g };
        }

        function init() {
            if (ac || !AC) return;
            try {
                ac = new AC();
                master = ac.createGain();
                master.gain.value = an ? 0.8 : 0;
                // Der Kompressor fängt Spitzen ab, wenn viele Geräusche zusammenkommen.
                var komp = ac.createDynamicsCompressor();
                komp.threshold.value = -14;
                komp.knee.value = 12;
                komp.ratio.value = 6;
                komp.attack.value = 0.003;
                komp.release.value = 0.2;
                master.connect(komp);
                komp.connect(ac.destination);
                rauschen = rauschenBauen();
                dauer = {
                    strahl: dauerQuelle('bandpass', 1800, 0.7),   // Wasserstrahl
                    zisch: dauerQuelle('highpass', 4500, 0.5),    // Zischen am Feuer
                    brausen: dauerQuelle('lowpass', 350, 0.7)     // Rauschen der Flammen
                };
            } catch (e) {
                ac = null;
            }
        }

        // Lautstärkeverlauf: schnell hoch, dann ausklingen.
        function huellkurve(g, t, spitze, laenge) {
            g.gain.setValueAtTime(0.0001, t);
            g.gain.linearRampToValueAtTime(spitze, t + 0.006);
            g.gain.exponentialRampToValueAtTime(0.0001, t + laenge);
        }

        function ton(freq, t, laenge, art, laut, bis) {
            var o = ac.createOscillator(), g = ac.createGain();
            o.type = art;
            o.frequency.setValueAtTime(freq, t);
            if (bis) o.frequency.exponentialRampToValueAtTime(bis, t + laenge);
            huellkurve(g, t, laut, laenge);
            o.connect(g);
            g.connect(master);
            o.start(t);
            o.stop(t + laenge + 0.05);
        }

        function stoss(t, laenge, typ, f0, f1, laut, q) {
            var quelle = ac.createBufferSource();
            quelle.buffer = rauschen;
            var filter = ac.createBiquadFilter(), g = ac.createGain();
            filter.type = typ;
            filter.Q.value = q || 0.8;
            filter.frequency.setValueAtTime(f0, t);
            if (f1) filter.frequency.exponentialRampToValueAtTime(f1, t + laenge);
            huellkurve(g, t, laut, laenge);
            quelle.connect(filter);
            filter.connect(g);
            g.connect(master);
            quelle.start(t, Math.random() * 1.5, laenge + 0.05);
        }

        function folge(noten, t, abstand, art, laut, laenge) {
            noten.forEach(function (f, i) { ton(f, t + i * abstand, laenge, art, laut); });
        }

        function setze(param, wert, t, zeit) { param.setTargetAtTime(wert, t, zeit); }

        function beschriften() {
            if (!tonEl) return;
            tonEl.hidden = !AC;
            tonEl.textContent = an ? 'Ton an' : 'Ton aus';
            tonEl.setAttribute('aria-pressed', an ? 'true' : 'false');
        }
        beschriften();

        return {
            // Muss aus einer Bedienung heraus aufgerufen werden (Klick, Tippen, Taste).
            entsperren: function () {
                init();
                if (ac && ac.state === 'suspended' && phase !== 'pause') ac.resume().catch(nichts);
            },

            umschalten: function () {
                an = !an;
                try { localStorage.setItem(TON_SCHLUESSEL, an ? '1' : '0'); } catch (e) { }
                if (master) setze(master.gain, an ? 0.8 : 0, ac.currentTime, 0.03);
                beschriften();
            },

            // Alarm beim Start: zwei Töne wie ein Martinshorn.
            start: function () {
                if (!ac || !an) return;
                var los = function () {
                    var t = ac.currentTime + 0.02;
                    var o = ac.createOscillator(), f = ac.createBiquadFilter(), g = ac.createGain();
                    o.type = 'sawtooth';
                    o.frequency.setValueAtTime(435, t);
                    o.frequency.setTargetAtTime(580, t + 0.45, 0.02);
                    o.frequency.setTargetAtTime(435, t + 0.9, 0.02);
                    f.type = 'lowpass';
                    f.frequency.value = 1700;
                    g.gain.setValueAtTime(0.0001, t);
                    g.gain.linearRampToValueAtTime(0.1, t + 0.04);
                    g.gain.setValueAtTime(0.1, t + 1.25);
                    g.gain.exponentialRampToValueAtTime(0.0001, t + 1.5);
                    o.connect(f);
                    f.connect(g);
                    g.connect(master);
                    o.start(t);
                    o.stop(t + 1.6);
                };
                if (ac.state === 'running') los(); else ac.resume().then(los).catch(nichts);
            },

            // Wird in jedem Spielschritt aufgerufen und führt die Dauergeräusche nach.
            update: function (sp, dt) {
                if (!bereit()) return;
                var t = ac.currentTime, s = sp.strahl, stufe = sp.stufe;

                // Wasserstrahl: lauter und tiefer bei mehr Wasser und größerem Rohr. Bei leerem Vorrat stottert er.
                var strahlLaut = 0, strahlFreq = 1800, strahlZeit = 0.04, zischLaut = 0;
                if (s && s.aktiv) {
                    var f = Math.sqrt(s.fluss);
                    strahlLaut = (0.07 + 0.2 * f) * (0.85 + 0.15 * stufe);
                    strahlFreq = (2600 - 330 * stufe) * (0.5 + 0.5 * f);
                    if (s.leer) { strahlLaut *= Math.random() < 0.5 ? 0.45 : 0.05; strahlZeit = 0.015; }
                    if (s.treffer) zischLaut = (s.verdampft ? 0.12 : 0.07) * (0.5 + 0.5 * Math.min(1, s.treffer.hp / 120));
                }
                setze(dauer.strahl.g.gain, strahlLaut, t, strahlZeit);
                setze(dauer.strahl.filter.frequency, strahlFreq, t, 0.05);
                setze(dauer.zisch.g.gain, zischLaut, t, 0.05);

                // Flammen: je mehr und je größer die Brände, desto lauter Rauschen und Knistern.
                var hitze = 0;
                sp.feuer.forEach(function (b) { hitze += Math.min(1, b.hp / 320); });
                setze(dauer.brausen.g.gain, hitze > 0 ? Math.min(0.1, 0.01 + 0.012 * hitze) : 0, t, 0.3);
                setze(dauer.brausen.filter.frequency, 260 + 60 * Math.min(hitze, 5), t, 0.3);
                if (hitze > 0) {
                    knisterRest -= dt * Math.min(25, 2 + 5 * Math.pow(hitze, 0.8));
                    for (var n = 0; knisterRest <= 0 && n < 3; n++) {
                        stoss(t + Math.random() * dt, 0.012 + Math.random() * 0.03, 'bandpass', 1200 + Math.random() * 3800, 0, 0.03 + 0.05 * Math.random(), 2);
                        knisterRest += 0.6 + Math.random() * 0.8;
                    }
                }

                // Wasser fast leer: leises Piepen, solange gespritzt wird.
                if (s && s.aktiv && sp.tank < 0.15 * GERAETE[stufe].tank) {
                    pieps.tank -= dt;
                    if (pieps.tank <= 0) { ton(740, t, 0.07, 'square', 0.035); pieps.tank = 0.45; }
                } else {
                    pieps.tank = 0;
                }

                // Gebäude in Gefahr: tiefer Warnton, der mit dem Schaden schneller wird.
                if (sp.schaden > 66) {
                    pieps.schaden -= dt;
                    if (pieps.schaden <= 0) { ton(330, t, 0.16, 'sawtooth', 0.05); pieps.schaden = 1.3 - 0.7 * Math.min(1, (sp.schaden - 66) / 34); }
                } else {
                    pieps.schaden = 0;
                }
            },

            neuerBrand: function () {
                if (!bereit()) return;
                var t = ac.currentTime;
                stoss(t, 0.45, 'bandpass', 220, 1400, 0.22, 0.9);
                ton(95, t, 0.3, 'sine', 0.25, 48);
            },

            // "groesse" ist die größte Stärke, die der Brand hatte: Große Brände klingen kräftiger.
            geloescht: function (groesse) {
                if (!bereit()) return;
                var t = ac.currentTime, gr = Math.min(1, groesse / 120);
                stoss(t, 0.35 + 0.3 * gr, 'highpass', 2600, 6000, 0.14 + 0.1 * gr, 0.5);
                ton(988, t + 0.04, 0.28, 'sine', 0.11);
                ton(1319, t + 0.12, 0.34, 'sine', 0.09);
            },

            upgradeBereit: function () {
                if (!bereit()) return;
                var t = ac.currentTime;
                ton(1175, t, 0.4, 'sine', 0.1);
                ton(1568, t + 0.13, 0.5, 'sine', 0.08);
            },

            upgradeGekauft: function (stufe) {
                if (!bereit()) return;
                var t = ac.currentTime, grund = 523 * Math.pow(1.12, Math.max(0, stufe - 1));
                stoss(t, 0.55, 'bandpass', 300, 1800, 0.2, 1.2);
                folge([grund, grund * 1.26, grund * 1.5, grund * 2], t + 0.1, 0.09, 'triangle', 0.12, 0.35);
            },

            ende: function (neuerRekord) {
                if (!ac) return;
                this.dauerAus();
                if (!bereit()) return;
                var t = ac.currentTime + 0.05;
                if (neuerRekord) {
                    folge([523, 659, 784, 1047, 1319], t, 0.11, 'triangle', 0.13, 0.45);
                } else {
                    var o = ac.createOscillator(), f = ac.createBiquadFilter(), g = ac.createGain();
                    o.type = 'sawtooth';
                    o.frequency.setValueAtTime(330, t);
                    o.frequency.exponentialRampToValueAtTime(82, t + 1.4);
                    f.type = 'lowpass';
                    f.frequency.value = 800;
                    huellkurve(g, t, 0.16, 1.5);
                    o.connect(f);
                    f.connect(g);
                    g.connect(master);
                    o.start(t);
                    o.stop(t + 1.6);
                    ton(60, t, 0.5, 'sine', 0.25);
                }
            },

            // Dauergeräusche ausblenden (am Spielende).
            dauerAus: function () {
                if (!ac || !dauer) return;
                var t = ac.currentTime;
                Object.keys(dauer).forEach(function (k) { setze(dauer[k].g.gain, 0, t, 0.02); });
                pieps.tank = 0;
                pieps.schaden = 0;
            },

            // Pause: sofort still, auch die Dauergeräusche, und den Ton anhalten.
            pause: function () {
                if (!ac || !dauer) return;
                var t = ac.currentTime;
                Object.keys(dauer).forEach(function (k) {
                    dauer[k].g.gain.cancelScheduledValues(t);
                    dauer[k].g.gain.setValueAtTime(0, t);
                });
                pieps.tank = 0;
                pieps.schaden = 0;
                if (ac.state === 'running') ac.suspend().catch(nichts);
            },

            weiter: function () {
                if (ac && ac.state === 'suspended') ac.resume().catch(nichts);
            }
        };
    })();

    if (tonEl) {
        tonEl.addEventListener('click', function () {
            Ton.entsperren();
            Ton.umschalten();
            tonEl.blur();
        });
    }

    document.addEventListener('keydown', function (e) {
        if ((e.key === 'm' || e.key === 'M') && !e.repeat && !e.ctrlKey && !e.metaKey && !e.altKey) {
            Ton.entsperren();
            Ton.umschalten();
        }
    });

    // ---------- Größe ----------
    function groesseAnpassen() {
        var rect = buehneEl.getBoundingClientRect();
        dpr = Math.min(window.devicePixelRatio || 1, 2);
        flaeche.width = Math.max(1, Math.round(rect.width * dpr));
        flaeche.height = Math.max(1, Math.round(rect.width * dpr * WELT.h / WELT.b));
        sk = flaeche.width / WELT.b;
        // Schriftgrößen wachsen mit dem Spielfeld, damit sie auf kleinen Bildschirmen nicht erdrücken.
        schriftKlein = Math.max(9, Math.min(13, rect.width / 85));
        schriftGross = Math.max(14, Math.min(22, rect.width / 55));
    }

    if (window.ResizeObserver) {
        new ResizeObserver(groesseAnpassen).observe(buehneEl);
    } else {
        window.addEventListener('resize', groesseAnpassen);
    }
    groesseAnpassen();

    // ---------- Eingabe ----------
    function zielSetzen(e) {
        var rect = flaeche.getBoundingClientRect();
        eingabe.x = (e.clientX - rect.left) / rect.width * WELT.b;
        eingabe.y = (e.clientY - rect.top) / rect.height * WELT.h;
    }

    flaeche.addEventListener('pointerdown', function (e) {
        Ton.entsperren();
        if (e.pointerType === 'mouse' && e.button !== 0) return;
        if (zeigerId !== null) return;
        zeigerId = e.pointerId;
        zeigerArt = e.pointerType || 'mouse';
        zeigerDa = true;
        zeigerDruck = true;
        zielSetzen(e);
        try { flaeche.setPointerCapture(e.pointerId); } catch (err) { }
        e.preventDefault();
    });

    flaeche.addEventListener('pointermove', function (e) {
        if (zeigerId !== null && e.pointerId !== zeigerId) return;
        zeigerDa = true;
        zeigerArt = e.pointerType || 'mouse';
        zielSetzen(e);
    });

    function zeigerLoslassen(e) {
        if (e.pointerId !== zeigerId) return;
        zeigerId = null;
        zeigerDruck = false;
    }
    flaeche.addEventListener('pointerup', zeigerLoslassen);
    flaeche.addEventListener('pointercancel', zeigerLoslassen);
    flaeche.addEventListener('pointerleave', function (e) { if (e.pointerType === 'mouse') zeigerDa = false; });
    flaeche.addEventListener('contextmenu', function (e) { e.preventDefault(); });

    function flussSetzen(prozent) {
        prozent = Math.max(0, Math.min(100, Math.round(prozent)));
        reglerEl.value = String(prozent);
        eingabe.fluss = prozent / 100;
        reglerAnzeigen();
    }

    function reglerAnzeigen() {
        var g = GERAETE[sp.stufe];
        var prozent = Math.round(eingabe.fluss * 100);
        reglerWertEl.textContent = prozent + ' % (ca. ' + literText(eingabe.fluss * g.literProMin) + ' l/min)';
        reglerEl.setAttribute('aria-valuetext', prozent + ' Prozent');
    }

    reglerEl.addEventListener('input', function () {
        eingabe.fluss = parseInt(reglerEl.value, 10) / 100;
        reglerAnzeigen();
    });

    buehneEl.addEventListener('wheel', function (e) {
        if (phase !== 'laeuft') return;
        e.preventDefault();
        flussSetzen(eingabe.fluss * 100 + (e.deltaY < 0 ? 5 : -5));
    }, { passive: false });

    document.addEventListener('keydown', function (e) {
        if (e.repeat && e.code === 'Space') { if (phase === 'laeuft') e.preventDefault(); return; }
        if (e.code === 'Space' && phase === 'laeuft') {
            e.preventDefault();
            tasteDruck = true;
        } else if ((e.key === 'p' || e.key === 'P' || e.key === 'Escape') && (phase === 'laeuft' || phase === 'pause')) {
            e.preventDefault();
            pauseUmschalten();
        } else if (phase === 'laeuft' && e.target !== reglerEl) {
            if (e.key === 'ArrowUp' || e.key === 'w' || e.key === 'W' || e.key === '+') {
                e.preventDefault();
                flussSetzen(eingabe.fluss * 100 + 5);
            } else if (e.key === 'ArrowDown' || e.key === 's' || e.key === 'S' || e.key === '-') {
                e.preventDefault();
                flussSetzen(eingabe.fluss * 100 - 5);
            }
        }
    });

    document.addEventListener('keyup', function (e) {
        if (e.code !== 'Space') return;
        tasteDruck = false;
        // Sonst würde die Leertaste beim Loslassen einen Knopf oder das Häkchen betätigen, der gerade den Fokus hat.
        if (phase === 'laeuft') e.preventDefault();
    });

    function pausieren() {
        if (phase === 'laeuft') pauseUmschalten();
    }

    document.addEventListener('visibilitychange', function () { if (document.hidden) pausieren(); });
    window.addEventListener('blur', pausieren);

    pauseEl.addEventListener('click', function () {
        if (phase === 'laeuft' || phase === 'pause') pauseUmschalten();
    });

    upgradeEl.addEventListener('click', function () {
        if (phase !== 'laeuft' && phase !== 'pause') return;
        if (Modell.upgraden(sp)) {
            var g = GERAETE[sp.stufe];
            meldung('Neue Ausrüstung: ' + g.name + ' – Tank wieder voll!', 3.5);
            reglerAnzeigen();
            Ton.upgradeGekauft(sp.stufe);
            upgradeWarBereit = Modell.upgradeMoeglich(sp);
        }
        upgradeEl.blur();
    });

    hilfeEl.checked = zeigeHilfe;
    hilfeEl.addEventListener('change', function () {
        zeigeHilfe = hilfeEl.checked;
        try { localStorage.setItem(HILFE_SCHLUESSEL, zeigeHilfe ? '1' : '0'); } catch (e) { }
    });

    overlayStartEl.addEventListener('click', function () {
        Ton.entsperren();
        if (phase === 'pause') {
            pauseUmschalten();
        } else {
            neuesSpielStarten();
        }
    });

    function neuesSpielStarten() {
        sp = Modell.neuesSpiel();
        teilchen = [];
        tropfenZaehler = 0;
        meldungText = '';
        zeigerDruck = false;
        tasteDruck = false;
        zeigerId = null;
        phase = 'laeuft';
        overlayEl.hidden = true;
        upgradeWarBereit = false;
        reglerAnzeigen();
        Ton.start();
    }

    function pauseUmschalten() {
        if (phase === 'laeuft') {
            phase = 'pause';
            tasteDruck = false;
            zeigerDruck = false;
            zeigerId = null;
            overlayTitelEl.textContent = 'Pause';
            overlayTextEl.innerHTML = '<p>Das Spiel ist angehalten. Die Brände warten nicht auf dich – aber jetzt gerade schon.</p>';
            overlayStartEl.textContent = 'Weiter';
            overlayEl.hidden = false;
            pauseEl.textContent = 'Weiter';
            Ton.pause();
        } else if (phase === 'pause') {
            phase = 'laeuft';
            overlayEl.hidden = true;
            pauseEl.textContent = 'Pause';
            Ton.weiter();
        }
    }

    function spielEnde() {
        phase = 'ende';
        var neuerRekord = sp.gesamt > rekord;
        if (neuerRekord) {
            rekord = sp.gesamt;
            speichereRekord();
        }
        overlayTitelEl.textContent = 'Einsatz beendet';
        overlayTextEl.innerHTML =
            '<p>Das Gebäude ist zu stark beschädigt. Das nächste Mal klappt es noch besser!</p>' +
            '<ul class="ergebnis">' +
            '<li><span>Einsatzdauer</span><strong>' + zeitText(sp.zeit) + '</strong></li>' +
            '<li><span>Gelöschte Brände</span><strong>' + sp.geloescht + '</strong></li>' +
            '<li><span>Punkte gesamt</span><strong>' + sp.gesamt + '</strong></li>' +
            '<li><span>Rekord</span><strong>' + rekord + (neuerRekord ? ' (neu!)' : '') + '</strong></li>' +
            '</ul>';
        overlayStartEl.textContent = 'Neuer Einsatz';
        overlayEl.hidden = false;
        pauseEl.textContent = 'Pause';
        Ton.ende(neuerRekord);
    }

    function meldung(text, sekunden) {
        meldungText = text;
        meldungBis = performance.now() + sekunden * 1000;
    }

    // ---------- Teilchen ----------
    function neuerTropfen(s) {
        var g = GERAETE[sp.stufe];
        var dist = abstand(DUESE.x, DUESE.y, s.landeX, s.landeY);
        var winkel = Math.atan2(s.landeY - DUESE.y, s.landeX - DUESE.x);
        var versatz = (Math.random() - 0.5) * (4 + 16 * s.fluss);
        teilchen.push({
            typ: 'tropfen', p: 0,
            dauer: 0.22 + dist / 2600,
            sx: DUESE.x + Math.cos(winkel) * 40, sy: DUESE.y + Math.sin(winkel) * 40,
            lx: s.landeX, ly: s.landeY,
            nx: -Math.sin(winkel), ny: Math.cos(winkel),
            versatz: versatz, bogen: 8 + dist * 0.07,
            groesse: 4 + g.dicke * 0.4 * (0.4 + 0.6 * s.fluss),
            x: DUESE.x, y: DUESE.y, px: DUESE.x, py: DUESE.y,
            kurz: s.kurz, trifft: !!s.treffer
        });
    }

    function spritzer(x, y, anzahl, staerke) {
        for (var i = 0; i < anzahl; i++) {
            var w = -Math.PI * (0.15 + Math.random() * 0.7);
            var v = (40 + Math.random() * 120) * staerke;
            teilchen.push({ typ: 'spritzer', x: x, y: y, vx: Math.cos(w) * v, vy: Math.sin(w) * v, leben: 0.4 + Math.random() * 0.3, max: 0.7, r: 2 + Math.random() * 3 });
        }
    }

    function dampf(x, y, anzahl, dunkel) {
        for (var i = 0; i < anzahl; i++) {
            teilchen.push({
                typ: dunkel ? 'rauch' : 'dampf', x: x + (Math.random() - 0.5) * 30, y: y + (Math.random() - 0.5) * 20,
                vx: (Math.random() - 0.5) * 30, vy: -(30 + Math.random() * 50),
                leben: 1 + Math.random() * 0.8, max: 1.8, r: 10 + Math.random() * 14
            });
        }
    }

    function schwebText(x, y, text) {
        teilchen.push({ typ: 'text', x: x, y: y, vx: 0, vy: -60, leben: 1.4, max: 1.4, text: text });
    }

    // Ort eines Wassertropfens auf seiner Flugbahn (p = 0 an der Düse, 1 am Ziel).
    function tropfenOrt(t, p) {
        return {
            x: t.sx + (t.lx - t.sx) * p + t.nx * t.versatz * p,
            y: t.sy + (t.ly - t.sy) * p + t.ny * t.versatz * p - t.bogen * 4 * p * (1 - p)
        };
    }

    function teilchenAktualisieren(dt) {
        for (var i = teilchen.length - 1; i >= 0; i--) {
            var t = teilchen[i];
            if (t.typ === 'tropfen') {
                t.p += dt / t.dauer;
                var p = Math.min(t.p, 1);
                var jetzt = tropfenOrt(t, p);
                var davor = tropfenOrt(t, Math.max(0, p - 0.16));   // Schweif, damit der Strahl zusammenhängend wirkt
                t.x = jetzt.x; t.y = jetzt.y; t.px = davor.x; t.py = davor.y;
                if (t.p >= 1) {
                    if (!t.trifft && Math.random() < 0.5) spritzer(t.lx, t.ly, 1, 0.8);
                    teilchen.splice(i, 1);
                }
            } else {
                t.leben -= dt;
                if (t.leben <= 0) { teilchen.splice(i, 1); continue; }
                t.x += t.vx * dt;
                t.y += t.vy * dt;
                if (t.typ === 'spritzer') t.vy += 520 * dt;
                else if (t.typ === 'rauch' || t.typ === 'dampf') t.r += 14 * dt;
            }
        }
        if (teilchen.length > 700) teilchen.splice(0, teilchen.length - 700);
    }

    // ---------- Spielschritt ----------
    function aktualisieren(dt) {
        eingabe.spritzt = zeigerDruck || tasteDruck;
        // Bildschirm-Pixel in Spielfeld-Einheiten umrechnen (sk zählt Zeichenpixel, also einschließlich Geräte-Pixelverhältnis).
        eingabe.zielhilfe = (ZIELHILFE_PX[zeigerArt] || ZIELHILFE_PX.mouse) * dpr / sk;
        Modell.schritt(sp, eingabe, dt);

        var s = sp.strahl;
        if (s && s.aktiv) {
            tropfenZaehler += (8 + 70 * s.fluss) * dt;
            while (tropfenZaehler >= 1) {
                tropfenZaehler -= 1;
                neuerTropfen(s);
            }
            if (s.treffer && Math.random() < dt * (s.verdampft ? 14 : 6)) {
                dampf(s.treffer.x, s.treffer.y, 1, false);
            }
        } else {
            tropfenZaehler = 0;
        }

        sp.ereignisse.forEach(function (ev) {
            if (ev.typ === 'geloescht') {
                dampf(ev.x, ev.y, 10, false);
                spritzer(ev.x, ev.y, 8, 1);
                schwebText(ev.x, ev.y - 40, '+' + ev.punkte);
                Ton.geloescht(ev.brand.spitze);
            } else if (ev.typ === 'neu') {
                meldung('Neuer Brand: ' + ev.brand.name + '!', 2.2);
                Ton.neuerBrand();
            }
        });

        // Sobald die nächste Ausrüstung bezahlbar wird, gibt es einmal einen Hinweiston.
        var upgradeBereit = Modell.upgradeMoeglich(sp);
        if (upgradeBereit && !upgradeWarBereit) Ton.upgradeBereit();
        upgradeWarBereit = upgradeBereit;

        Ton.update(sp, dt);

        sp.feuer.forEach(function (f) {
            if (f.hp > 55 && Math.random() < dt * (f.hp / 60)) {
                dampf(f.x, f.y - Modell.radius(f.hp) * 1.1, 1, true);
            }
        });

        teilchenAktualisieren(dt);

        if (sp.vorbei) spielEnde();
    }

    // ---------- Hinweise ----------
    function hinweisBestimmen() {
        if (phase !== 'laeuft') return '';
        if (performance.now() < meldungBis) return meldungText;
        var s = sp.strahl;
        if (s) {
            if (s.leer) return 'Wasservorrat leer – es kommt nur noch der Zufluss an. Weniger Wasser nehmen!';
            if (s.zuKurz) return 'Der Strahl reicht nicht bis zum Brand – mehr Wassermenge einstellen!';
            if (s.treffer && s.verdampft) return 'Zu schwach: Das Wasser verdampft am Brand – mehr Wassermenge oder ein stärkeres Rohr!';
        }
        return '';
    }

    // ---------- Zeichnen ----------
    function zunge(x, y, w, h, schwanken) {
        ctx.beginPath();
        ctx.moveTo(x - w, y);
        ctx.quadraticCurveTo(x - w * 1.1, y - h * 0.55, x + schwanken, y - h);
        ctx.quadraticCurveTo(x + w * 1.1, y - h * 0.55, x + w, y);
        ctx.quadraticCurveTo(x, y + w * 0.7, x - w, y);
        ctx.fill();
    }

    function feuerZeichnen(f, zeit) {
        var r = Modell.radius(f.hp);
        var bx = f.x, by = f.y + r * 0.45;

        var glut = ctx.createRadialGradient(f.x, f.y, r * 0.1, f.x, f.y, r * 2.4);
        glut.addColorStop(0, 'rgba(255,170,60,0.5)');
        glut.addColorStop(1, 'rgba(255,120,0,0)');
        ctx.fillStyle = glut;
        ctx.beginPath();
        ctx.arc(f.x, f.y, r * 2.4, 0, Math.PI * 2);
        ctx.fill();

        var n = 4 + Math.round(r / 14);
        var lagen = [{ m: 1, c: '#d62a12' }, { m: 0.68, c: '#ff8a1c' }, { m: 0.38, c: '#ffd84a' }];
        lagen.forEach(function (lage) {
            ctx.fillStyle = lage.c;
            for (var i = 0; i < n; i++) {
                var u = (i / (n - 1) - 0.5) * 2;
                var a = 0.5 + 0.5 * Math.sin(zeit * (6 + i * 0.9) + f.zufall + i * 1.7);
                var b = 0.5 + 0.5 * Math.sin(zeit * (9 + i * 0.5) + f.zufall * 1.3 + i);
                var h = r * (0.8 + 0.9 * (1 - Math.abs(u)) * (0.6 + 0.4 * a)) * lage.m;
                var w = r * 0.42 * lage.m * (0.8 + 0.3 * b);
                zunge(bx + u * r * 0.75 * (0.6 + 0.4 * lage.m), by, w, h, (b - 0.5) * r * 0.3);
            }
        });
    }

    function beschriftungZeichnen(f) {
        var g = GERAETE[sp.stufe];
        var m = Modell.mindestrate(f.hp);
        var text, farbe;
        if (m >= g.rate * 0.98) {
            text = Modell.groesse(f.hp) + ' · zu stark für dieses Rohr';
            farbe = '#ff8a80';
        } else {
            var ideal = Math.min(1, Modell.wirkrate(f.hp) / g.rate);
            text = Modell.groesse(f.hp) + ' · ideal ca. ' + (Math.round(ideal * 20) * 5) + ' %';
            farbe = '#ffffff';
        }
        var r = Modell.radius(f.hp);
        ctx.save();
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.font = '600 ' + (schriftKlein * dpr) + 'px "Segoe UI", Roboto, Helvetica, Arial, sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        var hoehe = schriftKlein * 1.5 * dpr;
        var breite = ctx.measureText(text).width + schriftKlein * dpr;
        var px = f.x * sk, py = (f.y + r * 0.45) * sk + hoehe;
        // Am unteren Rand würde die Beschriftung abgeschnitten, dann steht sie über dem Brand.
        if (py + hoehe / 2 > flaeche.height) py = (f.y - r * 1.5) * sk - hoehe / 2;
        // Und sie bleibt immer ganz im Bild.
        px = Math.max(breite / 2, Math.min(flaeche.width - breite / 2, px));
        ctx.fillStyle = 'rgba(10,20,30,0.72)';
        ctx.fillRect(px - breite / 2, py - hoehe / 2, breite, hoehe);
        ctx.fillStyle = farbe;
        ctx.fillText(text, px, py);
        ctx.restore();
    }

    function schlauchZeichnen(winkel) {
        var g = GERAETE[sp.stufe];
        var sx = DUESE.x - 300, sy = WELT.h + 50;
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        ctx.beginPath();
        ctx.moveTo(sx, sy);
        ctx.bezierCurveTo(sx + 160, sy - 20, DUESE.x - 220, DUESE.y + 40, DUESE.x - 14, DUESE.y + 6);
        ctx.strokeStyle = 'rgba(0,0,0,0.6)';
        ctx.lineWidth = g.dicke * 2.3 + 6;
        ctx.stroke();
        ctx.strokeStyle = g.farbe;
        ctx.lineWidth = g.dicke * 2.3;
        ctx.stroke();

        ctx.save();
        ctx.translate(DUESE.x, DUESE.y);
        ctx.rotate(winkel);
        var l = 60 + g.dicke * 2.4, w = 12 + g.dicke * 1.1;
        ctx.fillStyle = 'rgba(0,0,0,0.55)';
        ctx.fillRect(-12, -w / 2 - 2, l + 4, w + 4);
        ctx.fillStyle = g.rohr;
        ctx.fillRect(-10, -w / 2, l, w);
        ctx.fillStyle = '#b8c2cc';
        ctx.fillRect(-10, -w / 2 - 2, 10, w + 4);
        ctx.fillRect(l - 14, -w / 2 - 3, 8, w + 6);
        ctx.restore();
    }

    function reichweiteZeichnen() {
        var g = GERAETE[sp.stufe];
        var weite = Modell.reichweite(g, eingabe.fluss);
        ctx.save();
        ctx.setLineDash([16, 14]);
        ctx.lineWidth = 3;
        ctx.strokeStyle = 'rgba(190,230,255,0.6)';
        ctx.beginPath();
        ctx.arc(DUESE.x, DUESE.y, weite, Math.PI * 1.04, Math.PI * 1.96);
        ctx.stroke();
        ctx.restore();
    }

    // Dort zielt das Strahlrohr hin: auf den angepeilten Brand, sonst auf den Zeiger.
    function zielpunkt() {
        var s = sp.strahl;
        if (s && s.aktiv) return { x: s.zielX, y: s.zielY };
        return Modell.zielhilfe(sp, eingabe.x, eingabe.y, eingabe.zielhilfe);
    }

    function zielkreuzZeichnen() {
        if (!zeigerDa && !zeigerDruck) return;
        var g = GERAETE[sp.stufe];
        var weite = Modell.reichweite(g, eingabe.fluss);
        var z = zielpunkt();
        var d = abstand(DUESE.x, DUESE.y, z.x, z.y);
        var erreichbar = d <= weite;
        var farbe = erreichbar ? 'rgba(255,255,255,0.9)' : 'rgba(255,170,70,0.95)';
        // Das Kreuz bleibt auf dem Bildschirm gleich groß, egal wie klein das Spielfeld ist.
        var m = dpr / sk;
        ctx.save();
        ctx.strokeStyle = farbe;
        ctx.lineWidth = 2.5 * m;
        ctx.beginPath();
        ctx.arc(z.x, z.y, 14 * m, 0, Math.PI * 2);
        ctx.moveTo(z.x - 28 * m, z.y); ctx.lineTo(z.x - 18 * m, z.y);
        ctx.moveTo(z.x + 18 * m, z.y); ctx.lineTo(z.x + 28 * m, z.y);
        ctx.moveTo(z.x, z.y - 28 * m); ctx.lineTo(z.x, z.y - 18 * m);
        ctx.moveTo(z.x, z.y + 18 * m); ctx.lineTo(z.x, z.y + 28 * m);
        ctx.stroke();
        ctx.restore();
    }

    function teilchenZeichnen() {
        var s = sp.strahl;
        if (s && s.aktiv) {
            var g = GERAETE[sp.stufe];
            var dist = abstand(DUESE.x, DUESE.y, s.landeX, s.landeY);
            ctx.save();
            ctx.lineCap = 'round';
            ctx.strokeStyle = 'rgba(170,220,255,0.3)';
            ctx.lineWidth = 4 + g.dicke * 0.7 * (0.4 + 0.6 * s.fluss);
            ctx.beginPath();
            ctx.moveTo(DUESE.x, DUESE.y);
            ctx.quadraticCurveTo((DUESE.x + s.landeX) / 2, (DUESE.y + s.landeY) / 2 - (8 + dist * 0.07) * 2, s.landeX, s.landeY);
            ctx.stroke();
            ctx.restore();
        }

        ctx.lineCap = 'round';
        teilchen.forEach(function (t) {
            if (t.typ === 'tropfen') {
                ctx.strokeStyle = 'rgba(190,232,255,0.9)';
                ctx.lineWidth = t.groesse;
                ctx.beginPath();
                ctx.moveTo(t.px, t.py);
                ctx.lineTo(t.x, t.y);
                ctx.stroke();
            } else if (t.typ === 'spritzer') {
                ctx.fillStyle = 'rgba(190,230,255,' + Math.max(0, t.leben / t.max) + ')';
                ctx.beginPath();
                ctx.arc(t.x, t.y, t.r, 0, Math.PI * 2);
                ctx.fill();
            } else if (t.typ === 'dampf' || t.typ === 'rauch') {
                var a = Math.max(0, t.leben / t.max);
                ctx.fillStyle = t.typ === 'dampf' ? 'rgba(235,240,245,' + (0.5 * a) + ')' : 'rgba(60,60,65,' + (0.4 * a) + ')';
                ctx.beginPath();
                ctx.arc(t.x, t.y, t.r, 0, Math.PI * 2);
                ctx.fill();
            }
        });
    }

    function schwebtexteZeichnen() {
        ctx.save();
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.font = '800 ' + (schriftGross * dpr) + 'px "Segoe UI", Roboto, Helvetica, Arial, sans-serif';
        ctx.textAlign = 'center';
        teilchen.forEach(function (t) {
            if (t.typ !== 'text') return;
            ctx.globalAlpha = Math.max(0, Math.min(1, t.leben / t.max * 1.5));
            ctx.lineWidth = 4 * dpr;
            ctx.strokeStyle = 'rgba(0,0,0,0.7)';
            ctx.strokeText(t.text, t.x * sk, t.y * sk);
            ctx.fillStyle = '#7CFC7C';
            ctx.fillText(t.text, t.x * sk, t.y * sk);
        });
        ctx.restore();
    }

    function zeichnen(zeit) {
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.clearRect(0, 0, flaeche.width, flaeche.height);
        ctx.setTransform(sk, 0, 0, sk, 0, 0);

        var ziel = zielpunkt();
        var winkel = Math.atan2(ziel.y - DUESE.y, ziel.x - DUESE.x);

        if (phase === 'laeuft') reichweiteZeichnen();

        var geordnet = sp.feuer.slice().sort(function (a, b) { return a.y - b.y; });
        geordnet.forEach(function (f) { feuerZeichnen(f, zeit); });

        schlauchZeichnen(winkel);
        teilchenZeichnen();

        if (phase === 'laeuft') zielkreuzZeichnen();

        if (zeigeHilfe && phase === 'laeuft') {
            geordnet.forEach(beschriftungZeichnen);
        }
        schwebtexteZeichnen();
    }

    // ---------- Anzeigen ----------
    // Liter pro Minute lesbar: gerundet, mit Tausenderpunkt (2.400)
    function literText(l) {
        return Math.round(l).toLocaleString('de-DE');
    }

    function setzeText(el, schluessel, wert) {
        if (hudCache[schluessel] !== wert) {
            hudCache[schluessel] = wert;
            el.textContent = wert;
        }
    }

    function setzeBreite(el, schluessel, prozent) {
        var w = Math.round(prozent * 10) / 10 + '%';
        if (hudCache[schluessel] !== w) {
            hudCache[schluessel] = w;
            el.style.width = w;
        }
    }

    function zeitText(sekunden) {
        var s = Math.floor(sekunden);
        var m = Math.floor(s / 60);
        s = s % 60;
        return m + ':' + (s < 10 ? '0' : '') + s;
    }

    function anzeigenAktualisieren() {
        var g = GERAETE[sp.stufe];
        setzeText(punkteEl, 'punkte', String(sp.punkte));
        setzeText(zeitEl, 'zeit', zeitText(sp.zeit));
        setzeText(rekordEl, 'rekord', String(Math.max(rekord, sp.gesamt)));

        var tankProzent = sp.tank / g.tank * 100;
        setzeBreite(tankEl, 'tankb', tankProzent);
        setzeText(tankTextEl, 'tankt', Math.round(tankProzent) + ' %');
        tankEl.classList.toggle('knapp', tankProzent < 20);

        setzeBreite(schadenEl, 'schadenb', sp.schaden);
        setzeText(schadenTextEl, 'schadent', Math.round(sp.schaden) + ' %');
        schadenEl.classList.toggle('kritisch', sp.schaden > 66);

        setzeText(geraetEl, 'geraet', g.name);
        setzeText(geraetDatenEl, 'geraetd', 'Max. Durchfluss ' + literText(g.literProMin) + ' l/min');

        var n = Modell.naechstesGeraet(sp);
        if (n) {
            setzeText(upgradeEl, 'upgrade', 'Aufrüsten: ' + n.kurz + ' (' + n.kosten + ' Punkte)');
            upgradeEl.disabled = !Modell.upgradeMoeglich(sp);
            upgradeEl.classList.toggle('bereit', !upgradeEl.disabled);
        } else {
            setzeText(upgradeEl, 'upgrade', 'Höchste Ausbaustufe erreicht');
            upgradeEl.disabled = true;
            upgradeEl.classList.remove('bereit');
        }

        var h = hinweisBestimmen();
        if (hudCache.hinweis !== h) {
            hudCache.hinweis = h;
            hinweisEl.textContent = h;
            hinweisEl.hidden = !h;
        }
    }

    // ---------- Hauptschleife ----------
    var letzteZeit = 0;
    function schleife(ts) {
        var dt = Math.min(0.05, (ts - letzteZeit) / 1000 || 0);
        letzteZeit = ts;
        if (phase === 'laeuft') aktualisieren(dt);
        zeichnen(ts / 1000);
        anzeigenAktualisieren();
        requestAnimationFrame(schleife);
    }

    reglerAnzeigen();
    rekordEl.textContent = String(rekord);
    requestAnimationFrame(schleife);
})();
