// WebRadar ¨C robust init: canvas always sized, map loads on selection, draws even before data arrives.

const CANVAS = document.getElementById("radar");
const CTX    = CANVAS.getContext("2d");

// Topbar
const mapSelEl    = document.getElementById("mapSel");
const serverUrlEl = document.getElementById("serverUrl");
const btnConn     = document.getElementById("btnConnect");
const btnCopy     = document.getElementById("btnCopy");
const statusEl    = document.getElementById("status");

// Mapping controls
const zoomEl   = document.getElementById("zoom");
const cmppEl   = document.getElementById("cmpp");
const cxEl     = document.getElementById("cx");
const cyEl     = document.getElementById("cy");
const swapXYEl = document.getElementById("swapxy");
const flipXEl  = document.getElementById("flipx");
const flipYEl  = document.getElementById("flipy");

// Filters & icons
const fSelfEl  = document.getElementById("fSelf");
const fPMCsEl  = document.getElementById("fPMCs");
const fBotsEl  = document.getElementById("fBots");
const fDeadEl  = document.getElementById("fDead");
const maxRangeEl = document.getElementById("maxRange");

const szSelfEl = document.getElementById("szSelf");
const szPMCEl  = document.getElementById("szPMC");
const szBotEl  = document.getElementById("szBot");
const szDeadEl = document.getElementById("szDead");

const colSelfEl = document.getElementById("colSelf");
const colPMCEl  = document.getElementById("colPMC");
const colBotEl  = document.getElementById("colBot");
const colDeadEl = document.getElementById("colDead");

// Presets (baked)
const PRESETS = {
  Farm:   { map:"Farm.png",   cmPerPx:100, centerX:-65742.33, centerY: 19203.812, swapXY:false, flipX:false, flipY:false, zoom:0.5  },
  Valley: { map:"Valley.png", cmPerPx:100, centerX: -24702.749, centerY:-30996.181, swapXY:false, flipX:false, flipY:false, zoom:0.55 }
};
const LOCK_MAPPING_AXES = true;

// State
let activeKey = (mapSelEl?.value) || "Valley";
let baked = PRESETS[activeKey];

let dpr = window.devicePixelRatio || 1;
let es=null, pollTimer=null, connected=false, lastFrame=null;

// Image & pan state
let mapImg = new Image();
let imgPan = { x:0, y:0 };

function applyPreset(p){
  cmppEl.value = p.cmPerPx;
  cxEl.value   = p.centerX;
  cyEl.value   = p.centerY;
  swapXYEl.checked = !!p.swapXY;
  flipXEl.checked  = !!p.flipX;
  flipYEl.checked  = !!p.flipY;
  zoomEl.value     = p.zoom;

  if (LOCK_MAPPING_AXES){
    cmppEl.disabled   = true;
    swapXYEl.disabled = true;
    flipXEl.disabled  = true;
    flipYEl.disabled  = true;
  }

  // sensible default server in the input
  if (serverUrlEl) {
    serverUrlEl.value = (location.origin && location.origin.startsWith("http"))
      ? location.origin
      : "http://localhost:8088";
  }

  // reset pan when switching maps
  imgPan.x = 0; imgPan.y = 0;
}

function loadMap(src){
  mapImg = new Image();
  mapImg.onload  = () => drawFrame(lastFrame);
  mapImg.onerror = () => { console.warn("Map image failed:", src); drawFrame(lastFrame); };
  mapImg.src = src; // must exist next to index.html (Assets/WebRadar/)
}

function baseUrl(){
  const typed = serverUrlEl?.value?.trim() || "";
  if (typed.startsWith("http")) return typed.replace(/\/+$/,"");
  if (location.origin && location.origin.startsWith("http")) return location.origin;
  return "http://localhost:8088";
}

// Resize canvas to fill grid row
function resizeCanvas(){
  dpr = window.devicePixelRatio || 1;
  const rect = CANVAS.getBoundingClientRect();
  CANVAS.width  = Math.max(1, Math.round(rect.width  * dpr));
  CANVAS.height = Math.max(1, Math.round(rect.height * dpr));
  CTX.setTransform(1,0,0,1,0,0);
}

// Mapping read
function readMapping(){
  return {
    zoom: clamp(parseFloat(zoomEl.value) || baked.zoom, 0.25, 8),
    cmPerPx: clamp(parseFloat(cmppEl.value) || baked.cmPerPx, 1, 100000),
    centerX: parseFloat(cxEl.value) || baked.centerX,
    centerY: parseFloat(cyEl.value) || baked.centerY,
    swapXY: LOCK_MAPPING_AXES ? baked.swapXY : !!swapXYEl.checked,
    flipX:  LOCK_MAPPING_AXES ? baked.flipX  : !!flipXEl.checked,
    flipY:  LOCK_MAPPING_AXES ? baked.flipY  : !!flipYEl.checked,
    canvasW: CANVAS.width,
    canvasH: CANVAS.height,
    imgW: mapImg.naturalWidth  || 1024,
    imgH: mapImg.naturalHeight || 1024,
    cx: CANVAS.width * 0.5,
    cy: CANVAS.height * 0.5
  };
}

// World->screen (center only affects blips), then add image pan
function worldToScreen(wx, wy, m = readMapping()){
  let dx = wx - m.centerX;
  let dy = wy - m.centerY;

  let imDX = dx / m.cmPerPx;
  let imDY = dy / m.cmPerPx;

  if (m.swapXY){ const t = imDX; imDX = imDY; imDY = t; }
  if (m.flipX) imDX = -imDX;
  if (m.flipY) imDY = -imDY;

  const x = m.cx + imDX * m.zoom + imgPan.x;
  const y = m.cy + imDY * m.zoom + imgPan.y;
  return [x,y];
}

// Draw one frame (map first, then blips)
function drawFrame(frame){
  CTX.setTransform(1,0,0,1,0,0);
  CTX.clearRect(0,0,CANVAS.width,CANVAS.height);

  const m = readMapping();

  // Map cover render
  if (mapImg.complete && mapImg.naturalWidth){
    const cover = Math.max(m.canvasW/m.imgW, m.canvasH/m.imgH);
    const s = cover * m.zoom;
    const dw = m.imgW * s, dh = m.imgH * s;
    const dx = m.cx - dw/2 + imgPan.x;
    const dy = m.cy - dh/2 + imgPan.y;
    CTX.drawImage(mapImg, 0,0,m.imgW,m.imgH, dx,dy,dw,dh);
  } else {
    // fallback grid
    CTX.fillStyle="#000"; CTX.fillRect(0,0,CANVAS.width,CANVAS.height);
    CTX.strokeStyle="rgba(255,255,255,0.08)"; CTX.lineWidth=1*dpr;
    const step = Math.max(24*dpr, Math.min(CANVAS.width, CANVAS.height)/16);
    for (let x=0; x<=CANVAS.width; x+=step){ CTX.beginPath(); CTX.moveTo(x,0); CTX.lineTo(x,CANVAS.height); CTX.stroke(); }
    for (let y=0; y<=CANVAS.height; y+=step){ CTX.beginPath(); CTX.moveTo(0,y); CTX.lineTo(CANVAS.width,y); CTX.stroke(); }
  }

  if (!frame || frame.ok !== true) return;

  // Filters & icons
  const showSelf = !!(fSelfEl?.checked);
  const showPMCs = !!(fPMCsEl?.checked);
  const showBots = !!(fBotsEl?.checked);
  const showDead = !!(fDeadEl?.checked);
  const maxRangeM = Math.max(0, parseFloat(maxRangeEl?.value || "0"));

  const rSelf = clamp(parseInt(szSelfEl?.value || "7",10), 2, 30);
  const rPMC  = clamp(parseInt(szPMCEl?.value  || "5",10), 2, 30);
  const rBot  = clamp(parseInt(szBotEl?.value  || "4",10), 2, 30);
  const rDead = clamp(parseInt(szDeadEl?.value || "6",10), 2, 30);

  const cSelf = colSelfEl?.value || "#19ff6a";
  const cPMC  = colPMCEl?.value  || "#ff4a4a";
  const cBot  = colBotEl?.value  || "#0db1ff";
  const cDead = colDeadEl?.value || "#ffd600";

  // self first (for range)
  let selfX=null, selfY=null;
  if (frame.self){
    selfX = frame.self.x; selfY = frame.self.y;
    if (showSelf){
      const [sx, sy] = worldToScreen(selfX, selfY, m);
      drawSelf(sx, sy, rSelf, cSelf);
    }
  }

  // actors
  if (Array.isArray(frame.actors)){
    for (const a of frame.actors){
      if (maxRangeM > 0 && selfX !== null){
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

      const [x,y] = worldToScreen(a.x, a.y, m);
      if (a.dead)      drawDiamond(x,y,rDead,cDead);
      else if (a.bot)  drawCircle(x,y,rBot,cBot);
      else             drawSquare(x,y,rPMC,cPMC);
    }
  }
}

// Primitives
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

// Drag to pan (image+blips)
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

// Wheel zoom (about canvas center)
CANVAS.addEventListener("wheel", (e)=>{
  e.preventDefault();
  const m = readMapping();
  const z = clamp(m.zoom * (e.deltaY < 0 ? 1.1 : 0.9), 0.25, 8);
  if (z === m.zoom) return;
  zoomEl.value = z.toFixed(2);
  drawFrame(lastFrame);
}, { passive:false });

// Map selection hook
mapSelEl?.addEventListener("change", ()=>{
  activeKey = mapSelEl.value || "Valley";
  baked = PRESETS[activeKey];
  applyPreset(baked);
  loadMap(baked.map);
  drawFrame(lastFrame);
});

// Connect buttons
btnConn.addEventListener("click", ()=> connected ? disconnect() : connect());
btnCopy.addEventListener("click", copyMapping);

// Redraw on control changes
[
  zoomEl, cmppEl, cxEl, cyEl, swapXYEl, flipXEl, flipYEl,
  fSelfEl, fPMCsEl, fBotsEl, fDeadEl, maxRangeEl,
  szSelfEl, szPMCEl, szBotEl, szDeadEl,
  colSelfEl, colPMCEl, colBotEl, colDeadEl
].forEach(el => el?.addEventListener("input", ()=> drawFrame(lastFrame)));

// SSE + polling
function connect(){
  const base = baseUrl();
  try { es = new EventSource(base + "/stream"); } catch { return startPolling(base); }
  btnConn.textContent="Connecting¡­"; statusEl.textContent="connecting¡­";
  es.addEventListener("open", ()=>{
    connected=true; statusEl.textContent="connected (SSE)"; btnConn.textContent="Disconnect";
    if (pollTimer){ clearInterval(pollTimer); pollTimer=null; }
  });
  es.addEventListener("error", ()=>{
    if (es){ es.close(); es=null; }
    startPolling(base);
  });
  es.addEventListener("frame", ev=>{
    try { lastFrame = JSON.parse(ev.data); drawFrame(lastFrame); } catch{}
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

// Utilities
function clientToCanvas(e){
  const r = CANVAS.getBoundingClientRect();
  return { x:(e.clientX - r.left)*dpr, y:(e.clientY - r.top)*dpr };
}
function clamp(v,lo,hi){ return Math.max(lo, Math.min(hi, v)); }

function copyMapping(){
  const m = readMapping();
  const cfg = {
    map: baked.map,
    cmPerPx: m.cmPerPx,
    centerX: m.centerX,
    centerY: m.centerY,
    swapXY: m.swapXY,
    flipX: m.flipX,
    flipY: m.flipY,
    zoom: m.zoom
  };
  const txt = JSON.stringify(cfg, null, 2);
  console.log("[WebRadar mapping]", cfg);
  navigator.clipboard.writeText(txt).catch(()=>{});
  statusEl.textContent = "mapping copied";
  setTimeout(()=> statusEl.textContent = connected ? "connected" : "disconnected", 1200);
}

// Kick off
applyPreset(baked);
loadMap(baked.map);
resizeCanvas();
drawFrame(null); // draw map (or grid) even before data arrives
window.addEventListener("resize", ()=>{ resizeCanvas(); drawFrame(lastFrame); });
