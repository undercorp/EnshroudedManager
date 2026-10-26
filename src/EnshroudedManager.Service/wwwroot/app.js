"use strict";

const $ = (s, root = document) => root.querySelector(s);
const $$ = (s, root = document) => [...root.querySelectorAll(s)];
const el = (tag, props = {}, ...children) => {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(props)) {
    if (k === "class") e.className = v;
    else if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else if (v !== undefined && v !== null && v !== false) e.setAttribute(k, v === true ? "" : v);
  }
  for (const c of children.flat()) if (c != null) e.append(c);
  return e;
};

let token = null;
try { token = localStorage.getItem("em-token"); } catch { }

async function api(path, options = {}) {
  const res = await fetch("/api/" + path, {
    method: options.method || "GET",
    headers: { "Content-Type": "application/json", ...(token ? { Authorization: "Bearer " + token } : {}) },
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
  });
  if (res.status === 401 && !path.startsWith("auth/")) { showLogin(); throw new Error(t("Session expirée")); }
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  if (!res.ok) throw new Error(data?.message || t("Erreur {0}", res.status));
  return data;
}

const fmtDate = d => d ? new Date(d).toLocaleString(lang === "en" ? "en-GB" : "fr-BE", { dateStyle: "short", timeStyle: "short" }) : "—";
const fmtTime = d => new Date(d).toLocaleTimeString(lang === "en" ? "en-GB" : "fr-BE");
function fmtDuration(ms) {
  const m = Math.floor(ms / 60000), h = Math.floor(m / 60), d = Math.floor(h / 24);
  return d ? t("{0} j {1} h", d, h % 24) : h ? `${h} h ${m % 60} min` : `${m} min`;
}

/* ---------- login ---------- */

let setupMode = false;
async function showLogin() {
  clearInterval(timer);
  token = null;
  try { localStorage.removeItem("em-token"); } catch { }
  const state = await api("auth/state");
  setupMode = !state.configured;
  $("#login-intro").textContent = setupMode
    ? t("Première connexion : choisis le mot de passe qui protégera l'interface (8 caractères minimum).")
    : t("Connexion à l'outil de gestion du serveur.");
  $("#login-confirm-row").classList.toggle("hidden", !setupMode);
  $("#login-submit").textContent = setupMode ? t("Définir le mot de passe") : "Se connecter";
  $("#login-password").autocomplete = setupMode ? "new-password" : "current-password";
  $("#login").classList.remove("hidden");
  $("#login-password").focus();
}

$("#login-form").addEventListener("submit", async e => {
  e.preventDefault();
  const password = $("#login-password").value;
  $("#login-error").textContent = "";
  if (setupMode && password !== $("#login-confirm").value) { $("#login-error").textContent = t("Les deux mots de passe sont différents."); return; }
  try {
    const r = await api(setupMode ? "auth/setup" : "auth/login", { method: "POST", body: { password } });
    token = r.token;
    try { localStorage.setItem("em-token", token); } catch { }
    $("#login").classList.add("hidden");
    $("#login-password").value = $("#login-confirm").value = "";
    start();
  } catch (err) { $("#login-error").textContent = err.message; }
});

/* ---------- tabs ---------- */

function showTab(name) {
  $$("#tabs button").forEach(b => b.classList.toggle("active", b.dataset.tab === name));
  $$(".tab").forEach(t => t.classList.toggle("hidden", t.id !== "tab-" + name));
  if (name === "backups") loadBackups();
  if (name === "tool") loadSettings();
  try { localStorage.setItem("em-tab", name); } catch { }
}
$$("#tabs button").forEach(b => b.addEventListener("click", () => showTab(b.dataset.tab)));

/* ---------- dialog ---------- */

function confirmDialog(title, body, okLabel = t("Confirmer")) {
  const d = $("#dialog");
  $("#dialog-title").textContent = title;
  const b = $("#dialog-body");
  b.replaceChildren(...(Array.isArray(body) ? body : [el("p", {}, body)]));
  const ok = $("#dialog-ok"), cancel = $("#dialog-cancel");
  ok.textContent = okLabel;
  d.showModal();
  // Resolve from the clicks themselves: the dialog "close" event is not reliably delivered in every embedded browser.
  return new Promise(resolve => {
    const done = v => {
      ok.onclick = cancel.onclick = d.oncancel = null;
      if (d.open) d.close();
      resolve(v);
    };
    ok.onclick = e => { e.preventDefault(); done(true); };
    cancel.onclick = e => { e.preventDefault(); done(false); };
    d.oncancel = e => { e.preventDefault(); done(false); };
  });
}

/* ---------- status / dashboard ---------- */

let status = null;
let lastLogId = 0;
let configVersion = null;
const logLines = [];

async function refreshStatus() {
  try { status = await api("status"); } catch { return; }
  const s = status;
  $("#server-name").textContent = s.serverName;
  document.title = `${s.serverName} — Enshrouded Manager`;
  const pill = $("#state-pill");
  pill.textContent = s.stateLabel;
  pill.className = "pill " + s.state;
  $("#busy").textContent = s.busy || "";
  $("#busy").classList.toggle("hidden", !s.busy);
  $("#progress").classList.toggle("hidden", s.progress == null);
  if (s.progress != null) $("#progress").value = s.progress;
  $$("[data-action]").forEach(b => b.disabled = !!s.busy);

  $("#s-state").textContent = s.stateLabel;
  $("#s-uptime").textContent = s.startedAt ? t("Depuis {0}", fmtDuration(Date.now() - new Date(s.startedAt))) + (s.pid ? ` · PID ${s.pid}` : "") : "";
  $("#s-players").textContent = s.players ? `${s.players.current} / ${s.players.max}` : "—";
  $("#s-mem").textContent = s.memoryMb ? t("Mémoire : {0} Go", (s.memoryMb / 1024).toFixed(1)) : "";
  $("#s-local").textContent = s.localBuild || "?";
  $("#s-local").classList.toggle("update", s.updateAvailable);
  $("#s-remote").textContent = s.remoteBuild
    ? (s.updateAvailable ? t("Nouvelle version disponible : {0}", s.remoteBuild) : t("À jour avec Steam"))
    : t("Version Steam pas encore vérifiée");
  $("#s-auto").textContent = s.autoUpdate ? t("Automatiques") : t("Manuelles");
  $("#s-check").textContent = t("Vérifié : {0} · prochaine : {1}", fmtDate(s.lastCheck), s.nextCheck ? fmtTime(s.nextCheck).slice(0, 5) : "—")
    + (s.lastUpdate ? t(" · dernière MAJ : {0}", fmtDate(s.lastUpdate)) : "");

  if (s.lastLogId > lastLogId) refreshLogs();
  if (configVersion === 0 && s.configVersion) loadConfig();
  else if (configVersion !== null && s.configVersion !== configVersion) onConfigChangedElsewhere();
}

async function refreshLogs() {
  const entries = await api("logs?after=" + lastLogId);
  if (!entries.length) return;
  lastLogId = entries[entries.length - 1].id;
  logLines.push(...entries);
  if (logLines.length > 2000) logLines.splice(0, logLines.length - 2000);
  const line = e => el("div", { class: e.level }, el("span", { class: "t" }, fmtDate(e.time).replace(",", "")), e.message);
  const full = $("#full-log");
  const atBottom = full.scrollHeight - full.scrollTop - full.clientHeight < 40;
  full.replaceChildren(...logLines.map(line));
  if (atBottom) full.scrollTop = full.scrollHeight;
  const recent = $("#recent-log");
  recent.replaceChildren(...logLines.slice(-40).map(line));
  recent.scrollTop = recent.scrollHeight;
}

$$("[data-action]").forEach(b => b.addEventListener("click", async () => {
  if (b.dataset.confirm && !(await confirmDialog(b.textContent, b.dataset.confirm))) return;
  const out = b.closest("#tab-dashboard") ? $("#action-result") : null;
  if (out) { out.className = "result"; out.textContent = t("En cours…"); }
  b.disabled = true;
  try {
    const r = await api(b.dataset.action, { method: "POST" });
    if (out) { out.className = "result ok"; out.textContent = r.message; }
    if (b.dataset.action === "backups") loadBackups();
  } catch (err) {
    if (out) { out.className = "result err"; out.textContent = err.message; }
  } finally {
    refreshStatus();
  }
}));

/* ---------- config ---------- */

let schema = null, original = null, current = null, dirty = false;

const clone = o => JSON.parse(JSON.stringify(o));
const sectionId = s => "sec-" + s.normalize("NFD").replace(/[^\w]+/g, "-").toLowerCase();

async function loadConfig() {
  try {
    [schema, original] = await Promise.all([api("config/schema"), api("config")]);
  } catch {
    configVersion = 0; // retried by refreshStatus once the file exists
    $("#config-form").replaceChildren(el("div", { class: "card notice" },
      t("Le serveur n'est pas encore installé : les paramètres seront disponibles à la fin du téléchargement.")));
    return;
  }
  configVersion = original.version;
  current = clone(original.values);
  setDirty(false);
  $("#config-changed").classList.add("hidden");
  renderConfig();
}

function onConfigChangedElsewhere() {
  if (dirty) $("#config-changed").classList.remove("hidden");
  else loadConfig();
}
$("#config-reload").addEventListener("click", loadConfig);

function setDirty(v) {
  dirty = v;
  $("#savebar").classList.toggle("hidden", !v);
}

function updateDirty() {
  setDirty(JSON.stringify(current) !== JSON.stringify(original.values));
  $$(".field[data-key]", $("#config-form")).forEach(f => {
    const k = f.dataset.key;
    f.classList.toggle("changed", JSON.stringify(current[k]) !== JSON.stringify(original.values[k]));
  });
}

function rangeHint(f, value) {
  if (f.min == null || f.max == null) return f.help || "";
  const out = typeof value === "number" && (value < f.min || value > f.max);
  const range = t("Plage officielle : {0} – {1}", f.min, f.max) + (f.unit === "min" ? " min" : "");
  return { text: (f.help ? f.help + " · " : "") + range + (out ? t(" — valeur actuelle hors plage (accepté par le jeu, mais non garanti)") : ""), out };
}

function control(f, value, onChange) {
  switch (f.type) {
    case "bool":
      return el("input", { type: "checkbox", checked: !!value, onchange: e => onChange(e.target.checked) });
    case "enum":
      return el("select", { onchange: e => onChange(e.target.value) },
        f.options.map(o => el("option", { value: o, selected: o === value }, o)));
    case "tags": {
      const set = new Set(value || []);
      return el("div", { class: "tags" }, f.options.map(o => {
        const lab = el("label", { class: "tag" + (set.has(o) ? " on" : "") },
          el("input", { type: "checkbox", checked: set.has(o), onchange: e => {
            e.target.checked ? set.add(o) : set.delete(o);
            lab.classList.toggle("on", e.target.checked);
            onChange(f.options.filter(x => set.has(x)));
          } }), o);
        return lab;
      }));
    }
    case "password": {
      const input = el("input", { type: "password", value: value ?? "", autocomplete: "off", oninput: e => onChange(e.target.value) });
      return el("div", { class: "pwd" }, input,
        el("button", { type: "button", class: "btn small", onclick: e => {
          input.type = input.type === "password" ? "text" : "password";
          e.target.textContent = input.type === "password" ? t("Voir") : t("Cacher");
        } }, t("Voir")));
    }
    case "int":
    case "float":
    case "duration": {
      const num = el("input", { type: "number", step: f.type === "int" ? 1 : "any", value: value ?? "" });
      const parts = [num];
      // A slider only when the value fits the official range, so it never clamps the user's value.
      if (f.min != null && f.max != null && f.key !== "queryPort") {
        const slider = el("input", { type: "range", min: f.min, max: f.max, step: f.step || "any", value: value ?? f.min });
        const syncSlider = v => { slider.disabled = v < f.min || v > f.max; if (!slider.disabled) slider.value = v; };
        syncSlider(value);
        slider.addEventListener("input", () => { num.value = slider.value; onChange(Number(slider.value)); });
        num.addEventListener("input", () => { if (num.value !== "") { syncSlider(Number(num.value)); onChange(Number(num.value)); } });
        parts.unshift(slider);
      } else {
        num.addEventListener("input", () => { if (num.value !== "") onChange(Number(num.value)); });
      }
      if (f.unit) parts.push(el("span", { class: "unit" }, f.unit));
      return el("div", { class: "control" }, parts);
    }
    default:
      return el("input", { type: "text", value: value ?? "", oninput: e => onChange(e.target.value) });
  }
}

function fieldRow(f, getValue, setValue, dataKey) {
  const hint = el("div", { class: "hint" });
  const showHint = v => {
    const h = rangeHint(f, v);
    hint.textContent = typeof h === "string" ? h : h.text;
    hint.classList.toggle("out", !!h.out);
  };
  showHint(getValue());
  const c = control(f, getValue(), v => { setValue(v); showHint(v); updateDirty(); });
  return el("div", { class: "field", "data-key": dataKey },
    el("div", { class: "name" }, f.label), el("div", { class: "control" }, c), hint);
}

function renderConfig() {
  const form = $("#config-form");
  const nav = $("#config-nav");
  form.replaceChildren();
  nav.replaceChildren();
  const sections = [...new Set(schema.fields.map(f => f.section))];
  for (const s of [...sections, t("Groupes de joueurs")]) {
    nav.append(el("a", { href: "#" + sectionId(s), onclick: e => { e.preventDefault(); document.getElementById(sectionId(s)).scrollIntoView({ behavior: "smooth", block: "start" }); } }, s));
  }
  for (const s of sections) {
    const card = el("div", { class: "card", id: sectionId(s) }, el("h2", {}, s));
    for (const f of schema.fields.filter(x => x.section === s))
      card.append(fieldRow(f, () => current[f.key], v => current[f.key] = v, f.key));
    form.append(card);
  }
  form.append(renderGroups());
}

function renderGroups() {
  const card = el("div", { class: "card", id: sectionId(t("Groupes de joueurs")) },
    el("h2", {}, t("Groupes de joueurs")),
    el("p", { class: "muted" }, t("Chaque groupe a son mot de passe : le joueur obtient les droits du groupe dont il saisit le mot de passe en rejoignant le serveur.")));
  const list = el("div", { class: "groups" });
  const groups = current.userGroups;
  groups.forEach((g, i) => {
    const box = el("div", { class: "group" },
      el("div", { class: "head" }, el("strong", {}, g.name || t("Nouveau groupe")),
        el("button", { type: "button", class: "btn small", onclick: () => { groups.splice(i, 1); updateDirty(); card.replaceWith(renderGroups()); } }, t("Supprimer"))));
    for (const f of schema.groupFields)
      box.append(fieldRow(f, () => g[f.key], v => { g[f.key] = v; if (f.key === "name") box.querySelector("strong").textContent = v || t("Nouveau groupe"); }));
    list.append(box);
  });
  card.append(list, el("div", { class: "actions" }, el("button", { type: "button", class: "btn", onclick: () => {
    groups.push({ name: "", password: "", canKickBan: false, canAccessInventories: false, canEditWorld: true, canEditBase: false, canExtendBase: false, reservedSlots: 0 });
    updateDirty();
    card.replaceWith(renderGroups());
  } }, t("Ajouter un groupe"))));
  return card;
}

$("#config-cancel").addEventListener("click", () => { current = clone(original.values); setDirty(false); renderConfig(); });

$("#config-apply").addEventListener("click", async () => {
  try {
    const check = await api("config/validate", { method: "POST", body: current });
    if (!check.ok) {
      await confirmDialog(t("Paramètres invalides"), [el("ul", {}, check.errors.map(e => el("li", {}, e)))], t("OK"));
      return;
    }
    const body = [el("p", {}, t("Le serveur va être arrêté, le monde et l'ancienne configuration sauvegardés, puis le serveur relancé avec les nouveaux paramètres. Les joueurs connectés seront déconnectés."))];
    if (check.warnings.length) body.push(el("p", {}, t("Avertissements :")), el("ul", {}, check.warnings.map(w => el("li", {}, w))));
    if (!(await confirmDialog(t("Appliquer les paramètres ?"), body, t("Appliquer et redémarrer")))) return;
    $("#config-apply").disabled = true;
    const r = await api("config", { method: "PUT", body: current });
    if (!r.ok) {
      await confirmDialog(t("Échec"), [el("ul", {}, r.errors.map(e => el("li", {}, e)))], t("OK"));
      return;
    }
    await loadConfig();
  } catch (err) {
    await confirmDialog(t("Erreur"), err.message, t("OK"));
  } finally {
    $("#config-apply").disabled = false;
  }
});

window.addEventListener("beforeunload", e => { if (dirty) e.preventDefault(); });

/* ---------- backups ---------- */

async function loadBackups() {
  const list = await api("backups");
  const reasons = { "avant-maj": t("Avant mise à jour"), "avant-config": t("Avant changement de paramètres"), "avant-restauration": t("Avant restauration"), "manuelle": t("Manuelle") };
  $("#backup-list").replaceChildren(...(list.length ? list.map(b => el("tr", {},
    el("td", {}, fmtDate(b.time)),
    el("td", {}, reasons[b.reason] || b.reason),
    el("td", {}, (b.sizeBytes / 1048576).toFixed(1) + t(" Mo")),
    el("td", {},
      el("button", { class: "btn small", onclick: () => download(b.name) }, t("Télécharger")), " ",
      el("button", { class: "btn small", onclick: () => restore(b) }, t("Restaurer"))))) :
    [el("tr", {}, el("td", { colspan: 4, class: "muted" }, t("Aucune sauvegarde pour l'instant.")))]));
}

async function download(name) {
  const res = await fetch("/api/backups/" + encodeURIComponent(name), { headers: { Authorization: "Bearer " + token } });
  const blob = await res.blob();
  const a = el("a", { href: URL.createObjectURL(blob), download: name });
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 5000);
}

async function restore(b) {
  const cfg = el("input", { type: "checkbox", id: "restore-cfg" });
  const ok = await confirmDialog(t("Restaurer cette sauvegarde ?"), [
    el("p", {}, t("Le monde sera remplacé par celui du {0}. L'état actuel est sauvegardé juste avant, au cas où.", fmtDate(b.time))),
    el("label", { class: "row" }, el("span", {}, t("Restaurer aussi les paramètres (enshrouded_server.json)")), cfg),
  ], t("Restaurer"));
  if (!ok) return;
  const r = await api(`backups/${encodeURIComponent(b.name)}/restore?config=${cfg.checked}`, { method: "POST" });
  await confirmDialog("Restauration", r.message, t("OK"));
  loadBackups();
}

/* ---------- tool settings ---------- */

async function loadSettings() {
  const s = await api("settings");
  $("#set-autoupdate").checked = s.autoUpdate;
  $("#set-interval").value = s.checkIntervalMinutes;
  $("#set-autorestart").checked = s.autoRestart;
  $("#set-keep").value = s.backupsToKeep;
  $("#set-lang").value = s.language;
}

$("#settings-form").addEventListener("submit", async e => {
  e.preventDefault();
  const out = $("#settings-result");
  try {
    const r = await api("settings", { method: "PUT", body: {
      checkIntervalMinutes: Number($("#set-interval").value), autoUpdate: $("#set-autoupdate").checked,
      autoRestart: $("#set-autorestart").checked, backupsToKeep: Number($("#set-keep").value), language: $("#set-lang").value } });
    if ($("#set-lang").value !== lang) return location.reload();
    out.className = "result ok"; out.textContent = r.message;
    refreshStatus();
  } catch (err) { out.className = "result err"; out.textContent = err.message; }
});

$("#password-form").addEventListener("submit", async e => {
  e.preventDefault();
  const out = $("#password-result");
  try {
    const r = await api("auth/password", { method: "POST", body: { current: $("#pw-current").value, new: $("#pw-new").value } });
    token = r.token;
    try { localStorage.setItem("em-token", token); } catch { }
    out.className = "result ok"; out.textContent = t("Mot de passe changé. Les autres sessions (navigateurs, application bureau) sont déconnectées.");
    e.target.reset();
  } catch (err) { out.className = "result err"; out.textContent = err.message; }
});

$("#logout").addEventListener("click", async () => {
  try { await api("auth/logout", { method: "POST" }); } catch { }
  location.reload();
});

/* ---------- boot ---------- */

let timer = null;
async function start() {
  await refreshStatus();
  await loadConfig();
  let tab = "dashboard";
  try { tab = localStorage.getItem("em-tab") || tab; } catch { }
  showTab(tab);
  clearInterval(timer);
  timer = setInterval(refreshStatus, 2000);
}

(async () => {
  try { lang = (await api("auth/state")).language; } catch { }
  translateDom();
  if (!token) return showLogin();
  try { await api("status"); start(); } catch { }
})();
