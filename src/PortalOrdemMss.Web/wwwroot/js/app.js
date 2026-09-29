"use strict";

const PLACEHOLDER = "/img/placeholder.svg";
const state = { offset: 0, pageSize: 60, pedido: 0 };
const el = (id) => document.getElementById(id);

// Só aceita JSON: um erro HTML ou uma resposta vazia viram uma mensagem legível.
async function fetchJson(url) {
  const response = await fetch(url, { headers: { Accept: "application/json" } });
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

    if (reset) el("grelha").replaceChildren();
    artigos.forEach((a) => el("grelha").appendChild(buildCard(a)));
    state.offset += artigos.length;
    el("mais").hidden = artigos.length < state.pageSize;
    el("estado").textContent = state.offset === 0 ? "Nenhum artigo encontrado." : `${state.offset} artigos`;
  } catch (err) {
    if (pedido === state.pedido) el("estado").textContent = err.message;
  }
}

async function iniciar() {
  try {
    const config = await fetchJson("/api/config");
    state.pageSize = config.pageSize;
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
