// Mis·Gastos — interacciones de UI. Sin scripts inline: la política CSP solo permite este archivo.
(function () {
  "use strict";

  // Sidebar móvil
  document.addEventListener("click", function (e) {
    if (e.target.closest("[data-nav-toggle]")) document.body.classList.toggle("nav-open");
    else if (e.target.closest("[data-nav-close]")) document.body.classList.remove("nav-open");
  });
  document.addEventListener("keydown", function (e) {
    if (e.key === "Escape") document.body.classList.remove("nav-open");
  });

  // Mensajes flash
  function closeFlash(el) {
    el.classList.add("hide");
    setTimeout(function () { el.remove(); }, 260);
  }
  document.querySelectorAll("[data-flash-close]").forEach(function (b) {
    b.addEventListener("click", function () { closeFlash(b.closest(".flash")); });
  });
  document.querySelectorAll(".flash[data-autohide]").forEach(function (el) {
    setTimeout(function () { if (document.body.contains(el)) closeFlash(el); }, 6000);
  });

  // Mostrar / ocultar contraseña
  document.querySelectorAll("[data-toggle-pass]").forEach(function (b) {
    b.addEventListener("click", function () {
      var i = b.parentElement.querySelector("input");
      var show = i.type === "password";
      i.type = show ? "text" : "password";
      b.innerHTML = show ? '<i class="bi bi-eye-slash"></i>' : '<i class="bi bi-eye"></i>';
      b.setAttribute("aria-label", show ? "Ocultar contraseña" : "Mostrar contraseña");
    });
  });

  // Confirmación antes de acciones destructivas: <form data-confirm="¿Seguro?">
  document.querySelectorAll("form[data-confirm]").forEach(function (f) {
    f.addEventListener("submit", function (e) {
      if (!window.confirm(f.dataset.confirm)) e.preventDefault();
    });
  });

  // Enviar el formulario al cambiar el valor (selector de mes)
  document.querySelectorAll("[data-autosubmit]").forEach(function (i) {
    i.addEventListener("change", function () { if (i.value) i.form.submit(); });
  });

  // Validación visual de formularios (HTML5 + estilos de Bootstrap)
  document.querySelectorAll("form[data-validate]").forEach(function (form) {
    form.addEventListener("submit", function (e) {
      var ok = true;
      form.querySelectorAll("input, select, textarea").forEach(function (input) {
        if (input.closest("[hidden]")) return;
        var valid = input.checkValidity();
        input.classList.toggle("is-invalid", !valid);
        if (!valid) ok = false;
      });
      if (!ok) {
        e.preventDefault();
        e.stopImmediatePropagation();
        var first = form.querySelector(".is-invalid");
        if (first) first.focus();
      }
    });
    ["input", "change"].forEach(function (ev) {
      form.addEventListener(ev, function (e) {
        if (e.target.classList && e.target.classList.contains("is-invalid") && e.target.checkValidity()) e.target.classList.remove("is-invalid");
      });
    });
  });

  // Formato de montos (es-UY)
  function fmtMoney(v, moneda) {
    var n = isFinite(v) ? v : 0;
    return (moneda === "USD" ? "US$ " : "$ ") + n.toLocaleString("es-UY", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }
  var MESES = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];
  function fmtMes(anio, mes0) {
    var d = new Date(anio, mes0, 1);
    return MESES[d.getMonth()] + " " + d.getFullYear();
  }

  // Formulario de gasto: campos de cuotas, resumen en vivo, mes de facturación y sugerencias
  var formGasto = document.getElementById("form-gasto");
  if (formGasto) {
    var sw = formGasto.querySelector("[data-toggle-cuotas]");
    var bloque = document.getElementById("bloque-cuotas");
    var monto = formGasto.querySelector("[name=MontoCuota]");
    var moneda = formGasto.querySelector("[name=Moneda]");
    var fecha = formGasto.querySelector("[name=Fecha]");
    var cant = formGasto.querySelector("[name=CantidadCuotas]");
    var actual = formGasto.querySelector("[name=CuotaActual]");
    var desc = formGasto.querySelector("[name=Descripcion]");
    var categoria = formGasto.querySelector("[name=CategoriaId]");
    var lblMonto = document.getElementById("lbl-monto");
    var MESES_LARGOS = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    function $(id) { return document.getElementById(id); }
    function tarjetaElegida() { return formGasto.querySelector("[name=TarjetaId]:checked"); }

    // Mismo cálculo que PlanDeCuotas.PeriodoDeFacturacion en el servidor.
    function mesFacturacion(anio, mes0, dia, cierre) {
      if (!cierre) return mes0;
      var ultimo = Math.min(cierre, new Date(anio, mes0 + 1, 0).getDate());
      return dia > ultimo ? mes0 + 1 : mes0;
    }

    function actualizar() {
      var enCuotas = sw.checked;
      bloque.hidden = !enCuotas;
      lblMonto.textContent = enCuotas ? "Monto de cada cuota" : "Monto";
      $("s-lbl-cuota").textContent = enCuotas ? "Cuota" : "Monto";
      $("s-lbl-desde").textContent = enCuotas ? "Primera cuota" : "Mes";
      $("s-row-hasta").hidden = !enCuotas;

      var m = parseFloat(monto.value);
      var n = enCuotas ? Math.max(1, parseInt(cant.value, 10) || 1) : 1;
      var a = enCuotas ? Math.max(1, parseInt(actual.value, 10) || 1) : 1;
      if (enCuotas) actual.max = n;
      var cur = moneda.value;

      $("s-cuota").textContent = isFinite(m) && m > 0 ? fmtMoney(m, cur) : "—";
      $("s-cant").textContent = n;
      $("s-total").textContent = isFinite(m) && m > 0 ? fmtMoney(m * n, cur) : "—";

      var t = tarjetaElegida();
      var cierre = t && t.dataset.cierre ? parseInt(t.dataset.cierre, 10) : null;
      var aviso = $("s-cierre");
      var partes = (fecha.value || "").split("-");
      if (partes.length === 3) {
        var anio = parseInt(partes[0], 10), mes0 = parseInt(partes[1], 10) - 1, dia = parseInt(partes[2], 10);
        var factura = mesFacturacion(anio, mes0, dia, cierre);
        var primero = factura - (a - 1);
        $("s-desde").textContent = fmtMes(anio, primero);
        $("s-hasta").textContent = fmtMes(anio, primero + n - 1);
        if (cierre) {
          var d = new Date(anio, factura, 1);
          aviso.querySelector("span").textContent = "La tarjeta cierra el " + cierre + ": " +
            (factura !== mes0 ? "esta compra pasa al estado de cuenta de " : "esta compra entra en el estado de cuenta de ") +
            MESES_LARGOS[d.getMonth()] + " " + d.getFullYear() + ".";
          aviso.hidden = false;
        } else {
          aviso.hidden = true;
        }
      } else {
        $("s-desde").textContent = $("s-hasta").textContent = "—";
        aviso.hidden = true;
      }
    }

    [sw, monto, moneda, fecha, cant, actual].forEach(function (el) {
      el.addEventListener("input", actualizar);
      el.addEventListener("change", actualizar);
    });
    formGasto.querySelectorAll("[name=TarjetaId]").forEach(function (r) { r.addEventListener("change", actualizar); });
    sw.addEventListener("change", function () {
      if (sw.checked && (parseInt(cant.value, 10) || 1) < 2) cant.value = 2;
      actualizar();
    });
    actualizar();

    // Sugerencias: al elegir una descripción usada antes, completa categoría, medio de pago y moneda.
    var sugerencias = [];
    try { sugerencias = JSON.parse(formGasto.dataset.sugerencias || "[]"); } catch (e) { sugerencias = []; }
    if (sugerencias.length && desc) {
      desc.addEventListener("change", function () {
        var v = desc.value.trim().toLowerCase();
        var s = sugerencias.find(function (x) { return x.d.toLowerCase() === v; });
        if (!s) return;
        if (s.c != null && categoria && !categoria.value) categoria.value = String(s.c);
        var radio = formGasto.querySelector("[name=TarjetaId][value='" + (s.t == null ? "" : s.t) + "']");
        if (radio) radio.checked = true;
        if (s.m) moneda.value = s.m;
        $("hint-sugerencia").hidden = false;
        actualizar();
      });
    }
  }

  // Formulario de tarjeta: el día de cierre solo aplica a crédito
  var formTarjeta = document.getElementById("form-tarjeta");
  if (formTarjeta) {
    var bloqueCierre = document.getElementById("bloque-cierre");
    formTarjeta.querySelectorAll("[name=Tipo]").forEach(function (r) {
      r.addEventListener("change", function () {
        var credito = formTarjeta.querySelector("[name=Tipo]:checked").value === "Credito";
        bloqueCierre.hidden = !credito;
      });
    });
  }

  // Filtros de tabla: <div data-table-tools="idTabla"> con tipo (data-kind), selects [data-filter=x]
  // que comparan contra data-x de cada fila, y búsqueda. Si las filas tienen data-monto y data-moneda,
  // el pie [data-total=UYU|USD] muestra el total de lo visible.
  document.querySelectorAll("[data-table-tools]").forEach(function (tools) {
    var table = document.getElementById(tools.dataset.tableTools);
    if (!table) return;
    var rows = Array.prototype.slice.call(table.querySelectorAll("tbody tr[data-kind]"));
    var empty = table.querySelector("tbody tr.no-results");
    var totales = table.querySelectorAll("[data-total]");
    var totalLabel = table.querySelector("[data-total-label]");
    var selects = Array.prototype.slice.call(tools.querySelectorAll("select[data-filter]"));
    var kind = "all", q = "";

    function apply() {
      var shown = 0, sumas = { UYU: 0, USD: 0 };
      rows.forEach(function (r) {
        var ok = (kind === "all" || r.dataset.kind === kind) && (!q || (r.dataset.search || "").indexOf(q) !== -1);
        selects.forEach(function (s) { if (ok && s.value && r.dataset[s.dataset.filter] !== s.value) ok = false; });
        r.hidden = !ok;
        if (ok) {
          shown++;
          if (r.dataset.moneda && r.dataset.monto) sumas[r.dataset.moneda] += parseFloat(r.dataset.monto);
        }
      });
      if (empty) empty.hidden = shown !== 0;
      var filtrado = shown !== rows.length;
      totales.forEach(function (el) {
        var cur = el.dataset.total;
        el.textContent = fmtMoney(sumas[cur], cur);
        el.hidden = cur === "USD" && sumas.USD === 0;
      });
      if (totalLabel) totalLabel.textContent = filtrado ? "Total de lo filtrado (" + shown + ")" : "Total del mes";
    }
    tools.querySelectorAll("[data-kind-filter]").forEach(function (b) {
      b.addEventListener("click", function () {
        tools.querySelectorAll("[data-kind-filter]").forEach(function (x) { x.classList.remove("active"); x.setAttribute("aria-pressed", "false"); });
        b.classList.add("active"); b.setAttribute("aria-pressed", "true");
        kind = b.dataset.kindFilter; apply();
      });
    });
    selects.forEach(function (s) { s.addEventListener("change", apply); });
    var input = tools.querySelector("input[type=search]");
    if (input) input.addEventListener("input", function () {
      q = input.value.trim().toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "");
      apply();
    });
  });

  // Importación: marcar todas y total de lo seleccionado
  var formConfirmar = document.getElementById("form-confirmar");
  if (formConfirmar) {
    var filas = Array.prototype.slice.call(formConfirmar.querySelectorAll("[data-import-row]"));
    var todas = formConfirmar.querySelector("[data-check-all]");
    function casilla(f) { return f.querySelector("input[type=checkbox]"); }
    function recalcular() {
      var sumas = { UYU: 0, USD: 0 }, n = 0;
      filas.forEach(function (f) {
        if (!casilla(f).checked) return;
        n++;
        sumas[f.dataset.moneda] += parseFloat(f.dataset.monto);
      });
      formConfirmar.querySelector("[data-import-count]").textContent = n + " de " + filas.length + " seleccionados";
      formConfirmar.querySelectorAll("[data-import-total]").forEach(function (el) {
        var cur = el.dataset.importTotal;
        el.textContent = fmtMoney(sumas[cur], cur);
        el.hidden = cur === "USD" && sumas.USD === 0;
      });
      todas.checked = n === filas.length;
      todas.indeterminate = n > 0 && n < filas.length;
    }
    todas.addEventListener("change", function () {
      filas.forEach(function (f) { casilla(f).checked = todas.checked; });
      recalcular();
    });
    filas.forEach(function (f) { casilla(f).addEventListener("change", recalcular); });
    formConfirmar.addEventListener("submit", function (e) {
      if (!filas.some(function (f) { return casilla(f).checked; })) {
        e.preventDefault();
        window.alert("Marca al menos una línea para importar.");
      }
    });
    recalcular();
  }

  // App instalable: service worker (solo estáticos) y botón "Instalar en este dispositivo".
  if ("serviceWorker" in navigator) {
    window.addEventListener("load", function () {
      navigator.serviceWorker.register("/sw.js").catch(function () { /* sin SW la app funciona igual */ });
    });
  }
  var botonInstalar = document.querySelector("[data-instalar]");
  var avisoInstalada = document.querySelector("[data-instalada]");
  var standalone = window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true;
  if (avisoInstalada && standalone) avisoInstalada.hidden = false;
  var pedidoInstalacion = null;
  window.addEventListener("beforeinstallprompt", function (e) {
    e.preventDefault();
    pedidoInstalacion = e;
    if (botonInstalar) botonInstalar.hidden = false;
  });
  if (botonInstalar) botonInstalar.addEventListener("click", function () {
    if (!pedidoInstalacion) return;
    pedidoInstalacion.prompt();
    pedidoInstalacion.userChoice.finally(function () { pedidoInstalacion = null; botonInstalar.hidden = true; });
  });
  window.addEventListener("appinstalled", function () {
    if (botonInstalar) botonInstalar.hidden = true;
    if (avisoInstalada) avisoInstalada.hidden = false;
  });

  // ---------- Indicador de carga ----------
  // Al enviar un formulario: el botón muestra una ruedita y no acepta un segundo clic (evita gastos duplicados).
  // Al navegar: barra fina arriba, solo si la página tarda más de 250 ms (así no parpadea en cargas rápidas).
  var raiz = document.documentElement;
  var estado = document.querySelector("[data-carga-estado]");
  var temporizador = null;

  function mostrarCarga() {
    clearTimeout(temporizador);
    temporizador = setTimeout(function () {
      raiz.classList.add("cargando");
      if (estado) estado.textContent = "Cargando…";
    }, 250);
  }

  function ocultarCarga() {
    clearTimeout(temporizador);
    raiz.classList.remove("cargando");
    if (estado) estado.textContent = "";
    document.querySelectorAll("form[data-enviando]").forEach(function (f) { delete f.dataset.enviando; });
    document.querySelectorAll(".btn-cargando").forEach(function (b) {
      if (b.dataset.htmlOriginal != null) b.innerHTML = b.dataset.htmlOriginal;
      b.classList.remove("btn-cargando");
      b.removeAttribute("aria-busy");
      delete b.dataset.htmlOriginal;
    });
  }

  // Se registra en document (fase de burbuja): corre después de la validación y de los data-confirm,
  // así que si alguno canceló el envío (defaultPrevented) no se muestra nada.
  document.addEventListener("submit", function (e) {
    if (e.defaultPrevented) return;
    var form = e.target;
    if (form.dataset.enviando) { e.preventDefault(); return; }   // segundo clic mientras se envía
    form.dataset.enviando = "1";

    var boton = e.submitter || form.querySelector("[type=submit]");
    if (boton && boton.matches("button")) {
      // No se usa "disabled": el navegador dejaría de enviar el valor del botón (por ejemplo "Guardar y cargar otro").
      boton.dataset.htmlOriginal = boton.innerHTML;
      boton.classList.add("btn-cargando");
      boton.setAttribute("aria-busy", "true");
      var spinner = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span>';
      var texto = boton.dataset.textoCarga;
      if (boton.classList.contains("btn-icon")) boton.innerHTML = spinner;
      else if (texto) boton.innerHTML = spinner + "<span>" + texto + "</span>";
      else {
        var icono = boton.querySelector("i.bi");
        if (icono) icono.outerHTML = spinner; else boton.insertAdjacentHTML("afterbegin", spinner);
      }
    }
    mostrarCarga();
  });

  document.addEventListener("click", function (e) {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    var a = e.target.closest("a[href]");
    if (!a || a.target === "_blank" || a.hasAttribute("download") || a.hasAttribute("data-sin-carga")) return;
    var url = new URL(a.href, location.href);
    if (url.origin !== location.origin) return;
    if (url.pathname === location.pathname && url.search === location.search) return; // anclas en la misma página
    mostrarCarga();
  });

  // Al volver con el botón "atrás" el navegador puede restaurar la página tal como quedó: se limpia el estado.
  window.addEventListener("pageshow", ocultarCarga);
})();
