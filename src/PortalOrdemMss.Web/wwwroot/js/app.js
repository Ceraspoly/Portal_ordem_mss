"use strict";

const PLACEHOLDER = "/img/placeholder.svg";
const state = { offset: 0, pageSize: 60, pedido: 0, podeReordenar: false, ordenar: null, artigos: [] };
const el = (id) => document.getElementById(id);

// Só aceita JSON: um erro HTML ou uma resposta vazia viram uma mensagem legível.
async function fetchJson(url, options) {
  const response = await fetch(url, { headers: { Accept: "application/json", "Content-Type": "application/json" }, ...options });
  const type = response.headers.get("Content-Type") || "";
  const body = type.includes("application/json") ? await response.json() : null;
  if (!response.ok || body === null) {
    throw new Error((body && body.error) || `Erro ${response.status} ao contactar o servidor.`);
  }
  return body;
}

function buildCard(artigo) {
  const node = el("tpl-artigo").content.firstElementChild.cloneNode(true);
  const img = node.querySelector("img");
  img.src = artigo.imagemUrl || PLACEHOLDER;
  img.alt = artigo.nome;
  img.addEventListener("error", () => { if (!img.src.endsWith(PLACEHOLDER)) img.src = PLACEHOLDER; }, { once: true });
  node.querySelector(".nome").textContent = artigo.nome;
  node.querySelector(".familia").textContent = artigo.familiaNome || artigo.familia || "Sem família";
  node.querySelector(".ordem").textContent = artigo.ordem ? `Ordem ${artigo.ordem}` : "";
  node.title = artigo.codigo;
  node.dataset.codigo = artigo.codigo;
  return node;
}

async function carregar(reset) {
  const pedido = ++state.pedido;
  if (reset) state.offset = 0;

  const params = new URLSearchParams({ q: el("pesquisa").value.trim(), familia: el("familia").value, offset: state.offset });
  el("estado").textContent = "A carregar…";
  try {
    const artigos = await fetchJson(`/api/artigos?${params}`);
    if (pedido !== state.pedido) return; // chegou uma pesquisa mais recente

    if (reset) {
      el("grelha").replaceChildren();
      state.artigos = [];
    }
    artigos.forEach((a) => el("grelha").appendChild(buildCard(a)));
    state.artigos.push(...artigos);
    state.offset += artigos.length;
    el("mais").hidden = artigos.length < state.pageSize;
    el("estado").textContent = state.offset === 0 ? "Nenhum artigo encontrado." : `${state.offset} artigos`;
  } catch (err) {
    if (pedido === state.pedido) el("estado").textContent = err.message;
  }
  atualizarBotaoOrdenar();
}

// ---------- Reordenar (arrastar e largar) ----------

// Mostra os artigos arrastados que ficaram noutro sítio (só esses mudam de
// valor ao gravar: ficam com o CDU_MSS_ORDEM do anterior + "a").
// Só se reordena dentro de uma família e sem pesquisa: assim a lista
// carregada é um bloco seguido da ordem real e as trocas fazem sentido.
function podeEntrarEmOrdenar() {
  return state.podeReordenar && el("familia").value !== "" && el("pesquisa").value.trim() === "" && state.artigos.length > 1;
}

function atualizarBotaoOrdenar() {
  const btn = el("btn-ordenar");
  btn.hidden = !state.podeReordenar;
  btn.disabled = state.ordenar !== null || !podeEntrarEmOrdenar();
  btn.title = btn.disabled && state.ordenar === null ? "Escolhe uma família (sem pesquisa) para ordenar." : "";
}

function codigosNoEcra() {
  return [...el("grelha").children].map((n) => n.dataset.codigo);
}

function entrarEmOrdenar() {
  if (!podeEntrarEmOrdenar()) return;
  state.ordenar = { original: state.artigos.map((a) => ({ codigo: a.codigo, ordem: a.ordem || "" })), arrastados: new Set() };
  el("grelha").classList.add("a-ordenar");
  [...el("grelha").children].forEach((n) => { n.draggable = true; });
  el("barra-ordenar").hidden = false;
  el("pesquisa").disabled = el("familia").disabled = el("mais").disabled = true;
  const maisPorCarregar = !el("mais").hidden;
  el("ordenar-info").textContent = maisPorCarregar
    ? `Arrasta os artigos para a nova posição. Só os ${state.artigos.length} artigos já carregados entram na troca.`
    : "Arrasta os artigos para a nova posição.";
  marcarAlterados();
  atualizarBotaoOrdenar();
}

function sairDeOrdenar() {
  state.ordenar = null;
  el("grelha").classList.remove("a-ordenar");
  [...el("grelha").children].forEach((n) => { n.draggable = false; n.classList.remove("mudou"); });
  el("barra-ordenar").hidden = true;
  el("pesquisa").disabled = el("familia").disabled = el("mais").disabled = false;
  atualizarBotaoOrdenar();
}

function marcarAlterados() {
  const original = state.ordenar.original.map((a) => a.codigo);
  let alterados = 0;
  [...el("grelha").children].forEach((n, i) => {
    const mudou = state.ordenar.arrastados.has(n.dataset.codigo) && n.dataset.codigo !== original[i];
    n.classList.toggle("mudou", mudou);
    if (mudou) alterados++;
  });
  el("btn-guardar").disabled = alterados === 0;
  el("btn-guardar").textContent = alterados === 0 ? "Guardar ordem" : `Guardar ordem (${alterados})`;
  return alterados;
}

let arrastado = null;
el("grelha").addEventListener("dragstart", (e) => {
  const card = e.target.closest(".artigo");
  if (!state.ordenar || !card) return;
  arrastado = card;
  state.ordenar.arrastados.add(card.dataset.codigo);
  card.classList.add("a-arrastar");
  e.dataTransfer.effectAllowed = "move";
  e.dataTransfer.setData("text/plain", card.dataset.codigo);
});
el("grelha").addEventListener("dragover", (e) => {
  if (!arrastado) return;
  e.preventDefault();
  const alvo = e.target.closest(".artigo");
  if (!alvo || alvo === arrastado) return;
  const r = alvo.getBoundingClientRect();
  const depois = e.clientX > r.left + r.width / 2;
  alvo.parentNode.insertBefore(arrastado, depois ? alvo.nextSibling : alvo);
});
el("grelha").addEventListener("drop", (e) => e.preventDefault());
el("grelha").addEventListener("dragend", () => {
  if (!arrastado) return;
  arrastado.classList.remove("a-arrastar");
  arrastado = null;
  marcarAlterados();
});

function codigosAlterados() {
  return [...el("grelha").querySelectorAll(".artigo.mudou")].map((n) => n.dataset.codigo);
}

async function guardarOrdem() {
  const alterados = marcarAlterados();
  if (alterados === 0) return;
  const destino = state.demo ? "nos dados de demonstração" : "no CDU_MSS_ORDEM do Primavera";
  if (!confirm(`Vais gravar a nova ordem de ${alterados} artigo(s) ${destino}. Continuar?`)) return;

  el("btn-guardar").disabled = true;
  el("ordenar-info").textContent = "A gravar…";
  try {
    const r = await fetchJson("/api/ordem", {
      method: "POST",
      body: JSON.stringify({ original: state.ordenar.original, nova: codigosNoEcra(), arrastados: codigosAlterados() })
    });
    sairDeOrdenar();
    await carregar(true);
    el("estado").textContent = `Ordem gravada (${r.gravados} artigos). ${el("estado").textContent}`;
  } catch (err) {
    el("ordenar-info").textContent = err.message;
    el("btn-guardar").disabled = false;
  }
}

el("btn-ordenar").addEventListener("click", entrarEmOrdenar);
el("btn-cancelar").addEventListener("click", () => { sairDeOrdenar(); carregar(true); });
el("btn-guardar").addEventListener("click", guardarOrdem);

// ---------- Arranque ----------

async function iniciar() {
  try {
    const config = await fetchJson("/api/config");
    state.pageSize = config.pageSize;
    state.podeReordenar = config.podeReordenar;
    state.demo = config.demo;
    el("titulo").textContent = config.titulo;
    document.title = config.titulo;
    el("demo-badge").hidden = !config.demo;

    const familias = await fetchJson("/api/familias");
    familias.forEach((f) => el("familia").appendChild(new Option(f.nome, f.codigo)));
  } catch (err) {
    el("estado").textContent = err.message;
  }

  let debounce;
  el("pesquisa").addEventListener("input", () => {
    clearTimeout(debounce);
    debounce = setTimeout(() => carregar(true), 250);
  });
  el("familia").addEventListener("change", () => carregar(true));
  el("mais").addEventListener("click", () => carregar(false));

  await carregar(true);
}

iniciar();
