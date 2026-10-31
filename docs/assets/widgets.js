// Interactive canvas widgets for the landing page.
// Replaces static PNGs with live, clickable canvases that match the C# originals.

const DPR = Math.min(window.devicePixelRatio || 1, 3);
const FONT = '-apple-system, BlinkMacSystemFont, "Segoe UI Variable Text", "Segoe UI", sans-serif';

function setup(canvas, w, h) {
  canvas.width = w * DPR;
  canvas.height = h * DPR;
  canvas.style.width = w + "px";
  canvas.style.height = h + "px";
  const ctx = canvas.getContext("2d");
  ctx.scale(DPR, DPR);
  return ctx;
}

function roundRect(ctx, x, y, w, h, r) {
  r = Math.min(r, w / 2, h / 2);
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}

function fillCircle(ctx, color, cx, cy, r) {
  ctx.fillStyle = color;
  ctx.beginPath();
  ctx.arc(cx, cy, r, 0, Math.PI * 2);
  ctx.fill();
}

// ══════════════════════════════════════════════════════════════════
//  Analog Clock — matches AnalogClock.cs exactly
// ══════════════════════════════════════════════════════════════════

const CLOCK_THEMES = {
  light: {
    bgTop: "#2e2e30", bgBottom: "#1c1c1e", face: "#ffffff",
    numbers: "#111111", label: "#8e8e93", tickHour: "#6e6e73",
    tickMinute: "#bdbdc2", hands: "#111111",
    shadow: "rgba(0,0,0,0.18)", seconds: "#ff9500",
  },
  dark: {
    bgTop: "#3a3a3c", bgBottom: "#262628", face: "#111113",
    numbers: "#f5f5f7", label: "#8e8e93", tickHour: "#8e8e93",
    tickMinute: "#48484a", hands: "#f5f5f7",
    shadow: "rgba(0,0,0,0.35)", seconds: "#ff9f0a",
  },
};

export function analogClock(canvas, theme = "light") {
  const s = 170;
  const ctx = setup(canvas, s, s);
  const th = CLOCK_THEMES[theme];
  let raf;

  function draw() {
    ctx.clearRect(0, 0, s, s);
    const c = s / 2, r = s * 0.445;

    // Background rounded square
    const bgR = s * 0.225;
    roundRect(ctx, 0, 0, s, s, bgR);
    const grad = ctx.createLinearGradient(0, 0, 0, s);
    grad.addColorStop(0, th.bgTop);
    grad.addColorStop(1, th.bgBottom);
    ctx.fillStyle = grad;
    ctx.fill();

    // White face
    ctx.fillStyle = th.face;
    ctx.beginPath();
    ctx.arc(c, c, r, 0, Math.PI * 2);
    ctx.fill();

    // Tick marks
    for (let i = 0; i < 60; i++) {
      const major = i % 5 === 0;
      const a = (i * 6 * Math.PI) / 180;
      const dx = Math.sin(a), dy = -Math.cos(a);
      const inner = r * (major ? 0.895 : 0.925), outer = r * 0.965;
      ctx.strokeStyle = major ? th.tickHour : th.tickMinute;
      ctx.lineWidth = s * (major ? 0.009 : 0.0055);
      ctx.beginPath();
      ctx.moveTo(c + dx * inner, c + dy * inner);
      ctx.lineTo(c + dx * outer, c + dy * outer);
      ctx.stroke();
    }

    // Numbers
    ctx.fillStyle = th.numbers;
    ctx.font = `600 ${r * 0.27}px ${FONT}`;
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    for (let h = 1; h <= 12; h++) {
      const a = (h * 30 * Math.PI) / 180;
      ctx.fillText(String(h), c + Math.sin(a) * r * 0.735, c - Math.cos(a) * r * 0.735);
    }

    // Current time
    const now = new Date();
    const sec = now.getSeconds() + now.getMilliseconds() / 1000;
    const min = now.getMinutes() + sec / 60;
    const hour = (now.getHours() % 12) + min / 60;
    const shadowDy = s * 0.007;

    function hand(cx, cy, degrees, length, stemW, armW, color) {
      const a = (degrees * Math.PI) / 180;
      const dx = Math.sin(a), dy = -Math.cos(a);
      const joint = r * 0.13, armEnd = length - armW / 2;
      ctx.strokeStyle = color;
      ctx.lineCap = "round";
      ctx.lineWidth = stemW;
      ctx.beginPath();
      ctx.moveTo(cx, cy);
      ctx.lineTo(cx + dx * joint, cy + dy * joint);
      ctx.stroke();
      ctx.lineWidth = armW;
      ctx.beginPath();
      ctx.moveTo(cx + dx * joint, cy + dy * joint);
      ctx.lineTo(cx + dx * armEnd, cy + dy * armEnd);
      ctx.stroke();
    }

    // Shadow hands
    hand(c, c + shadowDy, hour * 30, r * 0.52, s * 0.016, s * 0.034, th.shadow);
    hand(c, c + shadowDy, min * 6, r * 0.84, s * 0.016, s * 0.034, th.shadow);
    // Real hands
    hand(c, c, hour * 30, r * 0.52, s * 0.016, s * 0.034, th.hands);
    hand(c, c, min * 6, r * 0.84, s * 0.016, s * 0.034, th.hands);
    // Hub
    fillCircle(ctx, th.hands, c, c, s * 0.03);

    // Seconds hand
    const sa = (sec * 6 * Math.PI) / 180;
    const sdx = Math.sin(sa), sdy = -Math.cos(sa);
    ctx.strokeStyle = th.seconds;
    ctx.lineWidth = s * 0.0115;
    ctx.lineCap = "round";
    ctx.beginPath();
    ctx.moveTo(c - sdx * r * 0.18, c - sdy * r * 0.18);
    ctx.lineTo(c + sdx * r * 0.92, c + sdy * r * 0.92);
    ctx.stroke();
    fillCircle(ctx, th.seconds, c, c, s * 0.021);
    fillCircle(ctx, th.face, c, c, s * 0.008);

    raf = requestAnimationFrame(draw);
  }
  draw();
  return () => cancelAnimationFrame(raf);
}

// ══════════════════════════════════════════════════════════════════
//  Focus Timer — matches FocusTimer.cs (a CardWidget, small card)
// ══════════════════════════════════════════════════════════════════

const TIMER_THEMES = {
  light: { bg: "#ffffff", fg: "#000000", fg2: "#6e6e73", fill: "#f2f2f7", orange: "#ff9500", onAccent: "#ffffff" },
  dark: { bg: "#1c1c1e", fg: "#ffffff", fg2: "#98989f", fill: "#2c2c2e", orange: "#ff9f0a", onAccent: "#ffffff" },
};

export function focusTimer(canvas, theme = "light") {
  const W = 170, H = 170;
  const ctx = setup(canvas, W, H);
  const pal = TIMER_THEMES[theme];

  let running = false, totalSec = 25 * 60, leftSec = 25 * 60, endTime = null;
  let raf;

  function secondsLeft() {
    if (running) return Math.max(0, Math.ceil((endTime - Date.now()) / 1000));
    return leftSec;
  }

  function start() {
    const left = secondsLeft() || totalSec;
    endTime = Date.now() + left * 1000;
    running = true;
  }
  function pause() {
    leftSec = secondsLeft();
    running = false;
    endTime = null;
  }
  function reset() {
    running = false;
    endTime = null;
    leftSec = totalSec;
  }

  const ringCx = 16 + 46, ringCy = 97;
  const playCx = W - 16 - 14, playCy = 80;
  const resetCx = W - 16 - 14, resetCy = 116;

  function within(px, py, cx, cy, r) {
    return (px - cx) ** 2 + (py - cy) ** 2 <= r * r;
  }

  canvas.style.cursor = "pointer";
  canvas.addEventListener("click", (e) => {
    const rect = canvas.getBoundingClientRect();
    const scaleX = W / rect.width, scaleY = H / rect.height;
    const px = (e.clientX - rect.left) * scaleX;
    const py = (e.clientY - rect.top) * scaleY;
    if (within(px, py, playCx, playCy, 17)) {
      if (running) pause(); else start();
    } else if (within(px, py, resetCx, resetCy, 17)) {
      reset();
    }
  });

  function draw() {
    const left = secondsLeft();
    if (running && left === 0) { pause(); leftSec = 0; }
    const m = 16;

    ctx.clearRect(0, 0, W, H);
    // Card background
    roundRect(ctx, 0, 0, W, H, 22);
    ctx.fillStyle = pal.bg;
    ctx.fill();

    // "Focus" label
    ctx.font = `600 13px ${FONT}`;
    ctx.textAlign = "left";
    ctx.textBaseline = "top";
    ctx.fillStyle = pal.orange;
    ctx.fillText("Focus", m, m - 1);

    // State label
    const state = left === 0 ? "Done" : running ? "Running" : left === totalSec ? "Ready" : "Paused";
    ctx.font = `400 11px ${FONT}`;
    ctx.textAlign = "right";
    ctx.fillStyle = pal.fg2;
    ctx.fillText(state, W - m, m + 1);

    // Ring
    const ringR = 46 - 4.5, stroke = 9;
    ctx.lineWidth = stroke;
    ctx.lineCap = "butt";
    // Track
    ctx.strokeStyle = pal.fill;
    ctx.beginPath();
    ctx.arc(ringCx, ringCy, ringR, 0, Math.PI * 2);
    ctx.stroke();
    // Arc
    const sweep = totalSec > 0 ? (Math.PI * 2 * left) / totalSec : 0;
    if (sweep > 0.01) {
      ctx.strokeStyle = pal.orange;
      ctx.lineCap = "round";
      ctx.beginPath();
      ctx.arc(ringCx, ringCy, ringR, -Math.PI / 2, -Math.PI / 2 + sweep);
      ctx.stroke();
    }

    // Time text
    const mins = Math.floor(left / 60);
    const secs = left % 60;
    const timeStr = mins + ":" + String(secs).padStart(2, "0");
    ctx.font = `600 20px ${FONT}`;
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.fillStyle = pal.fg;
    ctx.fillText(timeStr, ringCx, ringCy);

    // Play/pause button
    fillCircle(ctx, pal.orange, playCx, playCy, 14);
    ctx.fillStyle = pal.onAccent;
    if (running) {
      ctx.fillRect(playCx - 4.6, playCy - 5.5, 3.2, 11);
      ctx.fillRect(playCx + 1.4, playCy - 5.5, 3.2, 11);
    } else {
      ctx.beginPath();
      ctx.moveTo(playCx - 6 * 0.7, playCy - 6);
      ctx.lineTo(playCx - 6 * 0.7, playCy + 6);
      ctx.lineTo(playCx + 6, playCy);
      ctx.fill();
    }

    // Reset button
    fillCircle(ctx, pal.fill, resetCx, resetCy, 14);
    const ar = 5.5;
    ctx.strokeStyle = pal.fg;
    ctx.lineWidth = 1.7;
    ctx.lineCap = "round";
    ctx.beginPath();
    ctx.arc(resetCx, resetCy, ar, (-60 * Math.PI) / 180, (240 * Math.PI) / 180);
    ctx.stroke();
    // Arrowhead
    const ax = resetCx + ar * Math.cos((-60 * Math.PI) / 180);
    const ay = resetCy + ar * Math.sin((-60 * Math.PI) / 180);
    const as2 = 3.2;
    ctx.fillStyle = pal.fg;
    ctx.beginPath();
    ctx.moveTo(ax - as2, ay - as2 * 1.2);
    ctx.lineTo(ax + as2 * 1.3, ay - as2 * 0.2);
    ctx.lineTo(ax - as2 * 0.3, ay + as2 * 1.2);
    ctx.fill();

    raf = requestAnimationFrame(draw);
  }
  draw();
  return () => cancelAnimationFrame(raf);
}

// ══════════════════════════════════════════════════════════════════
//  Now Playing — matches NowPlayingWidget.cs with synthesized audio
// ══════════════════════════════════════════════════════════════════

const NOW_PAL = {
  bg: "#1c1c1e", fg: "#ffffff", fg2: "#98989f",
  fill: "#2c2c2e", onAccent: "#1c1c1e",
};

// Synthesize a lo-fi ambient track using Web Audio API.
function createSynth() {
  const ac = new AudioContext();
  const master = ac.createGain();
  master.gain.value = 0.35;
  master.connect(ac.destination);

  // Simple chord pad: two detuned triangle oscillators per note
  const chords = [
    [130.81, 164.81, 196.00],  // Cm
    [207.65, 261.63, 311.13],  // Ab
    [155.56, 196.00, 233.08],  // Eb
    [116.54, 146.83, 174.61],  // Bb
  ];
  // Melody notes over each chord (quarter-note timing)
  const melody = [
    [392.00, 311.13, 261.63, 311.13],  // G4 Eb4 C4 Eb4
    [523.25, 415.30, 311.13, 349.23],  // C5 Ab4 Eb4 F4
    [466.16, 392.00, 311.13, 392.00],  // Bb4 G4 Eb4 G4
    [349.23, 293.66, 233.08, 293.66],  // F4 D4 Bb3 D4
  ];

  const BPM = 72, BEAT = 60 / BPM;
  const BAR = BEAT * 4;
  const DURATION = BAR * chords.length;
  let source = null, startedAt = 0, offset = 0, playing = false;

  function renderBuffer() {
    const offline = new OfflineAudioContext(2, ac.sampleRate * DURATION, ac.sampleRate);
    const gain = offline.createGain();
    gain.gain.value = 0.6;
    gain.connect(offline.destination);

    // Pad chords
    for (let ci = 0; ci < chords.length; ci++) {
      const t0 = ci * BAR;
      for (const freq of chords[ci]) {
        for (const detune of [-6, 6]) {
          const osc = offline.createOscillator();
          osc.type = "triangle";
          osc.frequency.value = freq;
          osc.detune.value = detune;
          const g = offline.createGain();
          g.gain.setValueAtTime(0, t0);
          g.gain.linearRampToValueAtTime(0.12, t0 + 0.3);
          g.gain.setValueAtTime(0.12, t0 + BAR - 0.4);
          g.gain.linearRampToValueAtTime(0, t0 + BAR);
          osc.connect(g);
          g.connect(gain);
          osc.start(t0);
          osc.stop(t0 + BAR);
        }
      }
      // Melody
      for (let ni = 0; ni < melody[ci].length; ni++) {
        const t = t0 + ni * BEAT;
        const osc = offline.createOscillator();
        osc.type = "sine";
        osc.frequency.value = melody[ci][ni];
        const g = offline.createGain();
        g.gain.setValueAtTime(0, t);
        g.gain.linearRampToValueAtTime(0.18, t + 0.05);
        g.gain.exponentialRampToValueAtTime(0.001, t + BEAT * 0.9);
        osc.connect(g);
        g.connect(gain);
        osc.start(t);
        osc.stop(t + BEAT);
      }
    }
    return offline.startRendering();
  }

  let buffer = null;
  const ready = renderBuffer().then((b) => { buffer = b; });

  function play() {
    if (!buffer || playing) return;
    source = ac.createBufferSource();
    source.buffer = buffer;
    source.loop = true;
    source.connect(master);
    source.start(0, offset % DURATION);
    startedAt = ac.currentTime - offset;
    playing = true;
  }

  function pauseAudio() {
    if (!playing) return;
    offset = (ac.currentTime - startedAt) % DURATION;
    source.stop();
    source = null;
    playing = false;
  }

  function restart() {
    if (source) { source.stop(); source = null; }
    offset = 0;
    playing = false;
  }

  return {
    ready,
    play, pause: pauseAudio, restart,
    get playing() { return playing; },
    get position() { return playing ? (ac.currentTime - startedAt) % DURATION : offset % DURATION; },
    get duration() { return DURATION; },
    resume: () => ac.resume(),
  };
}

export function nowPlaying(canvas) {
  const W = 364, H = 170;
  const ctx = setup(canvas, W, H);
  const pal = NOW_PAL;
  let synth = null;
  let raf;

  const m = 16, artSize = H - 32;
  const rx = m + artSize + 14, rw = W - m - rx;
  const playCx = (rx + W - m) / 2, playCy = H - m - 18;
  const prevCx = playCx - 52, prevCy = playCy;
  const nextCx = playCx + 52, nextCy = playCy;

  function within(px, py, cx, cy, r) {
    return (px - cx) ** 2 + (py - cy) ** 2 <= r * r;
  }

  canvas.style.cursor = "pointer";
  canvas.addEventListener("click", async (e) => {
    const rect = canvas.getBoundingClientRect();
    const sx = W / rect.width, sy = H / rect.height;
    const px = (e.clientX - rect.left) * sx;
    const py = (e.clientY - rect.top) * sy;

    if (!synth) {
      synth = createSynth();
      await synth.ready;
    }
    await synth.resume();

    if (within(px, py, playCx, playCy, 20)) {
      if (synth.playing) synth.pause(); else synth.play();
    } else if (within(px, py, prevCx, prevCy, 16)) {
      synth.restart();
      synth.play();
    } else if (within(px, py, nextCx, nextCy, 16)) {
      synth.restart();
      synth.play();
    }
  });

  function drawArt() {
    ctx.save();
    roundRect(ctx, m, m, artSize, artSize, 10);
    ctx.clip();
    const grad = ctx.createLinearGradient(m, m, m, m + artSize);
    grad.addColorStop(0, "#f08a5d");
    grad.addColorStop(1, "#3d1e52");
    ctx.fillStyle = grad;
    ctx.fillRect(m, m, artSize, artSize);
    // Sun
    fillCircle(ctx, "#ffd27a", m + artSize * 0.68, m + artSize * 0.32, artSize * 0.17);
    // Hill
    ctx.fillStyle = "#0d3b4f";
    ctx.beginPath();
    ctx.ellipse(m + artSize * 0.5, m + artSize * 1.0, artSize * 0.45, artSize * 0.45, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }

  function drawControls(isPlaying) {
    // Play/pause
    fillCircle(ctx, pal.fg, playCx, playCy, 18);
    ctx.fillStyle = pal.bg;
    if (isPlaying) {
      ctx.fillRect(playCx - 5.5, playCy - 6.5, 4, 13);
      ctx.fillRect(playCx + 1.5, playCy - 6.5, 4, 13);
    } else {
      ctx.beginPath();
      ctx.moveTo(playCx - 7 * 0.6, playCy - 7);
      ctx.lineTo(playCx - 7 * 0.6, playCy + 7);
      ctx.lineTo(playCx + 7, playCy);
      ctx.fill();
    }

    // Previous / Next
    for (const [cx, cy, fwd] of [[prevCx, prevCy, false], [nextCx, nextCy, true]]) {
      fillCircle(ctx, pal.fill, cx, cy, 14);
      ctx.fillStyle = pal.fg;
      const d = fwd ? 1 : -1;
      for (let k = 0; k < 2; k++) {
        const x = cx + d * (-4.5 + k * 4.5);
        ctx.beginPath();
        ctx.moveTo(x, cy - 4.5);
        ctx.lineTo(x, cy + 4.5);
        ctx.lineTo(x + d * 4.5, cy);
        ctx.fill();
      }
    }
  }

  function draw() {
    const isPlaying = synth ? synth.playing : false;
    const pos = synth ? synth.position : 0;
    const dur = synth ? synth.duration : 13.33;

    ctx.clearRect(0, 0, W, H);
    // Card background
    roundRect(ctx, 0, 0, W, H, 22);
    ctx.fillStyle = pal.bg;
    ctx.fill();

    drawArt();

    // "NOW PLAYING" label
    ctx.font = `600 11px ${FONT}`;
    ctx.textAlign = "left";
    ctx.textBaseline = "top";
    ctx.fillStyle = pal.fg2;
    ctx.fillText("NOW PLAYING", rx, m - 1);

    // Title
    ctx.font = `600 15px ${FONT}`;
    ctx.fillStyle = pal.fg;
    ctx.fillText("Low Tide Radio", rx, m + 14);

    // Artist
    ctx.font = `400 13px ${FONT}`;
    ctx.fillStyle = pal.fg2;
    ctx.fillText("The Harbour Lights", rx, m + 34);

    // Progress bar
    const by = m + 64, bh = 4;
    roundRect(ctx, rx, by, rw, bh, bh / 2);
    ctx.fillStyle = pal.fill;
    ctx.fill();
    const frac = dur > 0 ? pos / dur : 0;
    if (frac > 0) {
      roundRect(ctx, rx, by, Math.max(bh, rw * frac), bh, bh / 2);
      ctx.fillStyle = pal.fg;
      ctx.fill();
    }

    // Time labels
    ctx.font = `400 11px ${FONT}`;
    ctx.textBaseline = "top";
    ctx.fillStyle = pal.fg2;
    ctx.textAlign = "left";
    ctx.fillText(formatTime(pos), rx, by + 8);
    ctx.textAlign = "right";
    ctx.fillText(formatTime(dur), rx + rw, by + 8);

    drawControls(isPlaying);
    raf = requestAnimationFrame(draw);
  }

  function formatTime(s) {
    const m = Math.floor(s / 60), sec = Math.floor(s) % 60;
    return m + ":" + String(sec).padStart(2, "0");
  }

  draw();
  return () => { cancelAnimationFrame(raf); if (synth) synth.pause(); };
}
