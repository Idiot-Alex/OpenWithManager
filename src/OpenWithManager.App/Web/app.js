const state = {
  fileKinds: [],
  query: "",
  status: "All",
  selectedId: null,
};

const statusFilters = ["All", "Review"];
const pending = new Map();
const host = window.chrome?.webview;

const elements = {
  statusFilters: document.querySelector("#statusFilters"),
  kindList: document.querySelector("#kindList"),
  detailPanel: document.querySelector("#detailPanel"),
  empty: document.querySelector("#emptyState"),
  search: document.querySelector("#searchInput"),
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

document.querySelector("#refreshButton").addEventListener("click", loadFileKinds);
document.querySelector("#settingsButton").addEventListener("click", openDefaultSettings);
document.querySelector("#exportButton").addEventListener("click", exportConfig);
document.querySelector("#importButton").addEventListener("click", importConfig);
elements.search.addEventListener("input", (event) => {
  state.query = event.target.value.trim().toLowerCase();
  render();
});

renderFilters();
loadFileKinds();

async function loadFileKinds() {
  try {
    setLoading();
    state.fileKinds = host ? await callHost("fileKinds:list") : sampleFileKinds();
    if (!state.selectedId || !state.fileKinds.some((kind) => kind.id === state.selectedId)) {
      state.selectedId = state.fileKinds[0]?.id ?? null;
    }
    render();
  } catch (error) {
    showToast(error.message);
    state.fileKinds = host ? [] : sampleFileKinds();
    render();
  }
}

async function exportConfig() {
  try {
    const result = await callHost("config:export");
    if (!result.cancelled) {
      showToast(`Exported ${result.count} file associations.`);
    }
  } catch (error) {
    showToast(error.message);
  }
}

async function importConfig() {
  try {
    const result = await callHost("config:import");
    if (result.cancelled) return;

    elements.importSummary.textContent = `${result.count} imported associations compared with this PC.`;
    elements.diffRows.innerHTML = result.diff.map(renderDiff).join("");
    elements.importDialog.showModal();
  } catch (error) {
    showToast(error.message);
  }
}

function render() {
  const visible = filteredFileKinds();

  if (!visible.some((kind) => kind.id === state.selectedId)) {
    state.selectedId = visible[0]?.id ?? state.fileKinds[0]?.id ?? null;
  }

  elements.kindList.innerHTML = visible.map(renderKindRow).join("");
  elements.empty.hidden = visible.length > 0;

  elements.kindList.querySelectorAll("[data-kind-id]").forEach((button) => {
    button.addEventListener("click", () => {
      state.selectedId = button.dataset.kindId;
      render();
    });
  });

  renderDetail();
  renderFilters();
}

function renderFilters() {
  elements.statusFilters.innerHTML = statusFilters
    .map((status) => {
      const active = state.status === status ? " active" : "";
      const count = status === "All"
        ? state.fileKinds.length
        : state.fileKinds.filter(needsReview).length;
      return `
        <button class="filter${active}" type="button" data-status="${status}">
          <span>${escapeHtml(statusLabel(status))}</span>
          <strong>${count}</strong>
        </button>
      `;
    })
    .join("");

  elements.statusFilters.querySelectorAll("[data-status]").forEach((button) => {
    button.addEventListener("click", () => {
      state.status = button.dataset.status;
      render();
    });
  });
}

function filteredFileKinds() {
  return state.fileKinds.filter((kind) => {
    const matchesStatus = state.status === "All" || needsReview(kind);
    const text = [
      kind.displayName,
      kind.shortName,
      kind.description,
      kind.primaryAppName,
      kind.primaryProgId,
      kind.status,
      ...(kind.extensions || []),
      ...(kind.items || []).flatMap((item) => [
        item.extension,
        item.description,
        item.friendlyName,
        item.progId,
        item.source,
      ]),
    ].join(" ").toLowerCase();

    return matchesStatus && (!state.query || text.includes(state.query));
  });
}

function renderKindRow(kind) {
  const selected = kind.id === state.selectedId ? " selected" : "";
  const status = statusDisplay(kind);
  const app = kind.primaryAppName || "No default app";

  return `
    <button class="kind-row${selected}" type="button" data-kind-id="${escapeAttribute(kind.id)}">
      <span class="kind-icon">${escapeHtml(kind.shortName.slice(0, 2))}</span>
      <span class="kind-main">
        <strong>${escapeHtml(kind.displayName)}</strong>
      </span>
      <span class="kind-app">${renderAppIdentity(app, kind.primaryIconDataUrl)}</span>
      <span class="${status.classes}">${status.label}</span>
    </button>
  `;
}

function renderDetail() {
  const kind = state.fileKinds.find((item) => item.id === state.selectedId);
  if (!kind) {
    elements.detailPanel.innerHTML = `
      <div class="detail-empty">
        <h2>No file kind selected</h2>
        <p>Pick a file kind to see its current app.</p>
      </div>
    `;
    return;
  }

  const status = statusDisplay(kind);
  const app = kind.primaryAppName || "No default app";
  const outliers = kind.outliers || [];

  elements.detailPanel.innerHTML = `
    <div class="detail-head">
      <div>
        <p class="eyebrow">File kind</p>
        <h2>${escapeHtml(kind.displayName)}</h2>
      </div>
      <span class="${status.classes}">${status.label}</span>
    </div>

    <section class="answer">
      <p>${escapeHtml(openingText(kind))}</p>
      ${renderAppIdentity(app, kind.primaryIconDataUrl, true)}
    </section>

    <div class="format-block">
      <p class="section-label">Included formats</p>
      <div class="chips">
        ${(kind.extensions || []).map((extension) => `<span>${escapeHtml(extension.replace(".", "").toUpperCase())}</span>`).join("")}
      </div>
    </div>

    ${outliers.length > 0 ? renderOutliers(outliers) : ""}

    <div class="detail-actions">
      <button class="button primary" type="button" id="changeAppButton">Choose app</button>
      <button class="button secondary" type="button" id="openSettingsButton">Open default apps</button>
    </div>
    <p class="settings-hint">Windows Settings will open.</p>

    <details class="technical">
      <summary>Technical details</summary>
      <div class="technical-list">
        ${(kind.items || []).map(renderTechnicalItem).join("")}
      </div>
    </details>
  `;

  document.querySelector("#changeAppButton").addEventListener("click", openDefaultSettings);
  document.querySelector("#openSettingsButton").addEventListener("click", openDefaultSettings);
}

async function openDefaultSettings() {
  try {
    await callHost("settings:openDefaultApps");
  } catch (error) {
    showToast(error.message);
  }
}

function renderOutliers(outliers) {
  return `
    <section class="outliers">
      <p class="section-label">Exceptions</p>
      ${outliers.map((item) => `
        <div class="outlier">
          <strong>${escapeHtml(item.extension.toUpperCase())}</strong>
          <span>${escapeHtml(item.appName || "No default app")}</span>
        </div>
      `).join("")}
    </section>
  `;
}

function needsReview(kind) {
  return kind.status !== "Consistent";
}

function openingText(kind) {
  if (kind.status === "Missing") {
    return "No default app reported";
  }

  return kind.status === "Mixed"
    ? `${kind.displayName} mostly open with`
    : `${kind.displayName} open with`;
}

function exceptionLabel(kind) {
  const exceptionCount = (kind.outliers || []).length;
  if (exceptionCount === 1) {
    return "1 exception";
  }

  if (exceptionCount > 1) {
    return `${exceptionCount} exceptions`;
  }

  return "Has exceptions";
}

function renderTechnicalItem(item) {
  return `
    <div class="technical-row">
      <strong>${escapeHtml(item.extension)}</strong>
      <span>${escapeHtml(item.friendlyName || item.progId || "No default app")}</span>
      <code>${escapeHtml(item.progId || "none")}</code>
      <em>${escapeHtml(sourceLabel(item.source))}</em>
    </div>
  `;
}

function renderDiff(item) {
  const status = diffStatusDisplay(item.status);

  return `
    <div class="diff-row">
      <strong>${escapeHtml(item.extension)}</strong>
      <div>
        <span>Current: ${escapeHtml(item.currentProgId || "none")}</span>
        <span>Snapshot: ${escapeHtml(item.importedProgId || "none")}</span>
      </div>
      <span class="${status.classes}">${status.label}</span>
    </div>
  `;
}

function setLoading() {
  elements.kindList.innerHTML = Array.from({ length: 11 }, (_, index) => `
    <div class="skeleton-row" style="--delay: ${index * 60}ms"></div>
  `).join("");
  elements.detailPanel.innerHTML = `
    <div class="detail-empty">
      <h2>Reading defaults</h2>
      <p>Checking the apps Windows uses for your files.</p>
    </div>
  `;
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

function renderAppIdentity(name, iconDataUrl, large = false) {
  const initial = appInitial(name);
  const size = large ? " app-large" : "";
  const icon = iconDataUrl
    ? `<img src="${escapeAttribute(iconDataUrl)}" alt="" />`
    : `<span>${escapeHtml(initial)}</span>`;

  return `
    <span class="app-identity${size}">
      ${icon}
      <strong>${escapeHtml(name)}</strong>
    </span>
  `;
}

function statusDisplay(kind) {
  if (kind.status === "Mixed") {
    return { label: exceptionLabel(kind), classes: "status mixed" };
  }

  if (kind.status === "Missing") {
    return { label: "No app set", classes: "status missing" };
  }

  return { label: "All set", classes: "status consistent" };
}

function diffStatusDisplay(status) {
  if (status === "Different") {
    return { label: "Changed", classes: "status mixed" };
  }

  if (status === "Missing locally") {
    return { label: "Missing here", classes: "status missing" };
  }

  return { label: "Same", classes: "status consistent" };
}

function statusLabel(status) {
  return {
    All: "All",
    Review: "Needs review",
  }[status] || status;
}

function sourceLabel(source) {
  return {
    UserChoice: "Set by you",
    Registry: "System default",
    Unknown: "Not found",
  }[source] || source;
}

function appInitial(name) {
  return (name || "?").trim().charAt(0).toUpperCase() || "?";
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

function escapeAttribute(value) {
  return escapeHtml(value).replaceAll("`", "&#096;");
}

function sampleFileKinds() {
  return [
    makeSampleKind("images", "Photos and images", "Images", "Pictures, screenshots, and image assets.", [".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"], "Photos", "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc", "Mixed", [
      sampleItem(".jpg", "JPEG image", "Photos", "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc"),
      sampleItem(".jpeg", "JPEG image", "Photos", "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc"),
      sampleItem(".png", "PNG image", "Photos", "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc"),
      sampleItem(".gif", "GIF image", "Photos", "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc"),
      sampleItem(".webp", "WebP image", "Google Chrome", "ChromeHTML"),
      sampleItem(".svg", "SVG image", "Visual Studio Code", "VSCode.svg"),
    ]),
    makeSampleKind("videos", "Videos", "Videos", "Movies, clips, and screen recordings.", [".mp4", ".mov", ".mkv"], "VLC media player", "VLC.mp4", "Consistent", [
      sampleItem(".mp4", "MP4 video", "VLC media player", "VLC.mp4"),
      sampleItem(".mov", "QuickTime video", "VLC media player", "VLC.mov"),
      sampleItem(".mkv", "Matroska video", "VLC media player", "VLC.mkv"),
    ]),
    makeSampleKind("music", "Music and audio", "Audio", "Songs, recordings, and sound files.", [".mp3", ".wav"], "Groove Music", "AppXqj98qxeaynz6dv4459ayz6bnqxbyaqcs", "Consistent", [
      sampleItem(".mp3", "MP3 audio", "Groove Music", "AppXqj98qxeaynz6dv4459ayz6bnqxbyaqcs"),
      sampleItem(".wav", "WAV audio", "Groove Music", "AppXqj98qxeaynz6dv4459ayz6bnqxbyaqcs"),
    ]),
    makeSampleKind("pdf", "PDF documents", "PDF", "Portable documents and forms.", [".pdf"], "Adobe Acrobat", "AcroExch.Document", "Consistent", [
      sampleItem(".pdf", "PDF document", "Adobe Acrobat", "AcroExch.Document"),
    ]),
    makeSampleKind("word", "Word documents", "Word", "Microsoft Word documents.", [".docx"], "Microsoft Word", "Word.Document.12", "Consistent", [
      sampleItem(".docx", "Word document", "Microsoft Word", "Word.Document.12"),
    ]),
    makeSampleKind("spreadsheets", "Spreadsheets", "Sheet", "Excel workbooks and spreadsheet files.", [".xlsx"], "Microsoft Excel", "Excel.Sheet.12", "Consistent", [
      sampleItem(".xlsx", "Excel workbook", "Microsoft Excel", "Excel.Sheet.12"),
    ]),
    makeSampleKind("presentations", "Presentations", "Deck", "PowerPoint presentation files.", [".pptx"], "Microsoft PowerPoint", "PowerPoint.Show.12", "Consistent", [
      sampleItem(".pptx", "PowerPoint presentation", "Microsoft PowerPoint", "PowerPoint.Show.12"),
    ]),
    makeSampleKind("notes", "Text and notes", "Text", "Plain text and Markdown notes.", [".txt", ".md"], "Visual Studio Code", "VSCode.md", "Mixed", [
      sampleItem(".txt", "Plain text", "Notepad", "txtfile", "Registry"),
      sampleItem(".md", "Markdown", "Visual Studio Code", "VSCode.md"),
    ]),
    makeSampleKind("archives", "Compressed files", "Archives", "Zip, 7-Zip, and other packaged files.", [".zip", ".rar", ".7z"], "File Explorer", "CompressedFolder", "Mixed", [
      sampleItem(".zip", "ZIP archive", "File Explorer", "CompressedFolder", "Registry"),
      sampleItem(".rar", "RAR archive", "WinRAR", "WinRAR"),
      sampleItem(".7z", "7-Zip archive", "7-Zip File Manager", "7-Zip.7z"),
    ]),
    makeSampleKind("code", "Code files", "Code", "Developer files that usually open in an editor.", [".json", ".js", ".ts", ".cs", ".py"], "Visual Studio Code", "VSCode.js", "Consistent", [
      sampleItem(".json", "JSON file", "Visual Studio Code", "VSCode.json"),
      sampleItem(".js", "JavaScript file", "Visual Studio Code", "VSCode.js"),
      sampleItem(".ts", "TypeScript file", "Visual Studio Code", "VSCode.ts"),
      sampleItem(".cs", "C# source file", "Visual Studio Code", "VSCode.cs"),
      sampleItem(".py", "Python source file", "Visual Studio Code", "VSCode.py"),
    ]),
    makeSampleKind("web", "Web pages", "Web", "HTML files and pages saved from the web.", [".html", ".htm"], "Google Chrome", "ChromeHTML", "Consistent", [
      sampleItem(".html", "HTML document", "Google Chrome", "ChromeHTML"),
      sampleItem(".htm", "HTML document", "Google Chrome", "ChromeHTML"),
    ]),
  ];
}

function makeSampleKind(id, displayName, shortName, description, extensions, primaryAppName, primaryProgId, status, items) {
  const primaryKey = primaryAppName || "No default app";
  const matching = items.filter((item) => (item.friendlyName || item.progId || "No default app") === primaryKey);
  const outliers = items
    .filter((item) => !matching.includes(item))
    .map((item) => ({
      extension: item.extension,
      description: item.description,
      appName: item.friendlyName || item.progId || "No default app",
      progId: item.progId,
      source: item.source,
    }));

  return {
    id,
    displayName,
    shortName,
    description,
    extensions,
    primaryAppName,
    primaryProgId,
    primaryIconDataUrl: null,
    matchingFormats: matching.length,
    totalFormats: items.length,
    status,
    outliers,
    items,
  };
}

function sampleItem(extension, description, friendlyName, progId, source = "UserChoice") {
  return {
    extension,
    category: "",
    description,
    progId,
    friendlyName,
    iconDataUrl: null,
    source,
  };
}
