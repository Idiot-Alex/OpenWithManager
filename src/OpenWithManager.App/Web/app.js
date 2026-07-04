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
      showToast(`Exported ${result.count} file types.`);
    }
  } catch (error) {
    showToast(error.message);
  }
}

async function importConfig() {
  try {
    const result = await callHost("config:import");
    if (result.cancelled) return;

    elements.importSummary.textContent = `Compared ${result.count} imported file types from ${result.path}. This app only compares snapshots; Windows Settings still controls the default app changes.`;
    elements.diffRows.innerHTML = result.diff.map(renderDiff).join("");
    elements.importDialog.showModal();
  } catch (error) {
    showToast(error.message);
  }
}

function render() {
  const categories = ["All", ...new Set(state.associations.map((item) => item.category))];
  elements.filters.innerHTML = categories
    .map((category) => {
      const active = category === state.category;
      const classes = active
        ? "rounded-md border border-blue-200 bg-blue-50 px-3 py-1.5 text-sm font-medium text-blue-700"
        : "rounded-md border border-slate-200 bg-white px-3 py-1.5 text-sm font-medium text-slate-600 transition hover:bg-slate-50";

      return `<button class="${classes}" aria-pressed="${active}" data-category="${escapeHtml(category)}">${escapeHtml(category)}</button>`;
    })
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
  const source = sourceDisplay(item.source);

  return `
    <tr class="transition hover:bg-slate-50">
      <td class="px-5 py-4 align-middle">
        <div class="flex items-center gap-3">
          <span class="grid h-10 min-w-14 place-items-center rounded-md bg-slate-100 px-2 font-mono text-sm font-semibold text-blue-700">${escapeHtml(item.extension)}</span>
          <div>
            <div class="font-medium text-slate-900">${escapeHtml(item.description)}</div>
            <div class="text-sm text-slate-500">${escapeHtml(item.category)}</div>
          </div>
        </div>
      </td>
      <td class="px-5 py-4 align-middle">
        <div class="font-medium text-slate-900">${name}</div>
        <div class="mt-0.5 font-mono text-xs text-slate-500">${progId}</div>
      </td>
      <td class="px-5 py-4 align-middle">
        <span class="${source.classes}">${source.label}</span>
      </td>
      <td class="px-5 py-4 text-right align-middle">
        <button class="rounded-md border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-blue-700 transition hover:bg-blue-50" data-open-extension="${escapeHtml(item.extension)}">Open settings</button>
      </td>
    </tr>
  `;
}

function renderDiff(item) {
  const status = diffStatusDisplay(item.status);

  return `
    <div class="grid grid-cols-[90px_1fr_130px] items-center gap-3 rounded-md border border-slate-200 bg-slate-50 px-4 py-3">
      <strong class="font-mono text-sm text-blue-700">${escapeHtml(item.extension)}</strong>
      <div>
        <div class="text-sm text-slate-500">Current: ${escapeHtml(item.currentProgId || "none")}</div>
        <div class="text-sm text-slate-500">Imported: ${escapeHtml(item.importedProgId || "none")}</div>
      </div>
      <span class="${status.classes}">${status.label}</span>
    </div>
  `;
}

function sourceDisplay(source) {
  if (source === "UserChoice") {
    return {
      label: "Set by you",
      classes: "inline-flex rounded-md border border-blue-200 bg-blue-50 px-2.5 py-1 text-xs font-medium text-blue-700",
    };
  }

  if (source === "Unknown") {
    return {
      label: "Not found",
      classes: "inline-flex rounded-md border border-amber-200 bg-amber-50 px-2.5 py-1 text-xs font-medium text-amber-700",
    };
  }

  return {
    label: "System default",
    classes: "inline-flex rounded-md border border-slate-200 bg-slate-50 px-2.5 py-1 text-xs font-medium text-slate-600",
  };
}

function diffStatusDisplay(status) {
  if (status === "Different") {
    return {
      label: "Changed",
      classes: "inline-flex justify-center rounded-md border border-amber-200 bg-amber-50 px-2.5 py-1 text-xs font-medium text-amber-700",
    };
  }

  if (status === "Missing locally") {
    return {
      label: "Missing here",
      classes: "inline-flex justify-center rounded-md border border-slate-200 bg-white px-2.5 py-1 text-xs font-medium text-slate-600",
    };
  }

  return {
    label: "Same",
    classes: "inline-flex justify-center rounded-md border border-emerald-200 bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-700",
  };
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
