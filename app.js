(function () {
  "use strict";

  const LEGACY_LS_KEY = "vitark_furniture_calc_v1";
  const LS_KEY = "vitan_furniture_calc_v1";
  const THEME_KEY = "vitan_furniture_calc_theme";
  const BACKGROUND_KEY = "vitan_furniture_calc_background";
  const systemThemeQuery = window.matchMedia("(prefers-color-scheme: dark)");
  const logoPath = "assets/logo.png";
  const defaultMaterialGroups = ["ЛДСП", "Фурнитура", "Кромка", "Прочее"];
  const units = [
    ["m2", "м²"],
    ["lm", "пог. м"],
    ["pc", "шт."]
  ];

  const seed = {
    counterparties: ["Частный заказчик", "Дилер"],
    materialGroups: defaultMaterialGroups,
    materials: {
      "ЛДСП": [
        { id: uid(), name: "ЛДСП Белый 16 мм", cost: 850, unit: "m2", sheetLength: 2750, sheetWidth: 1830 },
        { id: uid(), name: "ЛДСП Графит 18 мм", cost: 1180, unit: "m2", sheetLength: 2800, sheetWidth: 2070 }
      ],
      "Фурнитура": [
        { id: uid(), name: "Петля с доводчиком", cost: 110, unit: "pc" },
        { id: uid(), name: "Направляющие 450 мм", cost: 640, unit: "pc" }
      ],
      "Кромка": [
        { id: uid(), name: "Кромка ПВХ 2 мм белая", cost: 28, unit: "lm" },
        { id: uid(), name: "Кромка ПВХ 1 мм графит", cost: 24, unit: "lm" }
      ],
      "Прочее": [
        { id: uid(), name: "Упаковка", cost: 250, unit: "pc" }
      ]
    },
    catalog: {},
    projects: []
  };

  let state = loadState();
  let route = { name: "projects" };
  let modal = null;
  let cutState = null;
  let themeMode = localStorage.getItem(THEME_KEY) || "auto";
  if (!["light", "dark", "auto"].includes(themeMode)) themeMode = "auto";
  let theme = resolveTheme();
  let customBackground = localStorage.getItem(BACKGROUND_KEY) || "";
  let contextMenu = null;
  let openDropdown = null;
  let toasts = [];
  let edgeMenu = null;
  let suppressRenderMotion = false;
  let listSkeletonKey = "";
  let listSkeletonTimer = null;
  let openCatalogCounterparties = new Set();
  let catalogSort = "name";
  let materialSort = "name";
  let draggedDetailId = "";
  let detailDropPlacement = "before";
  let activeFormulaInput = null;
  let productEditorKey = "";
  let productEditorSnapshot = "";
  let pendingProductExit = null;
  let saveTimer = null;
  let refreshTimer = null;
  const editingMaterials = new Set();
  const productTotalsCache = new Map();

  const app = document.getElementById("app");
  const toastRoot = document.createElement("div");
  toastRoot.id = "toast-root";
  document.body.appendChild(toastRoot);
  applyTheme();
  if (customBackground) {
    document.body.dataset.bgLoading = "true";
    decodeImage(customBackground).then(applyBackground);
  } else {
    applyBackground();
  }
  installLiquidDisplacementMap();
  bindLiquidGlassMotion();

  function uid() {
    return Math.random().toString(36).slice(2, 10) + Date.now().toString(36).slice(-4);
  }

  function loadState() {
    try {
      const desktopRaw = window.vitanDesktop?.loadDatabase?.() || "";
      const browserRaw = localStorage.getItem(LS_KEY) || localStorage.getItem(LEGACY_LS_KEY);
      const raw = desktopRaw || browserRaw;
      const loaded = normalizeState(raw ? JSON.parse(raw) : structuredClone(seed));
      if (!localStorage.getItem(LS_KEY) && raw) {
        localStorage.setItem(LS_KEY, JSON.stringify(loaded));
      }
      if (window.vitanDesktop && !desktopRaw) window.vitanDesktop.saveDatabase(JSON.stringify(loaded));
      return loaded;
    } catch (error) {
      return normalizeState(structuredClone(seed));
    }
  }

  function normalizeState(nextState) {
    nextState.counterparties ||= [...seed.counterparties];
    nextState.materials ||= {};
    const groups = nextState.materialGroups?.length ? nextState.materialGroups : [...defaultMaterialGroups, ...Object.keys(nextState.materials)];
    nextState.materialGroups = [...new Set(groups)];
    nextState.materialGroups.forEach((group) => {
      nextState.materials[group] ||= [];
      nextState.materials[group].forEach((material, index) => {
        material.createdAt ||= Date.now() - index;
        material.textureDirection = Boolean(material.textureDirection);
      });
    });
    nextState.projects ||= [];
    const shouldSeedCatalog = !nextState.catalog;
    nextState.catalog ||= {};
    nextState.projects.forEach((project) => {
      project.products ||= [];
      project.payrolls ||= [];
      nextState.catalog[project.counterparty] ||= [];
      project.products.forEach((product, index) => {
        product.details ||= [];
        product.qty = Math.max(1, Number(product.qty) || 1);
        product.createdAt ||= Date.now() - index;
      });
    });
    nextState.counterparties.forEach((counterparty) => { nextState.catalog[counterparty] ||= []; });
    if (shouldSeedCatalog) {
      nextState.projects.forEach((project) => {
        const bucket = nextState.catalog[project.counterparty] ||= [];
        const known = new Set(bucket.map((product) => `${product.name}-${product.length}-${product.depth}-${product.height}`));
        project.products.forEach((product) => {
          const key = `${product.name}-${product.length}-${product.depth}-${product.height}`;
          if (known.has(key)) return;
          const copy = JSON.parse(JSON.stringify(product));
          copy.id = uid();
          copy.qty = 1;
          copy.details.forEach((detail) => detail.id = uid());
          bucket.push(copy);
          known.add(key);
        });
      });
    }
    Object.values(nextState.catalog).forEach((products) => {
      products.forEach((product, index) => {
        product.details ||= [];
        product.qty = Math.max(1, Number(product.qty) || 1);
        product.createdAt ||= Date.now() - index;
      });
    });
    return nextState;
  }

  function getMaterialTypes() {
    return state.materialGroups?.length ? state.materialGroups : Object.keys(state.materials);
  }

  function saveState() {
    if (saveTimer) {
      clearTimeout(saveTimer);
      saveTimer = null;
    }
    const raw = JSON.stringify(state);
    localStorage.setItem(LS_KEY, raw);
    if (window.vitanDesktop) {
      const result = window.vitanDesktop.saveDatabase(raw);
      if (!result?.ok) console.error("Не удалось сохранить файловую базу:", result?.error || "неизвестная ошибка");
    }
  }

  function saveStateSoon() {
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(() => saveState(), 300);
  }

  function flushSaveState() {
    if (!saveTimer) return;
    saveState();
  }

  function resolveTheme() {
    return themeMode === "auto" ? (systemThemeQuery.matches ? "dark" : "light") : themeMode;
  }

  function applyTheme() {
    theme = resolveTheme();
    document.documentElement.dataset.theme = theme;
    document.documentElement.dataset.themeMode = themeMode;
  }

  function applyBackground() {
    if (customBackground) {
      document.body.dataset.customBg = "true";
      delete document.body.dataset.bgLoading;
      document.documentElement.style.setProperty("--custom-bg", `url("${customBackground}")`);
    } else {
      delete document.body.dataset.customBg;
      delete document.body.dataset.bgLoading;
      document.documentElement.style.removeProperty("--custom-bg");
    }
  }

  function decodeImage(src) {
    return new Promise((resolve) => {
      const image = new Image();
      image.onload = () => {
        if (image.decode) image.decode().then(resolve).catch(resolve);
        else resolve();
      };
      image.onerror = resolve;
      image.src = src;
    });
  }

  function setThemeMode(mode) {
    if (!["light", "dark", "auto"].includes(mode)) return;
    themeMode = mode;
    localStorage.setItem(THEME_KEY, themeMode);
    applyTheme();
    renderQuiet();
    const label = themeMode === "auto" ? "Auto" : themeMode === "dark" ? "Темная" : "Светлая";
    notify("Тема изменена", label);
  }

  function toggleTheme() {
    setThemeMode(theme === "dark" ? "light" : "dark");
    return;
    notify(theme === "dark" ? "Тёмная тема включена" : "Светлая тема включена");
  }

  function breadcrumbHtml() {
    const project = getProject();
    const product = getProduct(project);
    const catalogProduct = getCatalogProduct();
    const crumbs = [{ label: "Проекты", action: "go-projects" }];
    if (route.name === "catalog" || route.name === "catalog-product") crumbs.push({ label: "Каталог", action: "open-catalog" });
    if (route.name === "catalog-product") crumbs.push({ label: route.counterparty || "Контрагент", action: "open-catalog" });
    if (route.name === "catalog-product") crumbs.push({ label: catalogProduct?.name || "Новое изделие", action: "noop" });
    if (route.name === "materials") crumbs.push({ label: "Материалы", action: "noop" });
    if (project) crumbs.push({ label: project.name || "Проект", action: "crumb-project" });
    if (route.name === "product") crumbs.push({ label: product?.name || "Новое изделие", action: route.productId ? "noop" : "noop" });
    if (route.name === "cut") crumbs.push({ label: "Карта кроя", action: "noop" });
    if (route.name === "details") crumbs.push({ label: "Деталировка", action: "noop" });

    return `
      <nav class="breadcrumb" aria-label="Навигация">
        ${crumbs.map((crumb, index) => `
          <button class="breadcrumb-item ${index === crumbs.length - 1 ? "active" : ""}" data-action="${crumb.action}" ${crumb.action === "noop" ? "disabled" : ""}>${esc(crumb.label)}</button>
          ${index < crumbs.length - 1 ? `<span class="breadcrumb-separator">/</span>` : ""}
        `).join("")}
      </nav>
    `;
  }

  function glassSelectHtml({ key, name, field, value, options, kind, type = "", detailId = "", canAdd = false, canDelete = false, canRename = false }) {
    const selected = options.find((option) => option.value === value) || options[0] || { value: "", label: "Выбрать" };
    const isOpen = openDropdown === key;
    const hiddenAttrs = [
      name ? `name="${name}"` : "",
      field ? `data-field="${field}"` : ""
    ].filter(Boolean).join(" ");
    return `
      <div class="glass-select ${isOpen ? "open" : ""}" data-select-key="${esc(key)}">
        <input type="hidden" ${hiddenAttrs} value="${esc(selected.value)}">
        <button class="glass-select-trigger" type="button" data-action="toggle-select" data-key="${esc(key)}" aria-expanded="${isOpen ? "true" : "false"}">
          <span>${esc(selected.label)}</span>
          <span class="select-chevron" aria-hidden="true">
            <svg viewBox="0 0 20 20" focusable="false"><path d="M5.5 7.5 10 12l4.5-4.5"/></svg>
          </span>
        </button>
        ${isOpen ? `
          <div class="glass-select-menu" data-select-menu="${esc(key)}">
            ${options.map((option, index) => `
              ${index ? `<div class="glass-select-separator" aria-hidden="true"></div>` : ""}
              <div class="glass-select-row ${option.value === selected.value ? "is-selected" : ""}">
                <button class="glass-select-option ${option.value === selected.value ? "selected" : ""}" type="button" data-action="select-value" data-key="${esc(key)}" data-kind="${esc(kind)}" data-value="${esc(option.value)}" data-type="${esc(type)}" data-detail-id="${esc(detailId)}">
                  <span class="select-check" aria-hidden="true"><svg viewBox="0 0 20 20" focusable="false"><path d="M4.5 10.4 8 13.8 15.7 6.2"/></svg></span>
                  <span class="select-label">${esc(option.label)}</span>
                </button>
                ${canRename ? `<button class="glass-select-small" type="button" data-action="select-rename" data-kind="${esc(kind)}" data-value="${esc(option.value)}" data-type="${esc(type)}" data-detail-id="${esc(detailId)}">Изм.</button>` : ""}
                ${canDelete ? `<button class="glass-select-delete" type="button" data-action="select-delete" data-kind="${esc(kind)}" data-value="${esc(option.value)}" data-type="${esc(type)}" data-detail-id="${esc(detailId)}">Удалить</button>` : ""}
              </div>
            `).join("")}
            ${canAdd ? `<button class="glass-select-add" type="button" data-action="select-add" data-key="${esc(key)}" data-kind="${esc(kind)}" data-type="${esc(type)}" data-detail-id="${esc(detailId)}">+ Добавить</button>` : ""}
          </div>
        ` : ""}
      </div>
    `;
  }

  function toastHtml() {
    if (!toasts.length) return "";
    return `<div class="toast-stack" aria-live="polite">${toasts.map((toast) => `
      <div class="toast ${toast.type || ""}">
        <strong>${esc(toast.title)}</strong>
        ${toast.text ? `<span>${esc(toast.text)}</span>` : ""}
      </div>
    `).join("")}</div>`;
  }

  function renderToasts() {
    toastRoot.innerHTML = toastHtml();
  }

  function notify(title, text = "", type = "") {
    const id = uid();
    toasts = [...toasts.slice(-2), { id, title, text, type }];
    renderToasts();
    window.setTimeout(() => {
      toasts = toasts.filter((toast) => toast.id !== id);
      renderToasts();
    }, 2600);
  }

  function emptyStateHtml({ title, text, action = "", actionText = "Создать", compact = false }) {
    return `
      <div class="empty-state ${compact ? "compact" : ""}">
        <div class="empty-state-skeleton" aria-hidden="true">
          <span></span>
          <span></span>
          <span></span>
        </div>
        <div class="empty-state-icon" aria-hidden="true">
          <svg viewBox="0 0 64 48" focusable="false">
            <path d="M10 22 17 10h30l7 12v14a4 4 0 0 1-4 4H14a4 4 0 0 1-4-4V22Z"/>
            <path d="M11 23h15l3 6h6l3-6h15"/>
          </svg>
        </div>
        <h3>${esc(title)}</h3>
        <p>${esc(text)}</p>
        ${action ? `<button class="ghost" data-action="${esc(action)}">${esc(actionText)}</button>` : ""}
      </div>
    `;
  }

  function listSkeletonHtml(colspan = 5) {
    return `
      <tr class="list-skeleton-row">
        <td colspan="${colspan}">
          <div class="list-skeleton" aria-hidden="true">
            <span></span>
            <span></span>
            <span></span>
          </div>
        </td>
      </tr>
    `;
  }

  function unitLabel(unit) {
    if (unit === "m2") return "м2";
    if (unit === "lm") return "м.пог";
    return "шт";
  }

  function isSheetMaterial(material) {
    return material?.unit === "m2" && Number(material.sheetLength) > 0 && Number(material.sheetWidth) > 0;
  }

  function materialGroupById(materialId) {
    for (const type of getMaterialTypes()) {
      if ((state.materials[type] || []).some((material) => material.id === materialId)) return type;
    }
    return getMaterialTypes()[0] || "Прочее";
  }

  function linearMaterials() {
    return getMaterialTypes().flatMap((type) => sortByMode(state.materials[type] || [], "name")
      .filter((material) => material.unit === "lm")
      .map((material) => ({ ...material, type })));
  }

  function installLiquidDisplacementMap() {
    const source = document.getElementById("liquid-displacement-source");
    if (!source) return;

    const size = 256;
    const canvas = document.createElement("canvas");
    canvas.width = size;
    canvas.height = size;
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    const image = ctx.createImageData(size, size);
    const data = image.data;
    const radius = 0.34;
    const bezel = 0.24;
    const glassThickness = 1.32;

    const smootherstep = (edge0, edge1, x) => {
      const t = Math.max(0, Math.min(1, (x - edge0) / (edge1 - edge0)));
      return t * t * t * (t * (t * 6 - 15) + 10);
    };

    const sdRoundRect = (x, y) => {
      const bx = 1 - radius;
      const by = 1 - radius;
      const qx = Math.abs(x) - bx;
      const qy = Math.abs(y) - by;
      const outside = Math.hypot(Math.max(qx, 0), Math.max(qy, 0));
      const inside = Math.min(Math.max(qx, qy), 0);
      return outside + inside - radius;
    };

    for (let py = 0; py < size; py += 1) {
      for (let px = 0; px < size; px += 1) {
        const nx = (px / (size - 1)) * 2 - 1;
        const ny = (py / (size - 1)) * 2 - 1;
        const sdf = sdRoundRect(nx, ny);
        const insideDistance = Math.max(0, -sdf);
        const borderT = 1 - smootherstep(0, bezel, insideDistance);

        const eps = 1 / size;
        const gx = sdRoundRect(nx + eps, ny) - sdRoundRect(nx - eps, ny);
        const gy = sdRoundRect(nx, ny + eps) - sdRoundRect(nx, ny - eps);
        const len = Math.hypot(gx, gy) || 1;
        const normalX = gx / len;
        const normalY = gy / len;

        const convexSquircle = Math.pow(1 - Math.pow(1 - borderT, 4), 0.25);
        const concave = 1 - convexSquircle;
        const lipBlend = smootherstep(0.35, 0.92, borderT);
        const lipProfile = convexSquircle * (1 - lipBlend) + concave * lipBlend;
        const innerFalloff = 1 - smootherstep(0.42, 0.92, Math.hypot(nx, ny));
        const magnitude = Math.min(1, Math.pow(lipProfile, 1.35) * glassThickness * (0.62 + innerFalloff * 0.18));
        const tangential = Math.sin((nx - ny) * Math.PI) * 0.035 * borderT;
        const vx = normalX * magnitude + -normalY * tangential;
        const vy = normalY * magnitude + normalX * tangential;

        const index = (py * size + px) * 4;
        data[index] = Math.round(128 + vx * 127);
        data[index + 1] = Math.round(128 + vy * 127);
        data[index + 2] = 128;
        data[index + 3] = 255;
      }
    }

    ctx.putImageData(image, 0, 0);
    source.setAttribute("href", canvas.toDataURL("image/png"));
  }

  function bindLiquidGlassMotion() {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const root = document.documentElement;
    const light = { x: window.innerWidth * 0.72, y: window.innerHeight * 0.18 };
    const target = { ...light };
    const last = { ...light };
    let raf = 0;
    function glassSurfaces() {
      return document.querySelectorAll(".topbar, .card, .stat, .table-wrap, .empty, .modal, .canvas-wrap, button");
    }

    function update() {
      raf = 0;
      light.x += (target.x - light.x) * 0.32;
      light.y += (target.y - light.y) * 0.32;
      const velocity = Math.min(1, Math.hypot(light.x - last.x, light.y - last.y) / 48);
      last.x = light.x;
      last.y = light.y;

      const px = Math.round((light.x / window.innerWidth) * 100);
      const py = Math.round((light.y / window.innerHeight) * 100);
      root.style.setProperty("--pointer-x", `${px}%`);
      root.style.setProperty("--pointer-y", `${py}%`);

      glassSurfaces().forEach((surface) => {
        const rect = surface.getBoundingClientRect();
        if (!rect.width || !rect.height) return;
        const localX = ((light.x - rect.left) / rect.width) * 100;
        const localY = ((light.y - rect.top) / rect.height) * 100;
        const clampedX = Math.max(-35, Math.min(135, localX));
        const clampedY = Math.max(-35, Math.min(135, localY));
        surface.style.setProperty("--glass-x", `${clampedX}%`);
        surface.style.setProperty("--glass-y", `${clampedY}%`);
      });

      const displacement = document.querySelector("#liquid-glass-filter feDisplacementMap");
      if (displacement) displacement.setAttribute("scale", String(Math.round(34 + velocity * 8)));

      if (Math.abs(target.x - light.x) > 0.3 || Math.abs(target.y - light.y) > 0.3) {
        raf = requestAnimationFrame(update);
      }
    }

    window.addEventListener("pointermove", (event) => {
      target.x = event.clientX;
      target.y = event.clientY;
      if (!raf) raf = requestAnimationFrame(update);
    }, { passive: true });

    window.addEventListener("resize", () => {
      target.x = window.innerWidth * 0.72;
      target.y = window.innerHeight * 0.18;
      if (!raf) raf = requestAnimationFrame(update);
    }, { passive: true });

    requestAnimationFrame(update);
  }

  function money(value) {
    return new Intl.NumberFormat("ru-RU", { style: "currency", currency: "RUB", maximumFractionDigits: 0 }).format(value || 0);
  }

  function fmt(value, digits = 2) {
    return new Intl.NumberFormat("ru-RU", { maximumFractionDigits: digits }).format(value || 0);
  }

  function esc(value) {
    return String(value ?? "").replace(/[&<>"']/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch]));
  }

  function getProject(id = route.projectId) {
    return state.projects.find((project) => project.id === id);
  }

  function getProduct(project, id = route.productId) {
    return project?.products.find((product) => product.id === id);
  }

  function getCatalogProducts(counterparty = route.counterparty) {
    if (!counterparty) return [];
    state.catalog ||= {};
    state.catalog[counterparty] ||= [];
    return state.catalog[counterparty];
  }

  function getCatalogProduct(counterparty = route.counterparty, id = route.productId) {
    return getCatalogProducts(counterparty).find((product) => product.id === id);
  }

  function cloneProductForStorage(product) {
    const copy = JSON.parse(JSON.stringify(product));
    copy.id = uid();
    copy.qty = 1;
    copy.createdAt = Date.now();
    copy.details ||= [];
    copy.details.forEach((detail) => detail.id = uid());
    normalizeFixedSizes(copy);
    return copy;
  }

  function sortByMode(items, mode) {
    return [...items].sort((a, b) => mode === "date"
      ? (Number(b.createdAt) || 0) - (Number(a.createdAt) || 0)
      : String(a.name || "").localeCompare(String(b.name || ""), "ru", { sensitivity: "base" }));
  }

  function matchingCatalogProduct(project, product) {
    const products = getCatalogProducts(project?.counterparty);
    if (product?.catalogProductId) return products.find((item) => item.id === product.catalogProductId) || null;
    return products.find((item) => item.name === product?.name
      && Number(item.length) === Number(product?.length)
      && Number(item.depth) === Number(product?.depth)
      && Number(item.height) === Number(product?.height)) || null;
  }

  function isCatalogRoute() {
    return route.name === "catalog-product";
  }

  function isProductEditorRoute() {
    return route.name === "product" || route.name === "catalog-product";
  }

  function normalizeFixedSizes(product) {
    product.fixedSizes = Array.from({ length: 6 }, (_, index) => {
      const row = product.fixedSizes?.[index] || {};
      return {
        name: row.name || "",
        value: Number(row.value) || 0
      };
    });
    return product.fixedSizes;
  }

  function editorKeyForRoute() {
    return [route.name, route.projectId || "", route.counterparty || "", route.productId || "draft"].join(":");
  }

  function snapshotProduct(product) {
    return JSON.stringify(product || null);
  }

  function ensureProductEditorSnapshot(product) {
    const key = editorKeyForRoute();
    if (productEditorKey !== key) {
      productEditorKey = key;
      productEditorSnapshot = snapshotProduct(product);
      pendingProductExit = null;
    }
  }

  function currentProductDirty() {
    if (!isProductEditorRoute()) return false;
    const product = currentEditableProduct();
    updateProductFromForm(product);
    return snapshotProduct(product) !== productEditorSnapshot;
  }

  function restoreProductSnapshot() {
    if (!isProductEditorRoute()) return;
    const snapshot = productEditorSnapshot ? JSON.parse(productEditorSnapshot) : null;

    if (isCatalogRoute()) {
      if (!route.productId) {
        if (state.catalogDrafts) delete state.catalogDrafts[route.counterparty];
        return;
      }
      const products = getCatalogProducts(route.counterparty);
      const index = products.findIndex((product) => product.id === route.productId);
      if (index >= 0 && snapshot) products[index] = snapshot;
      return;
    }

    const project = getProject();
    if (!project) return;
    if (!route.productId) {
      delete project.draft;
      return;
    }
    const index = project.products.findIndex((product) => product.id === route.productId);
    if (index >= 0 && snapshot) project.products[index] = snapshot;
  }

  function discardProductChanges() {
    restoreProductSnapshot();
    productEditorKey = "";
    productEditorSnapshot = "";
    pendingProductExit = null;
    saveState();
  }

  function runProductExitTarget(target) {
    const projectId = route.projectId;
    const counterparty = route.counterparty;
    productEditorKey = "";
    productEditorSnapshot = "";
    pendingProductExit = null;

    if (target === "catalog") {
      if (counterparty) openCatalogCounterparties.add(counterparty);
      route = { name: "catalog" };
      cutState = null;
      render();
      return;
    }
    if (target === "project") {
      route = { name: "project", projectId };
      cutState = null;
      render();
      return;
    }
    if (target === "projects") {
      route = { name: "projects" };
      modal = null;
      cutState = null;
      render();
      return;
    }
    if (target === "materials") {
      route = { name: "materials" };
      modal = null;
      cutState = null;
      render();
      return;
    }
  }

  function requestProductExit(target) {
    if (isProductEditorRoute() && currentProductDirty()) {
      pendingProductExit = target;
      modal = { type: "unsaved-product" };
      render();
      return true;
    }
    runProductExitTarget(target);
    return true;
  }

  function shouldWarnBeforeUnload() {
    try {
      return isProductEditorRoute() && currentProductDirty();
    } catch (error) {
      return false;
    }
  }

  function getMaterial(type, materialId) {
    return (state.materials[type] || []).find((material) => material.id === materialId);
  }

  function materialName(type, material) {
    return `${type || ""} ${material?.name || ""}`.toLowerCase();
  }

  function isDspMaterial(detail, material) {
    return materialName(detail.type, material).includes("дсп");
  }

  function isDvpMaterial(detail, material) {
    return materialName(detail.type, material).includes("двп");
  }

  function isProjectAreaMaterial(detail, material) {
    return material?.unit === "m2" && (isDspMaterial(detail, material) || isDvpMaterial(detail, material));
  }

  function isGlassMaterial(detail, material) {
    return materialName(detail.type, material).includes("стек");
  }

  function productQty(product) {
    return Math.max(1, Number(product.qty) || 1);
  }

  function productMetric(product, key) {
    const vars = {
      "длина изделия": product.length,
      "ширина изделия": product.length,
      "глубина изделия": product.depth,
      "высота изделия": product.height,
      "высота ножки": product.hasLegs ? product.legHeight : 0,
      "длина": product.length,
      "ширина": product.length,
      "глубина": product.depth,
      "высота": product.height,
      "ножка": product.hasLegs ? product.legHeight : 0,
      "ножки": product.hasLegs ? product.legHeight : 0
    };
    return Number(vars[String(key).toLowerCase()]) || 0;
  }

  function formulaReferences(product, currentDetailId = "") {
    const refs = [
      { label: "Высота изделия", expr: "высота изделия", value: product.height },
      { label: "Длина изделия", expr: "длина изделия", value: product.length },
      { label: "Глубина изделия", expr: "глубина изделия", value: product.depth },
      { label: "Высота ножки", expr: "высота ножки", value: product.hasLegs ? product.legHeight : 0 }
    ];
    normalizeFixedSizes(product).forEach((row) => {
      const name = String(row.name || "").trim();
      if (!name) return;
      refs.push({ label: `${name} · размер`, expr: `${name} размер`, value: row.value });
    });
    (product.details || []).forEach((detail) => {
      if (detail.id === currentDetailId) return;
      const name = detail.name || "Деталь";
      const calc = detailCalc(product, detail, new Set([currentDetailId]));
      refs.push({ label: `${name} · длина`, expr: `${name} длина`, value: calc.length });
      refs.push({ label: `${name} · ширина`, expr: `${name} ширина`, value: calc.width });
    });
    return refs;
  }

  function evaluateExpression(expr, product, currentDetailId = "", stack = new Set()) {
    if (expr === "" || expr == null) return 0;
    let normalized = String(expr).trim().replace(/^=/, "").toLowerCase().replace(/,/g, ".");
    ["длина изделия", "ширина изделия", "глубина изделия", "высота изделия", "высота ножки"]
      .sort((a, b) => b.length - a.length)
      .forEach((key) => {
        normalized = normalized.replaceAll(key, String(productMetric(product, key)));
      });
    normalizeFixedSizes(product)
      .filter((row) => String(row.name || "").trim())
      .sort((a, b) => String(b.name).length - String(a.name).length)
      .forEach((row) => {
        const key = `${String(row.name).trim().toLowerCase()} размер`;
        normalized = normalized.replaceAll(key, String(Number(row.value) || 0));
      });
    (product.details || []).forEach((detail) => {
      if (detail.id === currentDetailId || stack.has(detail.id)) return;
      const name = String(detail.name || "").trim().toLowerCase();
      if (!name) return;
      const lengthKey = `${name} длина`;
      const widthKey = `${name} ширина`;
      if (!normalized.includes(lengthKey) && !normalized.includes(widthKey)) return;
      const calc = detailCalc(product, detail, new Set([...stack, currentDetailId]));
      normalized = normalized.replaceAll(lengthKey, String(calc.length));
      normalized = normalized.replaceAll(widthKey, String(calc.width));
    });
    ["длина", "ширина", "глубина", "высота", "ножка", "ножки"]
      .sort((a, b) => b.length - a.length)
      .forEach((key) => {
        normalized = normalized.replaceAll(key, String(productMetric(product, key)));
      });
    if (!/^[0-9+\-*/().\s]+$/.test(normalized)) return Number(String(expr).replace(/^=/, "")) || 0;
    try {
      return Math.max(0, Number(Function(`"use strict"; return (${normalized})`)()) || 0);
    } catch (error) {
      return Number(String(expr).replace(/^=/, "")) || 0;
    }
  }

  function detailCalc(product, detail, stack = new Set()) {
    const length = evaluateExpression(detail.lengthExpr, product, detail.id, stack);
    const material = getMaterial(detail.type, detail.materialId);
    const width = material?.unit === "m2" ? evaluateExpression(detail.widthExpr, product, detail.id, stack) : 0;
    const qty = Number(detail.qty) || 1;
    const rawArea = length * width * qty / 1000000;
    const area = isProjectAreaMaterial(detail, material) ? rawArea : 0;
    const displayArea = material?.unit === "m2" ? rawArea : 0;
    const glassArea = isGlassMaterial(detail, material) ? rawArea : 0;
    const glassEdge = isGlassMaterial(detail, material) ? (length + width) * 2 * qty : 0;
    let usage = qty;
    if (material?.unit === "m2") usage = rawArea;
    if (material?.unit === "lm") usage = length * qty / 1000;
    const cost = usage * (Number(material?.cost) || 0);
    return { length, width, qty, area, displayArea, rawArea, glassArea, glassEdge, perimeter: material?.unit === "m2" ? (length + width) * 2 * qty / 1000 : 0, usage, cost, material };
  }

  function productTotalsSignature(product) {
    const usedMaterials = new Set((product.details || []).map((detail) => `${detail.type}:${detail.materialId}`));
    const materialState = [];
    usedMaterials.forEach((key) => {
      const [type, materialId] = key.split(":");
      const material = getMaterial(type, materialId);
      materialState.push([type, materialId, material?.name || "", material?.cost || 0, material?.unit || ""]);
    });
    return JSON.stringify({
      qty: productQty(product),
      length: product.length,
      depth: product.depth,
      height: product.height,
      hasLegs: product.hasLegs,
      legHeight: product.legHeight,
      fixedSizes: product.fixedSizes || [],
      details: (product.details || []).map((detail) => ({
        id: detail.id,
        name: detail.name,
        qty: detail.qty,
        type: detail.type,
        materialId: detail.materialId,
        lengthExpr: detail.lengthExpr,
        widthExpr: detail.widthExpr
      })),
      materials: materialState
    });
  }

  function productTotals(product) {
    const cacheId = product.id || "draft";
    const signature = productTotalsSignature(product);
    const cached = productTotalsCache.get(cacheId);
    if (cached?.signature === signature) return { ...cached.totals };
    const multiplier = productQty(product);
    const totals = (product.details || []).reduce((acc, detail) => {
      const calc = detailCalc(product, detail);
      acc.area += calc.area * multiplier;
      acc.rawArea += calc.rawArea * multiplier;
      acc.glassArea += calc.glassArea * multiplier;
      acc.glassEdge += calc.glassEdge * multiplier;
      acc.cost += calc.cost * multiplier;
      return acc;
    }, { area: 0, rawArea: 0, glassArea: 0, glassEdge: 0, cost: 0 });
    productTotalsCache.set(cacheId, { signature, totals: { ...totals } });
    return totals;
  }

  function allProjectDetails(project) {
    return project.products.flatMap((product) => {
      const multiplier = productQty(product);
      const copies = [];
      for (let productCopy = 0; productCopy < multiplier; productCopy += 1) {
        product.details.forEach((detail) => {
          const calc = detailCalc(product, detail);
          copies.push({ product, detail, calc, productCopy });
        });
      }
      return copies;
    });
  }

  function forEachStoredProduct(callback) {
    state.projects.forEach((project) => project.products.forEach(callback));
    Object.values(state.catalog || {}).forEach((products) => products.forEach(callback));
    Object.values(state.catalogDrafts || {}).forEach(callback);
  }

  function selectedPayrollArea(project, row) {
    const details = allProjectDetails(project).filter(({ detail, calc }) => isDspMaterial(detail, calc.material));
    if (row.scope !== "selected") return details.reduce((sum, item) => sum + item.calc.area, 0);
    return details
      .filter((item) => (row.detailKeys || []).includes(`${item.product.id}:${item.detail.id}`))
      .reduce((sum, item) => sum + item.calc.area, 0);
  }

  function projectGlassEdge(project) {
    return project.products.reduce((sum, product) => sum + productTotals(product).glassEdge, 0);
  }

  function projectTotals(project) {
    const productTotalsSum = project.products.reduce((acc, product) => {
      const totals = productTotals(product);
      acc.area += totals.area;
      acc.rawArea += totals.rawArea;
      acc.glassArea += totals.glassArea;
      acc.glassEdge += totals.glassEdge;
      acc.cost += totals.cost;
      return acc;
    }, { area: 0, rawArea: 0, glassArea: 0, glassEdge: 0, cost: 0 });
    const payroll = (project.payrolls || []).reduce((sum, row) => sum + payrollAmount(project, row), 0);
    return { area: productTotalsSum.area, rawArea: productTotalsSum.rawArea, glassArea: productTotalsSum.glassArea, glassEdge: productTotalsSum.glassEdge, productCost: productTotalsSum.cost, payroll, cost: productTotalsSum.cost + payroll };
  }

  function payrollAmount(project, row) {
    if (row.mode === "fixed") return Number(row.amount) || 0;
    if (row.mode === "glass-polish") return (projectGlassEdge(project) / 1000) * (Number(row.rate) || 0);
    const area = selectedPayrollArea(project, row);
    return area * (Number(row.rate) || 0);
  }

  function shell(content) {
    return `
      <div class="app-shell">
        <header class="topbar">
          <div class="brand">
            <div class="logo-wrap">
              <img src="${logoPath}" alt="Витан-К">
              <span class="logo-hitbox" data-action="go-projects" data-tooltip="К проектам" role="link" aria-label="Перейти к проектам" tabindex="0"></span>
            </div>
            <div>
              <h1>Просчет мебели</h1>
              ${breadcrumbHtml()}
            </div>
          </div>
          <nav class="top-actions glass-menubar" aria-label="Главное меню">
            <button class="ghost settings-button" data-action="open-catalog" data-tooltip="Открыть каталог изделий">Каталог изделий</button>
            <button class="ghost settings-button" data-action="open-materials" data-tooltip="Открыть общую базу материалов">Материалы</button>
            <button class="ghost settings-button" data-action="open-settings" data-tooltip="Настройки приложения">⚙︎ Настройки</button>
          </nav>
        </header>
        <main class="page">${content}</main>
        <input id="background-input" type="file" accept="image/*" hidden>
      </div>
      ${modal ? modalHtml() : ""}
      ${contextMenu ? contextMenuHtml() : ""}
    `;
  }

  function render() {
    if (route.name === "project" && !getProject()) route = { name: "projects" };
    if (route.name === "product" && route.productId && !getProduct(getProject())) route = { name: "project", projectId: route.projectId };
    if (route.name === "catalog-product" && route.productId && !getCatalogProduct()) route = { name: "catalog" };
    if (route.name === "materials") renderMaterialsPage();
    else if (route.name === "catalog") renderCatalogPage();
    else if (route.name === "cut") renderCutPage();
    else if (route.name === "details") renderDetailsPage();
    else if (route.name === "product" || route.name === "catalog-product") renderProductEditor();
    else if (route.name === "project") renderProjectPage();
    else renderProjectsPage();
    positionOpenDropdown();
    bindGlobal();
    suppressRenderMotion = false;
  }

  function renderQuiet() {
    suppressRenderMotion = true;
    render();
  }

  function positionOpenDropdown() {
    const select = app.querySelector(".glass-select.open");
    if (!select) return;
    const trigger = select.querySelector(".glass-select-trigger");
    const menu = select.querySelector(".glass-select-menu");
    if (!trigger || !menu) return;

    const rect = trigger.getBoundingClientRect();
    menu.classList.add("is-floating");
    menu.style.minWidth = `${Math.ceil(rect.width)}px`;
    menu.style.left = "0px";
    menu.style.top = "0px";
    app.appendChild(menu);

    const gap = 7;
    const viewportGap = 8;
    const menuRect = menu.getBoundingClientRect();
    const width = Math.min(menuRect.width, window.innerWidth - viewportGap * 2);
    const left = Math.min(Math.max(viewportGap, rect.left), window.innerWidth - width - viewportGap);
    const fitsBelow = rect.bottom + gap + menuRect.height <= window.innerHeight - viewportGap;
    const top = fitsBelow
      ? rect.bottom + gap
      : Math.max(viewportGap, rect.top - gap - menuRect.height);

    menu.style.left = `${Math.round(left)}px`;
    menu.style.top = `${Math.round(top)}px`;
  }

  function closeFloatingPanels(target = null) {
    const insideDropdown = target?.closest?.(".glass-select, .glass-select-menu");
    const insideFormula = target?.closest?.(".formula-input, .formula-command");
    let changed = false;

    if (!insideDropdown && openDropdown) {
      openDropdown = null;
      document.querySelectorAll(".glass-select.open").forEach((select) => {
        select.classList.remove("open");
        select.querySelector(".glass-select-trigger")?.setAttribute("aria-expanded", "false");
      });
      document.querySelectorAll(".glass-select-menu").forEach((menu) => menu.remove());
      changed = true;
    }

    if (!insideFormula) {
      const formulaPanel = document.querySelector(".formula-command");
      if (formulaPanel) {
        formulaPanel.remove();
        activeFormulaInput = null;
        changed = true;
      }
    }

    return changed;
  }

  function closeSelectDropdownNow() {
    openDropdown = null;
    document.querySelectorAll(".glass-select.open").forEach((select) => {
      select.classList.remove("open");
      select.querySelector(".glass-select-trigger")?.setAttribute("aria-expanded", "false");
    });
    document.querySelectorAll(".glass-select-menu").forEach((menu) => menu.remove());
  }

  function renderProjectsPage() {
    const cards = state.projects.map((project) => {
      const totals = projectTotals(project);
      return `
        <article class="card project-card">
          <h3>${esc(project.name)}</h3>
          <div class="meta-line">
            <span>Контрагент: ${esc(project.counterparty)}</span>
            <span>${esc(project.address || "Адрес не указан")}</span>
          </div>
          <div class="meta-line">
            <strong>${project.products.length} изд.</strong>
            <strong>${fmt(totals.area)} м²</strong>
            <strong>${money(totals.cost)}</strong>
          </div>
          <div class="row-actions glass-button-group">
            <button data-action="open-project" data-id="${project.id}">Открыть</button>
            <button class="ghost danger" data-action="delete-project" data-id="${project.id}">Удалить</button>
          </div>
        </article>
      `;
    }).join("");
    app.innerHTML = shell(`
      <div class="toolbar">
        <h2>Проекты</h2>
        <div class="toolbar-actions glass-button-group">
          <button data-action="new-project">Создать проект</button>
        </div>
      </div>
      ${state.projects.length ? `<section class="grid">${cards}</section>` : `
        <section class="empty">
          ${emptyStateHtml({ title: "Проектов пока нет", text: "Создайте первый проект, чтобы начать просчет мебели.", action: "new-project", actionText: "Создать проект" })}
        </section>
      `}
    `);
  }

  function renderCatalogPage() {
    const sections = state.counterparties.map((counterparty) => {
      const products = sortByMode(getCatalogProducts(counterparty), catalogSort);
      const expanded = openCatalogCounterparties.has(counterparty);
      const isLoading = expanded && listSkeletonKey === `catalog:${counterparty}`;
      const rows = expanded && !isLoading ? products.map((product, index) => {
        const totals = productTotals(product);
        return `
          <tr>
            <td>${index + 1}</td>
            <td><button class="ghost" data-action="edit-catalog-product" data-counterparty="${esc(counterparty)}" data-id="${product.id}">${esc(product.name || "Изделие без названия")}</button></td>
            <td class="nowrap">${fmt(product.length, 0)} × ${fmt(product.depth, 0)} × ${fmt(product.height, 0)}</td>
            <td class="nowrap">${fmt(totals.area)} м²</td>
            <td><button class="ghost danger icon-btn" data-action="delete-catalog-product" data-counterparty="${esc(counterparty)}" data-id="${product.id}" data-tooltip="Удалить из каталога">×</button></td>
          </tr>
        `;
      }).join("") : "";
      return `
        <section class="catalog-section ${expanded ? "is-open" : ""}">
          <div class="toolbar catalog-dealer" role="button" tabindex="0" data-action="toggle-catalog-counterparty" data-counterparty="${esc(counterparty)}">
            <div>
              <h2>${esc(counterparty)}</h2>
              <div class="muted">${products.length} изд. ${expanded ? "· раскрыто" : "· свернуто"}</div>
            </div>
            <div class="toolbar-actions glass-button-group">
              <button class="ghost icon-btn catalog-chevron" type="button" data-action="toggle-catalog-counterparty" data-counterparty="${esc(counterparty)}" aria-label="${expanded ? "Свернуть" : "Раскрыть"}">
                <svg viewBox="0 0 20 20" focusable="false" aria-hidden="true"><path d="M5.5 7.5 10 12l4.5-4.5"/></svg>
              </button>
              ${expanded ? `<button data-action="add-catalog-product" data-counterparty="${esc(counterparty)}">Добавить изделие</button>` : ""}
            </div>
          </div>
          ${expanded ? `<section class="table-wrap catalog-products">
            <table>
              <thead><tr><th>Номер</th><th>Название</th><th>Габариты</th><th>Квадратура ДСП/ДВП</th><th></th></tr></thead>
              <tbody>${isLoading ? listSkeletonHtml(5) : (rows || `<tr><td colspan="5"><div class="empty-state compact"><div class="empty-state-skeleton" aria-hidden="true"><span></span><span></span><span></span></div><div class="empty-state-icon" aria-hidden="true"><svg viewBox="0 0 64 48" focusable="false"><path d="M10 22 17 10h30l7 12v14a4 4 0 0 1-4 4H14a4 4 0 0 1-4-4V22Z"/><path d="M11 23h15l3 6h6l3-6h15"/></svg></div><h3>Изделий нет</h3><p>Добавьте шаблон изделия для этого контрагента.</p><button type="button" data-action="add-catalog-product" data-counterparty="${esc(counterparty)}">Добавить изделие</button></div></td></tr>`)}</tbody>
            </table>
          </section>` : ""}
        </section>
      `;
    }).join("");
    app.innerHTML = shell(`
      <div class="toolbar">
        <div>
          <h2>Каталог изделий</h2>
          <div class="muted">Изделия сгруппированы по контрагентам и не привязаны к конкретному проекту.</div>
        </div>
        <div class="toolbar-actions glass-button-group">
          <button class="ghost" data-action="toggle-catalog-sort">${catalogSort === "name" ? "По названию" : "По дате создания"}</button>
        </div>
      </div>
      ${sections || `<section class="empty">${emptyStateHtml({ title: "Контрагентов пока нет", text: "Создайте контрагента при добавлении проекта, чтобы вести каталог изделий.", action: "new-project", actionText: "Создать проект" })}</section>`}
    `);
  }

  function materialRowHtml(type, material, index, editing = false) {
    const key = `${type}:${material.id}`;
    const isEditing = editing || editingMaterials.has(key);
    const unit = material.unit || "pc";
    if (isEditing) {
      return `
        <tr data-material-row data-type="${esc(type)}" data-material-id="${material.id}">
          <td>${index + 1}</td>
          <td><input data-material-field="name" value="${esc(material.name || "")}" placeholder="Название материала"></td>
          <td>
            <select data-material-field="unit">
              ${["m2", "lm", "pc"].map((item) => `<option value="${item}" ${unit === item ? "selected" : ""}>${unitLabel(item)}</option>`).join("")}
            </select>
          </td>
          <td><input data-material-field="cost" type="number" min="0" step="0.01" value="${Number(material.cost) || 0}"></td>
          <td class="nowrap">
            <div class="material-sheet-editor ${unit === "m2" ? "" : "is-hidden"}">
              <input data-material-field="sheetLength" type="number" min="0" value="${Number(material.sheetLength) || 2750}">
              <span>×</span>
              <input data-material-field="sheetWidth" type="number" min="0" value="${Number(material.sheetWidth) || 1830}">
              <span>мм</span>
            </div>
            <span class="material-unit-preview ${unit === "m2" ? "is-hidden" : ""}">${unitLabel(unit)}</span>
          </td>
          <td><label class="texture-toggle"><input data-material-field="textureDirection" type="checkbox" ${material.textureDirection ? "checked" : ""}><span>→</span></label></td>
          <td>
            <div class="row-actions glass-button-group material-tools">
              <button class="ghost icon-btn" data-action="save-material-row" data-type="${esc(type)}" data-id="${material.id}" data-tooltip="Сохранить материал">✓</button>
              <button class="ghost danger icon-btn" data-action="remove-material" data-type="${esc(type)}" data-id="${material.id}" data-tooltip="Удалить материал">×</button>
            </div>
          </td>
        </tr>
      `;
    }
    return `
      <tr data-material-row data-type="${esc(type)}" data-material-id="${material.id}">
        <td>${index + 1}</td>
        <td>${esc(material.name)}</td>
        <td>${unitLabel(unit)}</td>
        <td class="nowrap">${money(material.cost)}</td>
        <td class="nowrap">${unit === "m2" && material.sheetLength && material.sheetWidth ? `${fmt(material.sheetLength, 0)} × ${fmt(material.sheetWidth, 0)} мм` : unitLabel(unit)}</td>
        <td>${material.textureDirection ? '<span class="texture-direction" title="Направление текстуры слева направо">→</span>' : "—"}</td>
        <td>
          <div class="row-actions glass-button-group material-tools">
            <button class="ghost icon-btn" data-action="edit-material-row" data-type="${esc(type)}" data-id="${material.id}" data-tooltip="Изменить материал">✎</button>
            <button class="ghost danger icon-btn" data-action="remove-material" data-type="${esc(type)}" data-id="${material.id}" data-tooltip="Удалить материал">×</button>
          </div>
        </td>
      </tr>
    `;
  }

  function materialEmptyRowHtml(type) {
    return `<tr data-empty-materials><td colspan="7"><div class="empty-state compact"><div class="empty-state-skeleton" aria-hidden="true"><span></span><span></span><span></span></div><div class="empty-state-icon" aria-hidden="true"><svg viewBox="0 0 64 48" focusable="false"><path d="M10 22 17 10h30l7 12v14a4 4 0 0 1-4 4H14a4 4 0 0 1-4-4V22Z"/><path d="M11 23h15l3 6h6l3-6h15"/></svg></div><h3>Материалов нет</h3><p>Добавьте материал в этот тип, чтобы использовать его в деталях изделий.</p><button type="button" data-action="add-material" data-type="${esc(type)}">Добавить материал</button></div></td></tr>`;
  }

  function materialSectionElement(type) {
    return Array.from(document.querySelectorAll(".material-section"))
      .find((item) => item.querySelector('[data-action="add-material"]')?.dataset.type === type);
  }

  function updateMaterialSectionCount(type) {
    const section = materialSectionElement(type);
    const muted = section?.querySelector(".catalog-dealer .muted");
    if (muted) muted.textContent = `${(state.materials[type] || []).length} материалов · общие для всех проектов и контрагентов`;
  }

  function renderMaterialsPage() {
    const groups = getMaterialTypes();
    const sections = groups.map((type) => {
      const materials = sortByMode(state.materials[type] || [], materialSort);
      const rows = materials.map((material, index) => materialRowHtml(type, material, index)).join("");
      return `
        <section class="catalog-section material-section">
          <div class="toolbar catalog-dealer">
            <div>
              <h2>${esc(type)}</h2>
              <div class="muted">${materials.length} материалов · общие для всех проектов и контрагентов</div>
            </div>
            <div class="toolbar-actions glass-button-group">
              <button data-action="add-material" data-type="${esc(type)}">Добавить материал</button>
              <button class="ghost" data-action="rename-material-group" data-type="${esc(type)}">Переименовать тип</button>
              <button class="ghost danger" data-action="delete-material-group" data-type="${esc(type)}">Удалить тип</button>
            </div>
          </div>
          <section class="table-wrap project-products">
            <table>
              <thead><tr><th>Номер</th><th>Материал</th><th>Ед.</th><th>Себестоимость</th><th>Ед. измерения</th><th>Текстура</th><th></th></tr></thead>
              <tbody>${rows || materialEmptyRowHtml(type)}</tbody>
            </table>
          </section>
        </section>
      `;
    }).join("");
    app.innerHTML = shell(`
      <div class="toolbar">
        <div>
          <h2>Материалы</h2>
          <div class="muted">Единая база материалов для всех проектов, контрагентов и каталога изделий.</div>
        </div>
        <div class="toolbar-actions glass-button-group">
          <button class="ghost" data-action="toggle-material-sort">${materialSort === "name" ? "По названию" : "По дате создания"}</button>
          <button data-action="add-material-group">Добавить тип</button>
        </div>
      </div>
      ${sections || `<section class="empty">${emptyStateHtml({ title: "Типов материалов нет", text: "Добавьте первый тип материалов.", action: "add-material-group", actionText: "Добавить тип" })}</section>`}
    `);
  }

  function renderProjectPage() {
    const project = getProject();
    const totals = projectTotals(project);
    const rows = project.products.map((product, index) => {
      const total = productTotals(product);
      return `
        <tr>
          <td>${index + 1}</td>
          <td><button class="ghost" data-action="edit-product" data-id="${product.id}">${esc(product.name)}</button></td>
          <td><input class="qty-input" data-action="product-qty" data-id="${product.id}" type="number" min="1" value="${productQty(product)}"></td>
          <td class="nowrap">${fmt(total.area)} м²</td>
          <td class="nowrap">${money(total.cost)}</td>
          <td><button class="ghost danger icon-btn" title="Удалить" data-action="delete-product" data-id="${product.id}">×</button></td>
        </tr>
      `;
    }).join("");
    const payrollRows = (project.payrolls || []).map((row) => `
      <tr>
        <td>${row.mode === "fixed" ? "Произвольно" : row.mode === "glass-polish" ? "Шлифовка стекла" : "За кв.м."}</td>
        <td>${esc(row.note || (row.mode === "glass-polish" ? `Грани стекла: ${fmt(totals.glassEdge / 1000)} пог. м` : row.mode === "area" ? (row.scope === "selected" ? "За выбранные позиции" : "За все ДСП") : "-"))}</td>
        <td class="nowrap">${money(payrollAmount(project, row))}</td>
        <td><button class="ghost danger icon-btn" data-action="delete-payroll" data-id="${row.id}">×</button></td>
      </tr>
    `).join("");

    app.innerHTML = shell(`
      <div class="toolbar">
        <div>
          <button class="ghost back-button" data-action="go-projects">← Назад</button>
          <h2>${esc(project.name)}</h2>
          <div class="muted">${esc(project.counterparty)}${project.address ? " · " + esc(project.address) : ""}</div>
        </div>
        <div class="toolbar-actions glass-button-group">
          <button data-action="add-product">Добавить изделие</button>
          <button class="ghost" data-action="add-payroll">Добавить ЗП</button>
          <button class="ghost" data-action="open-cut">Создать карту кроя</button>
          <button class="ghost" data-action="open-details">Создать деталировку</button>
        </div>
      </div>
      <section class="stats">
            <div class="stat"><span>Квадратура ДСП/ДВП</span><strong>${fmt(totals.area)} м²</strong></div>
            <div class="stat"><span>Себестоимость материалов</span><strong>${money(totals.productCost)}</strong></div>
            <div class="stat"><span>Итого с ЗП</span><strong>${money(totals.cost)}</strong></div>
          </section>
          <section class="table-wrap">
            <table>
          <thead><tr><th>Номер</th><th>Название</th><th>Кол-во</th><th>Квадратура</th><th>Себестоимость</th><th></th></tr></thead>
          <tbody>${rows || `<tr><td colspan="6">${emptyStateHtml({ title: "Изделий пока нет", text: "Добавьте изделие в проект, чтобы посчитать квадратуру и себестоимость.", action: "add-product", actionText: "Добавить изделие", compact: true })}</td></tr>`}</tbody>
        </table>
      </section>
      <h3>Зарплата</h3>
      <section class="table-wrap">
        <table>
          <thead><tr><th>Тип</th><th>Описание</th><th>Сумма</th><th></th></tr></thead>
          <tbody>${payrollRows || `<tr><td colspan="4">${emptyStateHtml({ title: "Начислений пока нет", text: "Добавьте ЗП произвольно или по квадратуре изделий.", action: "add-payroll", actionText: "Добавить ЗП", compact: true })}</td></tr>`}</tbody>
        </table>
      </section>
    `);
  }

  function renderProductEditor() {
    const project = getProject();
    const catalogMode = isCatalogRoute();
    const product = catalogMode ? (getCatalogProduct() || createCatalogDraft()) : (getProduct(project) || createProductDraft());
    const fixedSizes = normalizeFixedSizes(product);
    ensureProductEditorSnapshot(product);
    const catalogCopyMissing = !catalogMode && Boolean(route.productId) && !matchingCatalogProduct(project, product);
    const totals = productTotals(product);
    const detailRows = product.details.map((detail) => detailRowHtml(product, detail)).join("");
    const fixedSizeRows = fixedSizes.map((row, index) => `
      <div class="fixed-size-row" data-fixed-size-index="${index}">
        <input data-fixed-field="name" value="${esc(row.name)}" placeholder="Название">
        <input data-fixed-field="value" type="number" value="${Number(row.value) || 0}" placeholder="Размер, мм">
      </div>
    `).join("");

    app.innerHTML = shell(`
      <div class="toolbar">
        <div>
          <button class="ghost back-button" data-action="${catalogMode ? "back-catalog" : "back-project"}" data-tooltip="${catalogMode ? "Вернуться в каталог" : "Вернуться в проект"}">← ${catalogMode ? "К каталогу" : "К проекту"}</button>
          <h2>${route.productId ? "Карточка изделия" : "Новое изделие"}</h2>
          ${catalogMode ? `<div class="muted">${esc(route.counterparty || "")}</div>` : ""}
        </div>
        <div class="toolbar-actions glass-button-group">
          ${catalogCopyMissing ? '<button class="ghost" data-action="save-copy-to-catalog">Сохранить копию в каталог</button>' : ""}
          <button data-action="save-product">${route.productId ? "Сохранить" : "Создать"}</button>
        </div>
      </div>
      <section class="product-layout">
        <aside class="card">
          <div class="image-box ${product.image ? "has-image" : ""}">${product.image ? `<img src="${product.image}" alt="">` : "Изображение изделия"}</div>
          <div class="row-actions image-actions glass-button-group" style="margin-top:12px">
            <button class="ghost" data-action="attach-image" data-tooltip="Добавить изображение изделия">Прикрепить</button>
            <button class="ghost danger" data-action="remove-image" data-tooltip="Удалить изображение">Удалить</button>
            <input id="image-input" type="file" accept="image/*" hidden>
          </div>
          <div class="stats product-stats" style="grid-template-columns:1fr; margin:14px 0 0">
            <div class="stat"><span>Квадратура ДСП/ДВП</span><strong data-live-product-area>${fmt(totals.area)} м²</strong></div>
            <div class="stat"><span>Грани стекла</span><strong data-live-glass-edge>${fmt(totals.glassEdge, 0)} мм</strong></div>
            <div class="stat"><span>Себестоимость</span><strong data-live-product-cost>${money(totals.cost)}</strong></div>
          </div>
        </aside>
        <div class="product-main">
        <section class="card">
          <div class="form-grid">
            <div class="field full"><label>Название</label><input id="product-name" value="${esc(product.name)}"></div>
            ${catalogMode ? "" : `<div class="field"><label>Количество в проекте</label><input id="product-qty" type="number" min="1" value="${productQty(product)}"></div>`}
            <div class="field"><label>Длина, мм</label><input id="product-length" type="number" value="${product.length || 0}"></div>
            <div class="field"><label>Глубина, мм</label><input id="product-depth" type="number" value="${product.depth || 0}"></div>
            <div class="field"><label>Высота, мм</label><input id="product-height" type="number" value="${product.height || 0}"></div>
            <div class="field">
              <label>Ножки</label>
              <div class="check-row"><input id="product-has-legs" type="checkbox" ${product.hasLegs ? "checked" : ""}><span>Есть ножки</span></div>
            </div>
            <div class="field"><label>Высота ножки, мм</label><input id="product-leg-height" type="number" value="${product.legHeight || 0}"></div>
          </div>
        </section>
        <section class="card fixed-sizes-card fixed-sizes-card-inline">
          <div class="section-header">
            <div>
              <h2>Фиксированные размеры</h2>
              <div class="muted">Сервисные значения для формул. В квадратуру и себестоимость не входят.</div>
            </div>
          </div>
          <div class="fixed-size-grid">
            ${fixedSizeRows}
          </div>
        </section>
        </div>
      </section>
      <div class="toolbar" style="margin-top:18px">
        <h2>Детали</h2>
      </div>
      <section class="card fixed-sizes-card">
        <div class="section-header">
          <div>
            <h2>Фиксированные размеры</h2>
            <div class="muted">Сервисные значения для формул. В квадратуру и себестоимость не входят.</div>
          </div>
        </div>
        <div class="fixed-size-grid">
          ${fixedSizeRows}
        </div>
      </section>
      <div class="toolbar fixed-sizes-detail-title" style="margin-top:18px">
        <h2>Детали</h2>
      </div>
      <section class="table-wrap detail-table-wrap">
        <table class="detail-table">
          <thead><tr><th>Название</th><th>Кол-во</th><th>Тип</th><th>Материал</th><th>Длина</th><th>Ширина</th><th>Кромка</th><th>Расчет</th><th></th></tr></thead>
          <tbody>${detailRows || detailEmptyRowHtml()}</tbody>
        </table>
      </section>
      <div class="detail-add-row">
        <div class="glass-button-group">
          <button data-action="add-detail" data-tooltip="Добавить деталь">+ Добавить деталь</button>
        </div>
      </div>
    `);
    requestAnimationFrame(updateDetailTableOverflow);
  }

  function detailRowHtml(product, detail) {
    const materialOptions = sortByMode(state.materials[detail.type] || [], "name").map((material) => ({ value: material.id, label: material.name }));
    const typeOptions = getMaterialTypes().map((type) => ({ value: type, label: type }));
    const unit = detailCalc(product, detail);
    const materialUnit = unit.material?.unit || "pc";
    const showSize = materialUnit === "m2";
    const showLengthOnly = materialUnit === "lm";
    const showEdge = showSize && isDspMaterial(detail, unit.material);
    return `
      <tr data-detail-id="${detail.id}" ${detail.generatedEdgeFor ? `data-edge-for="${detail.generatedEdgeFor}"` : ""}>
        <td>
          <div class="detail-name-cell">
            <button class="drag-handle" type="button" draggable="true" data-drag-detail="${detail.id}" data-tooltip="Перетащить деталь">☰</button>
            <input data-field="detail-name" value="${esc(detail.name)}" placeholder="Боковина">
          </div>
        </td>
        <td><input data-field="detail-qty" type="number" min="1" value="${detail.qty || 1}"></td>
        <td>${glassSelectHtml({ key: `detail-type-${detail.id}`, field: "detail-type", value: detail.type, options: typeOptions, kind: "detail-type", detailId: detail.id, canAdd: true, canDelete: true, canRename: true })}</td>
        <td>
          ${glassSelectHtml({ key: `detail-material-${detail.id}`, field: "detail-material", value: detail.materialId, options: materialOptions, kind: "material", type: detail.type, detailId: detail.id, canAdd: true, canDelete: true, canRename: true })}
        </td>
        <td>${showSize || showLengthOnly ? `<div class="formula-wrap"><textarea class="formula-input" rows="1" data-field="detail-length" data-axis="length" placeholder="240 или =">${esc(detail.lengthExpr)}</textarea></div>` : `<span class="muted">по кол-ву</span>`}</td>
        <td>${showSize ? `<div class="formula-wrap"><textarea class="formula-input" rows="1" data-field="detail-width" data-axis="width" placeholder="240 или =">${esc(detail.widthExpr)}</textarea></div>` : `<span class="muted">${unitLabel(materialUnit)}</span>`}</td>
        <td>${showEdge ? `<button class="ghost edge-button" type="button" data-action="open-edge-menu" data-id="${detail.id}">Кромка</button>` : ""}</td>
        <td class="nowrap" data-calc-cell>${detailCalcText(unit)}</td>
        <td><button class="ghost danger icon-btn" data-action="remove-detail" data-id="${detail.id}" data-tooltip="Удалить деталь">×</button></td>
      </tr>
    `;
  }

  function detailCalcText(calc) {
    if (calc.material?.unit === "m2") return `${fmt(calc.displayArea)} м²<br><span class="muted">${fmt(calc.length, 0)} × ${fmt(calc.width, 0)}</span>`;
    if (calc.material?.unit === "lm") return `${fmt(calc.usage)} ${unitLabel("lm")}<br><span class="muted">${fmt(calc.length, 0)} мм</span>`;
    return `${fmt(calc.usage, 0)} ${unitLabel("pc")}`;
  }

  function detailEmptyRowHtml() {
    return `<tr data-empty-details><td colspan="9">${emptyStateHtml({ title: "Деталей пока нет", text: "Создайте первую деталь изделия и укажите материал, размеры и количество.", action: "add-detail", actionText: "Добавить деталь", compact: true })}</td></tr>`;
  }

  function replaceDetailRow(product, detailId) {
    const detail = product.details.find((item) => item.id === detailId);
    const row = document.querySelector(`[data-detail-id="${detailId}"]`);
    if (!detail || !row) return;
    row.outerHTML = detailRowHtml(product, detail);
    scheduleProductCalculations(product);
  }

  function openEdgeMenu(button) {
    const product = currentEditableProduct();
    updateProductFromForm(product);
    const detail = product.details.find((item) => item.id === button.dataset.id);
    if (!detail) return;
    const materials = linearMaterials();
    if (!materials.length) {
      notify("Нет материала для кромки", "Добавьте материал с единицей м.пог");
      return;
    }
    detail.edge ||= { top: false, right: false, bottom: false, left: false, materialId: materials[0].id };
    if (!detail.edge.materialId) detail.edge.materialId = materials[0].id;
    const rect = button.getBoundingClientRect();
    contextMenu = {
      type: "edge",
      detailId: detail.id,
      x: Math.min(rect.left + window.scrollX, window.scrollX + window.innerWidth - 320),
      y: rect.bottom + window.scrollY + 8
    };
    render();
  }

  function toggleEdgeSide(button) {
    const product = currentEditableProduct();
    const detail = product.details.find((item) => item.id === button.dataset.id);
    if (!detail) return;
    const side = button.dataset.side;
    if (!["top", "right", "bottom", "left"].includes(side)) return;
    const edge = detail.edge || {};
    detail.edge = {
      materialId: edge.materialId || linearMaterials()[0]?.id || "",
      top: side === "top" ? !Boolean(edge.top) : Boolean(edge.top),
      right: side === "right" ? !Boolean(edge.right) : Boolean(edge.right),
      bottom: side === "bottom" ? !Boolean(edge.bottom) : Boolean(edge.bottom),
      left: side === "left" ? !Boolean(edge.left) : Boolean(edge.left)
    };
    saveStateSoon();
    const active = Boolean(detail.edge[side]);
    button.classList.toggle("active", active);
    button.setAttribute("aria-pressed", active ? "true" : "false");
  }

  function setEdgeMaterial(detailId, materialId) {
    const product = currentEditableProduct();
    const detail = product.details.find((item) => item.id === detailId);
    if (!detail) return;
    detail.edge ||= {};
    detail.edge.materialId = materialId;
    saveStateSoon();
  }

  function edgeLengthForDetail(product, detail) {
    const calc = detailCalc(product, detail);
    const edge = detail.edge || {};
    let length = 0;
    if (edge.top) length += calc.length;
    if (edge.bottom) length += calc.length;
    if (edge.left) length += calc.width;
    if (edge.right) length += calc.width;
    return length * (Number(detail.qty) || 1);
  }

  function totalEdgeLengthForMaterial(product, materialId) {
    return (product.details || [])
      .filter((detail) => !detail.generatedEdgeFor && detail.edge?.materialId === materialId)
      .reduce((sum, detail) => sum + edgeLengthForDetail(product, detail), 0);
  }

  function applyEdge(detailId) {
    const product = currentEditableProduct();
    updateProductFromForm(product);
    const detail = product.details.find((item) => item.id === detailId);
    if (!detail?.edge?.materialId) return;
    const edgeLength = edgeLengthForDetail(product, detail);
    const edgeMaterial = linearMaterials().find((material) => material.id === detail.edge.materialId);
    if (!edgeMaterial || edgeLength <= 0) {
      notify("Кромка не выбрана", "Выберите стороны и материал");
      return;
    }
    const edgeType = edgeMaterial.type || materialGroupById(edgeMaterial.id);
    const totalLength = totalEdgeLengthForMaterial(product, edgeMaterial.id);
    let edgeDetail = product.details.find((item) => item.generatedEdgeMaterialId === edgeMaterial.id || (item.generatedEdgeFor && item.materialId === edgeMaterial.id));
    if (!edgeDetail) {
      edgeDetail = {
        id: uid(),
        name: `Кромка: ${edgeMaterial.name}`,
        qty: 1,
        type: edgeType,
        materialId: edgeMaterial.id,
        lengthExpr: String(Math.round(totalLength)),
        widthExpr: "",
        generatedEdgeMaterialId: edgeMaterial.id
      };
      product.details.push(edgeDetail);
      const tbody = document.querySelector(".detail-table tbody");
      tbody?.insertAdjacentHTML("beforeend", detailRowHtml(product, edgeDetail));
    } else {
      edgeDetail.type = edgeType;
      edgeDetail.name = `Кромка: ${edgeMaterial.name}`;
      edgeDetail.lengthExpr = String(Math.round(totalLength));
      edgeDetail.qty = 1;
      edgeDetail.generatedEdgeMaterialId = edgeMaterial.id;
      delete edgeDetail.generatedEdgeFor;
      replaceDetailRow(product, edgeDetail.id);
    }
    saveState();
    contextMenu = null;
    scheduleProductCalculations(product);
    renderQuiet();
    notify("Кромка применена", `${fmt(edgeLength, 0)} мм`);
  }

  function updateDetailTableOverflow() {
    const wrap = document.querySelector(".detail-table-wrap");
    const addRow = document.querySelector(".detail-add-row");
    if (!wrap) return;
    wrap.classList.remove("is-overflowing");
    addRow?.classList.remove("is-overflowing");
    const table = wrap.querySelector(".detail-table");
    if (!table) return;
    const overflowing = table.scrollWidth > wrap.clientWidth + 1;
    wrap.classList.toggle("is-overflowing", overflowing);
    addRow?.classList.toggle("is-overflowing", overflowing);
  }

  function renderCutPage() {
    const project = getProject();
    if (!cutState) cutState = buildCutState(project);
    const summary = cutSummaryStats(cutState);
    app.innerHTML = shell(`
      <div class="toolbar">
        <div>
          <button class="ghost back-button" data-action="back-project">← К проекту</button>
          <h2>Карта кроя</h2>
          <div class="muted">${esc(project.name)}</div>
        </div>
        <div class="toolbar-actions glass-button-group">
          <button class="ghost" data-action="relayout-cut">Пересчитать раскрой</button>
          <button data-action="print-cut">PDF / Печать</button>
        </div>
      </div>
      <section class="cut-panel">
        <aside class="card cut-controls">
          <div class="field"><label>Расстояние между деталями, мм</label><input id="cut-gap" type="number" min="0" value="${cutState.gap}"></div>
          <div class="field"><label>Подрезка края листа, мм</label><input id="cut-trim" type="number" min="0" value="${cutState.trim}"></div>
          <div class="field"><label>Минимальная сторона остатка, мм</label><input id="cut-keep" type="number" min="0" value="${cutState.keepSize}"></div>
          <div class="cut-summary">
            <strong>Гильотинный раскрой</strong>
            <span>3 стадии · поворот 0°/90° · направление текстуры учитывается</span>
          </div>
          <p class="muted">В раскрой попадают все материалы с размером листа. Детали разных материалов размещаются на отдельных листах.</p>
          <div class="cut-summary cut-summary-totals">
            <strong>Итоги раскроя</strong>
            ${summary.byMaterial.map((item) => `<span>${esc(item.name)}: ${item.count} л.</span>`).join("")}
            <span>Всего листов: ${summary.sheetCount}</span>
            <span>Площадь деталей: ${fmt(summary.partsArea)} м²</span>
            <span>Полезных остатков: ${summary.leftoverCount}</span>
            <span>Площадь остатков: ${fmt(summary.leftoverArea)} м²</span>
          </div>
        </aside>
        <div class="canvas-wrap"><canvas id="cut-canvas" width="1200" height="800"></canvas></div>
      </section>
    `);
    bindGlobal();
    drawCutCanvas();
  }

  function cutSummaryStats(model) {
    const byMaterial = model.groups.map((group) => ({ name: group.material.name, count: group.sheets.length }));
    const partsArea = model.groups.reduce((sum, group) => sum + group.parts.reduce((area, part) => area + part.w * part.h, 0), 0) / 1000000;
    const kept = model.groups.flatMap((group) => group.sheets.flatMap((sheet) => (sheet.leftovers || []).filter((item) => item.kept)));
    return {
      byMaterial,
      sheetCount: byMaterial.reduce((sum, item) => sum + item.count, 0),
      partsArea,
      leftoverCount: kept.length,
      leftoverArea: kept.reduce((sum, item) => sum + item.w * item.h, 0) / 1000000
    };
  }

  function renderDetailsPage() {
    const project = getProject();
    const tabs = project.products.map((product, index) => {
      const qty = productQty(product);
      const label = `${product.name}${qty > 1 ? ` × ${qty}` : ""}`;
      return `<button class="${route.productId === product.id || (!route.productId && index === 0) ? "active" : "ghost"}" data-action="details-product" data-id="${product.id}">${esc(label)}</button>`;
    }).join("");
    const product = getProduct(project, route.productId) || project.products[0];
    const rows = product ? product.details.map((detail) => {
      const calc = detailCalc(product, detail);
      return `<tr><td>${esc(detail.name)}</td><td>${calc.qty}</td><td>${esc(calc.material?.name || "")}</td><td>${fmt(calc.length, 0)} × ${fmt(calc.width, 0)}</td><td>${fmt(calc.area)} м²</td></tr>`;
    }).join("") : "";
    app.innerHTML = shell(`
      <div class="toolbar">
        <div>
          <button class="ghost back-button" data-action="back-project">← К проекту</button>
          <h2>Деталировка</h2>
        </div>
        <div class="toolbar-actions glass-button-group"><button data-action="export-details">Экспорт Excel</button></div>
      </div>
      <div class="tabs">${tabs || `<span class="muted">Изделий нет</span>`}</div>
      ${product ? `
        <section class="detail-report">
          <div>${product.image ? `<img src="${product.image}" alt="">` : `<div class="image-box">Без изображения</div>`}</div>
          <div>
            <h2>${esc(product.name)}</h2>
            <div class="meta-line">
              <span>${fmt(product.length, 0)} × ${fmt(product.depth, 0)} × ${fmt(product.height, 0)} мм</span>
              <span>${fmt(productTotals(product).area)} м²</span>
            </div>
          </div>
        </section>
        <section class="table-wrap">
          <table id="details-table">
            <thead><tr><th>Деталь</th><th>Кол-во</th><th>Материал</th><th>Размер</th><th>Квадратура</th></tr></thead>
            <tbody>${rows || `<tr><td colspan="5">${emptyStateHtml({ title: "Деталей нет", text: "Вернитесь в карточку изделия и добавьте детали для деталировки.", action: "back-project", actionText: "К проекту", compact: true })}</td></tr>`}</tbody>
          </table>
        </section>` : `<section class="empty">${emptyStateHtml({ title: "Изделий нет", text: "Добавьте изделие в проект, чтобы сформировать деталировку.", action: "back-project", actionText: "К проекту" })}</section>`}
    `);
  }

  function createProductDraft() {
    const project = getProject();
    if (!project.draft) {
      project.draft = { id: uid(), name: "", image: "", length: 600, depth: 500, height: 720, hasLegs: false, legHeight: 100, fixedSizes: [], details: [] };
    }
    normalizeFixedSizes(project.draft);
    return project.draft;
  }

  function createCatalogDraft() {
    state.catalog ||= {};
    const counterparty = route.counterparty || state.counterparties[0] || "Контрагент";
    state.catalog[counterparty] ||= [];
    state.catalogDrafts ||= {};
    if (!state.catalogDrafts[counterparty]) {
      state.catalogDrafts[counterparty] = { id: uid(), name: "", image: "", qty: 1, length: 600, depth: 500, height: 720, hasLegs: false, legHeight: 100, fixedSizes: [], details: [] };
    }
    normalizeFixedSizes(state.catalogDrafts[counterparty]);
    return state.catalogDrafts[counterparty];
  }

  function currentEditableProduct() {
    if (isCatalogRoute()) return getCatalogProduct() || createCatalogDraft();
    return getProduct(getProject()) || createProductDraft();
  }

  function updateProductFromForm(product) {
    updateProductBasics(product);
    document.querySelectorAll("[data-detail-id]").forEach((row) => updateDetailFromRow(row, product));
  }

  function updateProductBasics(product) {
    product.name = document.getElementById("product-name")?.value.trim() || "Изделие без названия";
    product.length = Number(document.getElementById("product-length")?.value) || 0;
    product.depth = Number(document.getElementById("product-depth")?.value) || 0;
    product.height = Number(document.getElementById("product-height")?.value) || 0;
    product.qty = Math.max(1, Number(document.getElementById("product-qty")?.value) || productQty(product));
    product.hasLegs = Boolean(document.getElementById("product-has-legs")?.checked);
    product.legHeight = Number(document.getElementById("product-leg-height")?.value) || 0;
    product.fixedSizes = Array.from({ length: 6 }, (_, index) => {
      const row = document.querySelector(`[data-fixed-size-index="${index}"]`);
      return {
        name: row?.querySelector('[data-fixed-field="name"]')?.value.trim() || "",
        value: Number(row?.querySelector('[data-fixed-field="value"]')?.value) || 0
      };
    });
  }

  function updateDetailFromRow(row, product) {
    const detail = product.details.find((item) => item.id === row.dataset.detailId);
    if (!detail) return;
    const fieldValue = (field, fallback = "") => row.querySelector(`[data-field="${field}"]`)?.value ?? fallback;
    detail.name = fieldValue("detail-name", detail.name);
    detail.qty = Number(fieldValue("detail-qty", detail.qty)) || 1;
    detail.type = fieldValue("detail-type", detail.type);
    detail.materialId = fieldValue("detail-material", detail.materialId);
    if (!getMaterial(detail.type, detail.materialId)) {
      detail.materialId = state.materials[detail.type]?.[0]?.id || "";
    }
    detail.lengthExpr = fieldValue("detail-length", detail.lengthExpr);
    detail.widthExpr = fieldValue("detail-width", detail.widthExpr);
  }

  function refreshProductCalculations(product) {
    document.querySelectorAll("[data-detail-id]").forEach((row) => {
      const detail = product.details.find((item) => item.id === row.dataset.detailId);
      const cell = row.querySelector("[data-calc-cell]");
      if (!detail || !cell) return;
      const calc = detailCalc(product, detail);
      cell.innerHTML = detailCalcText(calc);
    });
    const totals = productTotals(product);
    const area = document.querySelector("[data-live-product-area]");
    const glassEdge = document.querySelector("[data-live-glass-edge]");
    const cost = document.querySelector("[data-live-product-cost]");
    if (area) area.textContent = `${fmt(totals.area)} м²`;
    if (glassEdge) glassEdge.textContent = `${fmt(totals.glassEdge, 0)} мм`;
    if (cost) cost.textContent = money(totals.cost);
    requestAnimationFrame(updateDetailTableOverflow);
  }

  function scheduleProductCalculations(product) {
    if (refreshTimer) cancelAnimationFrame(refreshTimer);
    refreshTimer = requestAnimationFrame(() => {
      refreshTimer = null;
      refreshProductCalculations(product);
    });
  }

  function showFormulaCommand(input, product) {
    document.querySelector(".formula-command")?.remove();
    activeFormulaInput = input;
    if (!input.value.trim().startsWith("=")) return;
    const row = input.closest("[data-detail-id]");
    const currentDetailId = row?.dataset.detailId || "";
    const productRefs = [
      { label: "Длина", expr: "длина изделия", value: product.length },
      { label: "Глубина", expr: "глубина изделия", value: product.depth },
      { label: "Высота", expr: "высота изделия", value: product.height },
      { label: "Ножка", expr: "высота ножки", value: product.hasLegs ? product.legHeight : 0 }
    ];
    const fixedRefs = normalizeFixedSizes(product)
      .filter((item) => String(item.name || "").trim())
      .map((item) => ({
        name: item.name,
        label: `${item.name} размер`,
        refs: [{ label: "Размер", expr: `${item.name} размер`, value: item.value }]
      }));
    const detailRefs = (product.details || [])
      .filter((detail) => detail.id !== currentDetailId)
      .map((detail) => {
        const name = detail.name || "Деталь";
        const calc = detailCalc(product, detail, new Set([currentDetailId]));
        return {
          name,
          label: `${name} длина ширина`,
          refs: [
            { label: "Длина", expr: `${name} длина`, value: calc.length },
            { label: "Ширина", expr: `${name} ширина`, value: calc.width }
          ]
        };
      });
    const rect = input.getBoundingClientRect();
    const panel = document.createElement("div");
    panel.className = "formula-command";
    panel.style.left = `${rect.left + window.scrollX}px`;
    panel.style.top = `${rect.bottom + window.scrollY + 8}px`;
    panel.style.minWidth = `${Math.ceil(rect.width)}px`;
    panel.innerHTML = `
      <input class="formula-command-search" type="search" placeholder="Поиск ссылки" autocomplete="off">
      <div class="formula-command-title">Изделие</div>
      <div class="formula-command-row formula-command-product" data-formula-row data-label="${esc(`${product.name || "Изделие"} длина глубина высота ножка`.toLowerCase())}">
        <span class="formula-command-name">${esc(product.name || "Изделие")}</span>
        <div class="formula-command-actions">
          ${productRefs.map((ref) => `
            <button class="formula-command-action" type="button" data-action="formula-ref" data-expr="${esc(ref.expr)}" data-label="${esc(`${product.name || "Изделие"} ${ref.label}`.toLowerCase())}">
              <span>${esc(ref.label)}</span>
              <small>${fmt(ref.value, 0)} мм</small>
            </button>
          `).join("")}
        </div>
      </div>
      <div class="formula-command-list">
        ${fixedRefs.length ? `
          <div class="formula-command-title">Фиксированные размеры</div>
          ${fixedRefs.map((item, index) => `
            ${index ? `<div class="formula-command-separator" aria-hidden="true"></div>` : ""}
            <div class="formula-command-row" data-formula-row data-label="${esc(item.label.toLowerCase())}">
              <span class="formula-command-name">${esc(item.name)}</span>
              <div class="formula-command-actions">
                ${item.refs.map((ref) => `
                  <button class="formula-command-action" type="button" data-action="formula-ref" data-expr="${esc(ref.expr)}" data-label="${esc(`${item.name} ${ref.label}`.toLowerCase())}">
                    <span>${esc(ref.label)}</span>
                    <small>${fmt(ref.value, 0)} мм</small>
                  </button>
                `).join("")}
              </div>
            </div>
          `).join("")}
        ` : ""}
        <div class="formula-command-title">Детали</div>
        ${detailRefs.map((detail, index) => `
          ${index ? `<div class="formula-command-separator" aria-hidden="true"></div>` : ""}
          <div class="formula-command-row" data-formula-row data-label="${esc(detail.label.toLowerCase())}">
            <span class="formula-command-name">${esc(detail.name)}</span>
            <div class="formula-command-actions">
              ${detail.refs.map((ref) => `
                <button class="formula-command-action" type="button" data-action="formula-ref" data-expr="${esc(ref.expr)}" data-label="${esc(`${detail.name} ${ref.label}`.toLowerCase())}">
                  <span>${esc(ref.label)}</span>
                  <small>${fmt(ref.value, 0)} мм</small>
                </button>
              `).join("")}
            </div>
          </div>
        `).join("")}
        ${detailRefs.length ? "" : `<div class="formula-command-empty">Других деталей пока нет</div>`}
      </div>
    `;
    const search = panel.querySelector(".formula-command-search");
    search?.addEventListener("input", () => {
      const query = search.value.trim().toLowerCase();
      panel.querySelectorAll("[data-formula-row]").forEach((item) => {
        item.hidden = Boolean(query && !item.dataset.label.includes(query));
      });
      panel.querySelectorAll(".formula-command-separator").forEach((separator) => {
        let next = separator.nextElementSibling;
        while (next && !next.matches("[data-formula-row]")) next = next.nextElementSibling;
        separator.classList.toggle("is-hidden", !next || next.hidden);
      });
    });
    panel.onmousedown = (event) => {
      const button = event.target.closest('[data-action="formula-ref"]');
      if (button) {
        event.preventDefault();
        insertFormulaReference(button);
      }
    };
    document.body.appendChild(panel);
  }

  function insertFormulaReference(button) {
    const active = document.querySelector(".formula-input:focus") || activeFormulaInput;
    if (!active) return;
    const expr = button.dataset.expr || "";
    const value = active.value || "";
    let start = Number.isInteger(active.selectionStart) ? active.selectionStart : value.length;
    let end = Number.isInteger(active.selectionEnd) ? active.selectionEnd : start;
    if (/[+\-*/]\s*$/.test(value)) {
      start = value.length;
      end = value.length;
    }

    let nextValue;
    let nextCursor;
    if (value.trim() === "=") {
      nextValue = `= ${expr}`;
      nextCursor = nextValue.length;
    } else {
      const before = value.slice(0, start);
      const after = value.slice(end);
      const prefix = before && !/\s$/.test(before) ? " " : "";
      const suffix = after && !/^\s/.test(after) ? " " : "";
      nextValue = `${before}${prefix}${expr}${suffix}${after}`;
      nextCursor = before.length + prefix.length + expr.length;
    }

    active.value = nextValue;
    active.setSelectionRange?.(nextCursor, nextCursor);
    const product = currentEditableProduct();
    const row = active.closest("[data-detail-id]");
    if (row) updateDetailFromRow(row, product);
    saveState();
    refreshProductCalculations(product);
    document.querySelector(".formula-command")?.remove();
    active.focus();
    active.setSelectionRange?.(nextCursor, nextCursor);
  }

  function modalHtml() {
    const modalClass = `modal${suppressRenderMotion ? " no-animate" : ""}`;
    const backdropClass = `modal-backdrop${suppressRenderMotion ? " no-animate" : ""}`;
    if (modal.type === "unsaved-product") {
      return `
        <div class="${backdropClass}">
          <div class="${modalClass}">
            <header><h3>Есть несохраненные изменения</h3><button class="ghost icon-btn" type="button" data-action="cancel-product-exit">×</button></header>
            <main>
              <p class="muted">Сохранить изменения в карточке изделия перед выходом?</p>
            </main>
            <footer>
              <button class="ghost" type="button" data-action="discard-product-exit">Выйти без сохранения</button>
              <button class="ghost" type="button" data-action="cancel-product-exit">Отмена</button>
              <button type="button" data-action="save-product-exit">Сохранить</button>
            </footer>
          </div>
        </div>
      `;
    }
    if (modal.type === "project") {
      const options = state.counterparties.map((name) => ({ value: name, label: name }));
      const selectedCounterparty = modal.counterparty || state.counterparties[0] || "Контрагент";
      return `
        <div class="${backdropClass}">
          <form class="${modalClass}" data-modal-form="project">
            <header><h3>Создать проект</h3><button class="ghost icon-btn" type="button" data-action="close-modal">×</button></header>
            <main class="form-grid">
              <div class="field full"><label>Название проекта</label><input name="name" value="${esc(modal.name || "")}" required autofocus></div>
              <div class="field full"><label>Контрагент</label>${glassSelectHtml({ key: "project-counterparty", name: "counterparty", value: selectedCounterparty, options, kind: "counterparty", canAdd: true, canDelete: true })}</div>
              <div class="field full"><label>Адрес</label><input name="address" value="${esc(modal.address || "")}"></div>
            </main>
            <footer><button class="ghost" type="button" data-action="close-modal">Отмена</button><button>Создать проект</button></footer>
          </form>
        </div>
      `;
    }
    if (modal.type === "productPicker") {
      const project = getProject();
      const catalogProducts = getCatalogProducts(project.counterparty);
      const rows = catalogProducts.map((product) => `<button class="ghost" data-action="duplicate-product" data-id="${product.id}" data-source-catalog="${esc(project.counterparty)}">${esc(product.name || "Изделие без названия")}<span class="muted">Каталог · ${esc(project.counterparty)}</span></button>`).join("");
      return `
        <div class="${backdropClass}">
          <div class="${modalClass}">
            <header><h3>Добавить изделие</h3><button class="ghost icon-btn" data-action="close-modal">×</button></header>
            <main>
              <div class="picker-list">${rows || `<span class="muted">Готовых изделий для этого контрагента пока нет.</span>`}</div>
            </main>
            <footer><button class="ghost" data-action="open-catalog">Открыть каталог</button><button data-action="create-product">Создать изделие в проекте</button></footer>
          </div>
        </div>
      `;
    }
    if (modal.type === "payroll") {
      const project = getProject();
      modal.payrollMode ||= "fixed";
      modal.payrollScope ||= "all-dsp";
      const detailChecks = allProjectDetails(project)
        .filter(({ detail, calc }) => isDspMaterial(detail, calc.material))
        .map(({ product, detail, calc }) => `<label class="check-row"><input type="checkbox" name="detailKeys" value="${product.id}:${detail.id}" checked><span>${esc(product.name)} · ${esc(detail.name || "Деталь")} · ${fmt(calc.area)} м²</span></label>`)
        .join("");
      return `
        <div class="${backdropClass}">
          <form class="${modalClass}" data-modal-form="payroll">
            <header><h3>Добавить ЗП</h3><button class="ghost icon-btn" type="button" data-action="close-modal">×</button></header>
            <main class="form-grid">
              <div class="field full">
                <label>Тип начисления</label>
                <select name="mode" data-payroll-mode>
                  <option value="fixed" ${modal.payrollMode === "fixed" ? "selected" : ""}>Произвольно</option>
                  <option value="area" ${modal.payrollMode === "area" ? "selected" : ""}>За кв.м.</option>
                  <option value="glass-polish" ${modal.payrollMode === "glass-polish" ? "selected" : ""}>Шлифовка стекла</option>
                </select>
              </div>
              ${modal.payrollMode === "fixed" ? `
                <div class="field full"><label>Сумма</label><input name="amount" type="number" value="${modal.amount || 0}"></div>
              ` : modal.payrollMode === "glass-polish" ? `
                <div class="field full"><label>Сумма за пог. метр</label><input name="rate" type="number" value="${modal.rate || 0}"></div>
                <div class="field full"><label>Грани стекла в проекте</label><input type="text" value="${fmt(projectTotals(project).glassEdge / 1000)} пог. м" disabled></div>
              ` : `
                <div class="field full"><label>Сумма за квадрат</label><input name="rate" type="number" value="${modal.rate || 0}"></div>
                <div class="field full">
                  <label>Начислять</label>
                  <select name="scope" data-payroll-scope>
                    <option value="all-dsp" ${modal.payrollScope === "all-dsp" ? "selected" : ""}>За все ДСП в проекте</option>
                    <option value="selected" ${modal.payrollScope === "selected" ? "selected" : ""}>За определенные позиции</option>
                  </select>
                </div>
                ${modal.payrollScope === "selected" ? `<div class="field full"><label>Детали</label><div class="check-list">${detailChecks || `<span class="muted">ДСП-деталей нет</span>`}</div></div>` : ""}
              `}
              <div class="field full"><label>Комментарий</label><input name="note" value="${esc(modal.note || "")}"></div>
            </main>
            <footer><button class="ghost" type="button" data-action="close-modal">Отмена</button><button>Добавить</button></footer>
          </form>
        </div>
      `;
    }
    return "";
  }

  function contextMenuHtml() {
    if (contextMenu.type === "edge") return edgeMenuHtml();
    const items = contextMenu.items.map((item) => {
      if (item.separator) return `<div class="context-separator" role="separator"></div>`;
      if (item.html) return item.html;
      return `<button class="${item.danger ? "danger-item" : ""}" data-action="${item.action}" ${item.id ? `data-id="${item.id}"` : ""}>${item.label}</button>`;
    }).join("");
    return `
      <div class="context-layer" data-action="close-context">
        <div class="context-menu ${contextMenu.className || ""}" role="menu" style="left:${contextMenu.x}px; top:${contextMenu.y}px">
          ${items}
        </div>
      </div>
    `;
  }

  function edgeMenuHtml() {
    const product = currentEditableProduct();
    const detail = product.details.find((item) => item.id === contextMenu.detailId);
    if (!detail) return "";
    const calc = detailCalc(product, detail);
    const edge = detail.edge || {};
    const materials = linearMaterials();
    const selectedMaterialId = edge.materialId || materials[0]?.id || "";
    const ratio = calc.length && calc.width ? Math.min(2.15, Math.max(.55, calc.length / calc.width)) : 1.4;
    const shapeHeight = 184;
    const shapeWidth = Math.round(shapeHeight * ratio);
    const menuWidth = Math.max(260, Math.min(380, shapeWidth + 54));
    return `
      <div class="context-layer edge-context-layer" data-action="close-context">
        <div class="context-menu edge-menu" role="menu" style="left:${contextMenu.x}px; top:${contextMenu.y}px; width:${menuWidth}px; --edge-shape-width:${shapeWidth}px;">
          <div class="edge-preview">
            <button type="button" class="edge-side edge-top ${edge.top ? "active" : ""}" data-action="toggle-edge-side" data-side="top" data-id="${detail.id}" aria-pressed="${edge.top ? "true" : "false"}"></button>
            <div class="edge-middle">
              <button type="button" class="edge-side edge-left ${edge.left ? "active" : ""}" data-action="toggle-edge-side" data-side="left" data-id="${detail.id}" aria-pressed="${edge.left ? "true" : "false"}"></button>
              <div class="edge-face" aria-hidden="true">
                <span class="edge-dimension edge-dimension-top">${fmt(calc.length, 0)} мм</span>
                <span class="edge-dimension edge-dimension-right">${fmt(calc.width, 0)} мм</span>
                <span class="edge-dimension edge-dimension-bottom">${fmt(calc.length, 0)} мм</span>
                <span class="edge-dimension edge-dimension-left">${fmt(calc.width, 0)} мм</span>
              </div>
              <button type="button" class="edge-side edge-right ${edge.right ? "active" : ""}" data-action="toggle-edge-side" data-side="right" data-id="${detail.id}" aria-pressed="${edge.right ? "true" : "false"}"></button>
            </div>
            <button type="button" class="edge-side edge-bottom ${edge.bottom ? "active" : ""}" data-action="toggle-edge-side" data-side="bottom" data-id="${detail.id}" aria-pressed="${edge.bottom ? "true" : "false"}"></button>
          </div>
          <select class="edge-material-select" data-action="select-edge-material" data-id="${detail.id}">
            ${materials.map((material) => `<option value="${material.id}" ${material.id === selectedMaterialId ? "selected" : ""}>${esc(material.name)}</option>`).join("")}
          </select>
          <button class="glass-select-add" type="button" data-action="apply-edge" data-id="${detail.id}">Применить кромку</button>
        </div>
      </div>
    `;
  }

  function openContextMenu(event, items) {
    event.preventDefault();
    const width = 220;
    const height = Math.max(44, items.length * 38 + 14);
    contextMenu = {
      x: Math.min(event.clientX, window.innerWidth - width - 10),
      y: Math.min(event.clientY, window.innerHeight - height - 10),
      items
    };
    render();
  }

  function themeSwitcherHtml() {
    return `
      <div class="theme-switcher" role="group" aria-label="Режим темы">
        <button type="button" class="${themeMode === "light" ? "active" : ""}" data-action="set-theme-mode" data-theme-mode="light" aria-label="Светлая тема" title="Светлая тема">
          <svg viewBox="0 0 24 24" focusable="false" aria-hidden="true"><circle cx="12" cy="12" r="4"/><path d="M12 2.5v3M12 18.5v3M4.6 4.6l2.1 2.1M17.3 17.3l2.1 2.1M2.5 12h3M18.5 12h3M4.6 19.4l2.1-2.1M17.3 6.7l2.1-2.1"/></svg>
        </button>
        <button type="button" class="${themeMode === "dark" ? "active" : ""}" data-action="set-theme-mode" data-theme-mode="dark" aria-label="Темная тема" title="Темная тема">
          <svg viewBox="0 0 24 24" focusable="false" aria-hidden="true"><path d="M20.2 15.4A8.4 8.4 0 0 1 8.6 3.8 8.7 8.7 0 1 0 20.2 15.4Z"/></svg>
        </button>
        <button type="button" class="${themeMode === "auto" ? "active" : ""}" data-action="set-theme-mode" data-theme-mode="auto" aria-label="Автоматически по системе" title="Автоматически по системе">Auto</button>
      </div>
    `;
  }

  function openSettingsMenu(button) {
    const rect = button.getBoundingClientRect();
    const width = 240;
    const items = [
      { html: themeSwitcherHtml() },
      { separator: true },
      { label: "Сохранить базу", action: "export-backup" },
      { label: "Загрузить базу", action: "import-backup" },
      { separator: true },
      { label: "Заменить фон", action: "replace-background" },
      { label: "Сбросить фон", action: "reset-background" }
    ];
    contextMenu = {
      x: Math.min(rect.right - width, window.innerWidth - width - 10),
      y: rect.bottom + 8,
      className: "settings-menu",
      items
    };
    render();
  }

  function bindGlobal() {
    app.onclick = (event) => {
      const button = event.target.closest("[data-action]");
      const action = button?.dataset.action || "";
      const panelAction = ["toggle-select", "select-value", "select-add", "select-delete", "select-rename", "formula-ref"].includes(action);
      const closedFloating = panelAction ? false : closeFloatingPanels(event.target);
      if (!button) {
        if (closedFloating) event.preventDefault();
        return;
      }
      const id = button.dataset.id;
      const keepContext = ["toggle-edge-side", "select-edge-material"].includes(action);
      if (action !== "close-context" && !keepContext) contextMenu = null;
      if (action === "close-context") { contextMenu = null; render(); return; }
      if (action === "noop") return;
      if (action === "toggle-select") { preserveProjectModalDraft(); openDropdown = openDropdown === button.dataset.key ? null : button.dataset.key; renderQuiet(); return; }
      if (action === "select-value") { handleSelectValue(button); return; }
      if (action === "select-add") { handleSelectAdd(button); return; }
      if (action === "select-delete") { handleSelectDelete(button); return; }
      if (action === "select-rename") { handleSelectRename(button); return; }
      if (action === "formula-ref") { insertFormulaReference(button); return; }
      if (action === "open-edge-menu") { openEdgeMenu(button); return; }
      if (action === "toggle-edge-side") { toggleEdgeSide(button); return; }
      if (action === "apply-edge") { applyEdge(button.dataset.id); return; }
      if (action === "cancel-product-exit") { pendingProductExit = null; modal = null; renderQuiet(); return; }
      if (action === "discard-product-exit") {
        const target = pendingProductExit;
        discardProductChanges();
        modal = null;
        runProductExitTarget(target);
        return;
      }
      if (action === "save-product-exit") {
        const target = pendingProductExit;
        modal = null;
        saveProduct(target);
        return;
      }
      if (action === "go-projects") { if (isProductEditorRoute()) { requestProductExit("projects"); return; } route = { name: "projects" }; modal = null; cutState = null; render(); }
      if (action === "crumb-project") { if (isProductEditorRoute()) { requestProductExit("project"); return; } route = { name: "project", projectId: route.projectId }; modal = null; cutState = null; render(); }
      if (action === "open-materials") { if (isProductEditorRoute()) { requestProductExit("materials"); return; } route = { name: "materials" }; modal = null; cutState = null; render(); }
      if (action === "open-catalog") { if (isProductEditorRoute()) { requestProductExit("catalog"); return; } route = { name: "catalog" }; modal = null; cutState = null; render(); }
      if (action === "toggle-catalog-counterparty") {
        const counterparty = button.dataset.counterparty;
        const willOpen = !openCatalogCounterparties.has(counterparty);
        clearTimeout(listSkeletonTimer);
        if (willOpen) openCatalogCounterparties.add(counterparty);
        else openCatalogCounterparties.delete(counterparty);
        listSkeletonKey = willOpen ? `catalog:${counterparty}` : "";
        route = { name: "catalog" };
        render();
        if (listSkeletonKey) {
          listSkeletonTimer = setTimeout(() => {
            listSkeletonKey = "";
            renderQuiet();
          }, 600);
        }
      }
      if (action === "back-catalog") { requestProductExit("catalog"); return; }
      if (action === "add-catalog-product") { route = { name: "catalog-product", counterparty: button.dataset.counterparty || state.counterparties[0] || "Контрагент" }; modal = null; render(); }
      if (action === "edit-catalog-product") { route = { name: "catalog-product", counterparty: button.dataset.counterparty, productId: id }; render(); }
      if (action === "delete-catalog-product" && confirm("Удалить изделие из каталога?")) {
        const products = getCatalogProducts(button.dataset.counterparty);
        state.catalog[button.dataset.counterparty] = products.filter((product) => product.id !== id);
        saveState();
        renderQuiet();
        notify("Изделие удалено из каталога", "", "danger");
      }
      if (action === "toggle-catalog-sort") { catalogSort = catalogSort === "name" ? "date" : "name"; renderQuiet(); return; }
      if (action === "toggle-material-sort") { materialSort = materialSort === "name" ? "date" : "name"; renderQuiet(); return; }
      if (action === "open-settings") { openSettingsMenu(button); return; }
      if (action === "set-theme-mode") { setThemeMode(button.dataset.themeMode); return; }
      if (action === "toggle-theme") toggleTheme();
      if (action === "new-project") { modal = { type: "project" }; render(); }
      if (action === "close-modal") { modal = null; render(); }
      if (action === "open-project") { route = { name: "project", projectId: id }; render(); }
      if (action === "delete-project" && confirm("Удалить проект?")) { state.projects = state.projects.filter((p) => p.id !== id); saveState(); notify("Проект удалён", "", "danger"); }
      if (action === "add-product") { modal = { type: "productPicker" }; render(); }
      if (action === "create-product") { getProject().draft = null; route = { name: "product", projectId: route.projectId }; modal = null; render(); }
      if (action === "duplicate-product") duplicateProduct(id, button.dataset.sourceProject, button.dataset.sourceCatalog);
      if (action === "edit-product") { route = { name: "product", projectId: route.projectId, productId: id }; render(); }
      if (action === "delete-product" && confirm("Удалить изделие?")) { const p = getProject(); p.products = p.products.filter((x) => x.id !== id); saveState(); renderQuiet(); notify("Изделие удалено", "", "danger"); }
      if (action === "product-qty") return;
      if (action === "back-project") { if (isProductEditorRoute()) { requestProductExit("project"); return; } route = { name: "project", projectId: route.projectId }; cutState = null; render(); }
      if (action === "add-detail") addDetail();
      if (action === "remove-detail") removeDetail(id);
      if (action === "save-product") saveProduct();
      if (action === "save-copy-to-catalog") saveProductCopyToCatalog();
      if (action === "attach-image") document.getElementById("image-input")?.click();
      if (action === "remove-image") {
        const p = currentEditableProduct();
        updateProductFromForm(p);
        p.image = "";
        saveState();
        renderQuiet();
        notify("Изображение удалено");
      }
      if (action === "add-material-group") addMaterialGroup();
      if (action === "rename-material-group") renameMaterialGroup(button.dataset.type);
      if (action === "delete-material-group") deleteMaterialGroup(button.dataset.type);
      if (action === "add-material") addMaterial(button.dataset.type);
      if (action === "edit-material-row") editMaterialRow(button.dataset.type, id);
      if (action === "save-material-row") saveMaterialRow(button.dataset.type, id);
      if (action === "rename-material") renameMaterial(button.dataset.type, id);
      if (action === "remove-material") removeMaterial(button.dataset.type, id);
      if (action === "add-payroll") { modal = { type: "payroll", payrollMode: "fixed", payrollScope: "all-dsp" }; render(); }
      if (action === "delete-payroll") { const p = getProject(); p.payrolls = p.payrolls.filter((x) => x.id !== id); saveState(); notify("Начисление удалено", "", "danger"); }
      if (action === "open-cut") { route = { name: "cut", projectId: route.projectId }; cutState = null; render(); }
      if (action === "open-details") { route = { name: "details", projectId: route.projectId }; render(); }
      if (action === "details-product") { route.productId = id; render(); }
      if (action === "export-details") exportDetails();
      if (action === "relayout-cut") { updateCutSettings(); cutState = buildCutState(getProject()); render(); }
      if (action === "print-cut") printCut();
      if (action === "export-backup") exportBackup();
      if (action === "import-backup") importBackup();
      if (action === "replace-background") {
        contextMenu = null;
        render();
        setTimeout(() => document.getElementById("background-input")?.click(), 0);
      }
      if (action === "reset-background") { customBackground = ""; localStorage.removeItem(BACKGROUND_KEY); applyBackground(); notify("Фон сброшен"); }
    };

    app.onkeydown = (event) => {
      const actionTarget = event.target.closest("[data-action]");
      if (!actionTarget || !["Enter", " "].includes(event.key)) return;
      if (actionTarget.matches("button, input, textarea, select")) return;
      event.preventDefault();
      actionTarget.click();
    };

    app.oncontextmenu = (event) => {
      const projectCard = event.target.closest(".project-card");
      const productButton = event.target.closest('[data-action="edit-product"]');
      const payrollDelete = event.target.closest('[data-action="delete-payroll"]');
      if (projectCard) {
        const id = projectCard.querySelector("[data-action='open-project']")?.dataset.id;
        openContextMenu(event, [
          { label: "Открыть проект", action: "open-project", id },
          { separator: true },
          { label: "Удалить проект", action: "delete-project", id, danger: true }
        ]);
        return;
      }
      if (productButton) {
        const id = productButton.dataset.id;
        openContextMenu(event, [
          { label: "Открыть карточку", action: "edit-product", id },
          { label: "Создать деталировку", action: "open-details" },
          { separator: true },
          { label: "Удалить изделие", action: "delete-product", id, danger: true }
        ]);
        return;
      }
      if (payrollDelete) {
        openContextMenu(event, [
          { label: "Удалить начисление", action: "delete-payroll", id: payrollDelete.dataset.id, danger: true }
        ]);
        return;
      }
      if (route.name === "projects") {
        openContextMenu(event, [{ label: "Создать проект", action: "new-project" }]);
      } else if (route.name === "project") {
        openContextMenu(event, [
          { label: "Добавить изделие", action: "add-product" },
          { label: "Добавить ЗП", action: "add-payroll" },
          { separator: true },
          { label: "Карта кроя", action: "open-cut" },
          { label: "Деталировка", action: "open-details" }
        ]);
      }
    };

    app.oninput = (event) => {
      if (route.name === "project" && event.target.matches('[data-action="product-qty"]')) {
        const project = getProject();
        const product = getProduct(project, event.target.dataset.id);
        if (product) {
          product.qty = Math.max(1, Number(event.target.value) || 1);
          saveState();
          renderQuiet();
        }
        return;
      }
      if ((route.name === "product" || route.name === "catalog-product") && event.target.closest(".page")) {
        const product = currentEditableProduct();
        const detailRow = event.target.closest("[data-detail-id]");
        if (detailRow) {
          updateProductBasics(product);
          updateDetailFromRow(detailRow, product);
        } else {
          updateProductBasics(product);
        }
        saveStateSoon();
        scheduleProductCalculations(product);
        if (event.target.matches(".formula-input")) showFormulaCommand(event.target, product);
      }
      if (route.name === "cut" && (event.target.id === "cut-gap" || event.target.id === "cut-trim")) {
        updateCutSettings();
        layoutCut(cutState);
        drawCutCanvas();
      }
    };

    app.onchange = (event) => {
      if (event.target.id === "image-input") handleImage(event.target.files[0]);
      if (event.target.id === "background-input") handleBackground(event.target.files[0]);
      if (event.target.matches(".edge-material-select")) {
        setEdgeMaterial(event.target.dataset.id, event.target.value);
        return;
      }
      if (event.target.matches('[data-material-field="unit"]')) {
        const row = event.target.closest("[data-material-row]");
        const sheet = row?.querySelector(".material-sheet-editor");
        const preview = row?.querySelector(".material-unit-preview");
        const isSheet = event.target.value === "m2";
        sheet?.classList.toggle("is-hidden", !isSheet);
        preview?.classList.toggle("is-hidden", isSheet);
        if (preview) preview.textContent = unitLabel(event.target.value);
        return;
      }
      if (modal?.type === "payroll" && event.target.matches("[data-payroll-mode]")) {
        modal.payrollMode = event.target.value;
        preservePayrollModalDraft();
        renderQuiet();
        return;
      }
      if (modal?.type === "payroll" && event.target.matches("[data-payroll-scope]")) {
        modal.payrollScope = event.target.value;
        preservePayrollModalDraft();
        renderQuiet();
        return;
      }
      if ((route.name === "product" || route.name === "catalog-product") && event.target.closest("[data-detail-id]")) {
        const product = currentEditableProduct();
        const row = event.target.closest("[data-detail-id]");
        updateProductBasics(product);
        if (row) updateDetailFromRow(row, product);
        saveStateSoon();
        scheduleProductCalculations(product);
      }
    };

    app.onfocusin = (event) => {
      if (event.target.matches(".formula-input")) {
        event.target.classList.add("is-expanded");
        showFormulaCommand(event.target, currentEditableProduct());
      } else {
        closeFloatingPanels(event.target);
      }
    };

    app.onfocusout = (event) => {
      if (event.target.matches(".formula-input")) {
        event.target.classList.remove("is-expanded");
        setTimeout(() => document.querySelector(".formula-command")?.remove(), 120);
      }
    };

    document.onmousedown = (event) => {
      closeFloatingPanels(event.target);
    };

    app.ondragstart = (event) => {
      const handle = event.target.closest("[data-drag-detail]");
      if (!handle) return;
      draggedDetailId = handle.dataset.dragDetail;
      event.dataTransfer.effectAllowed = "move";
      event.dataTransfer.setData("text/plain", draggedDetailId);
      handle.closest("[data-detail-id]")?.classList.add("dragging");
    };

    app.ondragover = (event) => {
      if (!draggedDetailId || !event.target.closest("[data-detail-id]")) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
      const row = event.target.closest("[data-detail-id]");
      const rect = row.getBoundingClientRect();
      detailDropPlacement = event.clientY > rect.top + rect.height / 2 ? "after" : "before";
      document.querySelectorAll("[data-detail-id].drop-before, [data-detail-id].drop-after").forEach((item) => item.classList.remove("drop-before", "drop-after"));
      row.classList.add(detailDropPlacement === "after" ? "drop-after" : "drop-before");
    };

    app.ondrop = (event) => {
      const targetRow = event.target.closest("[data-detail-id]");
      if (!draggedDetailId || !targetRow) return;
      event.preventDefault();
      reorderDetail(draggedDetailId, targetRow.dataset.detailId, detailDropPlacement);
    };

    app.ondragend = () => {
      draggedDetailId = "";
      detailDropPlacement = "before";
      document.querySelectorAll("[data-detail-id].dragging, [data-detail-id].drop-before, [data-detail-id].drop-after").forEach((row) => row.classList.remove("dragging", "drop-before", "drop-after"));
    };

    document.querySelectorAll("[data-modal-form]").forEach((form) => {
      form.onsubmit = (event) => {
        event.preventDefault();
        if (form.dataset.modalForm === "project") submitProject(form);
        if (form.dataset.modalForm === "payroll") submitPayroll(form);
      };
    });

    if (route.name === "cut") bindCutCanvas();
  }

  function preserveProjectModalDraft() {
    if (modal?.type !== "project") return;
    const form = document.querySelector('[data-modal-form="project"]');
    if (!form) return;
    modal.name = form.querySelector('[name="name"]')?.value || "";
    modal.address = form.querySelector('[name="address"]')?.value || "";
    modal.counterparty = form.querySelector('[name="counterparty"]')?.value || modal.counterparty;
  }

  function preservePayrollModalDraft() {
    if (modal?.type !== "payroll") return;
    const form = document.querySelector('[data-modal-form="payroll"]');
    if (!form) return;
    modal.amount = form.querySelector('[name="amount"]')?.value || modal.amount || 0;
    modal.rate = form.querySelector('[name="rate"]')?.value || modal.rate || 0;
    modal.note = form.querySelector('[name="note"]')?.value || modal.note || "";
  }

  function submitProject(form) {
    const data = new FormData(form);
    const counterparty = data.get("counterparty") || modal?.counterparty || state.counterparties[0] || "Контрагент";
    const project = { id: uid(), name: data.get("name").trim(), counterparty, address: data.get("address").trim(), products: [], payrolls: [] };
    state.projects.unshift(project);
    saveState();
    modal = null;
    route = { name: "project", projectId: project.id };
    render();
    notify("Проект создан", project.name);
  }

  function handleSelectValue(button) {
    const { kind, value, type, detailId } = button.dataset;
    closeSelectDropdownNow();
    if (kind === "counterparty") {
      preserveProjectModalDraft();
      modal.counterparty = value;
      renderQuiet();
      return;
    }
    if (isProductEditorRoute()) {
      const product = currentEditableProduct();
      updateProductFromForm(product);
      const detail = product.details.find((item) => item.id === detailId);
      if (detail && kind === "detail-type") {
        detail.type = value;
        detail.materialId = state.materials[value]?.[0]?.id || "";
      }
      if (detail && kind === "material") detail.materialId = value;
      saveState();
      if (detail) replaceDetailRow(product, detail.id);
      notify(kind === "detail-type" ? "Тип детали изменён" : "Материал выбран");
    }
  }

  function handleSelectAdd(button) {
    const { kind, type, detailId, key } = button.dataset;
    openDropdown = null;
    if (kind === "counterparty") {
      preserveProjectModalDraft();
      const name = prompt("Название контрагента");
      if (!name) { renderQuiet(); return; }
      const trimmed = name.trim();
      if (!trimmed) { renderQuiet(); return; }
      if (!state.counterparties.includes(trimmed)) state.counterparties.push(trimmed);
      modal.counterparty = trimmed;
      saveState();
      suppressRenderMotion = true;
      openDropdown = key || "";
      renderQuiet();
      notify("Контрагент добавлен", trimmed);
      return;
    }
    if (kind === "detail-type") {
      const name = prompt("Название группы материалов");
      if (!name) { renderQuiet(); return; }
      const group = name.trim();
      if (!group) { renderQuiet(); return; }
      if (!state.materialGroups.includes(group)) state.materialGroups.push(group);
      state.materials[group] ||= [];
      const product = currentEditableProduct();
      updateProductFromForm(product);
      const detail = product.details.find((item) => item.id === detailId);
      if (detail) {
        detail.type = group;
        detail.materialId = "";
      }
      saveState();
      openDropdown = key || "";
      renderQuiet();
      notify("Группа материалов добавлена", group);
      return;
    }
    if (kind === "material") {
      const product = currentEditableProduct();
      updateProductFromForm(product);
      const material = addMaterial(type, { silentRender: true });
      const detail = product.details.find((item) => item.id === detailId);
      if (detail && material) detail.materialId = material.id;
      saveState();
      openDropdown = key || "";
      renderQuiet();
      notify(material ? "Материал добавлен" : "Добавление отменено", material?.name || "");
    }
  }

  function addMaterialGroup() {
    const name = prompt("Название типа материалов");
    if (!name) return;
    const group = name.trim();
    if (!group) return;
    if (state.materialGroups.includes(group)) {
      alert("Такой тип уже есть.");
      return;
    }
    state.materialGroups.push(group);
    state.materials[group] = [];
    saveState();
    notify("Тип материалов добавлен", group);
  }

  function handleSelectDelete(button) {
    const { kind, value, type } = button.dataset;
    openDropdown = null;
    if (kind === "counterparty") {
      preserveProjectModalDraft();
      if (state.counterparties.length <= 1) { renderQuiet(); return; }
      state.counterparties = state.counterparties.filter((name) => name !== value);
      if (modal.counterparty === value) modal.counterparty = state.counterparties[0] || "Контрагент";
      saveState();
      suppressRenderMotion = true;
      notify("Контрагент удалён", value, "danger");
      return;
    }
    if (kind === "material") {
      updateProductFromForm(currentEditableProduct());
      removeMaterial(type, value);
    }
    if (kind === "detail-type") {
      deleteMaterialGroup(value);
    }
  }

  function handleSelectRename(button) {
    const { kind, value, type } = button.dataset;
    openDropdown = null;
    if (kind === "detail-type") {
      renameMaterialGroup(value);
      return;
    }
    if (kind === "material") {
      updateProductFromForm(currentEditableProduct());
      renameMaterial(type, value);
    }
  }

  function renameMaterialGroup(oldName) {
    if (!state.materialGroups.includes(oldName)) return;
    const currentProduct = isProductEditorRoute() ? currentEditableProduct() : null;
    if (currentProduct) updateProductFromForm(currentProduct);
    const nextName = prompt("Новое название группы", oldName);
    if (!nextName) { renderQuiet(); return; }
    const trimmed = nextName.trim();
    if (!trimmed || trimmed === oldName) { renderQuiet(); return; }
    if (state.materialGroups.includes(trimmed)) {
      alert("Такая группа уже есть.");
      renderQuiet();
      return;
    }
    state.materialGroups = state.materialGroups.map((group) => group === oldName ? trimmed : group);
    state.materials[trimmed] = state.materials[oldName] || [];
    delete state.materials[oldName];
    forEachStoredProduct((product) => {
      product.details.forEach((detail) => {
        if (detail.type === oldName) detail.type = trimmed;
      });
    });
    if (currentProduct) {
      currentProduct.details.forEach((detail) => {
        if (detail.type === oldName) detail.type = trimmed;
      });
    }
    saveState();
    notify("Группа материалов переименована", trimmed);
  }

  function deleteMaterialGroup(group) {
    const currentProduct = isProductEditorRoute() ? currentEditableProduct() : null;
    if (currentProduct) updateProductFromForm(currentProduct);
    if (state.materialGroups.length <= 1) {
      alert("Нужна хотя бы одна группа материалов.");
      renderQuiet();
      return;
    }
    if (!confirm(`Удалить группу «${group}» и материалы внутри?`)) { renderQuiet(); return; }
    const fallback = state.materialGroups.find((item) => item !== group);
    state.materialGroups = state.materialGroups.filter((item) => item !== group);
    delete state.materials[group];
    forEachStoredProduct((product) => {
      product.details.forEach((detail) => {
        if (detail.type === group) {
          detail.type = fallback;
          detail.materialId = state.materials[fallback]?.[0]?.id || "";
        }
      });
    });
    if (currentProduct) {
      currentProduct.details.forEach((detail) => {
        if (detail.type === group) {
          detail.type = fallback;
          detail.materialId = state.materials[fallback]?.[0]?.id || "";
        }
      });
    }
    saveState();
    notify("Группа материалов удалена", group, "danger");
  }

  function submitPayroll(form) {
    const project = getProject();
    const data = new FormData(form);
    const detailKeys = Array.from(form.querySelectorAll('[name="detailKeys"]:checked')).map((input) => input.value);
    project.payrolls.push({
      id: uid(),
      mode: data.get("mode"),
      scope: data.get("scope") || "all-dsp",
      amount: Number(data.get("amount")) || 0,
      rate: Number(data.get("rate")) || 0,
      detailKeys,
      note: data.get("note").trim()
    });
    saveState();
    modal = null;
    notify("ЗП добавлена");
  }

  function addDetail() {
    const product = currentEditableProduct();
    updateProductBasics(product);
    const firstType = getMaterialTypes()[0] || "ЛДСП";
    state.materials[firstType] ||= [];
    const detail = { id: uid(), name: "", qty: 1, type: firstType, materialId: state.materials[firstType][0]?.id || "", lengthExpr: "длина изделия", widthExpr: "глубина изделия" };
    product.details.push(detail);
    saveStateSoon();
    const tbody = document.querySelector(".detail-table tbody");
    if (tbody) {
      const emptyRow = tbody.querySelector("[data-empty-details]");
      if (emptyRow) tbody.innerHTML = detailRowHtml(product, detail);
      else tbody.insertAdjacentHTML("beforeend", detailRowHtml(product, detail));
      tbody.querySelector(`[data-detail-id="${detail.id}"] [data-field="detail-name"]`)?.focus();
    }
    scheduleProductCalculations(product);
    notify("Деталь добавлена");
  }

  function reorderDetail(sourceId, targetId, placement = "before") {
    if (sourceId === targetId) return;
    const product = currentEditableProduct();
    const from = product.details.findIndex((detail) => detail.id === sourceId);
    const to = product.details.findIndex((detail) => detail.id === targetId);
    if (from < 0 || to < 0) return;
    const [moved] = product.details.splice(from, 1);
    let insertIndex = to + (placement === "after" ? 1 : 0);
    if (from < insertIndex) insertIndex -= 1;
    product.details.splice(Math.max(0, Math.min(product.details.length, insertIndex)), 0, moved);
    saveState();
    draggedDetailId = "";
    detailDropPlacement = "before";
    renderQuiet();
    notify("Порядок деталей изменён");
  }

  function removeDetail(id) {
    const product = currentEditableProduct();
    updateProductFromForm(product);
    product.details = product.details.filter((detail) => detail.id !== id && detail.generatedEdgeFor !== id);
    updateGeneratedEdgeRows(product);
    saveStateSoon();
    const row = document.querySelector(`[data-detail-id="${id}"]`);
    const tbody = row?.closest("tbody");
    row?.remove();
    tbody?.querySelectorAll("[data-detail-id]").forEach((item) => {
      const linked = item.dataset.edgeFor === id;
      if (linked) item.remove();
    });
    (product.details || [])
      .filter((detail) => detail.generatedEdgeMaterialId)
      .forEach((detail) => replaceDetailRow(product, detail.id));
    if (tbody && !product.details.length) tbody.innerHTML = detailEmptyRowHtml();
    scheduleProductCalculations(product);
    notify("Деталь удалена", "", "danger");
  }

  function updateGeneratedEdgeRows(product) {
    const generated = (product.details || []).filter((detail) => detail.generatedEdgeMaterialId);
    generated.forEach((detail) => {
      const totalLength = totalEdgeLengthForMaterial(product, detail.generatedEdgeMaterialId);
      if (totalLength > 0) {
        const material = getMaterial(detail.type, detail.materialId);
        detail.name = `Кромка: ${material?.name || "материал"}`;
        detail.lengthExpr = String(Math.round(totalLength));
        detail.qty = 1;
      }
    });
    product.details = product.details.filter((detail) => !detail.generatedEdgeMaterialId || totalEdgeLengthForMaterial(product, detail.generatedEdgeMaterialId) > 0);
  }

  function saveProduct(afterSaveTarget = "") {
    if (isCatalogRoute()) {
      const product = currentEditableProduct();
      updateProductFromForm(product);
      product.qty = 1;
      product.createdAt ||= Date.now();
      const counterparty = route.counterparty;
      const products = getCatalogProducts(counterparty);
      if (!route.productId) {
        products.push(product);
        if (state.catalogDrafts) delete state.catalogDrafts[counterparty];
      }
      saveState();
      if (counterparty) openCatalogCounterparties.add(counterparty);
      productEditorKey = "";
      productEditorSnapshot = "";
      pendingProductExit = null;
      if (afterSaveTarget) {
        notify("Изделие каталога сохранено", product.name);
        runProductExitTarget(afterSaveTarget);
        return;
      }
      route = { name: "catalog" };
      render();
      notify("Изделие каталога сохранено", product.name);
      return;
    }
    const project = getProject();
    const product = currentEditableProduct();
    updateProductFromForm(product);
    if (!route.productId) {
      const catalogTemplate = cloneProductForStorage(product);
      const projectCopy = cloneProductForStorage(catalogTemplate);
      projectCopy.qty = productQty(product);
      projectCopy.catalogProductId = catalogTemplate.id;
      getCatalogProducts(project.counterparty).push(catalogTemplate);
      project.products.push(projectCopy);
      openCatalogCounterparties.add(project.counterparty);
      project.draft = null;
    }
    delete project.draft;
    saveState();
    productEditorKey = "";
    productEditorSnapshot = "";
    pendingProductExit = null;
    if (afterSaveTarget) {
      notify("Изделие сохранено", product.name);
      runProductExitTarget(afterSaveTarget);
      return;
    }
    route = { name: "project", projectId: project.id };
    render();
    notify("Изделие сохранено", product.name);
  }

  function saveProductCopyToCatalog() {
    const project = getProject();
    const product = currentEditableProduct();
    if (!project || !product) return;
    updateProductFromForm(product);
    const existing = matchingCatalogProduct(project, product);
    if (existing) {
      product.catalogProductId = existing.id;
      saveState();
      renderQuiet();
      return;
    }
    const copy = cloneProductForStorage(product);
    delete copy.catalogProductId;
    getCatalogProducts(project.counterparty).push(copy);
    product.catalogProductId = copy.id;
    openCatalogCounterparties.add(project.counterparty);
    saveState();
    productEditorSnapshot = snapshotProduct(product);
    renderQuiet();
    notify("Копия сохранена в каталог", product.name);
  }

  function duplicateProduct(id, sourceProjectId = route.projectId, sourceCatalog = "") {
    const project = getProject();
    const sourceProject = state.projects.find((item) => item.id === sourceProjectId) || project;
    const source = sourceCatalog ? getCatalogProduct(sourceCatalog, id) : getProduct(sourceProject, id);
    if (!source) return;
    const copy = cloneProductForStorage(source);
    if (sourceCatalog) copy.catalogProductId = source.id;
    project.products.push(copy);
    saveState();
    modal = null;
    route = { name: "project", projectId: project.id };
    render();
    notify("Изделие скопировано", copy.name);
  }

  function handleImage(file) {
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      const product = currentEditableProduct();
      updateProductFromForm(product);
      product.image = reader.result;
      saveState();
      renderQuiet();
      notify("Изображение прикреплено");
    };
    reader.readAsDataURL(file);
  }

  function handleBackground(file) {
    if (!file) return;
    const reader = new FileReader();
    reader.onload = async () => {
      const nextBackground = reader.result;
      document.body.dataset.bgLoading = "true";
      await decodeImage(nextBackground);
      customBackground = nextBackground;
      localStorage.setItem(BACKGROUND_KEY, customBackground);
      applyBackground();
      notify("Фон заменён");
    };
    reader.readAsDataURL(file);
  }

  function addMaterial(type, options = {}) {
    state.materials[type] ||= [];
    if (route.name === "materials" && !options.silentRender) {
      const material = { id: uid(), name: "", cost: 0, unit: "pc", createdAt: Date.now(), textureDirection: false };
      state.materials[type].push(material);
      editingMaterials.add(`${type}:${material.id}`);
      productTotalsCache.clear();
      saveStateSoon();
      renderQuiet();
      document.querySelector(`[data-material-id="${material.id}"] [data-material-field="name"]`)?.focus();
      notify("Материал добавлен", "Заполните строку и нажмите ✓");
      return material;
    }
    const name = prompt("Название материала");
    if (!name) return null;
    const cost = Number(prompt("Себестоимость за единицу", "0")) || 0;
    const isSheetMaterial = `${type} ${name}`.toLowerCase().includes("дсп");
    const unit = prompt("Единица: m2, lm или pc", isSheetMaterial ? "m2" : "pc") || "pc";
    const material = { id: uid(), name, cost, unit, createdAt: Date.now(), textureDirection: false };
    if (isSheetMaterial) {
      material.sheetLength = Number(prompt("Длина листа, мм", "2750")) || 2750;
      material.sheetWidth = Number(prompt("Ширина листа, мм", "1830")) || 1830;
    }
    state.materials[type].push(material);
    productTotalsCache.clear();
    if (isProductEditorRoute()) {
      const product = currentEditableProduct();
      updateProductFromForm(product);
      product.details.forEach((detail) => { if (detail.type === type && !detail.materialId) detail.materialId = material.id; });
    }
    saveState();
    if (!options.silentRender) notify("Материал добавлен", material.name);
    return material;
  }

  function editMaterialRow(type, id) {
    const material = getMaterial(type, id);
    if (!material) return;
    editingMaterials.add(`${type}:${id}`);
    replaceMaterialRow(type, id);
  }

  function replaceMaterialRow(type, id) {
    const material = getMaterial(type, id);
    const row = materialRowElement(type, id);
    if (!material || !row) return;
    const index = (state.materials[type] || []).findIndex((item) => item.id === id);
    row.outerHTML = materialRowHtml(type, material, Math.max(0, index));
  }

  function materialRowElement(type, id) {
    return Array.from(document.querySelectorAll("[data-material-row]"))
      .find((row) => row.dataset.type === type && row.dataset.materialId === id);
  }

  function saveMaterialRow(type, id) {
    const material = getMaterial(type, id);
    const row = materialRowElement(type, id);
    if (!material || !row) return;
    const name = row.querySelector('[data-material-field="name"]')?.value.trim() || "Новый материал";
    const unit = row.querySelector('[data-material-field="unit"]')?.value || "pc";
    material.name = name;
    material.unit = unit;
    material.cost = Number(row.querySelector('[data-material-field="cost"]')?.value) || 0;
    material.textureDirection = Boolean(row.querySelector('[data-material-field="textureDirection"]')?.checked);
    if (unit === "m2") {
      material.sheetLength = Number(row.querySelector('[data-material-field="sheetLength"]')?.value) || 0;
      material.sheetWidth = Number(row.querySelector('[data-material-field="sheetWidth"]')?.value) || 0;
    } else {
      delete material.sheetLength;
      delete material.sheetWidth;
    }
    editingMaterials.delete(`${type}:${id}`);
    productTotalsCache.clear();
    saveState();
    renderQuiet();
    notify("Материал сохранён", material.name);
  }

  function renameMaterial(type, id) {
    const material = getMaterial(type, id);
    if (!material) return;
    const name = prompt("Новое название", material.name);
    if (!name) return;
    material.name = name;
    material.cost = Number(prompt("Себестоимость", material.cost)) || 0;
    saveState();
    notify("Материал изменён", material.name);
  }

  function removeMaterial(type, id) {
    if (!confirm("Удалить материал из списка?")) return;
    state.materials[type] = state.materials[type].filter((material) => material.id !== id);
    editingMaterials.delete(`${type}:${id}`);
    productTotalsCache.clear();
    saveState();
    if (route.name === "materials") {
      renderQuiet();
    }
    notify("Материал удалён", "", "danger");
  }

  function buildCutState(project) {
    const next = { gap: cutState?.gap ?? 6, trim: cutState?.trim ?? 10, keepSize: cutState?.keepSize ?? 100, groups: [], scale: 0.25, drag: null, selectedPartId: "", controlHits: [] };
    const groupsByMaterial = new Map();
    project.products.forEach((product) => {
      product.details.forEach((detail) => {
        const calc = detailCalc(product, detail);
        const material = calc.material;
        if (!material || !isSheetMaterial(material)) return;
        if (!groupsByMaterial.has(material.id)) {
          groupsByMaterial.set(material.id, { material, sheets: [], parts: [] });
        }
        for (let i = 0; i < calc.qty * productQty(product); i += 1) {
          groupsByMaterial.get(material.id).parts.push({
            id: uid(),
            productId: product.id,
            detailId: detail.id,
            name: `${product.name}: ${detail.name}`,
            baseW: calc.length,
            baseH: calc.width,
            w: calc.length,
            h: calc.width,
            x: 0,
            y: 0,
            sheet: 0,
            rotated: false,
            allowRotation: !material.textureDirection,
            manualRotation: null
          });
        }
      });
    });
    next.groups = Array.from(groupsByMaterial.values());
    layoutCut(next);
    return next;
  }

  function updateCutSettings() {
    if (!cutState) return;
    cutState.gap = Math.max(0, Number(document.getElementById("cut-gap")?.value) || 0);
    cutState.trim = Math.max(0, Number(document.getElementById("cut-trim")?.value) || 0);
    cutState.keepSize = Math.max(0, Number(document.getElementById("cut-keep")?.value) || 0);
    const gap = document.getElementById("cut-gap");
    const trim = document.getElementById("cut-trim");
    const keep = document.getElementById("cut-keep");
    if (gap) gap.value = cutState.gap;
    if (trim) trim.value = cutState.trim;
    if (keep) keep.value = cutState.keepSize;
  }

  function compareCutScores(a, b) {
    for (let index = 0; index < Math.max(a.length, b.length); index += 1) {
      const delta = (a[index] || 0) - (b[index] || 0);
      if (Math.abs(delta) > 0.0001) return delta;
    }
    return 0;
  }

  function cutPartOrientations(part) {
    const baseW = Math.max(0, Number(part.baseW ?? part.w) || 0);
    const baseH = Math.max(0, Number(part.baseH ?? part.h) || 0);
    if (part.manualRotation === true) return [{ w: baseH, h: baseW, rotated: true }];
    if (part.manualRotation === false || part.allowRotation === false) return [{ w: baseW, h: baseH, rotated: false }];
    const orientations = [{ w: baseW, h: baseH, rotated: false }];
    if (Math.abs(baseW - baseH) > 0.01) orientations.push({ w: baseH, h: baseW, rotated: true });
    return orientations;
  }
  function cutSorters() {
    return {
      area: (a, b) => (b.baseW * b.baseH) - (a.baseW * a.baseH),
      maxSide: (a, b) => Math.max(b.baseW, b.baseH) - Math.max(a.baseW, a.baseH) || (b.baseW * b.baseH) - (a.baseW * a.baseH),
      minSide: (a, b) => Math.min(b.baseW, b.baseH) - Math.min(a.baseW, a.baseH) || (b.baseW * b.baseH) - (a.baseW * a.baseH),
      width: (a, b) => b.baseW - a.baseW || b.baseH - a.baseH,
      height: (a, b) => b.baseH - a.baseH || b.baseW - a.baseW,
      perimeter: (a, b) => (b.baseW + b.baseH) - (a.baseW + a.baseH)
    };
  }

  function toCutAxes(item, firstOrientation) {
    return firstOrientation === "vertical"
      ? { p: item.w, q: item.h }
      : { p: item.h, q: item.w };
  }

  function fromCutAxes(rect, firstOrientation, trim) {
    return firstOrientation === "vertical"
      ? { x: trim + rect.p, y: trim + rect.q, w: rect.pSize, h: rect.qSize }
      : { x: trim + rect.q, y: trim + rect.p, w: rect.qSize, h: rect.pSize };
  }

  function makeGuillotineSheet(usableP, usableQ, firstOrientation) {
    return { usableP, usableQ, firstOrientation, strips: [], usedP: 0, placements: [] };
  }

  function cloneGuillotineSheet(sheet) {
    return {
      usableP: sheet.usableP,
      usableQ: sheet.usableQ,
      firstOrientation: sheet.firstOrientation,
      usedP: sheet.usedP,
      placements: sheet.placements.map((item) => ({ ...item })),
      strips: sheet.strips.map((strip) => ({
        ...strip,
        rows: strip.rows.map((row) => ({
          ...row,
          parts: row.parts.map((part) => ({ ...part }))
        }))
      }))
    };
  }

  function guillotinePartialScore(trial) {
    let rows = 0;
    let strips = 0;
    let occupiedEnvelope = 0;
    let rowRemainder = 0;
    trial.sheetStates.forEach((sheet) => {
      strips += sheet.strips.length;
      sheet.strips.forEach((strip) => {
        rows += strip.rows.length;
        occupiedEnvelope += strip.pSize * strip.usedQ;
        strip.rows.forEach((row) => { rowRemainder += Math.max(0, strip.pSize - row.usedP) * row.qSize; });
      });
    });
    return [trial.oversized.length, trial.sheetStates.length, rows + strips, occupiedEnvelope, rowRemainder];
  }

  function guillotineStateKey(trial) {
    return trial.sheetStates.map((sheet) => sheet.strips.map((strip) =>
      `${Math.round(strip.pSize)}:${strip.rows.map((row) => `${Math.round(row.qSize)}-${Math.round(row.usedP)}`).join(",")}`
    ).join("|")).join("/");
  }

  function guillotinePlacementCandidates(sheetStates, part, gap, mode) {
    const candidates = [];
    sheetStates.forEach((sheet, sheetIndex) => {
      cutPartOrientations(part).forEach((orientation) => {
        const axes = toCutAxes(orientation, sheet.firstOrientation);
        sheet.strips.forEach((strip, stripIndex) => {
          strip.rows.forEach((row, rowIndex) => {
            const remaining = strip.pSize - row.usedP;
            if (Math.abs(row.qSize - axes.q) > 0.01 || axes.p > remaining + 0.01) return;
            const addGap = row.parts.length ? gap : 0;
            if (axes.p + addGap > remaining + 0.01) return;
            candidates.push({ type: "row", sheetIndex, stripIndex, rowIndex, orientation, axes, score: [sheetIndex, 0, remaining - axes.p - addGap, strip.usableQ - strip.usedQ] });
          });
          const addGap = strip.rows.length ? gap : 0;
          if (axes.p <= strip.pSize + 0.01 && strip.usedQ + addGap + axes.q <= strip.usableQ + 0.01) {
            candidates.push({ type: "row-new", sheetIndex, stripIndex, orientation, axes, score: [sheetIndex, mode === "cuts" ? 2 : 1, strip.pSize - axes.p, strip.usableQ - strip.usedQ - addGap - axes.q] });
          }
        });
        const addGap = sheet.strips.length ? gap : 0;
        if (sheet.usedP + addGap + axes.p <= sheet.usableP + 0.01 && axes.q <= sheet.usableQ + 0.01) {
          candidates.push({ type: "strip-new", sheetIndex, orientation, axes, score: [sheetIndex, mode === "compact" ? 1 : 3, sheet.usableP - sheet.usedP - addGap - axes.p, sheet.usableQ - axes.q] });
        }
      });
    });
    candidates.forEach((candidate) => {
      if (mode === "area") candidate.score.push(candidate.score[2] * candidate.score[3]);
      if (mode === "cuts") candidate.score = [candidate.score[0], candidate.score[1], candidate.score[3], candidate.score[2]];
    });
    return candidates.sort((a, b) => compareCutScores(a.score, b.score));
  }

  function placeGuillotineCandidate(sheet, candidate, part, gap, trim) {
    let strip;
    let row;
    if (candidate.type === "strip-new") {
      const p = sheet.usedP + (sheet.strips.length ? gap : 0);
      strip = { p, pSize: candidate.axes.p, usedQ: 0, usableQ: sheet.usableQ, rows: [] };
      sheet.strips.push(strip);
      sheet.usedP = p + strip.pSize;
    } else {
      strip = sheet.strips[candidate.stripIndex];
    }
    if (candidate.type === "row" ) {
      row = strip.rows[candidate.rowIndex];
    } else {
      const q = strip.usedQ + (strip.rows.length ? gap : 0);
      row = { q, qSize: candidate.axes.q, usedP: 0, parts: [] };
      strip.rows.push(row);
      strip.usedQ = q + row.qSize;
    }
    const p = strip.p + row.usedP + (row.parts.length ? gap : 0);
    const axesRect = { p, q: row.q, pSize: candidate.axes.p, qSize: candidate.axes.q };
    const actual = fromCutAxes(axesRect, sheet.firstOrientation, trim);
    const placement = { id: part.id, ...actual, sheet: candidate.sheetIndex, rotated: candidate.orientation.rotated };
    row.parts.push({ ...axesRect, id: part.id });
    row.usedP = (p - strip.p) + candidate.axes.p;
    sheet.placements.push(placement);
    return placement;
  }

  function buildGuillotineSheetPlan(sheet, trim, gap, keepSize, sheetW, sheetH) {
    const cuts = [];
    const leftovers = [];
    const vertical = sheet.firstOrientation === "vertical";
    const addCut = (stage, orientation, p, q, length) => {
      const line = vertical
        ? (orientation === "p" ? { x1: trim + p, y1: trim + q, x2: trim + p, y2: trim + q + length } : { x1: trim + p, y1: trim + q, x2: trim + p + length, y2: trim + q })
        : (orientation === "p" ? { x1: trim + q, y1: trim + p, x2: trim + q + length, y2: trim + p } : { x1: trim + q, y1: trim + p, x2: trim + q, y2: trim + p + length });
      cuts.push({ stage, ...line, length });
    };
    const addLeftover = (p, q, pSize, qSize) => {
      if (pSize <= 0.01 || qSize <= 0.01) return;
      const rect = fromCutAxes({ p, q, pSize, qSize }, sheet.firstOrientation, trim);
      leftovers.push({ ...rect, kept: Math.min(rect.w, rect.h) >= keepSize });
    };

    sheet.strips.forEach((strip) => {
      if (strip.p + strip.pSize < sheet.usableP - 0.01) addCut(1, "p", strip.p + strip.pSize, 0, sheet.usableQ);
      strip.rows.forEach((row) => {
        if (row.q + row.qSize < strip.usableQ - 0.01) addCut(2, "q", strip.p, row.q + row.qSize, strip.pSize);
        row.parts.forEach((partRect) => {
          const localEnd = partRect.p - strip.p + partRect.pSize;
          if (localEnd < strip.pSize - 0.01) addCut(3, "p", partRect.p + partRect.pSize, row.q, row.qSize);
        });
        addLeftover(strip.p + row.usedP, row.q, strip.pSize - row.usedP, row.qSize);
      });
      addLeftover(strip.p, strip.usedQ, strip.pSize, strip.usableQ - strip.usedQ);
    });
    addLeftover(sheet.usedP, 0, sheet.usableP - sheet.usedP, sheet.usableQ);
    const trimCuts = trim > 0 ? [
      { stage: 0, x1: trim, y1: 0, x2: trim, y2: sheetH, length: sheetH },
      { stage: 0, x1: sheetW - trim, y1: 0, x2: sheetW - trim, y2: sheetH, length: sheetH },
      { stage: 0, x1: 0, y1: trim, x2: sheetW, y2: trim, length: sheetW },
      { stage: 0, x1: 0, y1: sheetH - trim, x2: sheetW, y2: sheetH - trim, length: sheetW }
    ] : [];
    const partArea = sheet.placements.reduce((sum, item) => sum + item.w * item.h, 0);
    const sheetArea = sheetW * sheetH;
    return {
      cuts: [...trimCuts, ...cuts],
      leftovers,
      stats: {
        efficiency: sheetArea ? partArea / sheetArea : 0,
        cutCount: cuts.length + trimCuts.length,
        cutLength: cuts.reduce((sum, cut) => sum + cut.length, 0) + trimCuts.reduce((sum, cut) => sum + cut.length, 0),
        keptCount: leftovers.filter((item) => item.kept).length,
        wasteArea: Math.max(0, sheetArea - partArea)
      }
    };
  }

  function guillotineTrialScore(trial) {
    const cutCount = trial.sheetStates.reduce((sum, sheet) => sum + sheet.plan.stats.cutCount, 0);
    const cutLength = trial.sheetStates.reduce((sum, sheet) => sum + sheet.plan.stats.cutLength, 0);
    const leftovers = trial.sheetStates.flatMap((sheet) => sheet.plan.leftovers);
    const largestRemainder = Math.max(0, ...leftovers.map((item) => item.w * item.h));
    const rotations = trial.placements.filter((item) => item.rotated).length;
    return [trial.oversized.length, trial.sheetStates.length, cutCount, cutLength, leftovers.length, -largestRemainder, rotations];
  }

  function packGuillotineTrial(parts, sheetW, sheetH, trim, gap, keepSize, sortMode, firstOrientation, mode) {
    const usableW = Math.max(0, sheetW - trim * 2);
    const usableH = Math.max(0, sheetH - trim * 2);
    const usableP = firstOrientation === "vertical" ? usableW : usableH;
    const usableQ = firstOrientation === "vertical" ? usableH : usableW;
    const makeSheet = () => makeGuillotineSheet(usableP, usableQ, firstOrientation);
    const ordered = parts.map((part) => ({ ...part, baseW: Number(part.baseW ?? part.w) || 0, baseH: Number(part.baseH ?? part.h) || 0 }))
      .sort(cutSorters()[sortMode]);
    const queueSize = parts.length > 100 ? 4 : parts.length > 60 ? 8 : 16;
    const branchLimit = parts.length > 100 ? 3 : parts.length > 60 ? 5 : 8;
    let trials = [{ sheetStates: [makeSheet()], placements: [], oversized: [] }];

    ordered.forEach((part) => {
      const nextTrials = [];
      trials.forEach((trial) => {
        const candidates = guillotinePlacementCandidates(trial.sheetStates, part, gap, mode).slice(0, branchLimit);
        const extraSheet = makeSheet();
        const newSheetCandidate = guillotinePlacementCandidates([extraSheet], part, gap, mode)[0];
        if (newSheetCandidate && (trial.sheetStates.length === 1 ? trial.sheetStates[0].placements.length > 0 : true)) {
          candidates.push({ ...newSheetCandidate, sheetIndex: trial.sheetStates.length, newSheet: extraSheet });
        }
        if (!candidates.length) {
          nextTrials.push({
            sheetStates: trial.sheetStates.map(cloneGuillotineSheet),
            placements: trial.placements.map((item) => ({ ...item })),
            oversized: [...trial.oversized, part.id]
          });
          return;
        }
        candidates.forEach((candidate) => {
          const branch = {
            sheetStates: trial.sheetStates.map(cloneGuillotineSheet),
            placements: trial.placements.map((item) => ({ ...item })),
            oversized: [...trial.oversized]
          };
          if (candidate.newSheet) branch.sheetStates.push(cloneGuillotineSheet(candidate.newSheet));
          const sheetState = branch.sheetStates[candidate.sheetIndex];
          branch.placements.push(placeGuillotineCandidate(sheetState, candidate, part, gap, trim));
          branch.partialScore = guillotinePartialScore(branch);
          nextTrials.push(branch);
        });
      });
      const unique = new Map();
      nextTrials.sort((a, b) => compareCutScores(a.partialScore || guillotinePartialScore(a), b.partialScore || guillotinePartialScore(b)));
      nextTrials.forEach((trial) => {
        const key = guillotineStateKey(trial);
        if (!unique.has(key) && unique.size < queueSize) unique.set(key, trial);
      });
      trials = Array.from(unique.values());
    });
    const completed = trials.map((trial) => {
      trial.sheetStates.forEach((sheet) => { sheet.plan = buildGuillotineSheetPlan(sheet, trim, gap, keepSize, sheetW, sheetH); });
      trial.score = guillotineTrialScore(trial);
      return trial;
    }).sort((a, b) => compareCutScores(a.score, b.score));
    const trial = completed[0] || { sheetStates: [makeSheet()], placements: [], oversized: parts.map((part) => part.id) };
    if (!trial.score) {
      trial.sheetStates.forEach((sheet) => { sheet.plan = buildGuillotineSheetPlan(sheet, trim, gap, keepSize, sheetW, sheetH); });
      trial.score = guillotineTrialScore(trial);
    }
    return trial;
  }

  function layoutCut(model) {
    const sortModes = ["area", "maxSide", "minSide", "width", "height", "perimeter"];
    const firstOrientations = ["vertical", "horizontal"];
    const modes = ["compact", "area", "cuts"];
    model.groups.forEach((group) => {
      const sheetW = Number(group.material.sheetLength) || 2750;
      const sheetH = Number(group.material.sheetWidth) || 1830;
      const trim = Math.max(0, Number(model.trim) || 0);
      const gap = Math.max(0, Number(model.gap) || 0);
      const keepSize = Math.max(0, Number(model.keepSize) || 0);
      group.parts.forEach((part) => {
        part.baseW = Number(part.baseW ?? part.w) || 0;
        part.baseH = Number(part.baseH ?? part.h) || 0;
      });
      let best = null;
      sortModes.forEach((sortMode) => {
        firstOrientations.forEach((firstOrientation) => {
          modes.forEach((mode) => {
            const trial = packGuillotineTrial(group.parts, sheetW, sheetH, trim, gap, keepSize, sortMode, firstOrientation, mode);
            if (!best || compareCutScores(trial.score, best.score) < 0) best = trial;
          });
        });
      });
      const placements = new Map((best?.placements || []).map((placement) => [placement.id, placement]));
      group.parts.forEach((part) => {
        const placement = placements.get(part.id);
        if (!placement) return;
        Object.assign(part, placement);
      });
      group.oversized = best?.oversized || [];
      group.sheets = (best?.sheetStates || []).map((sheetState) => ({
        x: 0,
        y: 0,
        w: sheetW,
        h: sheetH,
        firstOrientation: sheetState.firstOrientation,
        cuts: sheetState.plan.cuts,
        leftovers: sheetState.plan.leftovers,
        stats: sheetState.plan.stats,
        manual: false
      }));
      if (!group.sheets.length) group.sheets = [{ x: 0, y: 0, w: sheetW, h: sheetH, cuts: [], leftovers: [], stats: { efficiency: 0, cutCount: 0, cutLength: 0, keptCount: 0 } }];
    });
  }

  function drawCutCanvas(printMode = false) {
    const canvas = document.getElementById("cut-canvas");
    if (!canvas || !cutState) return;
    const ctx = canvas.getContext("2d");
    const dark = theme === "dark";
    const palette = dark
      ? { bg: "#ffffff", title: "#142033", sheet: "#ffffff", sheetBorder: "#596579", note: "#5f6b7a", part: "#cdefff", partBorder: "#087ec0", text: "#102233", cut: "#e0002a" }
      : { bg: "#ffffff", title: "#142033", sheet: "#ffffff", sheetBorder: "#596579", note: "#607586", part: "#dff5ff", partBorder: "#0a7fbd", text: "#102233", cut: "#e0002a" };
    const pad = 34;
    const largestW = Math.max(1, ...cutState.groups.flatMap((group) => group.sheets.map((sheet) => sheet.w)));
    const scale = Math.min(0.25, 940 / largestW);
    cutState.scale = scale;
    let height = pad;
    cutState.groups.forEach((group) => {
      height += 38;
      group.sheets.forEach((sheet, sheetIndex) => {
        const sheetParts = cutSheetParts(group, sheetIndex);
        height += sheet.h * scale + cutLegendMetrics(sheetParts, sheet.w * scale).height + 62;
      });
    });
    canvas.width = 1040;
    canvas.height = Math.max(700, height + pad);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.fillStyle = palette.bg;
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    let yCursor = pad;
    cutState.hit = [];
    cutState.controlHits = [];
    cutState.groups.forEach((group, groupIndex) => {
      ctx.fillStyle = palette.title;
      ctx.font = "700 18px Segoe UI";
      ctx.fillText(`${group.material.name}${group.material.textureDirection ? "  →" : ""}`, pad, yCursor);
      yCursor += 24;
      const groupCutNumbers = new Map();
      [...group.parts].sort((a, b) => a.name.localeCompare(b.name, "ru")).forEach((part) => {
        const key = cutPartGroupKey(part);
        if (!groupCutNumbers.has(key)) groupCutNumbers.set(key, groupCutNumbers.size + 1);
        part.cutNumber = groupCutNumbers.get(key);
      });
      group.sheets.forEach((sheet, sheetIndex) => {
        const sx = pad;
        const sy = yCursor + 24;
        const sw = sheet.w * scale;
        const sh = sheet.h * scale;
        const stats = sheet.stats || {};
        const sheetInfo = `Лист ${sheetIndex + 1}: ${group.material.name} · ${sheet.w} × ${sheet.h} мм · ${Math.round((stats.efficiency || 0) * 100)}% · резов ${stats.cutCount || 0} / ${Math.round(stats.cutLength || 0)} мм · остатков ${stats.keptCount || 0}${group.material.textureDirection ? " · текстура →" : ""}`;
        ctx.fillStyle = palette.note;
        ctx.font = "12px Segoe UI";
        ctx.fillText(sheetInfo, sx, yCursor + 15);
        ctx.fillStyle = palette.sheet;
        ctx.strokeStyle = palette.sheetBorder;
        ctx.lineWidth = 2;
        ctx.fillRect(sx, sy, sw, sh);
        ctx.strokeRect(sx, sy, sw, sh);
        const sheetParts = cutSheetParts(group, sheetIndex);
        (sheet.leftovers || []).filter((item) => item.kept).forEach((item) => {
          ctx.save();
          ctx.fillStyle = "rgba(52, 199, 89, 0.10)";
          ctx.strokeStyle = "#34a853";
          ctx.setLineDash([6, 4]);
          ctx.fillRect(sx + item.x * scale, sy + item.y * scale, item.w * scale, item.h * scale);
          ctx.strokeRect(sx + item.x * scale, sy + item.y * scale, item.w * scale, item.h * scale);
          if (item.w * scale >= 64 && item.h * scale >= 22) {
            ctx.setLineDash([]);
            ctx.fillStyle = "#187a35";
            ctx.font = "600 10px Segoe UI";
            ctx.fillText(`Остаток ${Math.round(item.w)}×${Math.round(item.h)}`, sx + item.x * scale + 4, sy + item.y * scale + 14);
          }
          ctx.restore();
        });
        sheetParts.forEach((part) => {
          const px = sx + part.x * scale;
          const py = sy + part.y * scale;
          const pw = Math.max(12, part.w * scale);
          const ph = Math.max(12, part.h * scale);
          ctx.fillStyle = palette.part;
          ctx.strokeStyle = palette.partBorder;
          ctx.lineWidth = 1;
          ctx.fillRect(px, py, pw, ph);
          ctx.strokeRect(px, py, pw, ph);
          drawCutPartLabel(ctx, part, px, py, pw, ph, palette.text);
          cutState.hit.push({ groupIndex, sheetIndex, part, rect: [px, py, pw, ph], sheetRect: [sx, sy, sw, sh] });
          if (cutState.selectedPartId === part.id && part.allowRotation !== false) drawCutPartControls(ctx, part, px, py, pw, ph, groupIndex);
        });
        drawCutLines(ctx, sheet, sx, sy, scale, printMode ? palette.cut : "rgba(224, 0, 42, 0.55)");
        const legendHeight = drawCutLegend(ctx, sheetParts, sx, sy + sh + 10, sw, palette);
        sheet.renderRect = { x: sx - 3, y: yCursor, w: sw + 6, h: 24 + sh + legendHeight + 12 };
        yCursor += sh + legendHeight + 62;
      });
    });
  }

  function cutSheetParts(group, sheetIndex) {
    return group.parts
      .filter((part) => part.sheet === sheetIndex)
      .sort((a, b) => a.y - b.y || a.x - b.x || a.name.localeCompare(b.name, "ru"));
  }

  function cutPartGroupKey(part) {
    if (part.productId && part.detailId) return `${part.productId}:${part.detailId}`;
    return `${part.name}|${part.baseW ?? part.w}×${part.baseH ?? part.h}`;
  }

  function cutLegendEntries(parts) {
    const entries = new Map();
    parts.forEach((part) => {
      const key = cutPartGroupKey(part);
      if (!entries.has(key)) {
        entries.set(key, {
          cutNumber: part.cutNumber,
          name: part.name,
          w: Number(part.baseW ?? part.w) || 0,
          h: Number(part.baseH ?? part.h) || 0,
          quantity: 0,
          rotatedQuantity: 0
        });
      }
      const entry = entries.get(key);
      entry.quantity += 1;
      if (part.rotated) entry.rotatedQuantity += 1;
    });
    return Array.from(entries.values()).sort((a, b) => a.cutNumber - b.cutNumber);
  }

  function cutLegendMetrics(parts, width) {
    const entries = cutLegendEntries(parts);
    const longestName = Math.max(0, ...entries.map((entry) => String(entry.name || "").length));
    const columns = width >= 720 && entries.length > 1 && longestName <= 32 ? 2 : 1;
    const rows = Math.ceil(entries.length / columns);
    return { columns, rows, height: entries.length ? 28 + rows * 19 : 0 };
  }

  function drawCutPartLabel(ctx, part, x, y, width, height, color) {
    const padding = Math.max(2, Math.min(5, width * 0.06));
    const fontSize = Math.max(7, Math.min(12, width / 5.2, height / 3.2));
    const lineHeight = fontSize + 2;
    const numberText = `№${part.cutNumber}${part.manualRotation === true ? " ↻" : ""}`;
    const sizeText = `${Math.round(part.w)}×${Math.round(part.h)}`;
    ctx.save();
    ctx.beginPath();
    ctx.rect(x + 1, y + 1, Math.max(0, width - 2), Math.max(0, height - 2));
    ctx.clip();
    ctx.fillStyle = color;
    ctx.font = `700 ${fontSize}px Segoe UI`;
    ctx.fillText(numberText, x + padding, y + padding + fontSize);
    ctx.font = `600 ${fontSize}px Segoe UI`;
    if (height >= lineHeight * 2 + padding * 2) {
      ctx.fillText(sizeText, x + padding, y + padding + fontSize + lineHeight);
    } else {
      const numberWidth = ctx.measureText(`${numberText} · `).width;
      ctx.fillText(`· ${sizeText}`, x + padding + numberWidth, y + padding + fontSize);
    }
    ctx.restore();
  }

  function drawCutPartControls(ctx, part, x, y, width, height, groupIndex) {
    if (width < 48 || height < 26) return;
    const size = 22;
    const gap = 4;
    const startX = x + width - size * 2 - gap - 4;
    const top = y + 4;
    [
      { action: "rotate-one", label: "↻", x: startX },
      { action: "rotate-all", label: "↻↻", x: startX + size + gap }
    ].forEach((control) => {
      ctx.save();
      ctx.fillStyle = "rgba(20, 32, 51, 0.86)";
      ctx.beginPath();
      ctx.roundRect(control.x, top, size, size, 6);
      ctx.fill();
      ctx.fillStyle = "#ffffff";
      ctx.font = `700 ${control.action === "rotate-all" ? 10 : 15}px Segoe UI`;
      ctx.textAlign = "center";
      ctx.textBaseline = "middle";
      ctx.fillText(control.label, control.x + size / 2, top + size / 2);
      ctx.restore();
      cutState.controlHits.push({ action: control.action, partId: part.id, groupIndex, rect: [control.x, top, size, size] });
    });
  }

  function drawCutLegend(ctx, parts, x, y, width, palette) {
    const metrics = cutLegendMetrics(parts, width);
    const entries = cutLegendEntries(parts);
    if (!entries.length) return 0;
    const columnWidth = width / metrics.columns;
    ctx.save();
    ctx.fillStyle = palette.title;
    ctx.font = "700 12px Segoe UI";
    ctx.fillText("Детали листа", x, y + 12);
    ctx.font = "11px Segoe UI";
    entries.forEach((entry, index) => {
      const column = Math.floor(index / metrics.rows);
      const row = index % metrics.rows;
      const itemX = x + column * columnWidth;
      const itemY = y + 31 + row * 19;
      const dimensions = `${Math.round(entry.w)} × ${Math.round(entry.h)} мм`;
      const quantity = entry.quantity > 1 ? ` · ${entry.quantity} шт.` : "";
      const rotation = entry.rotatedQuantity
        ? entry.rotatedQuantity === entry.quantity ? " · поворот 90°" : ` · повернуто: ${entry.rotatedQuantity}`
        : "";
      const text = `№${entry.cutNumber}  ${entry.name} — ${dimensions}${quantity}${rotation}`;
      ctx.save();
      ctx.beginPath();
      ctx.rect(itemX, itemY - 12, Math.max(0, columnWidth - 14), 18);
      ctx.clip();
      ctx.fillStyle = palette.text;
      ctx.fillText(text, itemX, itemY);
      ctx.restore();
    });
    ctx.restore();
    return metrics.height;
  }

  function drawCutLines(ctx, sheet, sx, sy, scale, color) {
    ctx.save();
    ctx.strokeStyle = color;
    ctx.lineWidth = 1.2;
    (sheet.cuts || []).forEach((cut) => {
      ctx.setLineDash(cut.stage === 0 ? [3, 3] : cut.stage === 1 ? [] : [7, 4]);
      ctx.beginPath();
      ctx.moveTo(sx + cut.x1 * scale, sy + cut.y1 * scale);
      ctx.lineTo(sx + cut.x2 * scale, sy + cut.y2 * scale);
      ctx.stroke();
    });
    ctx.restore();
  }

  function manualCutCandidates(region, parts) {
    const epsilon = 0.01;
    const candidates = [];
    const xEdges = [...new Set(parts.flatMap((part) => [part.x, part.x + part.w]))];
    const yEdges = [...new Set(parts.flatMap((part) => [part.y, part.y + part.h]))];
    xEdges.forEach((edge) => {
      if (edge <= region.x + epsilon || edge >= region.x + region.w - epsilon) return;
      if (parts.some((part) => edge > part.x + epsilon && edge < part.x + part.w - epsilon)) return;
      const before = parts.filter((part) => part.x + part.w <= edge + epsilon);
      const after = parts.filter((part) => part.x >= edge - epsilon);
      if (before.length + after.length !== parts.length) return;
      const leftEdge = before.length ? Math.max(...before.map((part) => part.x + part.w)) : edge;
      const rightEdge = after.length ? Math.min(...after.map((part) => part.x)) : edge;
      const line = before.length && after.length ? (leftEdge + rightEdge) / 2 : edge;
      candidates.push({
        orientation: "vertical",
        line,
        before,
        after,
        score: [before.length && after.length ? 0 : 1, Math.abs(before.length - after.length), -region.h]
      });
    });
    yEdges.forEach((edge) => {
      if (edge <= region.y + epsilon || edge >= region.y + region.h - epsilon) return;
      if (parts.some((part) => edge > part.y + epsilon && edge < part.y + part.h - epsilon)) return;
      const before = parts.filter((part) => part.y + part.h <= edge + epsilon);
      const after = parts.filter((part) => part.y >= edge - epsilon);
      if (before.length + after.length !== parts.length) return;
      const topEdge = before.length ? Math.max(...before.map((part) => part.y + part.h)) : edge;
      const bottomEdge = after.length ? Math.min(...after.map((part) => part.y)) : edge;
      const line = before.length && after.length ? (topEdge + bottomEdge) / 2 : edge;
      candidates.push({
        orientation: "horizontal",
        line,
        before,
        after,
        score: [before.length && after.length ? 0 : 1, Math.abs(before.length - after.length), -region.w]
      });
    });
    return candidates.sort((a, b) => compareCutScores(a.score, b.score));
  }

  function buildManualGuillotineCuts(region, parts, kerf) {
    const cuts = [];
    let unresolved = false;
    const kerfMargin = Math.max(0, Number(kerf) || 0) / 2 + 0.01;
    const split = (current, currentParts, depth) => {
      if (!currentParts.length) return;
      const fillsRegion = currentParts.length === 1
        && currentParts[0].x - current.x <= kerfMargin
        && currentParts[0].y - current.y <= kerfMargin
        && current.x + current.w - currentParts[0].x - currentParts[0].w <= kerfMargin
        && current.y + current.h - currentParts[0].y - currentParts[0].h <= kerfMargin;
      if (fillsRegion) return;
      const candidate = manualCutCandidates(current, currentParts)[0];
      if (!candidate) {
        unresolved = true;
        return;
      }
      const stage = Math.min(3, depth);
      if (candidate.orientation === "vertical") {
        cuts.push({ stage, x1: candidate.line, y1: current.y, x2: candidate.line, y2: current.y + current.h, length: current.h });
        split({ x: current.x, y: current.y, w: candidate.line - current.x, h: current.h }, candidate.before, depth + 1);
        split({ x: candidate.line, y: current.y, w: current.x + current.w - candidate.line, h: current.h }, candidate.after, depth + 1);
      } else {
        cuts.push({ stage, x1: current.x, y1: candidate.line, x2: current.x + current.w, y2: candidate.line, length: current.w });
        split({ x: current.x, y: current.y, w: current.w, h: candidate.line - current.y }, candidate.before, depth + 1);
        split({ x: current.x, y: candidate.line, w: current.w, h: current.y + current.h - candidate.line }, candidate.after, depth + 1);
      }
    };
    split(region, parts, 1);
    return { cuts, unresolved };
  }

  function buildManualLeftovers(region, parts, keepSize) {
    const xValues = [...new Set([region.x, region.x + region.w, ...parts.flatMap((part) => [part.x, part.x + part.w])])].sort((a, b) => a - b);
    const yValues = [...new Set([region.y, region.y + region.h, ...parts.flatMap((part) => [part.y, part.y + part.h])])].sort((a, b) => a - b);
    const rows = yValues.length - 1;
    const columns = xValues.length - 1;
    const free = Array.from({ length: rows }, (_, row) => Array.from({ length: columns }, (_, column) => {
      const x1 = xValues[column];
      const x2 = xValues[column + 1];
      const y1 = yValues[row];
      const y2 = yValues[row + 1];
      return !parts.some((part) => x1 < part.x + part.w - 0.01 && x2 > part.x + 0.01 && y1 < part.y + part.h - 0.01 && y2 > part.y + 0.01);
    }));
    const visited = Array.from({ length: rows }, () => Array(columns).fill(false));
    const leftovers = [];
    for (let row = 0; row < rows; row += 1) {
      for (let column = 0; column < columns; column += 1) {
        if (!free[row][column] || visited[row][column]) continue;
        let endColumn = column;
        while (endColumn + 1 < columns && free[row][endColumn + 1] && !visited[row][endColumn + 1]) endColumn += 1;
        let endRow = row;
        while (endRow + 1 < rows) {
          let canExtend = true;
          for (let checkColumn = column; checkColumn <= endColumn; checkColumn += 1) {
            if (!free[endRow + 1][checkColumn] || visited[endRow + 1][checkColumn]) { canExtend = false; break; }
          }
          if (!canExtend) break;
          endRow += 1;
        }
        for (let markRow = row; markRow <= endRow; markRow += 1) {
          for (let markColumn = column; markColumn <= endColumn; markColumn += 1) visited[markRow][markColumn] = true;
        }
        const rectangle = {
          x: xValues[column],
          y: yValues[row],
          w: xValues[endColumn + 1] - xValues[column],
          h: yValues[endRow + 1] - yValues[row]
        };
        leftovers.push({ ...rectangle, kept: Math.min(rectangle.w, rectangle.h) >= keepSize });
      }
    }
    return leftovers.filter((item) => item.w > 0.01 && item.h > 0.01);
  }

  function rebuildManualCutPlan(group, sheetIndex) {
    const sheet = group.sheets[sheetIndex];
    if (!sheet) return;
    const trim = Math.max(0, Number(cutState?.trim) || 0);
    const gap = Math.max(0, Number(cutState?.gap) || 0);
    const keepSize = Math.max(0, Number(cutState?.keepSize) || 0);
    const parts = group.parts.filter((part) => part.sheet === sheetIndex);
    const usableRegion = { x: trim, y: trim, w: Math.max(0, sheet.w - trim * 2), h: Math.max(0, sheet.h - trim * 2) };
    const manualPlan = buildManualGuillotineCuts(usableRegion, parts, gap);
    const trimCuts = trim > 0 ? [
      { stage: 0, x1: trim, y1: 0, x2: trim, y2: sheet.h, length: sheet.h },
      { stage: 0, x1: sheet.w - trim, y1: 0, x2: sheet.w - trim, y2: sheet.h, length: sheet.h },
      { stage: 0, x1: 0, y1: trim, x2: sheet.w, y2: trim, length: sheet.w },
      { stage: 0, x1: 0, y1: sheet.h - trim, x2: sheet.w, y2: sheet.h - trim, length: sheet.w }
    ] : [];
    const cuts = [...trimCuts, ...manualPlan.cuts];
    const leftovers = buildManualLeftovers(usableRegion, parts, keepSize);
    const partArea = parts.reduce((sum, part) => sum + part.w * part.h, 0);
    sheet.manual = true;
    sheet.manualUnresolved = manualPlan.unresolved;
    sheet.cuts = cuts;
    sheet.leftovers = leftovers;
    sheet.stats = {
      efficiency: sheet.w * sheet.h ? partArea / (sheet.w * sheet.h) : 0,
      cutCount: cuts.length,
      cutLength: cuts.reduce((sum, cut) => sum + cut.length, 0),
      keptCount: leftovers.filter((item) => item.kept).length,
      wasteArea: leftovers.reduce((sum, item) => sum + item.w * item.h, 0)
    };
  }

  function cutPartOverlaps(group, movingPart, sheetIndex, x, y) {
    const gap = Math.max(0, Number(cutState?.gap) || 0);
    const ax1 = x - gap;
    const ay1 = y - gap;
    const ax2 = x + movingPart.w + gap;
    const ay2 = y + movingPart.h + gap;
    return group.parts.some((part) => {
      if (part === movingPart || part.sheet !== sheetIndex) return false;
      return ax1 < part.x + part.w && ax2 > part.x && ay1 < part.y + part.h && ay2 > part.y;
    });
  }

  function isCutPlacementValid(group, movingPart, sheetIndex, x, y) {
    const sheet = group.sheets[sheetIndex];
    if (!sheet) return false;
    const trim = Math.max(0, Number(cutState?.trim) || 0);
    if (x < trim || y < trim) return false;
    if (x + movingPart.w > sheet.w - trim) return false;
    if (y + movingPart.h > sheet.h - trim) return false;
    return !cutPartOverlaps(group, movingPart, sheetIndex, x, y);
  }

  function slideCutPartOnAxis(group, movingPart, sheetIndex, position, target, axis) {
    const sheet = group.sheets[sheetIndex];
    const gap = Math.max(0, Number(cutState?.gap) || 0);
    const trim = Math.max(0, Number(cutState?.trim) || 0);
    const size = axis === "x" ? movingPart.w : movingPart.h;
    const crossSize = axis === "x" ? movingPart.h : movingPart.w;
    const sheetSize = axis === "x" ? sheet.w : sheet.h;
    const current = position[axis];
    const cross = axis === "x" ? position.y : position.x;
    let resolved = Math.max(trim, Math.min(sheetSize - size - trim, target));
    const direction = Math.sign(resolved - current);
    if (!direction) return current;

    group.parts.forEach((part) => {
      if (part === movingPart || part.sheet !== sheetIndex) return;
      const obstacle = axis === "x"
        ? { start: part.x, size: part.w, cross: part.y, crossSize: part.h }
        : { start: part.y, size: part.h, cross: part.x, crossSize: part.w };
      const crossesObstacle = cross < obstacle.cross + obstacle.crossSize + gap
        && cross + crossSize + gap > obstacle.cross;
      if (!crossesObstacle) return;

      if (direction > 0) {
        const stop = obstacle.start - gap - size;
        if (current <= stop && resolved > stop) resolved = Math.min(resolved, stop);
      } else {
        const stop = obstacle.start + obstacle.size + gap;
        if (current >= stop && resolved < stop) resolved = Math.max(resolved, stop);
      }
    });

    return Math.max(trim, Math.min(sheetSize - size - trim, resolved));
  }

  function moveCutPartWithSliding(group, part, sheetIndex, targetX, targetY) {
    if (part.sheet !== sheetIndex) {
      if (!isCutPlacementValid(group, part, sheetIndex, targetX, targetY)) return false;
      part.sheet = sheetIndex;
      part.x = targetX;
      part.y = targetY;
      return true;
    }

    const start = { x: part.x, y: part.y };
    const nextX = slideCutPartOnAxis(group, part, sheetIndex, start, targetX, "x");
    const afterX = { x: nextX, y: start.y };
    const nextY = slideCutPartOnAxis(group, part, sheetIndex, afterX, targetY, "y");
    if (!isCutPlacementValid(group, part, sheetIndex, nextX, nextY)) return false;
    if (nextX === part.x && nextY === part.y) return false;
    part.x = nextX;
    part.y = nextY;
    return true;
  }

  function bindCutCanvas() {
    const canvas = document.getElementById("cut-canvas");
    if (!canvas || canvas.dataset.bound) return;
    canvas.dataset.bound = "1";
    canvas.onmousedown = (event) => {
      const point = canvasPoint(canvas, event);
      const control = [...(cutState.controlHits || [])].reverse().find((item) => point.x >= item.rect[0] && point.x <= item.rect[0] + item.rect[2] && point.y >= item.rect[1] && point.y <= item.rect[1] + item.rect[3]);
      if (control) {
        rotateCutSelection(control.groupIndex, control.partId, control.action === "rotate-all");
        return;
      }
      const hit = [...(cutState.hit || [])].reverse().find((item) => point.x >= item.rect[0] && point.x <= item.rect[0] + item.rect[2] && point.y >= item.rect[1] && point.y <= item.rect[1] + item.rect[3]);
      if (!hit) { cutState.selectedPartId = ""; drawCutCanvas(); return; }
      cutState.drag = {
        hit,
        dx: point.x - hit.rect[0],
        dy: point.y - hit.rect[1],
        origin: { sheet: hit.part.sheet, x: hit.part.x, y: hit.part.y },
        startPoint: point,
        moved: false
      };
    };
    canvas.onmousemove = (event) => {
      if (!cutState.drag) return;
      const point = canvasPoint(canvas, event);
      if (!cutState.drag.moved && Math.hypot(point.x - cutState.drag.startPoint.x, point.y - cutState.drag.startPoint.y) < 4) return;
      cutState.drag.moved = true;
      const targetSheet = (cutState.hit || []).find((item) => point.x >= item.sheetRect[0] && point.x <= item.sheetRect[0] + item.sheetRect[2] && point.y >= item.sheetRect[1] && point.y <= item.sheetRect[1] + item.sheetRect[3] && item.groupIndex === cutState.drag.hit.groupIndex);
      const sheetRect = targetSheet?.sheetRect || cutState.drag.hit.sheetRect;
      const part = cutState.drag.hit.part;
      const group = cutState.groups[cutState.drag.hit.groupIndex];
      const nextSheetIndex = targetSheet?.sheetIndex ?? part.sheet;
      const sheet = group.sheets[nextSheetIndex];
      const trim = Math.max(0, Number(cutState.trim) || 0);
      const nextX = Math.max(trim, Math.min(sheet.w - part.w - trim, (point.x - sheetRect[0] - cutState.drag.dx) / cutState.scale));
      const nextY = Math.max(trim, Math.min(sheet.h - part.h - trim, (point.y - sheetRect[1] - cutState.drag.dy) / cutState.scale));
      if (!moveCutPartWithSliding(group, part, nextSheetIndex, nextX, nextY)) return;
      const activeSheet = group.sheets[nextSheetIndex];
      activeSheet.manual = true;
      activeSheet.cuts = [];
      activeSheet.leftovers = [];
      drawCutCanvas();
    };
    window.onmouseup = () => {
      if (!cutState?.drag) return;
      const { hit, origin } = cutState.drag;
      const group = cutState.groups[hit.groupIndex];
      const part = hit.part;
      if (!cutState.drag.moved) {
        cutState.selectedPartId = part.id;
        cutState.drag = null;
        drawCutCanvas();
        return;
      }
      if (!isCutPlacementValid(group, part, part.sheet, part.x, part.y)) {
        part.sheet = origin.sheet;
        part.x = origin.x;
        part.y = origin.y;
        drawCutCanvas();
      }
      rebuildManualCutPlan(group, origin.sheet);
      if (part.sheet !== origin.sheet) rebuildManualCutPlan(group, part.sheet);
      cutState.drag = null;
      drawCutCanvas();
    };
  }

  function rotateCutSelection(groupIndex, partId, rotateAll) {
    const group = cutState.groups[groupIndex];
    const selected = group?.parts.find((part) => part.id === partId);
    if (!selected || selected.allowRotation === false) return;
    const nextRotation = selected.manualRotation !== true;
    const targets = rotateAll
      ? group.parts.filter((part) => part.allowRotation !== false
        && Math.abs(Math.min(part.baseW, part.baseH) - Math.min(selected.baseW, selected.baseH)) < 0.01
        && Math.abs(Math.max(part.baseW, part.baseH) - Math.max(selected.baseW, selected.baseH)) < 0.01)
      : [selected];
    targets.forEach((part) => { part.manualRotation = nextRotation; });
    layoutCut(cutState);
    cutState.selectedPartId = selected.id;
    renderQuiet();
    notify(rotateAll ? "Одинаковые детали повернуты" : "Деталь повернута", "Раскрой пересчитан");
  }

  function canvasPoint(canvas, event) {
    const rect = canvas.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
  }

  function printCut() {
    const canvas = document.getElementById("cut-canvas");
    const project = getProject();
    const totals = projectTotals(project);
    drawCutCanvas(true);
    const pages = cutState.groups.flatMap((group) => group.sheets.map((sheet, sheetIndex) => {
      const rect = sheet.renderRect;
      const pageCanvas = document.createElement("canvas");
      pageCanvas.width = Math.ceil(rect.w * 2);
      pageCanvas.height = Math.ceil(rect.h * 2);
      const pageContext = pageCanvas.getContext("2d");
      pageContext.scale(2, 2);
      pageContext.drawImage(canvas, rect.x, rect.y, rect.w, rect.h, 0, 0, rect.w, rect.h);
      return { group, sheet, sheetIndex, image: pageCanvas.toDataURL("image/png") };
    }));
    const win = window.open("", "_blank");
    win.document.write(`<html><head><title>Карта кроя</title><style>@page{size:A4 landscape;margin:8mm}*{box-sizing:border-box}html,body{margin:0;padding:0}body{font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Arial,sans-serif;color:#111}.print-page{height:194mm;display:flex;flex-direction:column;break-after:page;page-break-after:always;overflow:hidden}.print-page:last-child{break-after:auto;page-break-after:auto}h1{font-size:14px;margin:0 0 3px}.meta{font-size:9px;margin-bottom:5px;display:flex;gap:12px;flex-wrap:wrap}.sheet-title{font-size:11px;font-weight:700;margin:2px 0 5px}.sheet-image{display:block;width:100%;height:auto;max-height:174mm;object-fit:contain;object-position:left top}</style></head><body>${pages.map(({ group, sheet, sheetIndex, image }) => `<section class="print-page"><h1>Карта кроя</h1><div class="meta"><span>Контрагент: ${esc(project.counterparty)}</span><span>Адрес: ${esc(project.address || "-")}</span><span>ДСП/ДВП: ${fmt(totals.area)} м²</span></div><div class="sheet-title">${esc(group.material.name)} · лист ${sheetIndex + 1} · ${sheet.w} × ${sheet.h} мм${group.material.textureDirection ? " · текстура →" : ""}</div><img class="sheet-image" src="${image}"></section>`).join("")}</body></html>`);
    win.document.close();
    win.focus();
    win.print();
    drawCutCanvas(false);
    notify("Карта кроя отправлена на печать");
  }

  async function exportDetails() {
    const project = getProject();
    if (!project.products.length) return;
    const attachments = [];
    let imageIndex = 0;
    const wrapBase64 = (value) => String(value || "").replace(/(.{1,76})/g, "$1\r\n").trim();
    const embeddedImageHtml = async (product) => {
      if (!product.image || !String(product.image).startsWith("data:")) return "";
      const match = String(product.image).match(/^data:([^;]+);base64,(.+)$/);
      if (!match) return "";
      const extension = match[1].includes("png") ? "png" : match[1].includes("gif") ? "gif" : "jpg";
      const location = `file:///product-image-${imageIndex += 1}.${extension}`;
      const dimensions = await imageExportDimensions(product.image);
      attachments.push({ location, mime: match[1], data: match[2] });
      return `<div class="product-image"><img src="${location}" width="${dimensions.width}" height="${dimensions.height}"></div>`;
    };
    const sections = (await Promise.all(project.products.map(async (product, index) => {
      const quantity = productQty(product);
      const rows = product.details.map((detail) => {
        const calc = detailCalc(product, detail);
        return `<tr><td>${esc(detail.name)}</td><td>${calc.qty * quantity}</td><td>${esc(calc.material?.name || "")}</td><td>${Math.round(calc.length)}</td><td>${Math.round(calc.width)}</td><td>${fmt(calc.displayArea * quantity)}</td></tr>`;
      }).join("");
      return `<section class="product-section ${index ? "new-page" : ""}">
        <h1>Деталировка · ${esc(project.name)}</h1>
        <h2>${esc(product.name)} · ${quantity} шт.</h2>
        <div class="dimensions">Размер изделия: ${fmt(product.length, 0)} × ${fmt(product.depth, 0)} × ${fmt(product.height, 0)} мм</div>
        ${await embeddedImageHtml(product)}
        <table><thead><tr><th>Деталь</th><th>Кол-во</th><th>Материал</th><th>Длина</th><th>Ширина</th><th>м²</th></tr></thead><tbody>${rows}</tbody></table>
      </section>`;
    }))).join("");
    const html = `<html><head><meta charset="utf-8"><style>@page{size:A4 portrait;margin:12mm}body{font-family:Arial,sans-serif;font-size:10pt;margin:0}.product-section{page-break-inside:auto}.product-section.new-page{page-break-before:always}h1{font-size:16pt;margin:0 0 6pt}h2{font-size:13pt;margin:0 0 4pt}.dimensions{font-weight:600;margin-bottom:7pt}.product-image{display:block;height:auto;min-height:190px;max-height:320px;margin:4pt 0 10pt;overflow:hidden}.product-image img{display:block;object-fit:contain;object-position:left top;border:1px solid #999}table{border-collapse:collapse;width:100%;page-break-inside:auto}thead{display:table-header-group}tr{page-break-inside:avoid}td,th{border:1px solid #999;padding:5px;text-align:left}</style></head><body>${sections}</body></html>`;
    if (attachments.length) {
      const boundary = `----=_VitanK_${Date.now()}`;
      const parts = [
        `MIME-Version: 1.0`,
        `Content-Type: multipart/related; boundary="${boundary}"`,
        ``,
        `--${boundary}`,
        `Content-Type: text/html; charset="utf-8"`,
        `Content-Location: file:///detalization.htm`,
        ``,
        html,
        ...attachments.flatMap((image) => [
          `--${boundary}`,
          `Content-Type: ${image.mime}`,
          `Content-Transfer-Encoding: base64`,
          `Content-Location: ${image.location}`,
          ``,
          wrapBase64(image.data)
        ]),
        `--${boundary}--`
      ].join("\r\n");
      downloadBlob(`${project.name || "detalization"}.xls`, parts, "application/vnd.ms-excel");
    } else {
      downloadBlob(`${project.name || "detalization"}.xls`, html, "application/vnd.ms-excel;charset=utf-8");
    }
    notify("Деталировка выгружена", "Excel-файл создан");
  }

  function imageExportDimensions(source) {
    return new Promise((resolve) => {
      const image = new Image();
      image.onload = () => {
        const width = Math.max(1, image.naturalWidth || image.width || 1);
        const height = Math.max(1, image.naturalHeight || image.height || 1);
        const maxWidth = 360;
        const maxHeight = 320;
        let scale = Math.min(maxWidth / width, maxHeight / height);
        const largest = Math.max(width * scale, height * scale);
        if (largest < 190) scale *= 190 / largest;
        resolve({ width: Math.round(width * scale), height: Math.round(height * scale) });
      };
      image.onerror = () => resolve({ width: 240, height: 240 });
      image.src = source;
    });
  }

  function exportBackup() {
    downloadBlob("vitan-projects.json", JSON.stringify(state, null, 2), "application/json");
    notify("База сохранена", "JSON-файл создан");
  }

  function importBackup() {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = "application/json";
    input.onchange = () => {
      const file = input.files[0];
      if (!file) return;
      const reader = new FileReader();
      reader.onload = () => {
        try {
          state = JSON.parse(reader.result);
          saveState();
          route = { name: "projects" };
          notify("База загружена");
        } catch (error) {
          alert("Не удалось загрузить файл базы.");
        }
      };
      reader.readAsText(file);
    };
    input.click();
  }

  function downloadBlob(filename, content, type) {
    const blob = new Blob([content], { type });
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = filename.replace(/[\\/:*?"<>|]/g, "_");
    link.click();
    URL.revokeObjectURL(link.href);
  }

  window.addEventListener("resize", () => {
    if (route.name === "product" || route.name === "catalog-product") updateDetailTableOverflow();
  });
  systemThemeQuery.addEventListener("change", () => {
    if (themeMode !== "auto") return;
    applyTheme();
  });
  window.addEventListener("beforeunload", (event) => {
    if (window.vitanDesktop) {
      saveState();
      return;
    }
    if (!shouldWarnBeforeUnload()) {
      flushSaveState();
      return;
    }
    event.preventDefault();
    event.returnValue = "";
    return "";
  });
  render();
})();
