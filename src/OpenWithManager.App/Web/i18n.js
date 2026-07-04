(function () {
  const storageKey = "openWithManager.language";

  const messages = {
    en: {
      appTitle: "OpenWith Manager",
      appTagline: "Choose apps for file kinds.",
      searchLabel: "Search files or apps",
      searchPlaceholder: "Search files or apps",
      actionsLabel: "Actions",
      language: "Language",
      refresh: "Refresh",
      export: "Export",
      compare: "Compare",
      openSettings: "Open settings",
      fileKinds: "File kinds",
      filterFileKinds: "Filter file kinds",
      selectedFileKind: "Selected file kind",
      emptyNoMatch: "No matching file kinds.",
      all: "All",
      needsReview: "Needs review",
      noFileKindSelected: "No file kind selected",
      pickFileKind: "Pick a file kind to see its current app.",
      fileKind: "File kind",
      currentApp: "Current app",
      includedFormats: "Included formats",
      exceptions: "Exceptions",
      chooseApp: "Choose app",
      openDefaultApps: "Open default apps",
      settingsHint: "Windows Settings will open.",
      technicalDetails: "Technical details",
      noDefaultApp: "No default app",
      noDefaultAppReported: "No default app reported",
      noAppSet: "No app set",
      allSet: "All set",
      oneException: "1 exception",
      exceptionsCount: "{count} exceptions",
      hasExceptions: "Has exceptions",
      mostlyOpenWith: "{name} mostly open with",
      openWith: "{name} open with",
      readingDefaultsTitle: "Reading defaults",
      readingDefaultsBody: "Checking the apps Windows uses for your files.",
      exportToast: "Exported {count} file associations.",
      importedSummary: "{count} imported associations compared with this PC.",
      hostDisconnected: "OpenWith Manager is not connected to the Windows host.",
      unknownBridgeError: "Unknown bridge error",
      snapshotComparison: "Snapshot comparison",
      close: "Close",
      current: "Current",
      snapshot: "Snapshot",
      none: "none",
      changed: "Changed",
      missingHere: "Missing here",
      same: "Same",
      setByYou: "Set by you",
      systemDefault: "System default",
      notFound: "Not found",
      "kind.images.name": "Photos and images",
      "kind.images.short": "Images",
      "kind.images.description": "Pictures, screenshots, and image assets.",
      "kind.videos.name": "Videos",
      "kind.videos.short": "Videos",
      "kind.videos.description": "Movies, clips, and screen recordings.",
      "kind.music.name": "Music and audio",
      "kind.music.short": "Audio",
      "kind.music.description": "Songs, recordings, and sound files.",
      "kind.pdf.name": "PDF documents",
      "kind.pdf.short": "PDF",
      "kind.pdf.description": "Portable documents and forms.",
      "kind.word.name": "Word documents",
      "kind.word.short": "Word",
      "kind.word.description": "Microsoft Word documents.",
      "kind.spreadsheets.name": "Spreadsheets",
      "kind.spreadsheets.short": "Sheet",
      "kind.spreadsheets.description": "Excel workbooks and spreadsheet files.",
      "kind.presentations.name": "Presentations",
      "kind.presentations.short": "Deck",
      "kind.presentations.description": "PowerPoint presentation files.",
      "kind.notes.name": "Text and notes",
      "kind.notes.short": "Text",
      "kind.notes.description": "Plain text and Markdown notes.",
      "kind.archives.name": "Compressed files",
      "kind.archives.short": "Archives",
      "kind.archives.description": "Zip, 7-Zip, and other packaged files.",
      "kind.code.name": "Code files",
      "kind.code.short": "Code",
      "kind.code.description": "Developer files that usually open in an editor.",
      "kind.web.name": "Web pages",
      "kind.web.short": "Web",
      "kind.web.description": "HTML files and pages saved from the web.",
    },
    "zh-Hans": {
      appTitle: "OpenWith Manager",
      appTagline: "按文件类型选择打开应用。",
      searchLabel: "搜索文件或应用",
      searchPlaceholder: "搜索文件或应用",
      actionsLabel: "操作",
      language: "语言",
      refresh: "刷新",
      export: "导出",
      compare: "对比",
      openSettings: "打开设置",
      fileKinds: "文件类型",
      filterFileKinds: "筛选文件类型",
      selectedFileKind: "已选文件类型",
      emptyNoMatch: "没有匹配的文件类型。",
      all: "全部",
      needsReview: "需检查",
      noFileKindSelected: "未选择文件类型",
      pickFileKind: "选择一种文件类型，查看当前打开应用。",
      fileKind: "文件类型",
      currentApp: "当前使用",
      includedFormats: "包含格式",
      exceptions: "例外",
      chooseApp: "选择应用",
      openDefaultApps: "默认应用",
      settingsHint: "将打开 Windows 设置。",
      technicalDetails: "技术明细",
      noDefaultApp: "未设置默认应用",
      noDefaultAppReported: "未发现默认应用",
      noAppSet: "未设置",
      allSet: "已设置",
      oneException: "1 个例外",
      exceptionsCount: "{count} 个例外",
      hasExceptions: "存在例外",
      mostlyOpenWith: "{name} 大多使用",
      openWith: "{name} 使用",
      readingDefaultsTitle: "正在读取默认项",
      readingDefaultsBody: "正在检查 Windows 用哪些应用打开你的文件。",
      exportToast: "已导出 {count} 个文件关联。",
      importedSummary: "已将 {count} 个导入关联与本机对比。",
      hostDisconnected: "OpenWith Manager 未连接到 Windows 主机。",
      unknownBridgeError: "未知桥接错误",
      snapshotComparison: "快照对比",
      close: "关闭",
      current: "当前",
      snapshot: "快照",
      none: "无",
      changed: "已变化",
      missingHere: "本机缺失",
      same: "相同",
      setByYou: "由你设置",
      systemDefault: "系统默认",
      notFound: "未找到",
      "kind.images.name": "照片和图片",
      "kind.images.short": "图片",
      "kind.images.description": "照片、截图和图片素材。",
      "kind.videos.name": "视频",
      "kind.videos.short": "视频",
      "kind.videos.description": "影片、剪辑和屏幕录制。",
      "kind.music.name": "音乐和音频",
      "kind.music.short": "音频",
      "kind.music.description": "歌曲、录音和声音文件。",
      "kind.pdf.name": "PDF 文档",
      "kind.pdf.short": "PDF",
      "kind.pdf.description": "便携文档和表单。",
      "kind.word.name": "Word 文档",
      "kind.word.short": "Word",
      "kind.word.description": "Microsoft Word 文档。",
      "kind.spreadsheets.name": "电子表格",
      "kind.spreadsheets.short": "表格",
      "kind.spreadsheets.description": "Excel 工作簿和电子表格文件。",
      "kind.presentations.name": "演示文稿",
      "kind.presentations.short": "演示",
      "kind.presentations.description": "PowerPoint 演示文件。",
      "kind.notes.name": "文本和笔记",
      "kind.notes.short": "文本",
      "kind.notes.description": "纯文本和 Markdown 笔记。",
      "kind.archives.name": "压缩文件",
      "kind.archives.short": "压缩",
      "kind.archives.description": "Zip、7-Zip 和其他打包文件。",
      "kind.code.name": "代码文件",
      "kind.code.short": "代码",
      "kind.code.description": "通常在编辑器中打开的开发文件。",
      "kind.web.name": "网页",
      "kind.web.short": "网页",
      "kind.web.description": "HTML 文件和从网页保存的页面。",
    },
  };

  function normalizeLanguage(value) {
    return String(value || "").toLowerCase().startsWith("zh") ? "zh-Hans" : "en";
  }

  function getStoredLanguage() {
    try {
      return localStorage.getItem(storageKey);
    } catch {
      return null;
    }
  }

  function getLanguage() {
    return normalizeLanguage(getStoredLanguage() || navigator.language || "en");
  }

  function setLanguage(language) {
    const normalized = normalizeLanguage(language);
    try {
      localStorage.setItem(storageKey, normalized);
    } catch {
      // Ignore storage failures; the selected language still applies to this render pass.
    }
    return normalized;
  }

  function interpolate(template, params) {
    return Object.entries(params || {}).reduce(
      (result, [key, value]) => result.replaceAll(`{${key}}`, String(value)),
      template,
    );
  }

  function t(key, params = {}, fallback = "") {
    const language = getLanguage();
    const template = messages[language]?.[key] ?? messages.en[key] ?? fallback ?? key;
    return interpolate(template, params);
  }

  window.openWithI18n = {
    messages,
    getLanguage,
    setLanguage,
    normalizeLanguage,
    t,
  };
}());
