(function(){
  const CANVAS = document.getElementById("radar");
  const CTX    = CANVAS.getContext("2d");

  // ©¤©¤ Fixed image pixel where the underground cluster should live ©¤©¤
  const ANCHOR_IMG_PX = { x:2010, y:1090 };

  // ©¤©¤ Static underground cluster config ©¤©¤
  const STATIC_Z_MAX        = -2000; // cm or deeper
  const STATIC_CELL         = 50;    // cm, XY bin size for clustering
  const STATIC_MIN_CLUSTER  = 2;     // min actors in a bin
  const STATIC_SPREAD_MAX   = 120;   // cm, tight spread inside bin
  const STATIC_TTL_MS       = 4000;  // hold last anchor briefly
  const SHOW_ANCHOR_DEBUG   = true;

  // Optional Y flip if your world +Y is opposite of image +Y
  const Y_FLIP = false;

  // UI
  const mapSelEl    = document.getElementById("mapSel");
  const serverUrlEl = document.getElementById("serverUrl");
  const btnConn     = document.getElementById("btnConnect");
  const btnCopy     = document.getElementById("btnCopy");
  const statusEl    = document.getElementById("status");
  const zoomEl      = document.getElementById("zoom");

  // We keep inScale only. Offsets are ignored per your request.
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

  // Maps (ingame rect is unused now, but kept for image loading)
  const MAPS = {
    Farm:   { map:"Farm.png",   ingame:{ left:0, top:0, right:0, bottom:0 }, input:{ scale:33,  offX:0, offY:0, zoom:4   }, units:"mappx" },
    Valley: { map:"Valley.png", ingame:{ left:0, top:0, right:0, bottom:0 }, input:{ scale:100, offX:0, offY:0, zoom:0.5 }, units:"mappx" }
  };

  let dpr = window.devicePixelRatio || 1;

  // Connection state
  let es=null, pollTimer=null, connected=false;
  let lastFrame = null;

  // Anchor state
  let lastSessionId = null;
  let staticAnchor   = null; // { pawn, x, y, ts }
  let anchorWorld    = null; // { x, y } world coords of the chosen anchor actor (when seen)
  let anchorPawn     = null; // numeric pointer/id

  // Map/view
  let activeKey = (mapSelEl?.value) || "Farm";
  let cur = MAPS[activeKey];
  let mapImg = new Image();
  let imgPan = { x: 0, y: 0 };

  // ©¤©¤ helpers ©¤©¤
  function resetAnchor(){ staticAnchor = null; anchorWorld = null; anchorPawn = null; }

  function readView(){
    const z = clamp(parseFloat(zoomEl?.value || "0.5") || 0.5, 0.05, 4);
    return {
      zoom: z,
      canvasW: CANVAS.width,
      canvasH: CANVAS.height,
      cx: CANVAS.width * 0.5,
      cy: CANVAS.height * 0.5,
      imgW: mapImg.naturalWidth  || 1,
      imgH: mapImg.naturalHeight || 1,
      inScale: Math.max(1, parseFloat(inScaleEl?.value || (cur.input?.scale ?? 100)) || 100),
    };
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

  // Convert WORLD (cm) ¡ú IMAGE px using the fixed anchor
  function worldToImagePx(x, y, v){
    if (!anchorWorld) return null;
    const sx = (x - anchorWorld.x) / v.inScale;
    const syRaw = (y - anchorWorld.y) / v.inScale;
    const sy = Y_FLIP ? -syRaw : syRaw;
    return [ ANCHOR_IMG_PX.x + sx, ANCHOR_IMG_PX.y + sy ];
  }

  // Render helpers
  function drawSelf(x,y,r,col){
    const rr = r*dpr; CTX.fillStyle = col;
    CTX.beginPath();
    CTX.moveTo(x, y-rr);
    CTX.lineTo(x+rr*0.85, y+rr*0.6);
    CTX.lineTo(x-rr*0.85, y+rr*0.6);
    CTX.closePath(); CTX.fill();
  }
  function drawCircle(x,y,r,col){ CTX.fillStyle=col; CTX.beginPath(); CTX.arc(x,y,r*dpr,0,Math.PI*2); CTX.fill(); }
  function drawSquare(x,y,half,col){
    const h = half*dpr; CTX.fillStyle=col; CTX.fillRect(x-h,y-h,h*2,h*2);
    CTX.strokeStyle="rgba(0,0,0,.9)"; CTX.lineWidth=1*dpr; CTX.strokeRect(x-h,y-h,h*2,h*2);
  }
  function drawDiamond(x,y,r,col){
    const rr=r*dpr; CTX.fillStyle=col; CTX.beginPath();
    CTX.moveTo(x,y-rr); CTX.lineTo(x+rr,y); CTX.lineTo(x,y+rr); CTX.lineTo(x-rr,y);
    CTX.closePath(); CTX.fill();
    CTX.strokeStyle="rgba(0,0,0,.9)"; CTX.lineWidth=1*dpr; CTX.stroke();
  }

  // ©¤©¤ map load & canvas ©¤©¤
  function resizeCanvas(){
    dpr = window.devicePixelRatio || 1;
    const rect = CANVAS.getBoundingClientRect();
    CANVAS.width  = Math.max(1, Math.round(rect.width  * dpr));
    CANVAS.height = Math.max(1, Math.round(rect.height * dpr));
    CTX.setTransform(1,0,0,1,0,0);
  }

  function loadMap(which){
    activeKey = which;
    cur = MAPS[activeKey] || MAPS.Farm;

    // Keep only inScale from saved config
    try{
      const raw = localStorage.getItem(`wr.map.${activeKey}`);
      if (raw){
        const saved = JSON.parse(raw);
        if (inScaleEl && saved?.inScale != null) inScaleEl.value = saved.inScale;
      } else {
        if (inScaleEl) inScaleEl.value = cur.input?.scale ?? 100;
      }
    }catch{}
    if (zoomEl && cur.input?.zoom != null) zoomEl.value = cur.input.zoom;

    imgPan.x = 0; imgPan.y = 0;
    mapImg = new Image();
    mapImg.onload  = () => drawFrame(lastFrame);
    mapImg.onerror = () => { console.warn("[WebRadar] failed to load", cur.map); drawFrame(lastFrame); };
    mapImg.src = cur.map;
  }

  // ©¤©¤ cluster & anchor selection ©¤©¤
  function getPawnId(a){
    // support several possible property names
    return a?.pawn ?? a?.ptr ?? a?.id ?? null;
  }

  function findDeepCluster(frame){
    const A = frame?.actors;
    if (!Array.isArray(A) || A.length === 0) return null;

    // collect candidates z <= -2000 with valid x/y and a pointer
    const cand = [];
    for (let i=0;i<A.length;i++){
      const a=A[i]; if (!a) continue;
      if (typeof a.x!=="number" || typeof a.y!=="number" || typeof a.z!=="number") continue;
      if (a.z > STATIC_Z_MAX) continue;
      const pid = getPawnId(a); if (pid == null) continue;
      cand.push({x:a.x,y:a.y,z:a.z,pid});
    }
    if (cand.length < STATIC_MIN_CLUSTER) return null;

    // bin by XY
    const bins = new Map();
    const key = (x,y)=> `${Math.round(x/STATIC_CELL)}:${Math.round(y/STATIC_CELL)}`;
    for (const c of cand){
      const k = key(c.x,c.y);
      let b = bins.get(k);
      if (!b){ b = {count:0,sumX:0,sumY:0,minX:c.x,maxX:c.x,minY:c.y,maxY:c.y, items:[]}; bins.set(k,b); }
      b.count++; b.sumX+=c.x; b.sumY+=c.y; b.items.push(c);
      if (c.x<b.minX) b.minX=c.x; if (c.x>b.maxX) b.maxX=c.x;
      if (c.y<b.minY) b.minY=c.y; if (c.y>b.maxY) b.maxY=c.y;
    }

    // choose best bin (densest + tight spread)
    let best=null;
    for (const b of bins.values()){
      if (b.count < STATIC_MIN_CLUSTER) continue;
      const spread = Math.max(b.maxX-b.minX, b.maxY-b.minY);
      if (spread > STATIC_SPREAD_MAX) continue;
      if (!best || b.count > best.count) best = b;
    }
    if (!best) return null;

    // centroid
    const cx = best.sumX / best.count;
    const cy = best.sumY / best.count;

    // pick ONE anchor actor in that bin: nearest to centroid (stable) or lowest Z as tiebreaker
    best.items.sort((u,v)=>{
      const du = (u.x-cx)*(u.x-cx)+(u.y-cy)*(u.y-cy);
      const dv = (v.x-cx)*(v.x-cx)+(v.y-cy)*(v.y-cy);
      if (du !== dv) return du - dv;
      return u.z - v.z; // deeper wins
    });
    const chosen = best.items[0];
    return { pawn: chosen.pid, x: chosen.x, y: chosen.y, ts: performance.now() };
  }

  function refreshAnchorFromFrame(frame){
    // reset on session change
    if (frame && typeof frame.session !== "undefined") {
      if (lastSessionId === null || frame.session !== lastSessionId) {
        lastSessionId = frame.session;
        resetAnchor();
      }
    }

    const now = performance.now();

    // If we already have a pawn anchor, refresh its current world XY from the frame
    if (anchorPawn != null && Array.isArray(frame?.actors)){
      for (const a of frame.actors){
        const pid = getPawnId(a);
        if (pid == null) continue;
        if (pid === anchorPawn && typeof a.x==="number" && typeof a.y==="number"){
          anchorWorld = { x:a.x, y:a.y };
          staticAnchor = { pawn: anchorPawn, x:a.x, y:a.y, ts: now };
          return;
        }
      }
      // if not seen, keep last for TTL
      if (staticAnchor && (now - staticAnchor.ts) < STATIC_TTL_MS){
        anchorWorld = { x: staticAnchor.x, y: staticAnchor.y };
        return;
      }
      // fallthrough to try re-detect
    }

    // Try to find (or re-find) a deep cluster and pin its chosen actor
    const cand = findDeepCluster(frame);
    if (cand){
      staticAnchor = cand;
      anchorPawn   = cand.pawn;
      anchorWorld  = { x:cand.x, y:cand.y };
      return;
    }

    // No anchor found: if we had a recent one, hold it briefly
    if (staticAnchor && (now - staticAnchor.ts) < STATIC_TTL_MS){
      anchorWorld = { x: staticAnchor.x, y: staticAnchor.y };
      return;
    }

    // Otherwise no anchor yet: stay unanchored (we'll keep scanning)
    anchorWorld = null;
  }

  // ©¤©¤ drawing ©¤©¤
  function drawFrame(frame){
    try{
      CTX.setTransform(1,0,0,1,0,0);
      CTX.clearRect(0,0,CANVAS.width,CANVAS.height);
      const v = readView();

      if (!(mapImg.complete && mapImg.naturalWidth)) {
        CTX.fillStyle="#000"; CTX.fillRect(0,0,CANVAS.width,CANVAS.height);
        CTX.strokeStyle="rgba(255,255,255,0.08)"; CTX.lineWidth=1*dpr;
        const step = Math.max(24*dpr, Math.min(CANVAS.width, CANVAS.height)/16);
        for (let x=0; x<=CANVAS.width; x+=step){ CTX.beginPath(); CTX.moveTo(x,0); CTX.lineTo(x,CANVAS.height); CTX.stroke(); }
        for (let y=0; y<=CANVAS.height; y+=step){ CTX.beginPath(); CTX.moveTo(0,y); CTX.lineTo(CANVAS.width,y); CTX.stroke(); }
        return;
      }

      refreshAnchorFromFrame(frame);

      const R = imageDrawRect(v);
      CTX.drawImage(mapImg, 0,0, v.imgW,v.imgH, R.dx, R.dy, R.dw, R.dh);

      if (SHOW_ANCHOR_DEBUG){
        // draw crosshair at fixed image pixel (where the cluster should be)
        const x = R.dx + ANCHOR_IMG_PX.x * R.s;
        const y = R.dy + ANCHOR_IMG_PX.y * R.s;
        CTX.strokeStyle = anchorWorld ? "rgba(120,220,255,0.95)" : "rgba(220,120,120,0.7)";
        CTX.lineWidth = 2*dpr;
        CTX.beginPath(); CTX.moveTo(x-6*dpr, y); CTX.lineTo(x+6*dpr, y); CTX.stroke();
        CTX.beginPath(); CTX.moveTo(x, y-6*dpr); CTX.lineTo(x, y+6*dpr); CTX.stroke();
      }

      if (frame && frame.ok === true) drawActors(frame, v, R);
    } catch (err){
      console.warn("[WebRadar] draw error:", err);
      CTX.setTransform(1,0,0,1,0,0);
      CTX.fillStyle="#000"; CTX.fillRect(0,0,CANVAS.width,CANVAS.height);
    }
  }

  function drawActors(frame, v, R){
    const showSelf = !!(fSelfEl?.checked);
    const showPMCs = !!(fPMCsEl?.checked);
    const showBots = !!(fBotsEl?.checked);
    const showDead = !!(fDeadEl?.checked);

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

    if (!anchorWorld){
      // no anchor yet ¡ú keep scanning; optionally paint nothing
      return;
    }

    // SELF (drawn relative to anchor; never used for anchoring)
    if (frame.self && typeof frame.self.x === "number" && typeof frame.self.y === "number" && showSelf){
      const img = worldToImagePx(frame.self.x, frame.self.y, v); if (img){
        const x = R.dx + img[0] * R.s, y = R.dy + img[1] * R.s;
        drawSelf(x, y, rSelf, cSelf);
      }
    }

    // ACTORS
    if (Array.isArray(frame.actors)){
      // if you want to confirm the anchor actor is exactly at the fixed pixel, you can draw it differently
      for (const a of frame.actors){
        if (!a || typeof a.x!=="number" || typeof a.y!=="number") continue;

        const pid = getPawnId(a);
        const img = worldToImagePx(a.x, a.y, v); if (!img) continue;

        // optional range gating (uses self vs anchor delta; fine either way)
        if (useRange && maxRangeM > 0 && frame.self){
          const dx = (a.x - frame.self.x), dy = (a.y - frame.self.y);
          const distM = Math.hypot(dx, dy) / 100.0;
          if (distM > maxRangeM) continue;
        }

        const x = R.dx + img[0] * R.s;
        const y = R.dy + img[1] * R.s;

        if (a.dead){ if (!showDead) continue; drawDiamond(x,y,rDead,cDead); continue; }
        if (a.bot) { if (!showBots) continue; drawCircle (x,y,rBot, cBot); }
        else       { if (!showPMCs) continue; drawSquare (x,y,rPMC, cPMC); }

        // (Optional) highlight the locked anchor pawn
        if (pid != null && pid === anchorPawn){
          CTX.strokeStyle = "rgba(255,255,255,0.9)";
          CTX.lineWidth = 2*dpr;
          CTX.beginPath(); CTX.arc(x,y,(rPMC+4)*dpr,0,Math.PI*2); CTX.stroke();
        }
      }
    }
  }

  // ©¤©¤ interaction ©¤©¤
  let isDragging=false, dragStart={x:0,y:0}, panStart={x:0,y:0};
  CANVAS.addEventListener("mousedown", (e)=>{ isDragging=true; CANVAS.classList.add("dragging"); dragStart=clientToCanvas(e); panStart={...imgPan}; });
  window.addEventListener("mouseup", ()=>{ if (isDragging){ isDragging=false; CANVAS.classList.remove("dragging"); }});
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
    // only persist inScale/zoom now
    try{
      const v = readView();
      localStorage.setItem(`wr.map.${activeKey}`, JSON.stringify({ inScale: v.inScale, zoom: v.zoom }));
    }catch{}
  }, { passive:false });

  // persist inScale/zoom changes (offsets are ignored)
  const saveScaleZoom = ()=>{
    try{
      const v = readView();
      localStorage.setItem(`wr.map.${activeKey}`, JSON.stringify({ inScale: v.inScale, zoom: v.zoom }));
    }catch{}
  };
  zoomEl?.addEventListener("input", ()=>{ drawFrame(lastFrame); saveScaleZoom(); });
  inScaleEl?.addEventListener("input", ()=>{ drawFrame(lastFrame); saveScaleZoom(); });

  // hotkey: clear anchor & keep scanning
  window.addEventListener("keydown", (e)=>{ if (e.key === "r" || e.key === "R") resetAnchor(); });

  // map selection
  mapSelEl?.addEventListener("change", ()=>{ loadMap(mapSelEl.value || "Farm"); });

  // ©¤©¤ connect/poll ©¤©¤
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
    es.addEventListener("open", ()=>{ connected=true; statusEl.textContent="connected (SSE)"; btnConn.textContent="Disconnect"; if (pollTimer){ clearInterval(pollTimer); pollTimer=null; }});
    es.addEventListener("error", ()=>{ if (es){ es.close(); es=null; } startPolling(base); });
    es.addEventListener("frame", ev=>{
      try { lastFrame = JSON.parse(ev.data); } catch { return; }
      drawFrame(lastFrame);
    });
  }

  function startPolling(base){
    if (pollTimer) clearInterval(pollTimer);
    connected=true; statusEl.textContent="connected (poll)"; btnConn.textContent="Disconnect";
    const tick = ()=> {
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
    const cfg = { map: cur.map, anchorPixel: ANCHOR_IMG_PX, scale_cm_per_imgpx: v.inScale, zoom: v.zoom };
    console.log("[WebRadar config]", cfg);
    navigator.clipboard.writeText(JSON.stringify(cfg, null, 2)).catch(()=>{});
    statusEl.textContent = "mapping copied";
    setTimeout(()=> statusEl.textContent = connected ? "connected" : "disconnected", 1200);
  });

  // utils
  function clientToCanvas(e){ const r = CANVAS.getBoundingClientRect(); return { x:(e.clientX - r.left)*dpr, y:(e.clientY - r.top)*dpr }; }
  function clamp(v,lo,hi){ return Math.max(lo, Math.min(hi, v)); }

  // kickoff
  resizeCanvas();
  loadMap(activeKey);
  drawFrame(null);
  window.addEventListener("resize", ()=>{ resizeCanvas(); drawFrame(lastFrame); });

  if (serverUrlEl) {
    serverUrlEl.value = (location.origin && /^https?:/i.test(location.origin)) ? location.origin : "http://localhost:8088";
  }
})();
