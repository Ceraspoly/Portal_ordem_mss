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
  node.classList.toggle("loja", artigo.loja === true);
  if (artigo.loja) node.title += " · CDU Loja";
  return node;
}

async function carregar(reset) {
  const pedido = ++state.pedido;
  if (reset) state.offset = 0;

  const params = new URLSearchParams({ q: el("pesquisa").value.trim(), familia: el("familia").value, offset: state.offset });
  if (el("so-loja").checked) params.set("loja", "true");
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
    el("estado").textContent = state.offset === 0 ? "Nenhum artigo encontrado." : `${state.offset} artigos${el("so-loja").checked ? " de loja" : ""}`;
    numerar();
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
  el("btn-historico").hidden = !state.podeReordenar;
  el("btn-historico").disabled = state.ordenar !== null;
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
    if (el("so-loja").checked) params.set("loja", "true");
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
    familia: el("familia").value,
    desempatar: false,
    previstas: 0
  };
  el("btn-desempatar").classList.remove("ativo");
  el("grelha").classList.add("a-ordenar");
  mudarVista(state.vista);
  [...el("grelha").children].forEach((n) => { n.draggable = true; });
  el("barra-ordenar").hidden = false;
  el("pesquisa").disabled = el("familia").disabled = el("mais").disabled = el("so-loja").disabled = true;
  ancora = null;
  atualizarSelecao();
  marcarAlterados();
  atualizarBotaoOrdenar();
}

function sairDeOrdenar() {
  clearTimeout(esperaPrevisao);
  state.ordenar = null;
  el("grelha").classList.remove("a-ordenar", "lista");
  [...el("grelha").children].forEach((n) => {
    n.draggable = false;
    n.classList.remove("mudou", "selecionado", "a-arrastar", "vai-mudar");
    n.querySelector(".ordem").textContent = textoOrdem(n.dataset.ordem);
  });
  el("barra-ordenar").hidden = true;
  el("pesquisa").disabled = el("familia").disabled = el("mais").disabled = el("so-loja").disabled = false;
  atualizarBotaoOrdenar();
}

// Número da posição de cada artigo na lista que está no ecrã.
function numerar() {
  [...el("grelha").children].forEach((n, i) => { n.querySelector(".posicao").textContent = i + 1; });
}

function marcarAlterados() {
  const original = state.ordenar.original.map((a) => a.codigo);
  let alterados = 0;
  [...el("grelha").children].forEach((n, i) => {
    const mudou = state.ordenar.arrastados.has(n.dataset.codigo) && n.dataset.codigo !== original[i];
    n.classList.toggle("mudou", mudou);
    if (mudou) alterados++;
  });
  state.ordenar.mudados = alterados;
  atualizarGuardar();
  numerar();
  return alterados;
}

// Quantos artigos vão mudar de CDU_MSS_ORDEM ao gravar. A desempatar conta
// o que o servidor previu (também os que não saíram do sítio).
function aGravar() {
  return state.ordenar.desempatar ? state.ordenar.previstas : state.ordenar.mudados;
}

function atualizarGuardar() {
  const n = aGravar();
  el("btn-guardar").disabled = n === 0;
  el("btn-guardar").textContent = n === 0 ? "Guardar ordem" : `Guardar ordem (${n})`;
}

// "Desempatar iguais": artigos seguidos com o mesmo valor (ex. 10 com
// "ab011") ficam "ab011a", "ab011b"… pela ordem do ecrã. Com artigos
// selecionados, só os grupos desses; sem seleção, todos os grupos.
function alternarDesempatar() {
  state.ordenar.desempatar = !state.ordenar.desempatar;
  state.ordenar.previstas = 0;
  el("btn-desempatar").classList.toggle("ativo", state.ordenar.desempatar);
  atualizarSelecao();
  atualizarGuardar();
  mostrarPrevisao();
}

// ---------- Seleção (Ctrl+clique / Shift+clique) ----------

// Artigos selecionados, pela ordem em que estão no ecrã. Com vários
// selecionados, arrastar, os botões, as setas e "A seguir a…" movem-nos
// todos juntos, como um bloco.
function selecionados() {
  return [...el("grelha").querySelectorAll(".artigo.selecionado")];
}

let ancora = null;
function selecionar(card, modo) {
  const todos = [...el("grelha").children];
  if (modo === "alternar") {
    card.classList.toggle("selecionado");
    ancora = card;
  } else if (modo === "intervalo" && ancora && ancora.isConnected) {
    const [a, b] = [todos.indexOf(ancora), todos.indexOf(card)].sort((x, y) => x - y);
    todos.forEach((n, i) => n.classList.toggle("selecionado", i >= a && i <= b));
  } else {
    todos.forEach((n) => n.classList.toggle("selecionado", n === card));
    ancora = card;
  }
  atualizarSelecao();
}

function atualizarSelecao() {
  if (!state.ordenar) return;
  const n = selecionados().length;
  el("btn-ordenar-nome").disabled = n < 2;
  if (state.ordenar.desempatar) {
    state.ordenar.info = n > 0
      ? "A desempatar os grupos de iguais dos artigos selecionados (vê a ordem nova em cada artigo). Grava com “Guardar ordem”."
      : "A desempatar todos os grupos de artigos com o mesmo valor (vê a ordem nova em cada artigo). Grava com “Guardar ordem”.";
    el("ordenar-info").textContent = state.ordenar.info;
    mostrarPrevisao();
    return;
  }
  state.ordenar.info = n > 1
    ? `${n} artigos selecionados: arrastar, os botões, as setas e “A seguir a…” movem-nos todos juntos. Esc limpa a seleção.`
    : "Arrasta, usa os botões, “A seguir a…”, ou clica num artigo e usa as setas. Ctrl+clique seleciona vários.";
  el("ordenar-info").textContent = state.ordenar.info;
}

// O bloco a mover a partir de um artigo: a seleção, se ele fizer parte
// dela; senão só ele (e passa a ser o selecionado).
function blocoDe(card) {
  if (card.classList.contains("selecionado")) return selecionados();
  selecionar(card);
  return [card];
}

function marcarArrastados(bloco) {
  bloco.forEach((c) => state.ordenar.arrastados.add(c.dataset.codigo));
}

// ---------- Reordenar (arrastar e largar) ----------

let arrastado = null;
let blocoArrastado = [];
el("grelha").addEventListener("dragstart", (e) => {
  const card = e.target.closest(".artigo");
  if (!state.ordenar || !card) return;
  arrastado = card;
  blocoArrastado = blocoDe(card);
  marcarArrastados(blocoArrastado);
  blocoArrastado.forEach((c) => c.classList.add("a-arrastar"));
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
  if (!alvo || blocoArrastado.includes(alvo)) return;
  const r = alvo.getBoundingClientRect();
  const depois = el("grelha").classList.contains("lista")
    ? e.clientY > r.top + r.height / 2
    : e.clientX > r.left + r.width / 2;
  alvo.parentNode.insertBefore(arrastado, depois ? alvo.nextSibling : alvo);
});
el("grelha").addEventListener("drop", (e) => e.preventDefault());
el("grelha").addEventListener("dragend", () => {
  if (!arrastado) return;
  // Durante o arrasto só o artigo agarrado anda; ao largar, o resto do
  // bloco junta-se a ele, pela ordem em que estavam.
  const marca = document.createComment("");
  arrastado.before(marca);
  blocoArrastado.forEach((c) => {
    marca.before(c);
    c.classList.remove("a-arrastar");
  });
  marca.remove();
  arrastado = null;
  blocoArrastado = [];
  aposMover();
});

// ---------- Mover com botões e teclado ----------

function aposMover() {
  marcarAlterados();
  mostrarPrevisao();
}

// Move o bloco (fica junto, pela ordem do ecrã) para o topo, o fim, ou uma
// posição acima/abaixo dos artigos que não fazem parte dele.
function moverBloco(bloco, onde) {
  const grelha = el("grelha");
  const todos = [...grelha.children];
  const resto = todos.filter((n) => !bloco.includes(n));
  let ref;
  if (onde === "topo") {
    ref = resto[0] || null;
  } else if (onde === "fim") {
    ref = null;
  } else if (onde === "cima") {
    const antes = todos.slice(0, todos.indexOf(bloco[0])).filter((n) => !bloco.includes(n)).pop();
    if (!antes) return;
    ref = antes;
  } else {
    const depois = todos.slice(todos.indexOf(bloco[bloco.length - 1]) + 1).find((n) => !bloco.includes(n));
    if (!depois) return;
    ref = depois.nextElementSibling;
    while (ref && bloco.includes(ref)) ref = ref.nextElementSibling;
  }
  marcarArrastados(bloco);
  bloco.forEach((c) => grelha.insertBefore(c, ref));
  bloco[0].scrollIntoView({ block: "nearest" });
  aposMover();
}

// Põe o bloco logo a seguir a outro artigo (escolhido no diálogo "A seguir a…").
function moverDepois(bloco, referencia) {
  if (bloco.includes(referencia)) return;
  marcarArrastados(bloco);
  let r = referencia;
  bloco.forEach((c) => { r.after(c); r = c; });
  bloco[0].scrollIntoView({ block: "center" });
  aposMover();
}

let aMover = [];
function abrirMover(card) {
  aMover = blocoDe(card);
  el("mover-nome").textContent = aMover.length === 1 ? card.querySelector(".nome").textContent : `${aMover.length} artigos selecionados`;
  el("lista-alvos").replaceChildren(...[...el("grelha").children]
    .filter((n) => !aMover.includes(n))
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
  const outros = [...el("grelha").children].filter((n) => !aMover.includes(n));
  const rotulo = (n) => `${n.dataset.codigo} — ${n.querySelector(".nome").textContent}`.toLowerCase();
  const exato = outros.find((n) => rotulo(n) === t || n.dataset.codigo.toLowerCase() === t);
  if (exato) return { alvo: exato };
  const parecidos = outros.filter((n) => rotulo(n).includes(t));
  if (parecidos.length === 1) return { alvo: parecidos[0] };
  return { erro: parecidos.length === 0 ? "Não encontrei esse artigo nesta lista." : `Há ${parecidos.length} artigos com esse texto. Escolhe um da lista.` };
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
  moverBloco(aMover, "topo");
});
el("mover-cancelar").addEventListener("click", () => el("dlg-mover").close());

// Ordena por nome os artigos selecionados, nos lugares que já ocupam
// (números e medidas pela ordem natural: "Vela 7 cm" antes de "Vela 20 cm").
// Só muda o ecrã: a nova ordem aparece em pré-visualização até gravar.
function ordenarSelecaoPorNome() {
  const sel = selecionados();
  if (sel.length < 2) return;
  const todos = [...el("grelha").children];
  const lugares = sel.map((c) => todos.indexOf(c));
  const nome = (c) => c.querySelector(".nome").textContent;
  const ordenados = [...sel].sort((a, b) => nome(a).localeCompare(nome(b), "pt", { numeric: true, sensitivity: "base" })
    || a.dataset.codigo.localeCompare(b.dataset.codigo));
  lugares.forEach((lugar, k) => { todos[lugar] = ordenados[k]; });
  marcarArrastados(sel);
  el("grelha").replaceChildren(...todos);
  aposMover();
}
el("btn-ordenar-nome").addEventListener("click", ordenarSelecaoPorNome);
el("btn-desempatar").addEventListener("click", alternarDesempatar);

el("grelha").addEventListener("click", (e) => {
  if (!state.ordenar) return;
  const card = e.target.closest(".artigo");
  if (!card) return;
  const botao = e.target.closest("button[data-mover]");
  if (!botao) {
    selecionar(card, e.ctrlKey || e.metaKey ? "alternar" : e.shiftKey ? "intervalo" : null);
    return;
  }
  if (botao.dataset.mover === "depois") {
    abrirMover(card);
    return;
  }
  moverBloco(blocoDe(card), botao.dataset.mover);
});

document.addEventListener("keydown", (e) => {
  if (!state.ordenar || el("dlg-mover").open || el("dlg-codigo").open) return;
  if (e.key === "Escape") {
    selecionados().forEach((n) => n.classList.remove("selecionado"));
    atualizarSelecao();
    return;
  }
  const destinos = { ArrowUp: "cima", ArrowDown: "baixo", Home: "topo", End: "fim" };
  const bloco = selecionados();
  if (!(e.key in destinos) || bloco.length === 0) return;
  e.preventDefault();
  moverBloco(bloco, destinos[e.key]);
});

function pedidoOrdem() {
  const pedido = { original: state.ordenar.original, nova: codigosNoEcra(), arrastados: codigosAlterados(), familia: state.ordenar.familia };
  if (state.ordenar.desempatar) {
    pedido.desempatar = true;
    pedido.selecao = selecionados().map((n) => n.dataset.codigo);
  }
  return pedido;
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
    state.ordenar.previstas = alteracoes.length;
    atualizarGuardar();
    cards.forEach((n) => {
      n.classList.toggle("vai-mudar", state.ordenar.desempatar && novos.has(n.dataset.codigo));
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
    if (pedido !== pedidoPrevisao || !state.ordenar) return;
    el("ordenar-info").textContent = err.message;
    state.ordenar.previstas = 0;
    atualizarGuardar();
  }
}

function codigosAlterados() {
  return [...el("grelha").querySelectorAll(".artigo.mudou")].map((n) => n.dataset.codigo);
}

// Pede o código de gravação (só fica em memória nesta página, nunca guardado).
let codigoEscrita = "";
function pedirCodigo(erro) {
  return new Promise((resolve) => {
    const dlg = el("dlg-codigo");
    el("codigo-escrita").value = "";
    el("codigo-erro").textContent = erro || "";
    el("codigo-erro").hidden = !erro;
    const fechar = (valor) => {
      el("form-codigo").removeEventListener("submit", aoSubmeter);
      el("codigo-cancelar").removeEventListener("click", aoCancelar);
      dlg.removeEventListener("cancel", aoCancelar);
      if (dlg.open) dlg.close();
      resolve(valor);
    };
    const aoSubmeter = () => fechar(el("codigo-escrita").value);
    const aoCancelar = (e) => { e.preventDefault(); fechar(null); };
    el("form-codigo").addEventListener("submit", aoSubmeter);
    el("codigo-cancelar").addEventListener("click", aoCancelar);
    dlg.addEventListener("cancel", aoCancelar);
    dlg.showModal();
    el("codigo-escrita").focus();
  });
}

// POST que grava no Primavera: pede o código quando o servidor o exige e
// volta a pedir se estiver errado.
async function postComCodigo(url, dados) {
  let erroCodigo = "";
  for (;;) {
    if (state.pedeCodigo && !codigoEscrita) {
      const c = await pedirCodigo(erroCodigo);
      if (c === null) throw new Error("Gravação cancelada.");
      codigoEscrita = c;
    }
    const response = await fetch(url, {
      method: "POST",
      headers: { Accept: "application/json", "Content-Type": "application/json", "X-Codigo-Escrita": codigoEscrita },
      body: JSON.stringify(dados)
    });
    const type = response.headers.get("Content-Type") || "";
    const body = type.includes("application/json") ? await response.json() : null;
    if (body && body.pedeCodigo) {
      codigoEscrita = "";
      state.pedeCodigo = true;
      if (response.status === 429) throw new Error(body.error);
      erroCodigo = body.error;
      continue;
    }
    if (!response.ok || body === null) throw new Error((body && body.error) || `Erro ${response.status} ao contactar o servidor.`);
    return body;
  }
}

async function guardarOrdem() {
  marcarAlterados();
  const alterados = aGravar();
  if (alterados === 0) return;
  const destino = state.demo ? "nos dados de demonstração" : "no CDU_MSS_ORDEM do Primavera";
  if (!confirm(`Vais gravar a nova ordem de ${alterados} artigo(s) ${destino}.\n\nAntes de gravar fica guardada uma cópia com o CDU_MSS_ORDEM atual de toda a família.\n\nContinuar?`)) return;

  el("btn-guardar").disabled = true;
  el("ordenar-info").textContent = "A gravar…";
  try {
    const r = await postComCodigo("/api/ordem", pedidoOrdem());
    sairDeOrdenar();
    await carregar(true);
    el("estado").textContent = `Ordem gravada (${r.gravados} artigos). ${el("estado").textContent}`;
  } catch (err) {
    el("ordenar-info").textContent = err.message;
    el("btn-guardar").disabled = false;
  }
}

// ---------- Histórico: últimas 15 gravações, com reverter ----------

function quando(data) {
  return new Date(data).toLocaleString("pt-PT", { dateStyle: "short", timeStyle: "short" });
}

function linhaGravacao(g, ultimas) {
  const li = document.createElement("li");
  li.className = "gravacao";
  li.classList.toggle("revertida", !!g.revertidaPor);

  const titulo = document.createElement("div");
  titulo.className = "quando";
  titulo.textContent = `${quando(g.data)} · ${g.descricao}`;

  const info = document.createElement("div");
  info.className = "info";
  info.textContent = `${g.familia} · gravado de ${g.origem}`;

  const acao = document.createElement("div");
  acao.className = "acao";
  if (g.revertidaPor) {
    const revertida = ultimas.find((x) => x.id === g.revertidaPor);
    acao.append(Object.assign(document.createElement("span"), {
      className: "etiqueta",
      textContent: revertida ? `Revertida em ${quando(revertida.data)}` : "Revertida"
    }));
  } else {
    const btn = Object.assign(document.createElement("button"), { type: "button", className: "btn", textContent: "Reverter" });
    btn.addEventListener("click", () => reverter(g, btn));
    acao.append(btn);
  }

  const detalhes = document.createElement("details");
  detalhes.append(Object.assign(document.createElement("summary"), { textContent: `Ver os ${g.alteracoes.length} artigo(s)` }));
  const ul = document.createElement("ul");
  g.alteracoes.forEach((a) => {
    ul.append(Object.assign(document.createElement("li"), { textContent: `${a.codigo}: ${a.anterior || "(vazio)"} → ${a.novo}` }));
  });
  detalhes.append(ul);

  li.append(titulo, acao, info, detalhes);
  return li;
}

async function abrirHistorico() {
  el("historico-estado").textContent = "A carregar…";
  el("historico-lista").replaceChildren();
  if (!el("dlg-historico").open) el("dlg-historico").showModal();
  try {
    const ultimas = await fetchJson("/api/historico");
    el("historico-estado").textContent = ultimas.length === 0
      ? "Ainda não há gravações."
      : "Reverter põe os artigos dessa gravação com a ordem que tinham antes. Se algum foi mudado depois, reverte primeiro as mais recentes.";
    el("historico-lista").replaceChildren(...ultimas.map((g) => linhaGravacao(g, ultimas)));
  } catch (err) {
    el("historico-estado").textContent = err.message;
  }
}

async function reverter(g, btn) {
  if (!confirm(`Reverter a gravação de ${quando(g.data)}?\n\n${g.alteracoes.length} artigo(s) voltam à ordem que tinham antes. Fica guardada uma cópia antes.`)) return;
  btn.disabled = true;
  el("historico-estado").textContent = "A reverter…";
  try {
    const r = await postComCodigo(`/api/historico/${encodeURIComponent(g.id)}/reverter`, {});
    await abrirHistorico();
    el("historico-estado").textContent = `Revertido (${r.gravados} artigos).`;
    carregar(true);
  } catch (err) {
    el("historico-estado").textContent = err.message;
    btn.disabled = false;
  }
}

el("btn-historico").addEventListener("click", abrirHistorico);
el("historico-fechar").addEventListener("click", () => el("dlg-historico").close());

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
    state.pedeCodigo = config.pedeCodigo === true;
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
  el("so-loja").addEventListener("change", () => carregar(true));
  el("mais").addEventListener("click", () => carregar(false));

  await carregar(true);
}

iniciar();
