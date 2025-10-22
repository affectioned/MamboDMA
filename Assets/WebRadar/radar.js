// WebRadar ¡ª absolute mapping with OriginX/OriginY (never tied to your spawn).
// Drag pans map+blips together; zoom scales both. Origin edits don't move the map.
// Map picker, filters, icon sizes/colors, SSE + polling fallback.

(function () {
  // Canvas
  var CANVAS = document.getElementById("radar");
  if (!CANVAS) { console.error("[WebRadar] #radar canvas not found"); return; }
  var CTX = CANVAS.getContext("2d");

  // Topbar
  var mapSelEl    = document.getElementById("mapSel");
  var serverUrlEl = document.getElementById("serverUrl");
  var btnConn     = document.getElementById("btnConnect");
  var btnCopy     = document.getElementById("btnCopy");
  var statusEl    = document.getElementById("status");

  // Mapping controls
  var zoomEl   = document.getElementById("zoom");
  var cmppEl   = document.getElementById("cmpp");
  var cxEl     = document.getElementById("cx"); // OriginX
  var cyEl     = document.getElementById("cy"); // OriginY
  var swapXYEl = document.getElementById("swapxy");
  var flipXEl  = document.getElementById("flipx");
  var flipYEl  = document.getElementById("flipy");

  // Filters & icons
  var fSelfEl    = document.getElementById("fSelf");
  var fPMCsEl    = document.getElementById("fPMCs");
  var fBotsEl    = document.getElementById("fBots");
  var fDeadEl    = document.getElementById("fDead");
  var maxRangeEl = document.getElementById("maxRange");

  var szSelfEl = document.getElementById("szSelf");
  var szPMCEl  = document.getElementById("szPMC");
  var szBotEl  = document.getElementById("szBot");
  var szDeadEl = document.getElementById("szDead");

  var colSelfEl = document.getElementById("colSelf");
  var colPMCEl  = document.getElementById("colPMC");
  var colBotEl  = document.getElementById("colBot");
  var colDeadEl = document.getElementById("colDead");

  // ©¤©¤ Per-map presets (baked). Use originX/originY (absolute world coords that map to image center).
  var PRESETS = {
    Farm: {
      map: "Farm.png",
      cmPerPx: 100,
      originX: -65742.33,
      originY:  19203.812,
      swapXY: false, flipX: false, flipY: false, zoom: 0.5
    },
    Valley: {
      map: "Valley.png",
      cmPerPx: 100,
      originX:  76602.749,
      originY: -41996.181,
      swapXY: false, flipX: false, flipY: false, zoom: 0.55
    }
  };

  // Lock axis tools if you don't want accidental drift
  var LOCK_MAPPING_AXES = true;

  // Persistence (optional) ¡ª remember last mapping per map
  function LS_KEY(k){ return "webradar_" + k; }

  // State
  var activeKey = mapSelEl && mapSelEl.value ? mapSelEl.value : "Valley";
  var baked = loadMapConfig(activeKey) || PRESETS[activeKey];

  var dpr = window.devicePixelRatio || 1;
  var es = null, pollTimer = null, connected = false, lastFrame = null;

  // Image & pan state
  var mapImg = new Image();
  var imgPan = { x: 0, y: 0 };

  // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
  // Init

  function applyPreset(p){
    if (cmppEl) cmppEl.value = p.cmPerPx;
    if (cxEl)   cxEl.value   = p.originX;
    if (cyEl)   cyEl.value   = p.originY;
    if (swapXYEl) swapXYEl.checked = !!p.swapXY;
    if (flipXEl)  flipXEl.checked  = !!p.flipX;
    if (flipYEl)  flipYEl.checked  = !!p.flipY;
    if (zoomEl)   zoomEl.value     = p.zoom;

    if (LOCK_MAPPING_AXES){
      if (cmppEl)   cmppEl.disabled   = true;
      if (swapXYEl) swapXYEl.disabled = true;
      if (flipXEl)  flipXEl.disabled  = true;
      if (flipYEl)  flipYEl.disabled  = true;
    }

    // Default server textbox
    if (serverUrlEl) {
      serverUrlEl.value = (location.origin && /^https?:/i.test(location.origin))
        ? location.origin
        : "http://localhost:8088";
    }

    // reset visual pan for new map
    imgPan.x = 0; imgPan.y = 0;
  }

  function loadMap(src){
    mapImg = new Image();
    mapImg.onload  = function(){ drawFrame(lastFrame); };
    mapImg.onerror = function(){ console.warn("[WebRadar] Map image failed:", src); drawFrame(lastFrame); };
    mapImg.src = src; // file must exist in Assets/WebRadar/
  }

  function resizeCanvas(){
    dpr = window.devicePixelRatio || 1;
    var rect = CANVAS.getBoundingClientRect();
    CANVAS.width  = Math.max(1, Math.round(rect.width  * dpr));
    CANVAS.height = Math.max(1, Math.round(rect.height * dpr));
    CTX.setTransform(1,0,0,1,0,0);
  }

  // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
  // Mapping

  function readMapping(){
    var zoom   = zoomEl ? parseFloat(zoomEl.value) : baked.zoom;
    var cmpp   = cmppEl ? parseFloat(cmppEl.value) : baked.cmPerPx;
    var ox     = cxEl   ? parseFloat(cxEl.value)   : baked.originX;
    var oy     = cyEl   ? parseFloat(cyEl.value)   : baked.originY;
    var swapXY = swapXYEl ? !!swapXYEl.checked : baked.swapXY;
    var flipX  = flipXEl  ? !!flipXEl.checked  : baked.flipX;
    var flipY  = flipYEl  ? !!flipYEl.checked  : baked.flipY;

    if (isNaN(zoom)) zoom = baked.zoom;
    if (isNaN(cmpp)) cmpp = baked.cmPerPx;
    if (isNaN(ox))   ox   = baked.originX;
    if (isNaN(oy))   oy   = baked.originY;

    if (LOCK_MAPPING_AXES){ swapXY = baked.swapXY; flipX = baked.flipX; flipY = baked.flipY; }

    return {
      zoom: clamp(zoom, 0.25, 8),
      cmPerPx: clamp(cmpp, 1, 100000),
      originX: ox,
      originY: oy,
      swapXY: swapXY,
      flipX:  flipX,
      flipY:  flipY,
      canvasW: CANVAS.width,
      canvasH: CANVAS.height,
      imgW: mapImg.naturalWidth  || 1024,
      imgH: mapImg.naturalHeight || 1024,
      cx: CANVAS.width * 0.5,
      cy: CANVAS.height * 0.5
    };
  }

  // Absolute world ¡ú screen using fixed origin; then apply visual pan
  function worldToScreen(wx, wy, m){
    m = m || readMapping();

    var dx = wx - m.originX;
    var dy = wy - m.originY;

    var imDX = dx / m.cmPerPx;
    var imDY = dy / m.cmPerPx;

    if (m.swapXY){ var t = imDX; imDX = imDY; imDY = t; }
    if (m.flipX) imDX = -imDX;
    if (m.flipY) imDY = -imDY;

    var x = m.cx + imDX * m.zoom + imgPan.x;
    var y = m.cy + imDY * m.zoom + imgPan.y;
    return [x, y];
  }

  // (Inverse, handy for tools)
  function screenToWorld(px, py, m){
    m = m || readMapping();
    var imDX = (px - (m.cx + imgPan.x)) / m.zoom;
    var imDY = (py - (m.cy + imgPan.y)) / m.zoom;
    if (m.flipX) imDX = -imDX;
    if (m.flipY) imDY = -imDY;
    if (m.swapXY){ var t = imDX; imDX = imDY; imDY = t; }
    return {
      x: m.originX + imDX * m.cmPerPx,
      y: m.originY + imDY * m.cmPerPx
    };
  }

  // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
  // Drawing

  function drawFrame(frame){
    CTX.setTransform(1,0,0,1,0,0);
    CTX.clearRect(0,0,CANVAS.width,CANVAS.height);

    var m = readMapping();

    // Cover render: scale image to fill canvas; origin edits do NOT move image (only blips)
    if (mapImg.complete && mapImg.naturalWidth){
      var cover = Math.max(m.canvasW/m.imgW, m.canvasH/m.imgH);
      var s  = cover * m.zoom;
      var dw = m.imgW * s, dh = m.imgH * s;
      var dx = m.cx - dw/2 + imgPan.x;
      var dy = m.cy - dh/2 + imgPan.y;
      CTX.drawImage(mapImg, 0,0,m.imgW,m.imgH, dx,dy,dw,dh);
    } else {
      // fallback grid
      CTX.fillStyle="#000"; CTX.fillRect(0,0,CANVAS.width,CANVAS.height);
      CTX.strokeStyle="rgba(255,255,255,0.08)"; CTX.lineWidth=1*dpr;
      var step = Math.max(24*dpr, Math.min(CANVAS.width, CANVAS.height)/16);
      for (var x=0; x<=CANVAS.width; x+=step){ CTX.beginPath(); CTX.moveTo(x,0); CTX.lineTo(x,CANVAS.height); CTX.stroke(); }
      for (var y=0; y<=CANVAS.height; y+=step){ CTX.beginPath(); CTX.moveTo(0,y); CTX.lineTo(CANVAS.width,y); CTX.stroke(); }
    }

    if (!frame || frame.ok !== true) return;

    // Filters & icon settings
    var showSelf = fSelfEl ? !!fSelfEl.checked : true;
    var showPMCs = fPMCsEl ? !!fPMCsEl.checked : true;
    var showBots = fBotsEl ? !!fBotsEl.checked : true;
    var showDead = fDeadEl ? !!fDeadEl.checked : true;
    var maxRangeM = 0;
    if (maxRangeEl) {
      var tmp = parseFloat(maxRangeEl.value);
      maxRangeM = isNaN(tmp) ? 0 : Math.max(0, tmp);
    }

    var rSelf = clamp(parseInt((szSelfEl && szSelfEl.value) ? szSelfEl.value : "7",10), 2, 30);
    var rPMC  = clamp(parseInt((szPMCEl && szPMCEl.value) ? szPMCEl.value : "5",10), 2, 30);
    var rBot  = clamp(parseInt((szBotEl && szBotEl.value) ? szBotEl.value : "4",10), 2, 30);
    var rDead = clamp(parseInt((szDeadEl && szDeadEl.value) ? szDeadEl.value : "6",10), 2, 30);

    var cSelf = (colSelfEl && colSelfEl.value) ? colSelfEl.value : "#19ff6a";
    var cPMC  = (colPMCEl  && colPMCEl.value)  ? colPMCEl.value  : "#ff4a4a";
    var cBot  = (colBotEl  && colBotEl.value)  ? colBotEl.value  : "#0db1ff";
    var cDead = (colDeadEl && colDeadEl.value) ? colDeadEl.value : "#ffd600";

    // Self (record for range calc)
    var selfX=null, selfY=null;
    if (frame.self && typeof frame.self.x === "number" && typeof frame.self.y === "number"){
      selfX = frame.self.x; selfY = frame.self.y;
      if (showSelf){
        var sxy = worldToScreen(selfX, selfY, m);
        drawSelf(sxy[0], sxy[1], rSelf, cSelf);
      }
    }

    // Actors
    if (frame.actors && frame.actors.length){
      for (var i=0;i<frame.actors.length;i++){
        var a = frame.actors[i];
        if (maxRangeM > 0 && selfX !== null){
          var dx2 = a.x - selfX, dy2 = a.y - selfY;
          var distM = Math.hypot(dx2,dy2) / 100.0;
          if (distM > maxRangeM) continue;
        }

        if (a.dead){
          if (!showDead) continue;
        } else if (a.bot){
          if (!showBots) continue;
        } else {
          if (!showPMCs) continue;
        }

        var pxy = worldToScreen(a.x, a.y, m);
        if (a.dead)      drawDiamond(pxy[0], pxy[1], rDead, cDead);
        else if (a.bot)  drawCircle (pxy[0], pxy[1], rBot,  cBot);
        else             drawSquare (pxy[0], pxy[1], rPMC,  cPMC);
      }
    }
  }

  // Primitives
  function drawSelf(x,y,r,col){
    var rr = r*dpr;
    CTX.fillStyle = col;
    CTX.beginPath();
    CTX.moveTo(x, y-rr);
    CTX.lineTo(x+rr*0.85, y+rr*0.6);
    CTX.lineTo(x-rr*0.85, y+rr*0.6);
    CTX.closePath();
    CTX.fill();
  }
  function drawCircle(x,y,r,col){
    CTX.fillStyle = col;
    CTX.beginPath();
    CTX.arc(x,y,r*dpr,0,Math.PI*2);
    CTX.fill();
  }
  function drawSquare(x,y,half,col){
    var h = half*dpr;
    CTX.fillStyle = col;
    CTX.fillRect(x-h,y-h,h*2,h*2);
    CTX.strokeStyle="rgba(0,0,0,.9)";
    CTX.lineWidth=1*dpr;
    CTX.strokeRect(x-h,y-h,h*2,h*2);
  }
  function drawDiamond(x,y,r,col){
    var rr = r*dpr;
    CTX.fillStyle = col;
    CTX.beginPath();
    CTX.moveTo(x,   y-rr);
    CTX.lineTo(x+rr,y);
    CTX.lineTo(x,   y+rr);
    CTX.lineTo(x-rr,y);
    CTX.closePath();
    CTX.fill();
    CTX.strokeStyle="rgba(0,0,0,.9)";
    CTX.lineWidth=1*dpr;
    CTX.stroke();
  }

  // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
  // Interaction

  // Drag to pan (map+blips)
  var isDragging=false, dragStart={x:0,y:0}, panStart={x:0,y:0};
  CANVAS.addEventListener("mousedown", function(e){
    isDragging = true; CANVAS.classList.add("dragging");
    dragStart = clientToCanvas(e);
    panStart = { x: imgPan.x, y: imgPan.y };
  });
  window.addEventListener("mouseup", function(){
    if (isDragging){ isDragging=false; CANVAS.classList.remove("dragging"); }
  });
  window.addEventListener("mousemove", function(e){
    if (!isDragging) return;
    var cur = clientToCanvas(e);
    imgPan.x = panStart.x + (cur.x - dragStart.x);
    imgPan.y = panStart.y + (cur.y - dragStart.y);
    drawFrame(lastFrame);
  });

  // Wheel zoom (about canvas center)
  CANVAS.addEventListener("wheel", function(e){
    e.preventDefault();
    var m = readMapping();
    var z = clamp(m.zoom * (e.deltaY < 0 ? 1.1 : 0.9), 0.25, 8);
    if (z === m.zoom) return;
    if (zoomEl) zoomEl.value = z.toFixed(2);
    drawFrame(lastFrame);
  }, { passive:false });

  // Optional: Alt+Click to set origin to clicked world point (quick calibration)
  CANVAS.addEventListener("click", function(e){
    if (!e.altKey) return;
    var m = readMapping();
    var rect = CANVAS.getBoundingClientRect();
    var px = (e.clientX - rect.left)*dpr;
    var py = (e.clientY - rect.top )*dpr;
    var w = screenToWorld(px, py, m);
    if (cxEl) cxEl.value = w.x.toFixed(3);
    if (cyEl) cyEl.value = w.y.toFixed(3);
    drawFrame(lastFrame);
    saveMapConfig(activeKey);
  });

  // Map selection
  if (mapSelEl){
    mapSelEl.addEventListener("change", function(){
      activeKey = mapSelEl.value || "Valley";
      baked = loadMapConfig(activeKey) || PRESETS[activeKey];
      applyPreset(baked);
      loadMap(baked.map);
      drawFrame(lastFrame);
    });
  }

  // Redraw & persist on control changes
  [
    zoomEl, cmppEl, cxEl, cyEl, swapXYEl, flipXEl, flipYEl,
    fSelfEl, fPMCsEl, fBotsEl, fDeadEl, maxRangeEl,
    szSelfEl, szPMCEl, szBotEl, szDeadEl,
    colSelfEl, colPMCEl, colBotEl, colDeadEl
  ].forEach(function(el){
    if (!el) return;
    el.addEventListener("input", function(){
      drawFrame(lastFrame);
      saveMapConfig(activeKey);
    });
  });

  // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
  // Server connection (SSE + polling fallback)

  function baseUrl(){
    var typed = serverUrlEl && serverUrlEl.value ? serverUrlEl.value.trim() : "";
    if (/^https?:/i.test(typed)) return typed.replace(/\/+$/,"");
    if (location.origin && /^https?:/i.test(location.origin)) return location.origin;
    return "http://localhost:8088";
  }

  function connect(){
    var base = baseUrl();
    try { es = new EventSource(base + "/stream"); }
    catch (e) { console.warn("[WebRadar] SSE failed, fallback to polling:", e); return startPolling(base); }
    if (btnConn) btnConn.textContent="Connecting¡­";
    if (statusEl) statusEl.textContent="connecting¡­";
    es.addEventListener("open", function(){
      connected=true;
      if (statusEl) statusEl.textContent="connected (SSE)";
      if (btnConn)  btnConn.textContent="Disconnect";
      if (pollTimer){ clearInterval(pollTimer); pollTimer=null; }
    });
    es.addEventListener("error", function(){
      if (es){ es.close(); es=null; }
      startPolling(base);
    });
    es.addEventListener("frame", function(ev){
      try { lastFrame = JSON.parse(ev.data); }
      catch(e){ return; }
      drawFrame(lastFrame);
    });
  }
  function startPolling(base){
    if (pollTimer) clearInterval(pollTimer);
    connected=true;
    if (statusEl) statusEl.textContent="connected (poll)";
    if (btnConn)  btnConn.textContent="Disconnect";
    function tick(){
      fetch(base + "/api/frame", { cache:"no-store" })
        .then(function(r){ return r.ok ? r.json() : Promise.reject(); })
        .then(function(j){ lastFrame=j; drawFrame(lastFrame); })
        .catch(function(){});
    }
    tick(); pollTimer = setInterval(tick, 100);
  }
  function disconnect(){
    if (es){ es.close(); es=null; }
    if (pollTimer){ clearInterval(pollTimer); pollTimer=null; }
    connected=false;
    if (statusEl) statusEl.textContent="disconnected";
    if (btnConn)  btnConn.textContent="Connect";
  }

  if (btnConn) btnConn.addEventListener("click", function(){ return connected ? disconnect() : connect(); });
  if (btnCopy) btnCopy.addEventListener("click", copyMapping);

  // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
  // Persistence

  function saveMapConfig(key){
    var m = readMapping();
    var obj = {
      map: baked.map,
      cmPerPx: m.cmPerPx,
      originX: m.originX,
      originY: m.originY,
      swapXY: m.swapXY,
      flipX: m.flipX,
      flipY: m.flipY,
      zoom: m.zoom
    };
    try { localStorage.setItem(LS_KEY(key), JSON.stringify(obj)); } catch(e){}
  }

  function loadMapConfig(key){
    try {
      var raw = localStorage.getItem(LS_KEY(key));
      return raw ? JSON.parse(raw) : null;
    } catch (e) { return null; }
  }

  // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
  // Utilities

  function clientToCanvas(e){
    var r = CANVAS.getBoundingClientRect();
    return { x:(e.clientX - r.left)*dpr, y:(e.clientY - r.top)*dpr };
  }
  function clamp(v,lo,hi){ return Math.max(lo, Math.min(hi, v)); }

  function copyMapping(){
    var m = readMapping();
    var cfg = {
      map: baked.map,
      cmPerPx: m.cmPerPx,
      originX: m.originX,
      originY: m.originY,
      swapXY: m.swapXY,
      flipX: m.flipX,
      flipY: m.flipY,
      zoom: m.zoom
    };
    var txt = JSON.stringify(cfg, null, 2);
    console.log("[WebRadar mapping]", cfg);
    if (navigator.clipboard && navigator.clipboard.writeText){
      navigator.clipboard.writeText(txt).catch(function(){});
    }
    if (statusEl){
      statusEl.textContent = "mapping copied";
      setTimeout(function(){ statusEl.textContent = connected ? "connected" : "disconnected"; }, 1200);
    }
  }

  // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
  // Kickoff

  applyPreset(baked);
  loadMap(baked.map);
  resizeCanvas();
  drawFrame(null); // draw map (or grid) before data arrives
  window.addEventListener("resize", function(){ resizeCanvas(); drawFrame(lastFrame); });
})();
