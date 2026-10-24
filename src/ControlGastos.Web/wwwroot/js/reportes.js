// Mis·Gastos — gráficos de la página de reportes (Chart.js 4).
// Los datos vienen en data-datos (JSON) de cada <canvas>: la CSP no permite scripts inline.
(function () {
  "use strict";
  if (!window.Chart) return;

  var css = getComputedStyle(document.documentElement);
  function v(nombre, defecto) { return (css.getPropertyValue(nombre) || "").trim() || defecto; }

  // Paleta categórica validada (orden fijo: contado, cuotas, fijos) y tokens de texto/grilla.
  var C = {
    s1: v("--series-1", "#2a78d6"),
    s2: v("--series-2", "#eb6834"),
    s3: v("--series-3", "#1baf7a"),
    texto: v("--text-2", "#5b6b63"),
    tenue: v("--muted", "#647069"),
    grilla: v("--viz-grid", "#e6e9e4"),
    superficie: v("--surface", "#ffffff"),
  };

  Chart.defaults.font.family = v("--font", "Inter, system-ui, sans-serif");
  Chart.defaults.font.size = 12;
  Chart.defaults.color = C.texto;

  function money(n, moneda) {
    return (moneda === "USD" ? "US$ " : "$ ") + Number(n).toLocaleString("es-UY", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }
  function compacto(n, moneda) {
    var s = moneda === "USD" ? "US$ " : "$ ";
    var a = Math.abs(n);
    if (a >= 1e6) return s + (n / 1e6).toLocaleString("es-UY", { maximumFractionDigits: 1 }) + " M";
    if (a >= 1e3) return s + (n / 1e3).toLocaleString("es-UY", { maximumFractionDigits: a >= 1e4 ? 0 : 1 }) + " k";
    return s + n.toLocaleString("es-UY", { maximumFractionDigits: 0 });
  }
  // Color con transparencia (meses proyectados).
  function alfa(hex, a) {
    var n = parseInt(hex.slice(1), 16);
    return "rgba(" + (n >> 16 & 255) + "," + (n >> 8 & 255) + "," + (n & 255) + "," + a + ")";
  }

  var tooltipBase = {
    backgroundColor: v("--ink", "#0b1f1a"),
    titleColor: "#fff",
    bodyColor: v("--ink-text", "#b9c7c0"),
    footerColor: "#fff",
    padding: 10,
    cornerRadius: 10,
    boxPadding: 4,
    usePointStyle: true,
  };

  // ---------- Columnas apiladas: gasto por mes y por tipo ----------
  function graficoMeses(canvas, datos) {
    var meses = datos.meses, moneda = datos.moneda;
    var series = [
      { clave: "contado", nombre: "Contado", color: C.s1 },
      { clave: "cuotas", nombre: "Cuotas", color: C.s2 },
      { clave: "fijos", nombre: "Fijos", color: C.s3 },
    ];

    // Solo el segmento superior de cada columna lleva el extremo redondeado (4px); la base queda recta.
    function esTope(ctx) {
      var i = ctx.dataIndex, ds = ctx.datasetIndex;
      for (var k = series.length - 1; k > ds; k--) if (meses[i][series[k].clave] > 0) return false;
      return true;
    }

    new Chart(canvas, {
      type: "bar",
      data: {
        labels: meses.map(function (m) { return m.etiqueta; }),
        datasets: series.map(function (s) {
          return {
            label: s.nombre,
            data: meses.map(function (m) { return m[s.clave]; }),
            backgroundColor: meses.map(function (m) { return m.proyectado ? alfa(s.color, 0.38) : s.color; }),
            hoverBackgroundColor: meses.map(function (m) { return m.proyectado ? alfa(s.color, 0.55) : s.color; }),
            // Separación de 2px en el color de la superficie entre segmentos (sin bordes de color).
            borderColor: C.superficie,
            borderWidth: { top: 2, bottom: 0, left: 0, right: 0 },
            borderSkipped: "bottom",
            borderRadius: function (ctx) { return esTope(ctx) ? { topLeft: 4, topRight: 4 } : 0; },
            maxBarThickness: 24,
            categoryPercentage: 0.7,
          };
        }),
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: "index", intersect: false },
        plugins: {
          legend: { display: false }, // la leyenda va en HTML, arriba del gráfico
          tooltip: Object.assign({}, tooltipBase, {
            callbacks: {
              title: function (items) {
                var m = meses[items[0].dataIndex];
                return m.largo + (m.proyectado ? " · proyección" : "");
              },
              label: function (it) { return " " + it.dataset.label + ": " + money(it.raw, moneda); },
              footer: function (items) {
                var t = items.reduce(function (a, it) { return a + it.raw; }, 0);
                return "Total: " + money(t, moneda);
              },
            },
          }),
        },
        scales: {
          x: { stacked: true, grid: { display: false }, border: { color: C.grilla }, ticks: { color: C.texto } },
          y: {
            stacked: true, beginAtZero: true,
            grid: { color: C.grilla, lineWidth: 1 }, border: { display: false },
            ticks: { color: C.tenue, maxTicksLimit: 6, callback: function (val) { return compacto(val, moneda); } },
          },
        },
      },
    });
  }

  // ---------- Barras horizontales de una serie (categoría, medio de pago) ----------
  // Una sola serie: un solo color (slot 1), sin leyenda; el título de la tarjeta dice qué se mide.
  var etiquetaEnPunta = {
    id: "etiquetaEnPunta",
    afterDatasetsDraw: function (chart) {
      var ctx = chart.ctx, meta = chart.getDatasetMeta(0), moneda = chart.$moneda;
      ctx.save();
      ctx.font = "600 12px " + Chart.defaults.font.family;
      ctx.fillStyle = C.texto;
      ctx.textBaseline = "middle";
      meta.data.forEach(function (bar, i) {
        var texto = compacto(chart.data.datasets[0].data[i], moneda);
        var ancho = ctx.measureText(texto).width;
        var x = bar.x + 6;
        // Si no entra a la derecha de la barra, se omite: el valor sigue en el tooltip y en la tabla.
        if (x + ancho > chart.chartArea.right + 70) return;
        ctx.fillText(texto, x, bar.y);
      });
      ctx.restore();
    },
  };

  function graficoBarras(canvas, datos) {
    var items = datos.items, moneda = datos.moneda;
    var chart = new Chart(canvas, {
      type: "bar",
      data: {
        labels: items.map(function (x) { return x.nombre; }),
        datasets: [{
          data: items.map(function (x) { return x.total; }),
          backgroundColor: C.s1,
          borderRadius: 4,
          borderSkipped: "start",
          maxBarThickness: 18,
          categoryPercentage: 0.8,
        }],
      },
      options: {
        indexAxis: "y",
        responsive: true,
        maintainAspectRatio: false,
        layout: { padding: { right: 72 } },
        plugins: {
          legend: { display: false },
          tooltip: Object.assign({}, tooltipBase, {
            displayColors: false,
            callbacks: { label: function (it) { return money(it.raw, moneda); } },
          }),
        },
        scales: {
          x: { beginAtZero: true, grid: { color: C.grilla }, border: { display: false }, ticks: { color: C.tenue, maxTicksLimit: 5, callback: function (val) { return compacto(val, moneda); } } },
          y: { grid: { display: false }, border: { color: C.grilla }, ticks: { color: C.texto } },
        },
      },
      plugins: [etiquetaEnPunta],
    });
    chart.$moneda = moneda;
    chart.update("none");
  }

  document.querySelectorAll("canvas[data-chart]").forEach(function (canvas) {
    var datos;
    try { datos = JSON.parse(canvas.dataset.datos || "{}"); } catch (e) { return; }
    if (canvas.dataset.chart === "meses") graficoMeses(canvas, datos);
    else if (canvas.dataset.chart === "barras") graficoBarras(canvas, datos);
  });
})();
