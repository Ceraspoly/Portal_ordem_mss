"use strict";

const PLACEHOLDER = "/img/placeholder.svg";
const state = { offset: 0, pageSize: 60, pedido: 0, podeReordenar: false, ordenar: null, artigos: [], vista: lerVista() };
const el = (id) => document.getElementById(id);

// Vista do modo de ordenação ("quadrados" ou "lista"), lembrada neste browser.
function lerVista() {
  try { return localStorage.getItem("ordem.vista") === "quadrados" ? "quadrados" : "lista"; } catch { return "lista"; }
}

function mudarVista(vista) {
  state.vista = vista;
  try { localStorage.setItem("ordem.vista", vista); } catch { /* sem armazenamento: só não fica lembrado */ }
  el("grelha").classList.toggle("lista", state.ordenar !== null && vista === "lista");
  document.querySelectorAll("[data-vista]").forEach((b) => b.classList.toggle("ativo", b.dataset.vista === vista));
}

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
  node.dataset.ordem = artigo.ordem || "";
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
// Só se reordena sem pesquisa: carrega-se a família inteira (ou, em "Todas
// as famílias", o catálogo inteiro), por isso a lista é a ordem real.
function podeEntrarEmOrdenar() {
  return state.podeReordenar && el("pesquisa").value.trim() === "" && state.artigos.length > 1;
}

function atualizarBotaoOrdenar() {
  const btn = el("btn-ordenar");
  btn.hidden = !state.podeReordenar;
  btn.disabled = state.ordenar !== null || !podeEntrarEmOrdenar();
  btn.title = btn.disabled && state.ordenar === null ? "Apaga o texto da pesquisa para ordenar." : "";
}

function codigosNoEcra() {
  return [...el("grelha").children].map((n) => n.dataset.codigo);
}

// Ao entrar no modo de ordenação carrega a família inteira (não só a
// primeira página) e mostra-a em lista compacta, com botões para mover.
async function entrarEmOrdenar() {
  if (!podeEntrarEmOrdenar()) return;
  el("btn-ordenar").disabled = true;
  el("estado").textContent = el("familia").value ? "A carregar a família inteira…" : "A carregar o catálogo inteiro…";
  let artigos;
  try {
    const params = new URLSearchParams({ familia: el("familia").value, todos: "true" });
    artigos = await fetchJson(`/api/artigos?${params}`);
  } catch (err) {
    el("estado").textContent = err.message;
    atualizarBotaoOrdenar();
    return;
  }

  state.pedido++; // ignora qualquer pesquisa ainda a chegar
  state.artigos = artigos;
  state.offset = artigos.length;
  el("grelha").replaceChildren(...artigos.map(buildCard));
  el("mais").hidden = true;
  el("estado").textContent = `${artigos.length} artigos`;

  state.ordenar = {
    original: artigos.map((a) => ({ codigo: a.codigo, ordem: a.ordem || "" })),
    arrastados: new Set(),
    familia: el("familia").value
  };
  el("grelha").classList.add("a-ordenar");
  mudarVista(state.vista);
  [...el("grelha").children].forEach((n) => { n.draggable = true; });
  el("barra-ordenar").hidden = false;
  el("pesquisa").disabled = el("familia").disabled = el("mais").disabled = true;
  state.ordenar.info = "Arrasta, usa os botões, “A seguir a…”, ou clica num artigo e usa as setas do teclado.";
  el("ordenar-info").textContent = state.ordenar.info;
  marcarAlterados();
  atualizarBotaoOrdenar();
}

function sairDeOrdenar() {
  clearTimeout(esperaPrevisao);
  state.ordenar = null;
  el("grelha").classList.remove("a-ordenar", "lista");
  [...el("grelha").children].forEach((n) => {
    n.draggable = false;
    n.classList.remove("mudou", "selecionado");
    n.querySelector(".ordem").textContent = textoOrdem(n.dataset.ordem);
  });
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
  // Desliza a página sozinha quando se arrasta perto do topo ou do fundo.
  const margem = 80;
  if (e.clientY < margem + 110) window.scrollBy(0, -18);
  else if (e.clientY > window.innerHeight - margem) window.scrollBy(0, 18);

  const alvo = e.target.closest(".artigo");
  if (!alvo || alvo === arrastado) return;
  const r = alvo.getBoundingClientRect();
  const depois = el("grelha").classList.contains("lista")
    ? e.clientY > r.top + r.height / 2
    : e.clientX > r.left + r.width / 2;
  alvo.parentNode.insertBefore(arrastado, depois ? alvo.nextSibling : alvo);
});
el("grelha").addEventListener("drop", (e) => e.preventDefault());
el("grelha").addEventListener("dragend", () => {
  if (!arrastado) return;
  arrastado.classList.remove("a-arrastar");
  arrastado = null;
  aposMover();
});

// ---------- Mover com botões e teclado ----------

function aposMover() {
  marcarAlterados();
  mostrarPrevisao();
}

function mover(card, destino) {
  const grelha = el("grelha");
  const itens = [...grelha.children];
  const atual = itens.indexOf(card);
  const alvo = Math.max(0, Math.min(itens.length - 1, destino));
  if (atual < 0 || alvo === atual) return;
  state.ordenar.arrastados.add(card.dataset.codigo);
  card.remove();
  const resto = [...grelha.children];
  grelha.insertBefore(card, resto[alvo] || null);
  selecionar(card);
  card.scrollIntoView({ block: "nearest" });
  aposMover();
}

// Põe o artigo logo a seguir a outro (escolhido no diálogo "A seguir a…").
function moverDepois(card, referencia) {
  if (card === referencia) return;
  state.ordenar.arrastados.add(card.dataset.codigo);
  referencia.after(card);
  selecionar(card);
  card.scrollIntoView({ block: "center" });
  aposMover();
}

let aMover = null;
function abrirMover(card) {
  aMover = card;
  el("mover-nome").textContent = card.querySelector(".nome").textContent;
  el("lista-alvos").replaceChildren(...[...el("grelha").children]
    .filter((n) => n !== card)
    .map((n) => new Option(`${n.dataset.codigo} — ${n.querySelector(".nome").textContent}`)));
  el("mover-alvo").value = "";
  el("mover-erro").hidden = true;
  el("dlg-mover").showModal();
  el("mover-alvo").focus();
}

// Aceita a opção da lista, o código exato, ou um texto que só apanhe um artigo.
function encontrarAlvo(texto) {
  const t = texto.trim().toLowerCase();
  if (!t) return { erro: "Escreve o código ou o nome do artigo." };
  const outros = [...el("grelha").children].filter((n) => n !== aMover);
  const rotulo = (n) => `${n.dataset.codigo} — ${n.querySelector(".nome").textContent}`.toLowerCase();
  const exato = outros.find((n) => rotulo(n) === t || n.dataset.codigo.toLowerCase() === t);
  if (exato) return { alvo: exato };
  const parecidos = outros.filter((n) => rotulo(n).includes(t));
  if (parecidos.length === 1) return { alvo: parecidos[0] };
  return { erro: parecidos.length === 0 ? "Não encontrei esse artigo nesta família." : `Há ${parecidos.length} artigos com esse texto. Escolhe um da lista.` };
}

el("form-mover").addEventListener("submit", (e) => {
  const r = encontrarAlvo(el("mover-alvo").value);
  if (r.erro) {
    e.preventDefault();
    el("mover-erro").textContent = r.erro;
    el("mover-erro").hidden = false;
    return;
  }
  moverDepois(aMover, r.alvo);
});
el("mover-inicio").addEventListener("click", () => {
  el("dlg-mover").close();
  mover(aMover, 0);
});
el("mover-cancelar").addEventListener("click", () => el("dlg-mover").close());

function selecionar(card) {
  el("grelha").querySelectorAll(".artigo.selecionado").forEach((n) => n.classList.remove("selecionado"));
  if (card) card.classList.add("selecionado");
}

el("grelha").addEventListener("click", (e) => {
  if (!state.ordenar) return;
  const card = e.target.closest(".artigo");
  if (!card) return;
  const botao = e.target.closest("button[data-mover]");
  if (!botao) {
    selecionar(card);
    return;
  }
  if (botao.dataset.mover === "depois") {
    abrirMover(card);
    return;
  }
  const i = [...el("grelha").children].indexOf(card);
  const destinos = { topo: 0, cima: i - 1, baixo: i + 1, fim: Infinity };
  mover(card, destinos[botao.dataset.mover]);
});

document.addEventListener("keydown", (e) => {
  if (!state.ordenar || el("dlg-mover").open) return;
  const card = el("grelha").querySelector(".artigo.selecionado");
  if (!card) return;
  const i = [...el("grelha").children].indexOf(card);
  const destinos = { ArrowUp: i - 1, ArrowDown: i + 1, Home: 0, End: Infinity };
  if (!(e.key in destinos)) return;
  e.preventDefault();
  mover(card, destinos[e.key]);
});

function pedidoOrdem() {
  return { original: state.ordenar.original, nova: codigosNoEcra(), arrastados: codigosAlterados(), familia: state.ordenar.familia };
}

function textoOrdem(ordem) {
  return ordem ? `Ordem ${ordem}` : "";
}

// Pergunta ao servidor (sem gravar) que ordem os artigos arrastados vão
// ter e mostra-a logo no cartão: "Ordem 0010 → 0030a".
let pedidoPrevisao = 0;
let esperaPrevisao;
function mostrarPrevisao() {
  clearTimeout(esperaPrevisao);
  esperaPrevisao = setTimeout(pedirPrevisao, 150);
}

async function pedirPrevisao() {
  if (!state.ordenar) return;
  const pedido = ++pedidoPrevisao;
  const cards = [...el("grelha").children];
  try {
    const alteracoes = await fetchJson("/api/ordem/previsao", { method: "POST", body: JSON.stringify(pedidoOrdem()) });
    if (pedido !== pedidoPrevisao || !state.ordenar) return;
    const novos = new Map(alteracoes.map((a) => [a.codigo, a.novo]));
    cards.forEach((n) => {
      const ordem = n.querySelector(".ordem");
      const novo = novos.get(n.dataset.codigo);
      if (novo === undefined) {
        ordem.textContent = textoOrdem(n.dataset.ordem);
      } else {
        ordem.replaceChildren(`Ordem ${n.dataset.ordem || "(vazio)"} → `, Object.assign(document.createElement("strong"), { textContent: novo }));
      }
    });
    el("ordenar-info").textContent = state.ordenar.info;
  } catch (err) {
    if (pedido === pedidoPrevisao) el("ordenar-info").textContent = err.message;
  }
}

function codigosAlterados() {
  return [...el("grelha").querySelectorAll(".artigo.mudou")].map((n) => n.dataset.codigo);
}

async function guardarOrdem() {
  const alterados = marcarAlterados();
  if (alterados === 0) return;
  const destino = state.demo ? "nos dados de demonstração" : "no CDU_MSS_ORDEM do Primavera";
  if (!confirm(`Vais gravar a nova ordem de ${alterados} artigo(s) ${destino}.\n\nAntes de gravar fica guardada uma cópia com o CDU_MSS_ORDEM atual de toda a família.\n\nContinuar?`)) return;

  el("btn-guardar").disabled = true;
  el("ordenar-info").textContent = "A gravar…";
  try {
    const r = await fetchJson("/api/ordem", {
      method: "POST",
      body: JSON.stringify(pedidoOrdem())
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
document.querySelectorAll("[data-vista]").forEach((b) => b.addEventListener("click", () => mudarVista(b.dataset.vista)));
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
