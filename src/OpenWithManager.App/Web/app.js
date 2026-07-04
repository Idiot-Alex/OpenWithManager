const i18n = window.openWithI18n;
const t = (key, params, fallback) => i18n.t(key, params, fallback);

const state = {
  fileKinds: [],
  query: "",
  status: "All",
  selectedId: null,
  selectedFormat: null,
  formatCandidates: null,
  formatLoading: false,
  selectedCandidateKey: null,
};

const statusFilters = ["All", "Review"];
const pending = new Map();
const host = window.chrome?.webview;

const elements = {
  appTitle: document.querySelector("#appTitle"),
  appTagline: document.querySelector("#appTagline"),
  searchLabel: document.querySelector(".search .sr-only"),
  actions: document.querySelector("#actions"),
  languageButton: document.querySelector("#languageButton"),
  languageLabel: document.querySelector("#languageLabel"),
  languageCurrent: document.querySelector("#languageCurrent"),
  refreshButton: document.querySelector("#refreshButton"),
  refreshButtonLabel: document.querySelector("#refreshButton .button-label"),
  exportButton: document.querySelector("#exportButton"),
  exportButtonLabel: document.querySelector("#exportButton .button-label"),
  importButton: document.querySelector("#importButton"),
  importButtonLabel: document.querySelector("#importButton .button-label"),
  settingsButton: document.querySelector("#settingsButton"),
  settingsButtonLabel: document.querySelector("#settingsButton .button-label"),
  kindListSection: document.querySelector("#kindListSection"),
  fileKindsLabel: document.querySelector("#fileKindsLabel"),
  statusFilters: document.querySelector("#statusFilters"),
  kindList: document.querySelector("#kindList"),
  detailPanel: document.querySelector("#detailPanel"),
  empty: document.querySelector("#emptyState"),
  search: document.querySelector("#searchInput"),
  toast: document.querySelector("#toast"),
  importDialog: document.querySelector("#importDialog"),
  dialogTitle: document.querySelector("#dialogTitle"),
  closeDialogButton: document.querySelector("#closeDialogButton"),
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
      callbacks.reject(new Error(response.error || t("unknownBridgeError")));
    }
  });
}

document.querySelector("#refreshButton").addEventListener("click", loadFileKinds);
document.querySelector("#settingsButton").addEventListener("click", openDefaultSettings);
document.querySelector("#exportButton").addEventListener("click", exportConfig);
document.querySelector("#importButton").addEventListener("click", importConfig);
elements.languageButton.addEventListener("click", () => {
  const nextLanguage = i18n.getLanguage() === "zh-Hans" ? "en" : "zh-Hans";
  i18n.setLanguage(nextLanguage);
  render();
});
elements.search.addEventListener("input", (event) => {
  state.query = event.target.value.trim().toLowerCase();
  render();
});

renderStaticText();
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
      showToast(t("exportToast", { count: result.count }));
    }
  } catch (error) {
    showToast(error.message);
  }
}

async function importConfig() {
  try {
    const result = await callHost("config:import");
    if (result.cancelled) return;

    elements.importSummary.textContent = t("importedSummary", { count: result.count });
    elements.diffRows.innerHTML = result.diff.map(renderDiff).join("");
    elements.importDialog.showModal();
  } catch (error) {
    showToast(error.message);
  }
}

function render() {
  renderStaticText();

  const visible = filteredFileKinds();

  if (!visible.some((kind) => kind.id === state.selectedId)) {
    state.selectedId = visible[0]?.id ?? state.fileKinds[0]?.id ?? null;
  }

  elements.kindList.innerHTML = visible.map(renderKindRow).join("");
  elements.empty.hidden = visible.length > 0;

  elements.kindList.querySelectorAll("[data-kind-id]").forEach((button) => {
    button.addEventListener("click", () => {
      state.selectedId = button.dataset.kindId;
      state.selectedFormat = null;
      state.formatCandidates = null;
      state.selectedCandidateKey = null;
      render();
    });
  });

  renderDetail();
  renderFilters();
}

function renderStaticText() {
  const language = i18n.getLanguage();
  document.documentElement.lang = language;
  document.title = t("appTitle");

  setText(elements.appTitle, t("appTitle"));
  setText(elements.appTagline, t("appTagline"));
  setText(elements.searchLabel, t("searchLabel"));
  setText(elements.languageLabel, t("language"));
  setText(elements.languageCurrent, language === "zh-Hans" ? "中" : "EN");
  setText(elements.refreshButtonLabel, t("refresh"));
  setText(elements.exportButtonLabel, t("export"));
  setText(elements.importButtonLabel, t("compare"));
  setText(elements.settingsButtonLabel, t("openSettings"));
  setText(elements.fileKindsLabel, t("fileKinds"));
  setText(elements.empty, t("emptyNoMatch"));
  setText(elements.dialogTitle, t("snapshotComparison"));
  setText(elements.closeDialogButton, t("close"));

  elements.search.placeholder = t("searchPlaceholder");
  elements.actions.setAttribute("aria-label", t("actionsLabel"));
  setButtonLabel(elements.languageButton, `${t("language")}: ${language === "zh-Hans" ? "中文" : "English"}`);
  setButtonLabel(elements.refreshButton, t("refresh"));
  setButtonLabel(elements.exportButton, t("export"));
  setButtonLabel(elements.importButton, t("compare"));
  setButtonLabel(elements.settingsButton, t("openSettings"));
  elements.kindListSection.setAttribute("aria-label", t("fileKinds"));
  elements.statusFilters.setAttribute("aria-label", t("filterFileKinds"));
  elements.detailPanel.setAttribute("aria-label", t("selectedFileKind"));

  refreshIcons();
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
      kindTitle(kind),
      kindShortName(kind),
      kindDescription(kind),
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
  const app = appName(kind.primaryAppName);

  return `
    <button class="kind-row${selected}" type="button" data-kind-id="${escapeAttribute(kind.id)}">
      <span class="kind-icon">${escapeHtml(kindShortName(kind).slice(0, 2))}</span>
      <span class="kind-main">
        <strong>${escapeHtml(kindTitle(kind))}</strong>
      </span>
      <span class="kind-app">${renderAppIdentity(app, kind.primaryIconDataUrl)}</span>
      ${renderStatus(status, true)}
    </button>
  `;
}

function renderDetail() {
  const kind = state.fileKinds.find((item) => item.id === state.selectedId);
  if (!kind) {
    elements.detailPanel.innerHTML = `
      <div class="detail-empty">
        <h2>${escapeHtml(t("noFileKindSelected"))}</h2>
        <p>${escapeHtml(t("pickFileKind"))}</p>
      </div>
    `;
    return;
  }

  const status = statusDisplay(kind);
  const app = appName(kind.primaryAppName);
  const outliers = kind.outliers || [];

  if (state.selectedFormat && !(kind.extensions || []).includes(state.selectedFormat)) {
    state.selectedFormat = null;
    state.formatCandidates = null;
    state.selectedCandidateKey = null;
  }

  if (state.selectedFormat) {
    renderFormatDetail(kind, state.selectedFormat);
    return;
  }

  elements.detailPanel.innerHTML = `
    <div class="detail-head">
      <div>
        <p class="eyebrow">${escapeHtml(t("fileKind"))}</p>
        <h2>${escapeHtml(kindTitle(kind))}</h2>
      </div>
      ${renderStatus(status)}
    </div>

    <section class="answer">
      <p>${escapeHtml(answerLabel(kind))}</p>
      ${renderAppIdentity(app, kind.primaryIconDataUrl, true)}
    </section>

    <div class="format-block">
      <p class="section-label">${escapeHtml(t("includedFormats"))}</p>
      <div class="chips">
        ${(kind.extensions || []).map(renderFormatChip).join("")}
      </div>
    </div>

    ${outliers.length > 0 ? renderOutliers(outliers) : ""}

    <div class="detail-actions">
      <button class="button primary" type="button" id="changeAppButton">
        ${icon("mouse-pointer-click")}
        <span>${escapeHtml(t("chooseApp"))}</span>
      </button>
      <button class="button secondary" type="button" id="openSettingsButton">
        ${icon("external-link")}
        <span>${escapeHtml(t("openDefaultApps"))}</span>
      </button>
    </div>
    <p class="settings-hint">${escapeHtml(t("settingsHint"))}</p>

    <details class="technical">
      <summary>${escapeHtml(t("technicalDetails"))}</summary>
      <div class="technical-list">
        ${(kind.items || []).map(renderTechnicalItem).join("")}
      </div>
    </details>
  `;

  document.querySelector("#changeAppButton").addEventListener("click", openDefaultSettings);
  document.querySelector("#openSettingsButton").addEventListener("click", openDefaultSettings);
  bindFormatChips();
  refreshIcons();
}

function renderFormatChip(extension) {
  return `
    <button class="chip" type="button" data-format-extension="${escapeAttribute(extension)}">
      ${escapeHtml(formatCode(extension))}
    </button>
  `;
}

function bindFormatChips() {
  elements.detailPanel.querySelectorAll("[data-format-extension]").forEach((button) => {
    button.addEventListener("click", () => selectFormat(button.dataset.formatExtension));
  });
}

async function selectFormat(extension) {
  state.selectedFormat = extension;
  state.formatCandidates = null;
  state.formatLoading = true;
  state.selectedCandidateKey = null;
  render();

  try {
    const result = host
      ? await callHost("formats:candidates", { extension })
      : sampleFormatCandidates(extension);

    if (state.selectedFormat !== extension) {
      return;
    }

    state.formatCandidates = result;
    state.selectedCandidateKey = candidateKey(result.current || result.candidates?.[0]);
  } catch (error) {
    showToast(error.message);
  } finally {
    if (state.selectedFormat === extension) {
      state.formatLoading = false;
      render();
    }
  }
}

function renderFormatDetail(kind, extension) {
  const item = (kind.items || []).find((format) => format.extension === extension);
  const candidates = state.formatCandidates?.candidates || [];
  const current = state.formatCandidates?.current || (item
    ? {
        appName: appName(item.friendlyName || item.progId),
        progId: item.progId,
        iconDataUrl: item.iconDataUrl,
        source: "Current",
        isCurrent: true,
      }
    : null);
  const selectedCandidate = candidates.find((candidate) => candidateKey(candidate) === state.selectedCandidateKey)
    || current
    || candidates[0];

  elements.detailPanel.innerHTML = `
    <div class="detail-head">
      <div>
        <button class="back-link" type="button" id="backToKindButton">
          ${icon("arrow-left")}
          <span>${escapeHtml(t("backToFileKind"))}</span>
        </button>
        <p class="eyebrow">${escapeHtml(t("format"))}</p>
        <h2>${escapeHtml(formatTitle(item, extension))}</h2>
      </div>
      <span class="format-code">${escapeHtml(formatCode(extension))}</span>
    </div>

    <section class="answer">
      <p>${escapeHtml(t("currentApp"))}</p>
      ${renderAppIdentity(appName(current?.appName), current?.iconDataUrl, true)}
    </section>

    <section class="candidate-block">
      <p class="section-label">${escapeHtml(t("recommendedApps"))}</p>
      ${state.formatLoading ? renderCandidateLoading() : renderCandidates(candidates)}
    </section>

    <div class="detail-actions">
      <button class="button primary" type="button" id="changeFormatButton" ${selectedCandidate ? "" : "disabled"}>
        ${icon("external-link")}
        <span>${escapeHtml(t("changeInWindows"))}</span>
      </button>
    </div>
    <p class="settings-hint">${escapeHtml(t("formatSettingsHint", {
      extension: formatCode(extension),
      app: appName(selectedCandidate?.appName),
    }))}</p>

    <details class="technical">
      <summary>${escapeHtml(t("technicalDetails"))}</summary>
      <div class="technical-list">
        ${item ? renderTechnicalItem(item) : ""}
      </div>
    </details>
  `;

  document.querySelector("#backToKindButton").addEventListener("click", () => {
    state.selectedFormat = null;
    state.formatCandidates = null;
    state.selectedCandidateKey = null;
    render();
  });

  elements.detailPanel.querySelectorAll("[data-candidate-key]").forEach((button) => {
    button.addEventListener("click", () => {
      state.selectedCandidateKey = button.dataset.candidateKey;
      render();
    });
  });

  document.querySelector("#changeFormatButton").addEventListener("click", () => openFormatSettings(extension, selectedCandidate));
  refreshIcons();
}

function renderCandidateLoading() {
  return `
    <div class="candidate-list">
      <div class="candidate-row muted">${escapeHtml(t("loadingApps"))}</div>
    </div>
  `;
}

function renderCandidates(candidates) {
  if (!candidates.length) {
    return `
      <div class="candidate-list">
        <div class="candidate-row muted">${escapeHtml(t("noCandidateApps"))}</div>
      </div>
    `;
  }

  return `
    <div class="candidate-list">
      ${candidates.map((candidate) => {
        const key = candidateKey(candidate);
        const selected = key === state.selectedCandidateKey ? " selected" : "";
        return `
          <button class="candidate-row${selected}" type="button" data-candidate-key="${escapeAttribute(key)}">
            ${renderAppIdentity(appName(candidate.appName), candidate.iconDataUrl)}
            <span class="candidate-source">${escapeHtml(candidateSourceLabel(candidate.source))}</span>
          </button>
        `;
      }).join("")}
    </div>
  `;
}

async function openFormatSettings(extension, candidate) {
  try {
    await callHost("settings:openExtension", {
      extension,
      progId: candidate?.progId,
      appName: candidate?.appName,
    });
    showToast(t("chooseInWindowsToast", {
      extension: formatCode(extension),
      app: appName(candidate?.appName),
    }));
  } catch (error) {
    showToast(error.message);
  }
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
      <p class="section-label">${escapeHtml(t("exceptions"))}</p>
      ${outliers.map((item) => `
        <div class="outlier">
          <strong>${escapeHtml(item.extension.toUpperCase())}</strong>
          <span>${escapeHtml(appName(item.appName))}</span>
        </div>
      `).join("")}
    </section>
  `;
}

function needsReview(kind) {
  return kind.status !== "Consistent";
}

function answerLabel(kind) {
  if (kind.status === "Missing") {
    return t("noDefaultAppReported");
  }

  return t("currentApp");
}

function exceptionLabel(kind) {
  const exceptionCount = (kind.outliers || []).length;
  if (exceptionCount === 1) {
    return t("oneException");
  }

  if (exceptionCount > 1) {
    return t("exceptionsCount", { count: exceptionCount });
  }

  return t("hasExceptions");
}

function renderTechnicalItem(item) {
  return `
    <div class="technical-row">
      <strong>${escapeHtml(item.extension)}</strong>
      <span>${escapeHtml(appName(item.friendlyName || item.progId))}</span>
      <code>${escapeHtml(item.progId || t("none"))}</code>
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
        <span>${escapeHtml(t("current"))}: ${escapeHtml(item.currentProgId || t("none"))}</span>
        <span>${escapeHtml(t("snapshot"))}: ${escapeHtml(item.importedProgId || t("none"))}</span>
      </div>
      ${renderStatus(status)}
    </div>
  `;
}

function setLoading() {
  elements.kindList.innerHTML = Array.from({ length: 11 }, (_, index) => `
    <div class="skeleton-row" style="--delay: ${index * 60}ms"></div>
  `).join("");
  elements.detailPanel.innerHTML = `
    <div class="detail-empty">
      <h2>${escapeHtml(t("readingDefaultsTitle"))}</h2>
      <p>${escapeHtml(t("readingDefaultsBody"))}</p>
    </div>
  `;
}

function callHost(action, payload = {}) {
  if (!host) {
    return Promise.reject(new Error(t("hostDisconnected")));
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

function renderStatus(status, compact = false) {
  if (compact && status.variant === "consistent") {
    return `
      <span class="${status.classes} status-icon" aria-label="${escapeAttribute(status.label)}" title="${escapeAttribute(status.label)}">
        ${icon("check")}
      </span>
    `;
  }

  return `<span class="${status.classes}">${escapeHtml(status.label)}</span>`;
}

function statusDisplay(kind) {
  if (kind.status === "Mixed") {
    return { label: exceptionLabel(kind), classes: "status mixed", variant: "mixed" };
  }

  if (kind.status === "Missing") {
    return { label: t("noAppSet"), classes: "status missing", variant: "missing" };
  }

  return { label: t("allSet"), classes: "status consistent", variant: "consistent" };
}

function diffStatusDisplay(status) {
  if (status === "Different") {
    return { label: t("changed"), classes: "status mixed" };
  }

  if (status === "Missing locally") {
    return { label: t("missingHere"), classes: "status missing" };
  }

  return { label: t("same"), classes: "status consistent" };
}

function statusLabel(status) {
  return {
    All: t("all"),
    Review: t("needsReview"),
  }[status] || status;
}

function sourceLabel(source) {
  return {
    UserChoice: t("setByYou"),
    Registry: t("systemDefault"),
    Unknown: t("notFound"),
  }[source] || source;
}

function candidateSourceLabel(source) {
  return {
    Current: t("current"),
    RegisteredApplication: t("recommended"),
    OpenWithProgids: t("knownForFormat"),
    OpenWithList: t("usedBefore"),
  }[source] || source;
}

function kindTitle(kind) {
  return t(`kind.${kind.id}.name`, {}, kind.displayName);
}

function kindShortName(kind) {
  return t(`kind.${kind.id}.short`, {}, kind.shortName);
}

function kindDescription(kind) {
  return t(`kind.${kind.id}.description`, {}, kind.description);
}

function appName(name) {
  return !name || name === "No default app" ? t("noDefaultApp") : name;
}

function formatTitle(item, extension) {
  return t(`format.${formatCode(extension).toLowerCase()}.name`, {}, item?.description || extension.toUpperCase());
}

function formatCode(extension) {
  return extension.replace(".", "").toUpperCase();
}

function candidateKey(candidate) {
  if (!candidate) {
    return null;
  }

  return candidate.progId ? `prog:${candidate.progId}` : `app:${candidate.appName}`;
}

function appInitial(name) {
  return (name || "?").trim().charAt(0).toUpperCase() || "?";
}

function setText(element, value) {
  if (element) {
    element.textContent = value;
  }
}

function setButtonLabel(button, label) {
  button.setAttribute("aria-label", label);
  button.title = label;
}

function icon(name) {
  return `<i data-lucide="${escapeAttribute(name)}" aria-hidden="true"></i>`;
}

function refreshIcons() {
  window.lucide?.createIcons({
    attrs: {
      "stroke-width": 1.8,
    },
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

function sampleFormatCandidates(extension) {
  const kind = state.fileKinds.find((item) => (item.extensions || []).includes(extension));
  const format = kind?.items?.find((item) => item.extension === extension);
  const current = format
    ? sampleCandidate(format.friendlyName || format.progId || t("noDefaultApp"), format.progId, "Current", true)
    : null;
  const candidates = [
    current,
    ...sampleCandidatePool(extension),
    ...(kind?.items || []).map((item) => sampleCandidate(item.friendlyName || item.progId, item.progId, "OpenWithProgids")),
  ].filter(Boolean);

  const distinctCandidates = [];
  const seenCandidateKeys = new Set();
  candidates.forEach((candidate) => {
    const key = candidateKey(candidate);
    if (key && !seenCandidateKeys.has(key)) {
      seenCandidateKeys.add(key);
      distinctCandidates.push(candidate);
    }
  });

  return {
    extension,
    description: format?.description || extension,
    current,
    candidates: distinctCandidates,
  };
}

function sampleCandidatePool(extension) {
  if ([".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"].includes(extension)) {
    return [
      sampleCandidate("Photos", "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc", "RegisteredApplication"),
      sampleCandidate("Google Chrome", "ChromeHTML", "RegisteredApplication"),
      sampleCandidate("Visual Studio Code", "VSCode.svg", "OpenWithProgids"),
    ];
  }

  if ([".txt", ".md", ".json", ".js", ".ts", ".cs", ".py"].includes(extension)) {
    return [
      sampleCandidate("Visual Studio Code", "VSCode.js", "RegisteredApplication"),
      sampleCandidate("Notepad", "txtfile", "OpenWithList"),
    ];
  }

  if (extension === ".pdf") {
    return [
      sampleCandidate("Adobe Acrobat", "AcroExch.Document", "RegisteredApplication"),
      sampleCandidate("Microsoft Edge", "MSEdgePDF", "OpenWithProgids"),
    ];
  }

  return [];
}

function sampleCandidate(appNameValue, progId, source, isCurrent = false) {
  if (!appNameValue) {
    return null;
  }

  return {
    appName: appNameValue,
    progId,
    iconDataUrl: null,
    source,
    isCurrent,
  };
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
