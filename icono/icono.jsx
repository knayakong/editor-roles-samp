// Ícono v2 del Editor de roles SA-MP — atardecer de Los Santos (ciudad + palmeras a contraluz),
// burbuja de chat con sombra dura y "RP" verde Grove con contorno y sombra, al estilo de los títulos de GTA.
// Se corre por COM (DoJavaScript). Mesa 256×256 (y hacia ARRIBA); exporta PNG 256/64/48/32/24/16 y guarda el .ai.
(function () {
  var doc = null;
  try {
    var CARPETA = File($.fileName).parent.fsName.split("\\").join("/") + "/"; // la carpeta de este script
    function col(h) { var c = new RGBColor(); c.red = parseInt(h.substr(0, 2), 16); c.green = parseInt(h.substr(2, 2), 16); c.blue = parseInt(h.substr(4, 2), 16); return c; }
    var NEGRO = col("111014");

    doc = app.documents.add(DocumentColorSpace.RGB, 256, 256);
    doc.artboards[0].artboardRect = [0, 256, 256, 0];
    var capa = doc.layers[0]; capa.name = "icono";

    function Y(y) { return 256 - y; } // y desde ARRIBA
    function caja(x, y, w, h, r) { return capa.pathItems.roundedRectangle(Y(y), x, w, h, r, r); }
    function trazo(it, ancho, c) { it.stroked = true; it.strokeColor = c || NEGRO; it.strokeWidth = ancho; it.strokeJoin = StrokeJoin.ROUNDENDJOIN; it.strokeCap = StrokeCap.ROUNDENDCAP; }
    function sinTrazo(it) { it.stroked = false; }
    function relleno(it, c) { it.filled = true; it.fillColor = c; }
    function degradado(nombre, paradas) {
      var g = doc.gradients.add(); g.name = nombre; g.type = GradientType.LINEAR;
      while (g.gradientStops.length < paradas.length) g.gradientStops.add();
      for (var i = 0; i < paradas.length; i++) { g.gradientStops[i].color = col(paradas[i][0]); g.gradientStops[i].rampPoint = paradas[i][1]; g.gradientStops[i].midPoint = 50; }
      var gc = new GradientColor(); gc.gradient = g; return gc;
    }
    function vertical(it) { it.rotate(-90, false, false, true, false, Transformation.CENTER); } // degradado arriba→abajo
    function poligono(pts, nombre) { var p = capa.pathItems.add(); p.setEntirePath(pts); p.closed = true; p.name = nombre || ""; return p; }

    var piezas = []; // lo que va recortado dentro del cuadrado

    // 1) cielo: atardecer en 4 tonos
    var cielo = caja(10, 10, 236, 236, 50); cielo.name = "cielo";
    relleno(cielo, degradado("atardecer", [["FFD27A", 0], ["FF8A4C", 34], ["C23F7E", 68], ["3A1C6B", 100]])); vertical(cielo); sinTrazo(cielo);
    piezas.push(cielo);

    // 2) sol con halo
    var halo = capa.pathItems.ellipse(Y(34), 128 - 86, 172, 172); halo.name = "halo";
    relleno(halo, col("FFE9A6")); sinTrazo(halo); halo.opacity = 28; piezas.push(halo);
    var sol = capa.pathItems.ellipse(Y(58), 128 - 60, 120, 120); sol.name = "sol";
    relleno(sol, degradado("sol", [["FFF6C2", 0], ["FFC15A", 100]])); vertical(sol); sinTrazo(sol); piezas.push(sol);

    // 3) ciudad a contraluz
    var silueta = col("2B1244");
    var edificios = [[10, 26, 50], [34, 18, 66], [50, 22, 40], [70, 16, 92], [84, 26, 70], [108, 14, 116], [120, 24, 82], [142, 18, 58], [158, 26, 98], [182, 20, 64], [200, 24, 80], [222, 26, 52]];
    for (var e = 0; e < edificios.length; e++) {
      var ed = capa.pathItems.rectangle(Y(246 - edificios[e][2]), edificios[e][0], edificios[e][1], edificios[e][2]);
      ed.name = "edificio"; relleno(ed, silueta); sinTrazo(ed); piezas.push(ed);
    }
    var antena = capa.pathItems.rectangle(Y(246 - 116 - 18), 114, 2, 18); relleno(antena, silueta); sinTrazo(antena); piezas.push(antena);
    var luz = col("FFC56B");
    var ventanas = [[74, 162], [74, 174], [111, 140], [111, 152], [111, 164], [126, 172], [132, 184], [164, 156], [172, 168], [164, 180], [206, 176], [212, 188]];
    for (var v = 0; v < ventanas.length; v++) { var vn = capa.pathItems.rectangle(Y(ventanas[v][1]), ventanas[v][0], 3, 4); relleno(vn, luz); sinTrazo(vn); vn.opacity = 80; piezas.push(vn); }

    // 4) palmeras en silueta (tronco curvo que se afina + hojas caídas)
    var hojaCol = col("1C0B30");
    function hoja(cx, cy, ang, largo, ancho, caida) {
      var izq = [], der = [], N = 14;
      for (var i = 0; i <= N; i++) {
        var t = i / N;
        var px = cx + largo * t * Math.cos(ang), py = cy + largo * t * Math.sin(ang) - caida * t * t;
        var dx = largo * Math.cos(ang), dy = largo * Math.sin(ang) - 2 * caida * t;
        var l = Math.sqrt(dx * dx + dy * dy); var nx = -dy / l, ny = dx / l;
        var w = ancho * Math.sin(Math.PI * Math.min(1, t * 1.05)) * (1 - 0.35 * t);
        izq.push([px + nx * w, py + ny * w]); der.unshift([px - nx * w, py - ny * w]);
      }
      var h = poligono(izq.concat(der), "hoja"); relleno(h, hojaCol); sinTrazo(h); piezas.push(h);
    }
    function palmera(x, alto, inclina, esc) {
      var base = 10, N = 12, tope = null, izq = [], der = [];
      for (var i = 0; i <= N; i++) {
        var t = i / N;
        var px = x + inclina * t * t * alto, py = base + alto * t;
        var w = (7 - 3.2 * t) * esc;
        izq.push([px - w, py]); der.unshift([px + w, py]);
        if (i == N) tope = [px, py];
      }
      var tronco = poligono(izq.concat(der), "tronco"); relleno(tronco, hojaCol); sinTrazo(tronco); piezas.push(tronco);
      var angs = [0.15, 0.75, 1.35, 1.9, 2.45, 3.0, 3.55];
      for (var k = 0; k < angs.length; k++) hoja(tope[0], tope[1], angs[k], 46 * esc, 7.5 * esc, 20 * esc);
    }
    palmera(30, 172, 0.10, 1.0);
    palmera(226, 122, -0.16, 0.85);

    // 5) brillo suave arriba
    var brillo = capa.pathItems.ellipse(Y(-36), -24, 304, 128); brillo.name = "brillo";
    relleno(brillo, col("FFFFFF")); sinTrazo(brillo); brillo.opacity = 14; piezas.push(brillo);

    // recorte: todo lo anterior vive DENTRO del cuadrado redondeado
    var grupo = capa.groupItems.add(); grupo.name = "escena";
    for (var p = 0; p < piezas.length; p++) piezas[p].move(grupo, ElementPlacement.PLACEATBEGINNING); // el cielo queda al fondo
    var mascara = caja(10, 10, 236, 236, 50); mascara.move(grupo, ElementPlacement.PLACEATBEGINNING);
    grupo.clipped = true;

    // 6) burbuja de chat con sombra dura
    var blanco = col("F7F5FA");
    function burbuja(dx, dy, c, op, conTrazo) {
      var cola = poligono([[98 + dx, Y(122) - dy], [86 + dx, Y(152) - dy], [126 + dx, Y(124) - dy]], "cola");
      relleno(cola, c); if (conTrazo) trazo(cola, 8); else sinTrazo(cola);
      var b = caja(72 + dx, 40 + dy, 160, 88, 22); b.name = "burbuja"; relleno(b, c); if (conTrazo) trazo(b, 8); else sinTrazo(b);
      if (op < 100) { cola.opacity = op; b.opacity = op; }
      if (conTrazo) { var tapa = caja(99 + dx, 122 + dy, 16, 11, 2); relleno(tapa, c); sinTrazo(tapa); } // borra el trazo de la burbuja SOLO dentro de la cola
    }
    burbuja(7, 7, NEGRO, 55, false);   // sombra
    burbuja(0, 0, blanco, 100, true);  // burbuja
    // renglones: /me (morado, con su *) · habla (gris) · /do (verde)
    var renglones = [["B98FE0", 118], ["5A5A66", 92], ["5CC23C", 106]];
    for (var i = 0; i < renglones.length; i++) {
      var r = caja(90 + (i == 0 ? 18 : 0), 56 + i * 22, renglones[i][1] - (i == 0 ? 18 : 0), 13, 6.5); r.name = "renglon-" + (i + 1);
      relleno(r, col(renglones[i][0])); trazo(r, 3.5);
    }
    var ast = capa.textFrames.add(); ast.contents = "*";
    ast.textRange.characterAttributes.textFont = app.textFonts.getByName("Impact"); ast.textRange.characterAttributes.size = 40;
    ast.textRange.characterAttributes.fillColor = col("B98FE0");
    var astO = ast.createOutline(); var ab = astO.geometricBounds; astO.translate(88 - ab[0], Y(50) - ab[1]);

    // 7) "RP" estilo título de GTA: verde Grove en degradado, contorno negro grueso, sombra desplazada
    var tf = capa.textFrames.add(); tf.contents = "RP";
    var ca = tf.textRange.characterAttributes;
    ca.textFont = app.textFonts.getByName("Impact"); ca.size = 86; ca.tracking = 30;
    var letras = tf.createOutline(); letras.name = "RP";
    var m = app.getRotationMatrix(0); m.mValueC = 0.16; letras.transform(m);
    var borde = letras.duplicate(); borde.name = "RP-borde";
    var sombra = letras.duplicate(); sombra.name = "RP-sombra";
    borde.move(letras, ElementPlacement.PLACEAFTER); sombra.move(borde, ElementPlacement.PLACEAFTER);
    function pintar(g, fn) {
      for (var j = 0; j < g.pageItems.length; j++) {
        var it = g.pageItems[j];
        if (it.typename == "GroupItem") { pintar(it, fn); continue; }
        var ps = it.typename == "CompoundPathItem" ? it.pathItems : [it];
        for (var k = 0; k < ps.length; k++) fn(ps[k]);
      }
    }
    pintar(sombra, function (q) { relleno(q, NEGRO); trazo(q, 16); });
    pintar(borde, function (q) { relleno(q, NEGRO); trazo(q, 14); });
    var verde = degradado("grove", [["C8F77A", 0], ["6FD24A", 45], ["2F8F3A", 100]]);
    pintar(letras, function (q) { relleno(q, verde); sinTrazo(q); });
    pintar(letras, function (q) { q.rotate(-90, false, false, true, false, Transformation.CENTER); });
    sombra.translate(5, -5); sombra.opacity = 70;
    var vb = sombra.visibleBounds; // [izq, arriba, der, abajo]
    var dx = 230 - vb[2], dy = 20 - vb[3];
    letras.translate(dx, dy); borde.translate(dx, dy); sombra.translate(dx, dy);

    // 8) marco: borde negro + filo de luz interior
    var marco = caja(10, 10, 236, 236, 50); marco.name = "marco"; marco.filled = false; trazo(marco, 10);
    var filo = caja(17, 17, 222, 222, 44); filo.name = "filo"; filo.filled = false; trazo(filo, 2, col("FFFFFF")); filo.opacity = 22;

    doc.saveAs(new File(CARPETA + "icono.ai"));
    var tamanos = [256, 64, 48, 32, 24, 16];
    for (var t = 0; t < tamanos.length; t++) {
      var o = new ExportOptionsPNG24(); o.artBoardClipping = true; o.transparency = true; o.antiAliasing = true;
      o.horizontalScale = o.verticalScale = tamanos[t] / 256 * 100;
      doc.exportFile(new File(CARPETA + "icono_" + tamanos[t] + ".png"), ExportType.PNG24, o);
    }
    var o2 = new ExportOptionsPNG24(); o2.artBoardClipping = true; o2.transparency = true; o2.antiAliasing = true; o2.horizontalScale = o2.verticalScale = 400;
    doc.exportFile(new File(CARPETA + "icono_1024_revision.png"), ExportType.PNG24, o2);
    doc.close(SaveOptions.DONOTSAVECHANGES);
    return "ok";
  } catch (e) {
    try { if (doc) doc.close(SaveOptions.DONOTSAVECHANGES); } catch (e2) {}
    return "ERR línea " + e.line + ": " + e.message;
  }
})();
