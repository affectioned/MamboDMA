// WebRadar ¨C map background does NOT move when editing centerX/centerY.
// Dragging pans map+blips together (visual pan), wheel zoom scales both.
// Baked mapping keeps orientation; center inputs only affect blip mapping.

const CANVAS = document.getElementById("radar");
const CTX    = CANVAS.getContext("2d");
const serverUrlEl = document.getElementById("serverUrl");

// UI
const zoomEl   = document.getElementById("zoom");
const cmppEl   = document.getElementById("cmpp");
const cxEl     = document.getElementById("cx");
const cyEl     = document.getElementById("cy");
const swapXYEl = document.getElementById("swapxy");
const flipXEl  = document.getElementById("flipx");
const flipYEl  = document.getElementById("flipy");
const btnConn  = document.getElementById("btnConnect");
const statusEl = document.getElementById("status");
const btnCopy  = document.getElementById("btnCopy");

// ©¤©¤ Per-map baked config (add more maps here)
const BAKED_MAPS = {
  Farm: {
    map: "Farm.png",
    cmPerPx: 100,
    centerX: -65742.33,
    centerY:  19203.812,
    swapXY: false, flipX: false, flipY: false, zoom: 0.5
  },
  Valley: {
    map: "Valley.png",
    cmPerPx: 100,
    centerX:  76602.749,
    centerY: -41996.181,
    swapXY: false, flipX: false, flipY: false, zoom: 0.55
  }
};

// Pick which baked entry to use by default:
const ACTIVE_KEY = "Valley";
const BAKED = BAKED_MAPS[ACTIVE_KEY];

// Lock orientation tools so your calibration can¡¯t drift by accident
const LOCK_MAPPING_AXES = true;

// DPR-aware canvas
let dpr = window.devicePixelRatio || 1;
resizeCanvas();
window.addEventListener("resize", () => { resizeCanvas(); drawFrame(lastFrame); });

// Map image
const mapImg = new Image();
mapImg.src = BAKED.map;
mapImg.onload = () => drawFrame(lastFrame);

// Initialize UI from baked values
function applyBaked() {
  cmppEl.value       = BAKED.cmPerPx;
  cxEl.value         = BAKED.centerX;
  cyEl.value         = BAKED.centerY;
  swapXYEl.checked   = !!BAKED.swapXY;
  flipXEl.checked    = !!BAKED.flipX;
  flipYEl.checked    = !!BAKED.flipY;
  zoomEl.value       = BAKED.zoom;

  if (LOCK_MAPPING_AXES) {
    cmppEl.disabled   = true;
    swapXYEl.disabled = true;
    flipXEl.disabled  = true;
    flipYEl.disabled  = true;
  }

  if (serverUrlEl) {
    serverUrlEl.value = (location.origin && location.origin.startsWith("http"))
      ? location.origin
      : "http://localhost:8088";
  }
}
applyBaked();

// Connection (SSE + polling fallback)
let es = null, pollTimer = null, connected = false, lastFrame = null;

btnConn.addEventListener("click", () => connected ? disconnect() : connect());
[zoomEl, cmppEl, cxEl, cyEl, swapXYEl, flipXEl, flipYEl].forEach(el =>
  el.addEventListener("input", ()=>{ logCurrentMapping("ui-change"); drawFrame(lastFrame); })
);
btnCopy.addEventListener("click", copyMapping);

function baseUrl() {
  const typed = serverUrlEl && serverUrlEl.value ? serverUrlEl.value.trim() : "";
  if (typed.startsWith("http")) return typed.replace(/\/+$/,"");
  if (location.origin && location.origin.startsWith("http")) return location.origin;
  return "http://localhost:8088";
}

function connect() {
  const base = baseUrl();
  try { es = new EventSource(base + "/stream"); } catch { return startPolling(base); }
  btnConn.textContent = "Connecting¡­";
  statusEl.textContent = "connecting¡­";
  es.addEventListener("open", () => {
    connected = true; statusEl.textContent = "connected (SSE)"; btnConn.textContent = "Disconnect";
    if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
  });
  es.addEventListener("error", () => { if (es) { es.close(); es = null; } startPolling(base); });
  es.addEventListener("frame", ev => { try { lastFrame = JSON.parse(ev.data); drawFrame(lastFrame); } catch{} });
}
function startPolling(base) {
  if (pollTimer) clearInterval(pollTimer);
  connected = true; statusEl.textContent = "connected (poll)"; btnConn.textContent = "Disconnect";
  const tick = ()=> {
    fetch(base + "/api/frame", { cache: "no-store" })
      .then(r => r.ok ? r.json() : Promise.reject())
      .then(j => { lastFrame = j; drawFrame(lastFrame); })
      .catch(()=>{});
  };
  tick();
  pollTimer = setInterval(tick, 100);
}
function disconnect(){
  if (es){ es.close(); es=null; }
  if (pollTimer){ clearInterval(pollTimer); pollTimer=null; }
  connected = false; statusEl.textContent = "disconnected"; btnConn.textContent = "Connect";
}

// ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
// Visual pan state: this moves the *image* and the *blips* together
// (CenterX/CenterY inputs WON'T affect the image anymore)
let imgPan = { x: 0, y: 0 };

// Drag to pan map+blips (adjusts imgPan only)
let isDragging=false, dragStart={x:0,y:0}, panStart={x:0,y:0};
CANVAS.addEventListener("mousedown", (e) => {
  isDragging = true; CANVAS.classList.add("dragging");
  dragStart = clientToCanvas(e);
  panStart = { x: imgPan.x, y: imgPan.y };
});
window.addEventListener("mouseup", () => {
  if (isDragging){ isDragging=false; CANVAS.classList.remove("dragging"); }
});
window.addEventListener("mousemove", (e) => {
  if (!isDragging) return;
  const cur = clientToCanvas(e);
  const dxPx = cur.x - dragStart.x;
  const dyPx = cur.y - dragStart.y;
  imgPan.x = panStart.x + dxPx;
  imgPan.y = panStart.y + dyPx;
  drawFrame(lastFrame);
});

// Wheel zoom (about canvas center); we keep imgPan as-is
CANVAS.addEventListener("wheel", (e) => {
  e.preventDefault();
  const m = readMapping();
  const z = clamp(m.zoom * (e.deltaY < 0 ? 1.1 : 0.9), 0.25, 8);
  if (z === m.zoom) return;
  zoomEl.value = z.toFixed(2);
  drawFrame(lastFrame);
}, { passive:false });

// Arrow keys nudge center (ONLY moves blips wrt image for calibration)
window.addEventListener("keydown", (e)=>{
  const step = (e.shiftKey ? 100 : 10);
  const m = readMapping();
  let dx=0, dy=0;
  if (e.key === "ArrowLeft") dx = -step;
  else if (e.key === "ArrowRight") dx = +step;
  else if (e.key === "ArrowUp") dy = +step;
  else if (e.key === "ArrowDown") dy = -step;
  else return;

  if (m.swapXY){ const t = dx; dx = dy; dy = t; }
  if (m.flipX) dx = -dx;
  if (m.flipY) dy = -dy;

  cxEl.value = (m.centerX + dx).toFixed(3);
  cyEl.value = (m.centerY + dy).toFixed(3);
  drawFrame(lastFrame);
});

// ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
// Mapping helpers

function readMapping(){
  return {
    zoom: clamp(parseFloat(zoomEl.value) || BAKED.zoom, 0.25, 8),
    cmPerPx: clamp(parseFloat(cmppEl.value) || BAKED.cmPerPx, 1, 100000),
    centerX: parseFloat(cxEl.value) || BAKED.centerX,
    centerY: parseFloat(cyEl.value) || BAKED.centerY,
    swapXY: LOCK_MAPPING_AXES ? BAKED.swapXY : !!swapXYEl.checked,
    flipX:  LOCK_MAPPING_AXES ? BAKED.flipX  : !!flipXEl.checked,
    flipY:  LOCK_MAPPING_AXES ? BAKED.flipY  : !!flipYEl.checked,
    canvasW: CANVAS.width,
    canvasH: CANVAS.height,
    imgW: mapImg.naturalWidth  || 1024,
    imgH: mapImg.naturalHeight || 1024,
    cx: CANVAS.width*0.5,
    cy: CANVAS.height*0.5
  };
}

// World¡úscreen (centerX/centerY affects only blip positions), then add imgPan
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
  return [x, y];
}

// (Optional) screen¡úworld if needed later
function screenToWorld(px, py, m=readMapping()){
  let imDX = (px - (m.cx + imgPan.x)) / m.zoom;
  let imDY = (py - (m.cy + imgPan.y)) / m.zoom;
  if (m.flipX) imDX = -imDX;
  if (m.flipY) imDY = -imDY;
  if (m.swapXY){ const t=imDX; imDX=imDY; imDY=t; }
  const dx = imDX * m.cmPerPx;
  const dy = imDY * m.cmPerPx;
  return { x: m.centerX + dx, y: m.centerY + dy };
}

// ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
// Drawing

function drawFrame(frame){
  CTX.setTransform(1,0,0,1,0,0);
  CTX.clearRect(0,0,CANVAS.width,CANVAS.height);

  const m = readMapping();

  // Cover render: image scales with zoom; **center inputs no longer shift image**.
  if (mapImg.complete && mapImg.naturalWidth){
    const scaleCover = Math.max(m.canvasW/m.imgW, m.canvasH/m.imgH);
    const s = scaleCover * m.zoom;

    const destW = m.imgW * s;
    const destH = m.imgH * s;
    const destX = m.cx - destW/2 + imgPan.x;
    const destY = m.cy - destH/2 + imgPan.y;

    CTX.drawImage(mapImg, 0,0,m.imgW,m.imgH, destX,destY,destW,destH);
  } else {
    drawGrid();
  }

  if (!frame || frame.ok !== true) return;

  // self
  if (frame.self){
    const [sx, sy] = worldToScreen(frame.self.x, frame.self.y, m);
    drawSelf(sx, sy);
  }

  // actors
  const botsCol = "#0db1ff", pmcCol = "#ff4a4a", deadCol = "rgba(255,214,0,0.85)";
  for (const a of frame.actors){
    const [x, y] = worldToScreen(a.x, a.y, m);
    if (a.dead) drawDiamond(x,y,6,deadCol);
    else if (a.bot) drawCircle(x,y,4,botsCol);
    else drawSquare(x,y,5,pmcCol);
  }
}

function drawGrid(){
  CTX.fillStyle="#000"; CTX.fillRect(0,0,CANVAS.width,CANVAS.height);
  CTX.strokeStyle="rgba(255,255,255,0.08)"; CTX.lineWidth=1*dpr;
  const step = Math.max(24*dpr, Math.min(CANVAS.width, CANVAS.height)/16);
  for (let x=0; x<=CANVAS.width; x+=step){ CTX.beginPath(); CTX.moveTo(x,0); CTX.lineTo(x,CANVAS.height); CTX.stroke(); }
  for (let y=0; y<=CANVAS.height; y+=step){ CTX.beginPath(); CTX.moveTo(0,y); CTX.lineTo(CANVAS.width,y); CTX.stroke(); }
}

function drawSelf(x,y){
  CTX.fillStyle="#19ff6a";
  CTX.beginPath(); CTX.moveTo(x,y-7*dpr); CTX.lineTo(x+6*dpr,y+4*dpr); CTX.lineTo(x-6*dpr,y+4*dpr); CTX.closePath(); CTX.fill();
}
function drawCircle(x,y,r,col){ CTX.fillStyle=col; CTX.beginPath(); CTX.arc(x,y,r*dpr,0,Math.PI*2); CTX.fill(); }
function drawSquare(x,y,half,col){
  const h=half*dpr;
  CTX.fillStyle=col; CTX.fillRect(x-h,y-h,h*2,h*2);
  CTX.strokeStyle="rgba(0,0,0,.9)"; CTX.lineWidth=1*dpr; CTX.strokeRect(x-h,y-h,h*2,h*2);
}
function drawDiamond(x,y,r,col){
  const rr=r*dpr;
  CTX.fillStyle=col;
  CTX.beginPath(); CTX.moveTo(x,y-rr); CTX.lineTo(x+rr,y); CTX.lineTo(x,y+rr); CTX.lineTo(x-rr,y); CTX.closePath(); CTX.fill();
  CTX.strokeStyle="rgba(0,0,0,.9)"; CTX.lineWidth=1*dpr; CTX.stroke();
}

// ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
// Utilities

function resizeCanvas(){
  dpr = window.devicePixelRatio || 1;
  const rect = CANVAS.getBoundingClientRect();
  CANVAS.width  = Math.max(1, Math.round(rect.width  * dpr));
  CANVAS.height = Math.max(1, Math.round(rect.height * dpr));
  CTX.setTransform(1,0,0,1,0,0);
}
function clientToCanvas(e){
  const r = CANVAS.getBoundingClientRect();
  return { x:(e.clientX - r.left)*dpr, y:(e.clientY - r.top)*dpr };
}
function clamp(v,lo,hi){ return Math.max(lo, Math.min(hi,v)); }

function copyMapping(){
  const m = readMapping();
  const cfg = {
    map: BAKED.map,
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
