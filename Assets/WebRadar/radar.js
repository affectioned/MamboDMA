// WebRadar ¡ª map-pixel mapping with per-map input scale/offset defaults.
// Incoming feed ¡ú (scale + offset) ¡ú game map pixels ¡ú image pixels ¡ú cover-fit draw.
// Pan/zoom is purely visual; players stay glued to the map.

(function(){
  const CANVAS = document.getElementById("radar");
  const CTX    = CANVAS.getContext("2d");

  // UI
  const mapSelEl    = document.getElementById("mapSel");
  const serverUrlEl = document.getElementById("serverUrl");
  const btnConn     = document.getElementById("btnConnect");
  const btnCopy     = document.getElementById("btnCopy");
  const statusEl    = document.getElementById("status");
  const zoomEl      = document.getElementById("zoom");

  // Input scaling/offset
  const inScaleEl = document.getElementById("inScale");
  const inOffXEl  = document.getElementById("inOffX");
  const inOffYEl  = document.getElementById("inOffY");

  // Filters & icons
  const fSelfEl    = document.getElementById("fSelf");
  const fPMCsEl    = document.getElementById("fPMCs");
  const fBotsEl    = document.getElementById("fBots");
  const fDeadEl    = document.getElementById("fDead");
  const maxRangeEl = document.getElementById("maxRange");

  const szSelfEl = document.getElementById("szSelf");
  const szPMCEl  = document.getElementById("szPMC");
  const szBotEl  = document.getElementById("szBot");
  const szDeadEl = document.getElementById("szDead");

  const colSelfEl = document.getElementById("colSelf");
  const colPMCEl  = document.getElementById("colPMC");
  const colBotEl  = document.getElementById("colBot");
  const colDeadEl = document.getElementById("colDead");

  // Per-map config (includes your Farm numbers)
  const MAPS = {
    Farm: {
      map: "Farm.png",
      // in-game map box in map pixels:
      ingame: { left: 240, top: 0, right: 4078, bottom: 2158 },
      input:  { scale: 33, offX: 3599, offY: 685, zoom: 4 },
      units: "mappx"
    },
    Valley: {
      map: "Valley.png",
      ingame: { left: 0, top: 0, right: 1, bottom: 1 }, // placeholder
      input:  { scale: 100, offX: 0, offY: 0, zoom: 0.5 },
      units: "mappx"
    }
  };

  let dpr = window.devicePixelRatio || 1;

  // Single declarations (avoid redeclare errors)
  let es = null;
  let pollTimer = null;
  let connected = false;
  let lastFrame = null;

  let activeKey = (mapSelEl?.value) || "Farm";
  let cur = MAPS[activeKey];

  // Image + view
  let mapImg = new Image();
  let imgPan = { x: 0, y: 0 };

  // ---------- Canvas sizing ----------
  function resizeCanvas(){
    dpr = window.devicePixelRatio || 1;
    const rect = CANVAS.getBoundingClientRect();
    CANVAS.width  = Math.max(1, Math.round(rect.width  * dpr));
    CANVAS.height = Math.max(1, Math.round(rect.height * dpr));
    CTX.setTransform(1,0,0,1,0,0);
  }

  // ---------- Map load ----------
  function loadMap(which){
    activeKey = which;
    cur = MAPS[activeKey] || MAPS.Farm;

    // Apply per-map defaults to inputs
    if (zoomEl && cur.input && cur.input.zoom != null) zoomEl.value = cur.input.zoom;
    if (inScaleEl && cur.input) inScaleEl.value = cur.input.scale;
    if (inOffXEl  && cur.input) inOffXEl.value  = cur.input.offX;
    if (inOffYEl  && cur.input) inOffYEl.value  = cur.input.offY;

    imgPan.x = 0; imgPan.y = 0;

    mapImg = new Image();
    mapImg.onload  = () => drawFrame(lastFrame);
    mapImg.onerror = () => { console.warn("[WebRadar] failed to load", cur.map); drawFrame(lastFrame); };
    mapImg.src = cur.map;
  }

  // ---------- View read ----------
  function readView(){
    const z = clamp(parseFloat(zoomEl?.value || "0.5") || 0.5, 0.05, 4); // cap at 4 (slider max)
    return {
      zoom: z,
      canvasW: CANVAS.width,
      canvasH: CANVAS.height,
      cx: CANVAS.width * 0.5,
      cy: CANVAS.height * 0.5,
      imgW: mapImg.naturalWidth  || 1,
      imgH: mapImg.naturalHeight || 1,

      // input transform
      inScale: Math.max(1, parseFloat(inScaleEl?.value || (cur.input?.scale ?? 100)) || 100),
      inOffX:  parseFloat(inOffXEl?.value  || (cur.input?.offX  ?? 0)) || 0,
      inOffY:  parseFloat(inOffYEl?.value  || (cur.input?.offY  ?? 0)) || 0
    };
  }

  // ---------- Coord transforms ----------
  function feedToMapPx(x, y, v){
    const sx = x / v.inScale + v.inOffX;
    const sy = y / v.inScale + v.inOffY;
    return [sx, sy];
  }
  function mapPxToImagePx(mx, my, v){
    const inW = cur.ingame.right - cur.ingame.left;
    const inH = cur.ingame.bottom - cur.ingame.top;
    const ix = (mx - cur.ingame.left) * (v.imgW / inW);
    const iy = (my - cur.ingame.top)  * (v.imgH / inH);
    return [ix, iy];
  }
  function imageDrawRect(v){
    const sw = v.imgW, sh = v.imgH;
    const cover = Math.max(v.canvasW / sw, v.canvasH / sh);
    const s  = cover * v.zoom;
    const dw = sw * s, dh = sh * s;
    const dx = v.cx - dw/2 + imgPan.x;
    const dy = v.cy - dh/2 + imgPan.y;
    return { dx, dy, s, dw, dh };
  }

  // ---------- Draw ----------
  function drawFrame(frame){
    CTX.setTransform(1,0,0,1,0,0);
    CTX.clearRect(0,0,CANVAS.width,CANVAS.height);

    const v = readView();

    if (mapImg.complete && mapImg.naturalWidth){
      const R = imageDrawRect(v);
      CTX.drawImage(mapImg, 0,0, v.imgW,v.imgH, R.dx, R.dy, R.dw, R.dh);

      if (frame && frame.ok === true){
        drawActors(frame, v, R);
      }
    } else {
      CTX.fillStyle="#000"; CTX.fillRect(0,0,CANVAS.width,CANVAS.height);
      CTX.strokeStyle="rgba(255,255,255,0.08)"; CTX.lineWidth=1*dpr;
      const step = Math.max(24*dpr, Math.min(CANVAS.width, CANVAS.height)/16);
      for (let x=0; x<=CANVAS.width; x+=step){ CTX.beginPath(); CTX.moveTo(x,0); CTX.lineTo(x,CANVAS.height); CTX.stroke(); }
      for (let y=0; y<=CANVAS.height; y+=step){ CTX.beginPath(); CTX.moveTo(0,y); CTX.lineTo(CANVAS.width,y); CTX.stroke(); }
    }
  }

  function drawActors(frame, v, R){
    const showSelf = !!(fSelfEl?.checked);
    const showPMCs = !!(fPMCsEl?.checked);
    const showBots = !!(fBotsEl?.checked);
    const showDead = !!(fDeadEl?.checked);

    // Range disabled when using map pixels
    const useRange = cur.units !== "mappx";
    const maxRangeM = Math.max(0, parseFloat(maxRangeEl?.value || "0"));

    const rSelf = clamp(parseInt(szSelfEl?.value || "7",10), 2, 30);
    const rPMC  = clamp(parseInt(szPMCEl?.value  || "5",10), 2, 30);
    const rBot  = clamp(parseInt(szBotEl?.value  || "4",10), 2, 30);
    const rDead = clamp(parseInt(szDeadEl?.value || "6",10), 2, 30);

    const cSelf = colSelfEl?.value || "#19ff6a";
    const cPMC  = colPMCEl?.value  || "#ff4a4a";
    const cBot  = colBotEl?.value  || "#0db1ff";
    const cDead = colDeadEl?.value || "#ffd600";

    // Self
    let selfX=null, selfY=null;
    if (frame.self && typeof frame.self.x === "number" && typeof frame.self.y === "number"){
      selfX = frame.self.x; selfY = frame.self.y;
      if (showSelf){
        const [mx,my] = feedToMapPx(selfX, selfY, v);
        const [ix,iy] = mapPxToImagePx(mx, my, v);
        const sx = R.dx + ix * R.s;
        const sy = R.dy + iy * R.s;
        drawSelf(sx, sy, rSelf, cSelf);
      }
    }

    // Actors
    if (Array.isArray(frame.actors)){
      for (const a of frame.actors){
        if (useRange && maxRangeM > 0 && selfX !== null){
          const dx = a.x - selfX, dy = a.y - selfY;
          const distM = Math.hypot(dx,dy) / 100.0;
          if (distM > maxRangeM) continue;
        }

        if (a.dead){
          if (!showDead) continue;
        } else if (a.bot){
          if (!showBots) continue;
        } else {
          if (!showPMCs) continue;
        }

        const [mx,my] = feedToMapPx(a.x, a.y, v);
        const [ix,iy] = mapPxToImagePx(mx, my, v);
        const x = R.dx + ix * R.s;
        const y = R.dy + iy * R.s;

        if (a.dead)      drawDiamond(x,y,rDead,cDead);
        else if (a.bot)  drawCircle (x,y,rBot, cBot);
        else             drawSquare (x,y,rPMC, cPMC);
      }
    }
  }

  // ---------- Primitives ----------
  function drawSelf(x,y,r,col){
    const rr = r*dpr;
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
    const h = half*dpr;
    CTX.fillStyle = col;
    CTX.fillRect(x-h,y-h,h*2,h*2);
    CTX.strokeStyle="rgba(0,0,0,.9)";
    CTX.lineWidth=1*dpr;
    CTX.strokeRect(x-h,y-h,h*2,h*2);
  }
  function drawDiamond(x,y,r,col){
    const rr = r*dpr;
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

  // ---------- Interaction: pan & zoom ----------
  let isDragging=false, dragStart={x:0,y:0}, panStart={x:0,y:0};
  CANVAS.addEventListener("mousedown", (e)=>{
    isDragging = true; CANVAS.classList.add("dragging");
    dragStart = clientToCanvas(e);
    panStart = { x: imgPan.x, y: imgPan.y };
  });
  window.addEventListener("mouseup", ()=>{
    if (isDragging){ isDragging=false; CANVAS.classList.remove("dragging"); }
  });
  window.addEventListener("mousemove", (e)=>{
    if (!isDragging) return;
    const cur = clientToCanvas(e);
    imgPan.x = panStart.x + (cur.x - dragStart.x);
    imgPan.y = panStart.y + (cur.y - dragStart.y);
    drawFrame(lastFrame);
  });
  CANVAS.addEventListener("wheel", (e)=>{
    e.preventDefault();
    const z = clamp((parseFloat(zoomEl.value)||0.5) * (e.deltaY < 0 ? 1.1 : 0.9), 0.25, 4);
    zoomEl.value = z.toFixed(2);
    drawFrame(lastFrame);
  }, { passive:false });
  zoomEl?.addEventListener("input", ()=> drawFrame(lastFrame));
  inScaleEl?.addEventListener("input", ()=> drawFrame(lastFrame));
  inOffXEl?.addEventListener("input", ()=> drawFrame(lastFrame));
  inOffYEl?.addEventListener("input", ()=> drawFrame(lastFrame));

  // ---------- Map selection ----------
  mapSelEl?.addEventListener("change", ()=>{
    loadMap(mapSelEl.value || "Farm");
  });

  // ---------- Connection (SSE + poll) ----------
  function baseUrl(){
    const typed = serverUrlEl?.value?.trim() || "";
    if (/^https?:/i.test(typed)) return typed.replace(/\/+$/,"");
    if (location.origin && /^https?:/i.test(location.origin)) return location.origin;
    return "http://localhost:8088";
  }

  function connect(){
    const base = baseUrl();
    try { es = new EventSource(base + "/stream"); }
    catch { return startPolling(base); }
    statusEl.textContent="connecting¡­"; btnConn.textContent="Connecting¡­";
    es.addEventListener("open", ()=>{
      connected=true; statusEl.textContent="connected (SSE)"; btnConn.textContent="Disconnect";
      if (pollTimer){ clearInterval(pollTimer); pollTimer=null; }
    });
    es.addEventListener("error", ()=>{
      if (es){ es.close(); es=null; }
      startPolling(base);
    });
    es.addEventListener("frame", ev=>{
      try { lastFrame = JSON.parse(ev.data); } catch { return; }
      drawFrame(lastFrame);
    });
  }
  function startPolling(base){
    if (pollTimer) clearInterval(pollTimer);
    connected=true; statusEl.textContent="connected (poll)"; btnConn.textContent="Disconnect";
    const tick = ()=>{
      fetch(base + "/api/frame", { cache:"no-store" })
        .then(r => r.ok ? r.json() : Promise.reject())
        .then(j => { lastFrame=j; drawFrame(lastFrame); })
        .catch(()=>{});
    };
    tick(); pollTimer = setInterval(tick, 100);
  }
  function disconnect(){
    if (es){ es.close(); es=null; }
    if (pollTimer){ clearInterval(pollTimer); pollTimer=null; }
    connected=false; statusEl.textContent="disconnected"; btnConn.textContent="Connect";
  }
  btnConn.addEventListener("click", ()=> connected ? disconnect() : connect());
  btnCopy?.addEventListener("click", ()=>{
    const v = readView();
    const cfg = {
      map: cur.map,
      ingame: cur.ingame,
      inputScale: v.inScale,
      inOffsetX: v.inOffX,
      inOffsetY: v.inOffY,
      zoom: v.zoom
    };
    console.log("[WebRadar mapping]", cfg);
    navigator.clipboard.writeText(JSON.stringify(cfg, null, 2)).catch(()=>{});
    statusEl.textContent = "mapping copied";
    setTimeout(()=> statusEl.textContent = connected ? "connected" : "disconnected", 1200);
  });

  // ---------- Utils ----------
  function clientToCanvas(e){
    const r = CANVAS.getBoundingClientRect();
    return { x:(e.clientX - r.left)*dpr, y:(e.clientY - r.top)*dpr };
  }
  function clamp(v,lo,hi){ return Math.max(lo, Math.min(hi, v)); }

  // ---------- Kickoff ----------
  resizeCanvas();
  loadMap(activeKey);
  drawFrame(null);
  window.addEventListener("resize", ()=>{ resizeCanvas(); drawFrame(lastFrame); });

  if (serverUrlEl) {
    serverUrlEl.value = (location.origin && /^https?:/i.test(location.origin))
      ? location.origin
      : "http://localhost:8088";
  }
})();
