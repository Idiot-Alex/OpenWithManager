const state = {
  associations: [],
  query: "",
  category: "All",
};

const pending = new Map();

const elements = {
  rows: document.querySelector("#associationRows"),
  empty: document.querySelector("#emptyState"),
  filters: document.querySelector("#categoryFilters"),
  search: document.querySelector("#searchInput"),
  totalCount: document.querySelector("#totalCount"),
  userChoiceCount: document.querySelector("#userChoiceCount"),
  unknownCount: document.querySelector("#unknownCount"),
  toast: document.querySelector("#toast"),
  importDialog: document.querySelector("#importDialog"),
  importSummary: document.querySelector("#importSummary"),
  diffRows: document.querySelector("#diffRows"),
};

window.chrome.webview.addEventListener("message", (event) => {
  const response = event.data;
  const callbacks = pending.get(response.id);
  if (!callbacks) return;

  pending.delete(response.id);
  if (response.ok) {
    callbacks.resolve(response.data);
  } else {
    callbacks.reject(new Error(response.error || "Unknown bridge error"));
  }
});

document.querySelector("#refreshButton").addEventListener("click", loadAssociations);
document.querySelector("#settingsButton").addEventListener("click", () => callHost("settings:openDefaultApps"));
document.querySelector("#exportButton").addEventListener("click", exportConfig);
document.querySelector("#importButton").addEventListener("click", importConfig);
elements.search.addEventListener("input", (event) => {
  state.query = event.target.value.trim().toLowerCase();
  render();
});

loadAssociations();

async function loadAssociations() {
  try {
    state.associations = await callHost("associations:list");
    render();
  } catch (error) {
    showToast(error.message);
  }
}

async function exportConfig() {
  try {
    const result = await callHost("config:export");
    if (!result.cancelled) {
      showToast(`Exported ${result.count} associations.`);
    }
  } catch (error) {
    showToast(error.message);
  }
}

async function importConfig() {
  try {
    const result = await callHost("config:import");
    if (result.cancelled) return;

    elements.importSummary.textContent = `Compared ${result.count} imported associations from ${result.path}. This version only compares; it does not modify Windows defaults.`;
    elements.diffRows.innerHTML = result.diff.map(renderDiff).join("");
    elements.importDialog.showModal();
  } catch (error) {
    showToast(error.message);
  }
}

function render() {
  const categories = ["All", ...new Set(state.associations.map((item) => item.category))];
  elements.filters.innerHTML = categories
    .map((category) => `<button class="filter" aria-pressed="${category === state.category}" data-category="${category}">${category}</button>`)
    .join("");

  elements.filters.querySelectorAll("button").forEach((button) => {
    button.addEventListener("click", () => {
      state.category = button.dataset.category;
      render();
    });
  });

  const visible = state.associations.filter(matchesFilters);
  elements.rows.innerHTML = visible.map(renderRow).join("");
  elements.empty.hidden = visible.length > 0;

  elements.rows.querySelectorAll("[data-open-extension]").forEach((button) => {
    button.addEventListener("click", () => callHost("settings:openExtension", { extension: button.dataset.openExtension }));
  });

  elements.totalCount.textContent = state.associations.length;
  elements.userChoiceCount.textContent = state.associations.filter((item) => item.source === "UserChoice").length;
  elements.unknownCount.textContent = state.associations.filter((item) => item.source === "Unknown").length;
}

function matchesFilters(item) {
  const inCategory = state.category === "All" || item.category === state.category;
  const text = [
    item.extension,
    item.category,
    item.description,
    item.progId,
    item.friendlyName,
    item.source,
  ].join(" ").toLowerCase();

  return inCategory && (!state.query || text.includes(state.query));
}

function renderRow(item) {
  const name = escapeHtml(item.friendlyName || "Unknown application");
  const progId = escapeHtml(item.progId || "No ProgID found");

  return `
    <tr>
      <td><span class="extension">${escapeHtml(item.extension)}</span><div class="muted">${escapeHtml(item.description)}</div></td>
      <td>${escapeHtml(item.category)}</td>
      <td><strong>${name}</strong><div class="muted">${progId}</div></td>
      <td><span class="source ${escapeHtml(item.source)}">${escapeHtml(item.source)}</span></td>
      <td><button class="rowAction" data-open-extension="${escapeHtml(item.extension)}">Manage</button></td>
    </tr>
  `;
}

function renderDiff(item) {
  return `
    <div class="diffItem">
      <strong>${escapeHtml(item.extension)}</strong>
      <div>
        <div class="muted">Current: ${escapeHtml(item.currentProgId || "none")}</div>
        <div class="muted">Imported: ${escapeHtml(item.importedProgId || "none")}</div>
      </div>
      <span class="source ${item.status === "Different" ? "Unknown" : ""}">${escapeHtml(item.status)}</span>
    </div>
  `;
}

function callHost(action, payload = {}) {
  const id = crypto.randomUUID();

  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    window.chrome.webview.postMessage({ id, action, payload });
  });
}

function showToast(message) {
  elements.toast.textContent = message;
  elements.toast.hidden = false;
  window.clearTimeout(showToast.timeout);
  showToast.timeout = window.setTimeout(() => {
    elements.toast.hidden = true;
  }, 3600);
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}
