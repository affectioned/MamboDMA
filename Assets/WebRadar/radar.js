(function(){
  const CANVAS = document.getElementById("radar");
  const CTX    = CANVAS.getContext("2d");

  // --------- UI refs ---------
  const toolbarEl   = document.getElementById("toolbar");
  const collapseBtn = document.getElementById("collapseBtn");
  const tabs        = Array.from(document.querySelectorAll(".tabs .tab"));
  const tabPages    = {
    general: document.getElementById("tab-general"),
    players: document.getElementById("tab-players"),
  };

  const mapSelEl    = document.getElementById("mapSel");
  const btnRescan   = document.getElementById("btnRescan");
  const serverUrlEl = document.getElementById("serverUrl");
  const btnConn     = document.getElementById("btnConnect");
  const btnSaveMap  = document.getElementById("btnSaveMap");
  const statusEl    = document.getElementById("status");

  // General controls
  const zoomEl      = document.getElementById("zoom");
  const inScaleEl   = document.getElementById("inScale");
  const yFlipEl     = document.getElementById("yFlip");
  const offXEl      = document.getElementById("offX");
  const offYEl      = document.getElementById("offY");

  // Player settings
  const fSelfEl    = document.getElementById("fSelf");
  const fPMCsEl    = document.getElementById("fPMCs");
  const fBotsEl    = document.getElementById("fBots");
  const fDeadEl    = document.getElementById("fDead");
  const maxRangeEl = document.getElementById("maxRange");
  const szSelfEl   = document.getElementById("szSelf");
  const szPMCEl    = document.getElementById("szPMC");
  const szBotEl    = document.getElementById("szBot");
  const szDeadEl   = document.getElementById("szDead");
  const colSelfEl  = document.getElementById("colSelf");
  const colPMCEl   = document.getElementById("colPMC");
  const colBotEl   = document.getElementById("colBot");
  const colDeadEl  = document.getElementById("colDead");

  let dpr = window.devicePixelRatio || 1;

  // Connection state
  let es=null, pollTimer=null;
  let lastFrame = null;

  // Map state
  let mapList = [];
  let activeMap = null;
  let mapImg = new Image();
  let imgPan = { x:0, y:0 };

  // --------- Per-map config ---------
  const cfgKey = (name)=> `wr.mapcfg.${name}`;
  const defaultCfg = ()=> ({
    inScale: 33.120,
    zoom: 0.5,
    yFlip: false,
    file: null,
    worldOffsetCm: { x: 0, y: 0 }
  });

  function mergeCfg(base, override){
    if (!override) return base;
    const out = { ...base };
    if (typeof override.inScale === "number" && isFinite(override.inScale) && override.inScale > 0) out.inScale = override.inScale;
    if (typeof override.zoom === "number"   && isFinite(override.zoom))                               out.zoom    = override.zoom;
    if (typeof override.yFlip === "boolean")                                                         out.yFlip   = override.yFlip;
    if (override.worldOffsetCm && typeof override.worldOffsetCm.x==="number" && typeof override.worldOffsetCm.y==="number"){
      out.worldOffsetCm = { x: override.worldOffsetCm.x, y: override.worldOffsetCm.y };
    }
    if (typeof override.file === "string") out.file = override.file;
    return out;
  }

  function loadCfg(name){
    try{
      const raw = localStorage.getItem(cfgKey(name));
      const cfg = raw ? JSON.parse(raw) : defaultCfg();
      if (!(cfg.inScale > 0)) cfg.inScale = 33.120;
      if (typeof cfg.zoom !== "number") cfg.zoom = 0.5;
      if (typeof cfg.yFlip !== "boolean") cfg.yFlip = false;
      if (!cfg.worldOffsetCm || typeof cfg.worldOffsetCm.x!=="number" || typeof cfg.worldOffsetCm.y!=="number")
        cfg.worldOffsetCm = { x: 0, y: 0 };
      return cfg;
    }catch{ return defaultCfg(); }
  }
  function saveCfg(name, cfg){
    try{ localStorage.setItem(cfgKey(name), JSON.stringify(cfg)); }catch{}
  }

  function urlDirname(url){
    const q = url.split("?")[0].split("#")[0];
    const idx = q.lastIndexOf("/");
    return idx >= 0 ? q.slice(0, idx) : "";
  }
  function joinUrl(dir, file){
    if (!dir) return `/${file.replace(/^\/+/,"")}`;
    return `${dir.replace(/\/+$/,"")}/${file.replace(/^\/+/,"")}`;
  }

  async function fetchJsonNoThrow(url, init){
    try{
      const r = await fetch(url, Object.assign({ cache:"no-store" }, init || {}));
      if (!r.ok) return null;
      return await r.json();
    }catch{ return null; }
  }

  async function loadSidecarForMap(mapMeta){
    if (!mapMeta) return null;
    const url = mapMeta.url || ("/" + (mapMeta.file || ""));
    const dir = urlDirname(url);
    const name = mapMeta.name;

    const mapCfgUrl = joinUrl(dir, `${name}.json`);
    const exact = await fetchJsonNoThrow(mapCfgUrl);
    if (exact) return { cfg: exact, source: mapCfgUrl };

    const defaultUrl = joinUrl(dir, `default.json`);
    const def = await fetchJsonNoThrow(defaultUrl);
    if (def) return { cfg: def, source: defaultUrl };

    return null;
  }

  // --------- Canvas & view ---------
  function resizeCanvas(){
    dpr = window.devicePixelRatio || 1;
    const rect = CANVAS.getBoundingClientRect();
    CANVAS.width  = Math.max(1, Math.round(rect.width  * dpr));
    CANVAS.height = Math.max(1, Math.round(rect.height * dpr));
    CTX.setTransform(1,0,0,1,0,0);
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

  function readView(){
    const cfg = activeMap ? loadCfg(activeMap.name) : defaultCfg();
    const z = clamp(parseFloat(zoomEl?.value ?? cfg.zoom) || cfg.zoom, 0.05, 10);
    const scl = Math.max(0.001, parseFloat(inScaleEl?.value ?? cfg.inScale) || cfg.inScale);
    return {
      zoom: z,
      canvasW: CANVAS.width,
      canvasH: CANVAS.height,
      cx: CANVAS.width * 0.5,
      cy: CANVAS.height * 0.5,
      imgW: mapImg.naturalWidth  || 1,
      imgH: mapImg.naturalHeight || 1,
      inScale: scl,
      yFlip: cfg.yFlip,
      worldOffsetCm: cfg.worldOffsetCm
    };
  }

  // --------- Mapping math ---------
  function worldToImagePx(x, y, v){
    const ox = v.worldOffsetCm?.x || 0;
    const oy = v.worldOffsetCm?.y || 0;
    const sx = (x + ox) / v.inScale;
    const syRaw = (y + oy) / v.inScale;
    const sy = v.yFlip ? -syRaw : syRaw;
    return [ sx, sy ];
  }

  // --------- Drawing helpers ---------
  function drawSelfMarker(x,y,r,deg,col){
    const a = (deg * Math.PI / 180);
    const rr = r*dpr;
    const p0 = { x: x + Math.sin(a) * rr,            y: y - Math.cos(a) * rr            };
    const p1 = { x: x - Math.cos(a) * rr * 0.7,      y: y - Math.sin(a) * rr * 0.7      };
    const p2 = { x: x + Math.cos(a) * rr * 0.7,      y: y + Math.sin(a) * rr * 0.7      };
    CTX.fillStyle = col;
    CTX.beginPath(); CTX.moveTo(p0.x,p0.y); CTX.lineTo(p1.x,p1.y); CTX.lineTo(p2.x,p2.y); CTX.closePath(); CTX.fill();
    CTX.lineWidth = 1*dpr; CTX.strokeStyle="rgba(0,0,0,.9)"; CTX.stroke();
  }

  function drawEntityCircleWithHeading(x,y,r,deg,color,arrow){
    CTX.fillStyle = color;
    CTX.beginPath(); CTX.arc(x,y,r*dpr,0,Math.PI*2); CTX.fill();
    const a = (deg||0) * Math.PI/180;
    const L = r * 1.6 * dpr;
    CTX.strokeStyle = "rgba(0,0,0,.9)";
    CTX.lineWidth = 1.5*dpr;
    CTX.beginPath();
    CTX.moveTo(x, y);
    CTX.lineTo(x + Math.sin(a) * L, y - Math.cos(a) * L);
    CTX.stroke();
    if (arrow){
      CTX.fillStyle = color;
      const ay = y - (r*1.9*dpr);
      const ax = x;
      const s = 3*dpr;
      CTX.beginPath();
      if (arrow === 'up'){
        CTX.moveTo(ax, ay - s);
        CTX.lineTo(ax - s, ay + s);
        CTX.lineTo(ax + s, ay + s);
      } else {
        CTX.moveTo(ax, ay + s);
        CTX.lineTo(ax - s, ay - s);
        CTX.lineTo(ax + s, ay - s);
      }
      CTX.closePath(); CTX.fill();
      CTX.strokeStyle="rgba(0,0,0,.9)"; CTX.lineWidth=1*dpr; CTX.stroke();
    }
  }

  function drawGridBackdrop(){
    CTX.fillStyle="#000"; CTX.fillRect(0,0,CANVAS.width,CANVAS.height);
    CTX.strokeStyle="rgba(255,255,255,0.08)"; CTX.lineWidth=1*dpr;
    const step = Math.max(24*dpr, Math.min(CANVAS.width, CANVAS.height)/16);
    for (let x=0; x<=CANVAS.width; x+=step){ CTX.beginPath(); CTX.moveTo(x,0); CTX.lineTo(x,CANVAS.height); CTX.stroke(); }
    for (let y=0; y<=CANVAS.height; y+=step){ CTX.beginPath(); CTX.moveTo(0,y); CTX.lineTo(CANVAS.width,y); CTX.stroke(); }
  }

  function drawFrame(frame){
    try{
      CTX.setTransform(1,0,0,1,0,0);
      CTX.clearRect(0,0,CANVAS.width,CANVAS.height);
      const v = readView();

      if (!(mapImg.complete && mapImg.naturalWidth)) {
        drawGridBackdrop();
        return;
      }

      const R = imageDrawRect(v);
      CTX.drawImage(mapImg, 0,0, v.imgW,v.imgH, R.dx, R.dy, R.dw, R.dh);

      if (frame && frame.ok === true){
        const showSelf = !!(fSelfEl?.checked);
        const showPMCs = !!(fPMCsEl?.checked);
        const showBots = !!(fBotsEl?.checked);
        const showDead = !!(fDeadEl?.checked);

        const useRange  = true;
        const maxRangeM = Math.max(0, parseFloat(maxRangeEl?.value || "0"));

        const rSelf = clamp(parseInt(szSelfEl?.value || "8",10), 2, 30);
        const rPMC  = clamp(parseInt(szPMCEl?.value  || "6",10), 2, 30);
        const rBot  = clamp(parseInt(szBotEl?.value  || "5",10), 2, 30);
        const rDead = clamp(parseInt(szDeadEl?.value || "6",10), 2, 30);

        const cSelf = colSelfEl?.value || "#19ff6a";
        const cPMC  = colPMCEl?.value  || "#ff4a4a";
        const cBot  = colBotEl?.value  || "#0db1ff";
        const cDead = colDeadEl?.value || "#ffd600";

        let selfZ = 0;
        if (frame.self && typeof frame.self.x==="number" && typeof frame.self.y==="number"){
          selfZ = (typeof frame.self.z === "number") ? frame.self.z : 0;
          if (showSelf){
            const img = worldToImagePx(frame.self.x, frame.self.y, v);
            const yaw = (typeof frame.self.yaw === "number") ? frame.self.yaw : 0;
            const sx = R.dx + img[0]*R.s, sy = R.dy + img[1]*R.s;
            drawSelfMarker(sx, sy, rSelf, yaw, cSelf);
          }
        }

        if (Array.isArray(frame.actors)){
          const selfId = frame.self?.id ?? frame.self?.pawn ?? null;
          for (const a of frame.actors){
            if (!a || typeof a.x!=="number" || typeof a.y!=="number") continue;
            if (a.self === true) continue;
            if (selfId != null && (a.id === selfId || a.pawn === selfId)) continue;

            if (useRange && maxRangeM > 0 && frame.self){
              const dx = (a.x - frame.self.x), dy = (a.y - frame.self.y);
              const distM = Math.hypot(dx,dy)/100; if (distM > maxRangeM) continue;
            }

            const img = worldToImagePx(a.x, a.y, v);
            const x = R.dx + img[0]*R.s, y = R.dy + img[1]*R.s;

            const yaw = (typeof a.yaw === "number") ? a.yaw : 0;
            const az  = (typeof a.z   === "number") ? a.z   : 0;
            const altArrow = (frame.self && Math.abs(az - selfZ) > 50) ? (az > selfZ ? 'up' : 'down') : null;

            if (a.dead){ if (!showDead) continue; drawEntityCircleWithHeading(x,y,rDead, yaw, cDead, altArrow); continue; }
            if (a.bot) { if (!showBots) continue; drawEntityCircleWithHeading(x,y,rBot,  yaw, cBot,  altArrow); }
            else       { if (!showPMCs) continue; drawEntityCircleWithHeading(x,y,rPMC, yaw, cPMC, altArrow); }
          }
        }
      }
    } catch (err){
      console.warn("[WebRadar] draw error:", err);
      CTX.setTransform(1,0,0,1,0,0);
      CTX.fillStyle="#000"; CTX.fillRect(0,0,CANVAS.width,CANVAS.height);
    }
  }

  // --------- Map loading & UI wiring ---------
  function applyCfgToUI(name){
    const cfg = loadCfg(name);
    if (zoomEl)    zoomEl.value    = (cfg.zoom ?? 0.5);
    if (inScaleEl) inScaleEl.value = (cfg.inScale ?? 33.120).toFixed(3);
    if (yFlipEl)   yFlipEl.checked = !!cfg.yFlip;
    if (offXEl)    offXEl.value    = Math.round(cfg.worldOffsetCm?.x ?? 0);
    if (offYEl)    offYEl.value    = Math.round(cfg.worldOffsetCm?.y ?? 0);
  }

  function loadMapImage(fileOrUrl){
    imgPan.x = 0; imgPan.y = 0;
    mapImg = new Image();
    mapImg.onload  = ()=> drawFrame(lastFrame);
    mapImg.onerror = ()=> { console.warn("[WebRadar] failed to load", fileOrUrl); drawFrame(lastFrame); };
    mapImg.src = fileOrUrl;
  }

  async function setActiveMapByName(name){
    const m = mapList.find(x => x.name === name) || mapList[0];
    if (!m) return;
    activeMap = m;

    let cfg = loadCfg(m.name);
    if (!cfg.file) { cfg.file = m.file || ""; }

    const side = await loadSidecarForMap(m);
    if (side && side.cfg){
      cfg = mergeCfg(cfg, side.cfg);
      saveCfg(m.name, cfg);
      statusFlash(`loaded ${side.source}`);
    }

    applyCfgToUI(m.name);
    loadMapImage(m.url || ("/" + (m.file || "")));
  }

  async function refreshMapList(){
    try{
      const base = baseUrl();
      const r = await fetch(base + "/api/maps", { cache:"no-store" });
      if (!r.ok) throw 0;
      const arr = await r.json();
      if (!Array.isArray(arr) || arr.length === 0) throw 0;
      mapList = arr.map(m => ({ name: m.name, file: m.file, url: m.url || ("/" + m.file) }));
    }catch{
      mapList = [
        { name:"Farm",   file:"Farm.png",   url:"/Farm/Farm.png"   },
        { name:"Valley", file:"Valley.png", url:"/Valley/Valley.png" }
      ];
    }

    if (mapSelEl){
      mapSelEl.innerHTML = "";
      for (const m of mapList){
        const opt = document.createElement("option");
        opt.value = m.name; opt.textContent = m.name;
        mapSelEl.appendChild(opt);
      }
      mapSelEl.value = mapList[0]?.name || "";
    }
    if (mapList.length) await setActiveMapByName(mapSelEl.value);
  }

  function persistUIToCfg(){
    if (!activeMap) return;
    const cfg = loadCfg(activeMap.name);
    cfg.zoom    = clamp(parseFloat(zoomEl.value)||cfg.zoom, 0.05, 10);
    cfg.inScale = Math.max(0.001, parseFloat(inScaleEl.value) || cfg.inScale);
    cfg.yFlip   = !!yFlipEl.checked;
    cfg.file    = activeMap.file;

    const ox = offXEl ? parseFloat(offXEl.value) : (cfg.worldOffsetCm?.x ?? 0);
    const oy = offYEl ? parseFloat(offYEl.value) : (cfg.worldOffsetCm?.y ?? 0);
    cfg.worldOffsetCm = { x: isFinite(ox) ? ox : 0, y: isFinite(oy) ? oy : 0 };

    saveCfg(activeMap.name, cfg);
  }

  // --------- Save Map Setup ---------
  async function saveMapSetup(){
    if (!activeMap){ statusFlash("no active map"); return; }
    const cfg = loadCfg(activeMap.name);
    const url = activeMap.url || ("/" + (activeMap.file || ""));
    const dir = urlDirname(url);
    const name = activeMap.name;
    const sidecarPath = joinUrl(dir, `${name}.json`);

    const payload = JSON.stringify({
      file: cfg.file || activeMap.file,
      inScale: cfg.inScale,
      yFlip: cfg.yFlip,
      worldOffsetCm: cfg.worldOffsetCm,
      zoom: cfg.zoom
    }, null, 2);

    let ok = false;
    try{
      const r = await fetch(sidecarPath, {
        method: "PUT",
        headers: { "content-type": "application/json" },
        body: payload
      });
      ok = r.ok;
    }catch{}

    if (!ok){
      try{
        const r = await fetch(`/api/mapcfg/${encodeURIComponent(name)}`, {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ dir, name, config: JSON.parse(payload) })
        });
        ok = r.ok;
      }catch{}
    }

    if (!ok){
      const a = document.createElement("a");
      a.href = URL.createObjectURL(new Blob([payload], {type:"application/json"}));
      a.download = `${name}.json`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      statusFlash(`downloaded ${name}.json (place next to the map image)`);
      return;
    }

    statusFlash(`saved ${sidecarPath}`);
  }

  // --------- Connect/poll ---------
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
      statusEl.textContent="connected (SSE)"; btnConn.textContent="Disconnect";
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
    statusEl.textContent="connected (poll)"; btnConn.textContent="Disconnect";
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
    statusEl.textContent="disconnected"; btnConn.textContent="Connect";
  }

  // --------- Interaction ---------
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
    const z = clamp((parseFloat(zoomEl.value)||0.5) * (e.deltaY < 0 ? 1.1 : 0.9), 0.25, 10);
    zoomEl.value = z.toFixed(2);
    persistUIToCfg();
    drawFrame(lastFrame);
  }, { passive:false });

  // --------- Toolbar UX ---------
  collapseBtn?.addEventListener("click", ()=>{
    const collapsed = toolbarEl.classList.toggle("collapsed");
    collapseBtn.textContent = collapsed ? "?" : "?";
    // Force a canvas resize because the grid row height changed
    resizeCanvas();
    drawFrame(lastFrame);
  });

  tabs.forEach(btn=>{
    btn.addEventListener("click", ()=>{
      tabs.forEach(b=> b.classList.remove("active"));
      btn.classList.add("active");
      const key = btn.dataset.tab;
      Object.entries(tabPages).forEach(([k,el])=>{
        if (k === key) el.classList.add("active"); else el.classList.remove("active");
      });
      // Canvas size unaffected, but redraw for UX
      drawFrame(lastFrame);
    });
  });

  // buttons & inputs
  btnConn?.addEventListener("click", ()=> (es||pollTimer) ? disconnect() : connect());
  btnRescan?.addEventListener("click", ()=> refreshMapList());
  btnSaveMap?.addEventListener("click", ()=> saveMapSetup());

  mapSelEl?.addEventListener("change", ()=>{ setActiveMapByName(mapSelEl.value).then(()=> drawFrame(lastFrame)); });
  for (const el of [zoomEl, inScaleEl, yFlipEl, offXEl, offYEl,
                    fSelfEl, fPMCsEl, fBotsEl, fDeadEl, maxRangeEl,
                    szSelfEl, szPMCEl, szBotEl, szDeadEl,
                    colSelfEl, colPMCEl, colBotEl, colDeadEl]) {
    el?.addEventListener("input",  ()=>{ persistUIToCfg(); drawFrame(lastFrame); });
    el?.addEventListener("change", ()=>{ persistUIToCfg(); drawFrame(lastFrame); });
  }

  // utils
  function clientToCanvas(e){ const r = CANVAS.getBoundingClientRect(); return { x:(e.clientX - r.left)*dpr, y:(e.clientY - r.top)*dpr }; }
  function clamp(v,lo,hi){ return Math.max(lo, Math.min(hi, v)); }
  function statusFlash(txt){
    if (!statusEl) return;
    statusEl.textContent = txt;
    setTimeout(()=> statusEl.textContent = (es||pollTimer) ? "connected" : "disconnected", 1400);
  }

  // kickoff
  resizeCanvas();
  window.addEventListener("resize", ()=>{ resizeCanvas(); drawFrame(lastFrame); });

  if (serverUrlEl) {
    serverUrlEl.value = (location.origin && /^https?:/i.test(location.origin)) ? location.origin : "http://localhost:8088";
  }

  refreshMapList().then(()=> drawFrame(null));
})();
