const state = {
  associations: [],
  query: "",
  category: "All",
};

const pending = new Map();
const host = window.chrome?.webview;

const elements = {
  groups: document.querySelector("#fileGroups"),
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

if (host) {
  host.addEventListener("message", (event) => {
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
}

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
    state.associations = host ? await callHost("associations:list") : sampleAssociations();
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
  const groups = groupAssociations(state.associations);
  const filterOptions = ["All", "Needs attention", ...groups.map((group) => group.category)];

  elements.filters.innerHTML = filterOptions
    .map((filter) => {
      const active = filter === state.category;
      const classes = active
        ? "rounded-md border border-blue-200 bg-blue-50 px-3 py-1.5 text-sm font-medium text-blue-700"
        : "rounded-md border border-slate-200 bg-white px-3 py-1.5 text-sm font-medium text-slate-600 transition hover:bg-slate-50";

      return `<button class="${classes}" aria-pressed="${active}" data-category="${escapeHtml(filter)}">${escapeHtml(filter)}</button>`;
    })
    .join("");

  elements.filters.querySelectorAll("button").forEach((button) => {
    button.addEventListener("click", () => {
      state.category = button.dataset.category;
      render();
    });
  });

  const visible = groups.filter(matchesGroupFilters);
  elements.groups.innerHTML = visible.map(renderGroup).join("");
  elements.empty.hidden = visible.length > 0;

  elements.groups.querySelectorAll("[data-open-extension]").forEach((button) => {
    button.addEventListener("click", () => callHost("settings:openExtension", { extension: button.dataset.openExtension }));
  });

  elements.groups.querySelectorAll("[data-open-defaults]").forEach((button) => {
    button.addEventListener("click", () => callHost("settings:openDefaultApps"));
  });

  elements.totalCount.textContent = groups.length;
  elements.userChoiceCount.textContent = groups.filter((group) => group.status === "Consistent").length;
  elements.unknownCount.textContent = groups.filter((group) => group.status !== "Consistent").length;
}

function groupAssociations(items) {
  const byCategory = new Map();

  for (const item of items) {
    const category = item.category || "Other";
    if (!byCategory.has(category)) {
      byCategory.set(category, []);
    }

    byCategory.get(category).push(item);
  }

  return [...byCategory.entries()]
    .map(([category, categoryItems]) => {
      const sorted = categoryItems.slice().sort((a, b) => a.extension.localeCompare(b.extension));
      const appCounts = new Map();

      for (const item of sorted) {
        const app = displayAppName(item);
        appCounts.set(app, (appCounts.get(app) || 0) + 1);
      }

      const apps = [...appCounts.entries()].sort((a, b) => b[1] - a[1]);
      const unknownCount = sorted.filter((item) => item.source === "Unknown" || !item.progId).length;
      const appCount = apps.length;
      const mainApp = apps[0]?.[0] || "No default app";
      const status = unknownCount > 0 ? "Needs default" : appCount > 1 ? "Mixed apps" : "Consistent";

      return {
        category,
        title: categoryTitle(category),
        description: categoryDescription(category),
        items: sorted,
        appCount,
        mainApp,
        status,
        unknownCount,
      };
    })
    .sort((a, b) => a.title.localeCompare(b.title));
}

function matchesGroupFilters(group) {
  const inCategory =
    state.category === "All" ||
    group.category === state.category ||
    (state.category === "Needs attention" && group.status !== "Consistent");

  const text = [
    group.title,
    group.description,
    group.category,
    group.mainApp,
    group.status,
    ...group.items.flatMap((item) => [
      item.extension,
      item.description,
      item.progId,
      item.friendlyName,
      item.source,
    ]),
  ].join(" ").toLowerCase();

  return inCategory && (!state.query || text.includes(state.query));
}

function renderGroup(group) {
  const status = groupStatusDisplay(group);
  const appsLine =
    group.appCount === 1
      ? `All ${group.items.length} file types open with ${group.mainApp}.`
      : `${group.items.length} file types use ${group.appCount} different apps.`;

  return `
    <article class="overflow-hidden rounded-lg border border-slate-200 bg-white shadow-sm">
      <div class="flex flex-col justify-between gap-4 border-b border-slate-200 px-5 py-4 lg:flex-row lg:items-start">
        <div>
          <div class="flex flex-wrap items-center gap-2">
            <h2 class="text-lg font-semibold text-slate-900">${escapeHtml(group.title)}</h2>
            <span class="${status.classes}">${status.label}</span>
          </div>
          <p class="mt-1 text-sm text-slate-500">${escapeHtml(group.description)}</p>
          <p class="mt-2 text-sm font-medium text-slate-700">${escapeHtml(appsLine)}</p>
        </div>
        <button class="btn-secondary shrink-0" data-open-defaults>Open Windows settings</button>
      </div>
      <div class="divide-y divide-slate-100">
        ${group.items.map(renderFileType).join("")}
      </div>
    </article>
  `;
}

function renderFileType(item) {
  const name = escapeHtml(displayAppName(item));
  const progId = escapeHtml(item.progId || "No ProgID found");
  const source = sourceDisplay(item.source);

  return `
    <div class="grid gap-3 px-5 py-3 transition hover:bg-slate-50 md:grid-cols-[minmax(220px,1fr)_minmax(240px,1.2fr)_150px] md:items-center">
      <div class="flex items-center gap-3">
        <span class="grid h-9 min-w-14 place-items-center rounded-md bg-slate-100 px-2 font-mono text-sm font-semibold text-blue-700">${escapeHtml(item.extension)}</span>
        <div>
          <div class="font-medium text-slate-900">${escapeHtml(item.description)}</div>
          <div class="text-xs text-slate-500">${escapeHtml(item.category)}</div>
        </div>
      </div>
      <div>
        <div class="font-medium text-slate-900">${name}</div>
        <div class="mt-0.5 font-mono text-xs text-slate-500">${progId}</div>
      </div>
      <div class="flex items-center justify-between gap-2 md:justify-end">
        <span class="${source.classes}">${source.label}</span>
        <button class="rounded-md border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-blue-700 transition hover:bg-blue-50" data-open-extension="${escapeHtml(item.extension)}">Change</button>
      </div>
    </div>
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

function displayAppName(item) {
  return item.friendlyName || item.progId || "No default app";
}

function categoryTitle(category) {
  return {
    Archive: "Compressed files",
    Audio: "Music and audio",
    Code: "Code and scripts",
    Document: "Documents",
    Image: "Photos and images",
    Video: "Videos",
    Web: "Web pages",
  }[category] || category;
}

function categoryDescription(category) {
  return {
    Archive: "Zip, 7-Zip, and other packaged files.",
    Audio: "Songs, recordings, and sound files.",
    Code: "Developer files that usually open in an editor.",
    Document: "Office, PDF, text, and writing files.",
    Image: "Pictures, screenshots, and image formats.",
    Video: "Movies, clips, and screen recordings.",
    Web: "HTML files and pages saved from the web.",
  }[category] || "Related file types on this PC.";
}

function groupStatusDisplay(group) {
  if (group.status === "Needs default") {
    return {
      label: `${group.unknownCount} missing default`,
      classes: "inline-flex rounded-md border border-amber-200 bg-amber-50 px-2.5 py-1 text-xs font-medium text-amber-700",
    };
  }

  if (group.status === "Mixed apps") {
    return {
      label: "Uses multiple apps",
      classes: "inline-flex rounded-md border border-sky-200 bg-sky-50 px-2.5 py-1 text-xs font-medium text-sky-700",
    };
  }

  return {
    label: "Consistent",
    classes: "inline-flex rounded-md border border-emerald-200 bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-700",
  };
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
  if (!host) {
    return Promise.reject(new Error("OpenWith Manager is not connected to the Windows host."));
  }

  const id = crypto.randomUUID();

  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    host.postMessage({ id, action, payload });
  });
}

function sampleAssociations() {
  return [
    { extension: ".pdf", category: "Document", description: "PDF document", progId: "AcroExch.Document", friendlyName: "Adobe Acrobat", source: "UserChoice" },
    { extension: ".docx", category: "Document", description: "Word document", progId: "Word.Document.12", friendlyName: "Microsoft Word", source: "UserChoice" },
    { extension: ".txt", category: "Document", description: "Plain text", progId: "txtfile", friendlyName: "Notepad", source: "Registry" },
    { extension: ".jpg", category: "Image", description: "JPEG image", progId: "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc", friendlyName: "Photos", source: "UserChoice" },
    { extension: ".png", category: "Image", description: "PNG image", progId: "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc", friendlyName: "Photos", source: "UserChoice" },
    { extension: ".webp", category: "Image", description: "WebP image", progId: "ChromeHTML", friendlyName: "Google Chrome", source: "UserChoice" },
    { extension: ".mp4", category: "Video", description: "MP4 video", progId: "VLC.mp4", friendlyName: "VLC media player", source: "UserChoice" },
    { extension: ".mkv", category: "Video", description: "Matroska video", progId: null, friendlyName: null, source: "Unknown" },
    { extension: ".js", category: "Code", description: "JavaScript file", progId: "VSCode.js", friendlyName: "Visual Studio Code", source: "UserChoice" },
    { extension: ".cs", category: "Code", description: "C# source file", progId: "VSCode.cs", friendlyName: "Visual Studio Code", source: "UserChoice" },
  ];
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
