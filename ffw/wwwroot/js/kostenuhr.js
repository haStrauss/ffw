(function () {
    'use strict';

    // ---------- Einstellungen (dauerhaft im Browser gespeichert) ----------
    var DEFAULTS = { count: 5, rate: 60, goal: 100 };
    var STORAGE_KEY = 'kostenuhr.settings';

    var settings = loadSettings();

    function loadSettings() {
        try {
            var raw = localStorage.getItem(STORAGE_KEY);
            if (raw) {
                var s = JSON.parse(raw);
                return {
                    count: isFinite(s.count) && s.count >= 0 ? s.count : DEFAULTS.count,
                    rate: isFinite(s.rate) && s.rate >= 0 ? s.rate : DEFAULTS.rate,
                    goal: isFinite(s.goal) && s.goal > 0 ? s.goal : DEFAULTS.goal
                };
            }
        } catch (e) { /* localStorage evtl. nicht verfuegbar */ }
        return { count: DEFAULTS.count, rate: DEFAULTS.rate, goal: DEFAULTS.goal };
    }

    function saveSettings() {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(settings)); } catch (e) { }
    }

    // ---------- Berechnung ----------
    // Betrag = Anzahl Mitarbeiter x Stundensatz / 3600 x t (Sekunden)
    // Der Betrag wird pro Tick aufsummiert, damit geaenderte Einstellungen
    // waehrend des Laufs nur fuer die Zukunft gelten.
    var amount = 0;
    var elapsedMs = 0;
    var running = false;
    var lastTs = 0;
    var timerId = null;

    function costPerSecond() {
        return settings.count * settings.rate / 3600;
    }

    function tick() {
        if (!running) return;
        var now = performance.now();
        var dt = now - lastTs;
        lastTs = now;
        elapsedMs += dt;
        amount += (dt / 1000) * costPerSecond();
    }

    // ---------- Elemente ----------
    var $ = function (id) { return document.getElementById(id); };
    var amountEl = $('amount'), elapsedEl = $('elapsed'), ringEl = $('ring');
    var progressEl = $('ring-progress');
    var btnStart = $('btn-start'), btnStop = $('btn-stop'), btnReset = $('btn-reset');
    var inCount = $('in-count'), inRate = $('in-rate'), inGoal = $('in-goal');
    var summaryEl = $('summary');

    var RADIUS = 120;
    var CIRC = 2 * Math.PI * RADIUS;
    progressEl.style.strokeDasharray = CIRC;

    var eur = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' });
    var eur4 = new Intl.NumberFormat('de-DE', {
        style: 'currency', currency: 'EUR', minimumFractionDigits: 4, maximumFractionDigits: 4
    });
    var num = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 2 });

    // ---------- Anzeige ----------
    function pad(n) { return n < 10 ? '0' + n : '' + n; }

    function render() {
        tick();

        var text = eur.format(amount);
        amountEl.textContent = text;
        amountEl.style.fontSize = fitFontSize(text) + 'px';

        var total = Math.floor(elapsedMs / 1000);
        elapsedEl.textContent =
            pad(Math.floor(total / 3600)) + ':' + pad(Math.floor((total % 3600) / 60)) + ':' + pad(total % 60);

        // Ein Vollkreis entspricht dem Ring-Ziel, danach beginnt der Ring von vorn.
        var fraction = settings.goal > 0 ? (amount % settings.goal) / settings.goal : 0;
        progressEl.style.strokeDashoffset = CIRC * (1 - fraction);
        progressEl.style.opacity = fraction > 0 ? 1 : 0;
    }

    // Schrift so skalieren, dass der Betrag im Innenraum des Rings bleibt.
    function fitFontSize(text) {
        var inner = ringEl.clientWidth * 0.62;
        if (!inner || !text.length) return 36;
        return Math.max(16, Math.min(56, inner / (text.length * 0.58)));
    }

    function updateButtons() {
        btnStart.disabled = running;
        btnStop.disabled = !running;
    }

    function updateSummary() {
        var perHour = settings.count * settings.rate;
        summaryEl.textContent =
            'Gesamtkosten: ' + eur.format(perHour) + ' pro Stunde\n' +
            '(' + eur4.format(perHour / 3600) + ' pro Sekunde)';
    }

    // ---------- Start / Stopp / Reset ----------
    function start() {
        if (running) return;
        running = true;
        lastTs = performance.now();
        timerId = setInterval(render, 50);
        updateButtons();
    }

    function stop() {
        if (!running) return;
        tick();
        running = false;
        clearInterval(timerId);
        timerId = null;
        render();
        updateButtons();
    }

    function reset() {
        stop();
        amount = 0;
        elapsedMs = 0;
        render();
    }

    btnStart.addEventListener('click', start);
    btnStop.addEventListener('click', stop);
    btnReset.addEventListener('click', reset);

    // ---------- Einstellungen: Eingabe ----------
    function parseNumber(s) {
        if (s == null) return NaN;
        s = String(s).trim().replace(/\s/g, '');
        if (!s) return NaN;
        // Deutsches Format: "1.234,56" -> "1234.56"; "60,5" -> "60.5"
        if (s.indexOf(',') !== -1) s = s.replace(/\./g, '').replace(',', '.');
        return /^\d*\.?\d+$|^\d+\.$/.test(s) ? parseFloat(s) : NaN;
    }

    inCount.addEventListener('input', function () {
        var v = parseNumber(inCount.value);
        if (isFinite(v) && v >= 0 && Math.floor(v) === v) {
            tick(); // bisherige Zeit noch mit den alten Werten verrechnen
            settings.count = v;
            saveSettings();
        }
        updateSummary();
    });

    inRate.addEventListener('input', function () {
        var v = parseNumber(inRate.value);
        if (isFinite(v) && v >= 0) {
            tick();
            settings.rate = v;
            saveSettings();
        }
        updateSummary();
    });

    inGoal.addEventListener('input', function () {
        var v = parseNumber(inGoal.value);
        if (isFinite(v) && v > 0) {
            settings.goal = v;
            saveSettings();
        }
        updateSummary();
        render();
    });

    // ---------- Tabs ----------
    var tabMain = $('tab-main'), tabSettings = $('tab-settings');
    var viewMain = $('view-main'), viewSettings = $('view-settings');

    function showTab(main) {
        tabMain.classList.toggle('active', main);
        tabSettings.classList.toggle('active', !main);
        tabMain.setAttribute('aria-selected', main);
        tabSettings.setAttribute('aria-selected', !main);
        viewMain.hidden = !main;
        viewSettings.hidden = main;
        if (main) render();
    }

    tabMain.addEventListener('click', function () { showTab(true); });
    tabSettings.addEventListener('click', function () { showTab(false); });

    window.addEventListener('resize', render);

    // ---------- Init ----------
    inCount.value = String(settings.count);
    inRate.value = num.format(settings.rate);
    inGoal.value = num.format(settings.goal);
    updateSummary();
    updateButtons();
    render();
})();
