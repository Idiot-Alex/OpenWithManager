const previewFileKinds = [
  makePreviewKind("images", "Photos and images", "Images", "Pictures, screenshots, and image assets.", [".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"], "Photos", "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc", "Mixed", [
    previewItem(".jpg", "JPEG image", "Photos", "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc"),
    previewItem(".jpeg", "JPEG image", "Photos", "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc"),
    previewItem(".png", "PNG image", "Photos", "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc"),
    previewItem(".gif", "GIF image", "Photos", "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc"),
    previewItem(".webp", "WebP image", "Google Chrome", "ChromeHTML"),
    previewItem(".svg", "SVG image", "Visual Studio Code", "VSCode.svg"),
  ]),
  makePreviewKind("videos", "Videos", "Videos", "Movies, clips, and screen recordings.", [".mp4", ".mov", ".mkv"], "VLC media player", "VLC.mp4", "Consistent", [
    previewItem(".mp4", "MP4 video", "VLC media player", "VLC.mp4"),
    previewItem(".mov", "QuickTime video", "VLC media player", "VLC.mov"),
    previewItem(".mkv", "Matroska video", "VLC media player", "VLC.mkv"),
  ]),
  makePreviewKind("music", "Music and audio", "Audio", "Songs, recordings, and sound files.", [".mp3", ".wav"], "Groove Music", "AppXqj98qxeaynz6dv4459ayz6bnqxbyaqcs", "Consistent", [
    previewItem(".mp3", "MP3 audio", "Groove Music", "AppXqj98qxeaynz6dv4459ayz6bnqxbyaqcs"),
    previewItem(".wav", "WAV audio", "Groove Music", "AppXqj98qxeaynz6dv4459ayz6bnqxbyaqcs"),
  ]),
  makePreviewKind("pdf", "PDF documents", "PDF", "Portable documents and forms.", [".pdf"], "Adobe Acrobat", "AcroExch.Document", "Consistent", [
    previewItem(".pdf", "PDF document", "Adobe Acrobat", "AcroExch.Document"),
  ]),
  makePreviewKind("word", "Word documents", "Word", "Microsoft Word documents.", [".docx"], "Microsoft Word", "Word.Document.12", "Consistent", [
    previewItem(".docx", "Word document", "Microsoft Word", "Word.Document.12"),
  ]),
  makePreviewKind("spreadsheets", "Spreadsheets", "Sheet", "Excel workbooks and spreadsheet files.", [".xlsx"], "Microsoft Excel", "Excel.Sheet.12", "Consistent", [
    previewItem(".xlsx", "Excel workbook", "Microsoft Excel", "Excel.Sheet.12"),
  ]),
  makePreviewKind("presentations", "Presentations", "Deck", "PowerPoint presentation files.", [".pptx"], "Microsoft PowerPoint", "PowerPoint.Show.12", "Consistent", [
    previewItem(".pptx", "PowerPoint presentation", "Microsoft PowerPoint", "PowerPoint.Show.12"),
  ]),
  makePreviewKind("notes", "Text and notes", "Text", "Plain text and Markdown notes.", [".txt", ".md"], "Visual Studio Code", "VSCode.md", "Mixed", [
    previewItem(".txt", "Plain text", "Notepad", "txtfile", "Registry"),
    previewItem(".md", "Markdown", "Visual Studio Code", "VSCode.md"),
  ]),
  makePreviewKind("archives", "Compressed files", "Archives", "Zip, 7-Zip, and other packaged files.", [".zip", ".rar", ".7z"], "File Explorer", "CompressedFolder", "Mixed", [
    previewItem(".zip", "ZIP archive", "File Explorer", "CompressedFolder", "Registry"),
    previewItem(".rar", "RAR archive", "WinRAR", "WinRAR"),
    previewItem(".7z", "7-Zip archive", "7-Zip File Manager", "7-Zip.7z"),
  ]),
  makePreviewKind("code", "Code files", "Code", "Developer files that usually open in an editor.", [".json", ".js", ".ts", ".cs", ".py"], "Visual Studio Code", "VSCode.js", "Consistent", [
    previewItem(".json", "JSON file", "Visual Studio Code", "VSCode.json"),
    previewItem(".js", "JavaScript file", "Visual Studio Code", "VSCode.js"),
    previewItem(".ts", "TypeScript file", "Visual Studio Code", "VSCode.ts"),
    previewItem(".cs", "C# source file", "Visual Studio Code", "VSCode.cs"),
    previewItem(".py", "Python source file", "VSCode.py"),
  ]),
  makePreviewKind("web", "Web pages", "Web", "HTML files and pages saved from the web.", [".html", ".htm"], "Google Chrome", "ChromeHTML", "Consistent", [
    previewItem(".html", "HTML document", "Google Chrome", "ChromeHTML"),
    previewItem(".htm", "HTML document", "Google Chrome", "ChromeHTML"),
  ]),
];

const listeners = new Set();

window.chrome = {
  webview: {
    addEventListener(type, listener) {
      if (type === "message") {
        listeners.add(listener);
      }
    },
    postMessage(request) {
      window.setTimeout(() => {
        try {
          dispatchPreviewResponse(request.id, true, handlePreviewRequest(request), null);
        } catch (error) {
          dispatchPreviewResponse(request.id, false, null, error.message);
        }
      }, 160);
    },
  },
};

function handlePreviewRequest(request) {
  if (request.action === "fileKinds:list") {
    return previewFileKinds;
  }

  if (request.action === "formats:candidates") {
    return previewFormatCandidates(request.payload.extension);
  }

  if (request.action === "settings:openDefaultApps" || request.action === "settings:openExtension") {
    return { opened: true };
  }

  if (request.action === "config:export" || request.action === "config:import") {
    return { cancelled: true };
  }

  throw new Error(`Unknown preview action: ${request.action}`);
}

function dispatchPreviewResponse(id, ok, data, error) {
  listeners.forEach((listener) => listener({ data: { id, ok, data, error } }));
}

function previewFormatCandidates(extension) {
  const kind = previewFileKinds.find((item) => (item.extensions || []).includes(extension));
  const format = kind?.items?.find((item) => item.extension === extension);
  const current = format
    ? previewCandidate(format.friendlyName || format.progId || "No default app", format.progId, "Current", true)
    : null;
  const candidates = [
    current,
    ...previewCandidatePool(extension),
    ...(kind?.items || []).map((item) => previewCandidate(item.friendlyName || item.progId, item.progId, "OpenWithProgids")),
  ].filter(Boolean);

  const distinctCandidates = [];
  const seen = new Set();
  candidates.forEach((candidate) => {
    const key = candidate.progId ? `prog:${candidate.progId}` : `app:${candidate.appName}`;
    if (!seen.has(key)) {
      seen.add(key);
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

function previewCandidatePool(extension) {
  if ([".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg"].includes(extension)) {
    return [
      previewCandidate("Photos", "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc", "RegisteredApplication"),
      previewCandidate("Google Chrome", "ChromeHTML", "RegisteredApplication"),
      previewCandidate("Visual Studio Code", "VSCode.svg", "OpenWithProgids"),
    ];
  }

  if ([".txt", ".md", ".json", ".js", ".ts", ".cs", ".py"].includes(extension)) {
    return [
      previewCandidate("Visual Studio Code", "VSCode.js", "RegisteredApplication"),
      previewCandidate("Notepad", "txtfile", "OpenWithList"),
    ];
  }

  if (extension === ".pdf") {
    return [
      previewCandidate("Adobe Acrobat", "AcroExch.Document", "RegisteredApplication"),
      previewCandidate("Microsoft Edge", "MSEdgePDF", "OpenWithProgids"),
    ];
  }

  return [];
}

function previewCandidate(appName, progId, source, isCurrent = false) {
  return {
    appName,
    progId,
    iconDataUrl: null,
    source,
    isCurrent,
  };
}

function makePreviewKind(id, displayName, shortName, description, extensions, primaryAppName, primaryProgId, status, items) {
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

function previewItem(extension, description, friendlyName, progId, source = "UserChoice") {
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
