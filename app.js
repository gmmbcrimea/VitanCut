(function () {
  "use strict";

  const LEGACY_LS_KEY = "vitark_furniture_calc_v1";
  const LS_KEY = "vitan_furniture_calc_v1";
  const THEME_KEY = "vitan_furniture_calc_theme";
  const BACKGROUND_KEY = "vitan_furniture_calc_background";
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
  let theme = localStorage.getItem(THEME_KEY) || (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
  let customBackground = localStorage.getItem(BACKGROUND_KEY) || "";
  let contextMenu = null;
  let openDropdown = null;
  let toasts = [];
  let currentViewKey = "";
  let pageTransition = false;
  let suppressRenderMotion = false;
  let skeletonTimer = null;
  let draggedDetailId = "";
  let detailDropPlacement = "before";
  let activeFormulaInput = null;

  const app = document.getElementById("app");
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
      const raw = localStorage.getItem(LS_KEY) || localStorage.getItem(LEGACY_LS_KEY);
      const loaded = normalizeState(raw ? JSON.parse(raw) : structuredClone(seed));
      if (!localStorage.getItem(LS_KEY) && raw) {
        localStorage.setItem(LS_KEY, JSON.stringify(loaded));
      }
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
    nextState.materialGroups.forEach((group) => { nextState.materials[group] ||= []; });
    nextState.projects ||= [];
    const shouldSeedCatalog = !nextState.catalog;
    nextState.catalog ||= {};
    nextState.projects.forEach((project) => {
      project.products ||= [];
      project.payrolls ||= [];
      nextState.catalog[project.counterparty] ||= [];
      project.products.forEach((product) => {
        product.details ||= [];
        product.qty = Math.max(1, Number(product.qty) || 1);
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
      products.forEach((product) => {
        product.details ||= [];
        product.qty = Math.max(1, Number(product.qty) || 1);
      });
    });
    return nextState;
  }

  function getMaterialTypes() {
    return state.materialGroups?.length ? state.materialGroups : Object.keys(state.materials);
  }

  function saveState() {
    localStorage.setItem(LS_KEY, JSON.stringify(state));
  }

  function applyTheme() {
    document.documentElement.dataset.theme = theme;
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

  function toggleTheme() {
    theme = theme === "dark" ? "light" : "dark";
    localStorage.setItem(THEME_KEY, theme);
    applyTheme();
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
          <div class="glass-select-menu">
            ${options.map((option) => `
              <div class="glass-select-row">
                <button class="glass-select-option ${option.value === selected.value ? "selected" : ""}" type="button" data-action="select-value" data-key="${esc(key)}" data-kind="${esc(kind)}" data-value="${esc(option.value)}" data-type="${esc(type)}" data-detail-id="${esc(detailId)}">
                  ${esc(option.label)}
                </button>
                ${canRename ? `<button class="glass-select-small" type="button" data-action="select-rename" data-kind="${esc(kind)}" data-value="${esc(option.value)}" data-type="${esc(type)}" data-detail-id="${esc(detailId)}">Изм.</button>` : ""}
                ${canDelete ? `<button class="glass-select-delete" type="button" data-action="select-delete" data-kind="${esc(kind)}" data-value="${esc(option.value)}" data-type="${esc(type)}" data-detail-id="${esc(detailId)}">Удалить</button>` : ""}
              </div>
            `).join("")}
            ${canAdd ? `<button class="glass-select-add" type="button" data-action="select-add" data-kind="${esc(kind)}" data-type="${esc(type)}" data-detail-id="${esc(detailId)}">+ Добавить</button>` : ""}
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

  function notify(title, text = "", type = "") {
    const id = uid();
    toasts = [...toasts.slice(-2), { id, title, text, type }];
    if (modal) suppressRenderMotion = true;
    render();
    window.setTimeout(() => {
      toasts = toasts.filter((toast) => toast.id !== id);
      if (modal) renderQuiet();
      else render();
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
          <svg viewBox="0 0 48 48" focusable="false">
            <path d="M11 19.5 16.5 9h15L37 19.5v14A5.5 5.5 0 0 1 31.5 39h-15A5.5 5.5 0 0 1 11 33.5v-14Z"/>
            <path d="M12 20h9l2.2 4h1.6L27 20h9"/>
          </svg>
        </div>
        <h3>${esc(title)}</h3>
        <p>${esc(text)}</p>
        ${action ? `<button class="ghost" data-action="${esc(action)}">${esc(actionText)}</button>` : ""}
      </div>
    `;
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

  function isCatalogRoute() {
    return route.name === "catalog-product";
  }

  function isProductEditorRoute() {
    return route.name === "product" || route.name === "catalog-product";
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
    (product.details || []).forEach((detail) => {
      if (detail.id === currentDetailId || stack.has(detail.id)) return;
      const name = String(detail.name || "").trim().toLowerCase();
      if (!name) return;
      const calc = detailCalc(product, detail, new Set([...stack, currentDetailId]));
      normalized = normalized.replaceAll(`${name} длина`, String(calc.length));
      normalized = normalized.replaceAll(`${name} ширина`, String(calc.width));
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
    const width = evaluateExpression(detail.widthExpr, product, detail.id, stack);
    const qty = Number(detail.qty) || 1;
    const material = getMaterial(detail.type, detail.materialId);
    const rawArea = length * width * qty / 1000000;
    const area = isDspMaterial(detail, material) ? rawArea : 0;
    const glassArea = isGlassMaterial(detail, material) ? rawArea : 0;
    const glassEdge = isGlassMaterial(detail, material) ? (length + width) * 2 * qty : 0;
    const perimeter = (length + width) * 2 * qty / 1000;
    let usage = qty;
    if (material?.unit === "m2") usage = rawArea;
    if (material?.unit === "lm") usage = detail.type === "Кромка" ? perimeter : Math.max(length, width) * qty / 1000;
    const cost = usage * (Number(material?.cost) || 0);
    return { length, width, qty, area, rawArea, glassArea, glassEdge, perimeter, usage, cost, material };
  }

  function productTotals(product) {
    const multiplier = productQty(product);
    return (product.details || []).reduce((acc, detail) => {
      const calc = detailCalc(product, detail);
      acc.area += calc.area * multiplier;
      acc.rawArea += calc.rawArea * multiplier;
      acc.glassArea += calc.glassArea * multiplier;
      acc.glassEdge += calc.glassEdge * multiplier;
      acc.cost += calc.cost * multiplier;
      return acc;
    }, { area: 0, rawArea: 0, glassArea: 0, glassEdge: 0, cost: 0 });
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

  function routeKey() {
    return [route.name, route.projectId || "", route.counterparty || "", route.productId || ""].join(":");
  }

  function shell(content) {
    return `
      <div class="app-shell">
        <header class="topbar">
          <div class="brand">
            <img src="${logoPath}" alt="Витан-К">
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
        <main class="page ${pageTransition ? "page-enter page-skeleton" : ""}">${content}</main>
        <input id="background-input" type="file" accept="image/*" hidden>
      </div>
      ${modal ? modalHtml() : ""}
      ${contextMenu ? contextMenuHtml() : ""}
      ${toastHtml()}
    `;
  }

  function render() {
    if (route.name === "project" && !getProject()) route = { name: "projects" };
    if (route.name === "product" && route.productId && !getProduct(getProject())) route = { name: "project", projectId: route.projectId };
    if (route.name === "catalog-product" && route.productId && !getCatalogProduct()) route = { name: "catalog" };
    const nextViewKey = routeKey();
    pageTransition = !suppressRenderMotion && nextViewKey !== currentViewKey;
    currentViewKey = nextViewKey;
    if (pageTransition) {
      clearTimeout(skeletonTimer);
      skeletonTimer = setTimeout(() => {
        pageTransition = false;
        renderQuiet();
      }, 1000);
    }
    if (route.name === "materials") renderMaterialsPage();
    else if (route.name === "catalog") renderCatalogPage();
    else if (route.name === "cut") renderCutPage();
    else if (route.name === "details") renderDetailsPage();
    else if (route.name === "product" || route.name === "catalog-product") renderProductEditor();
    else if (route.name === "project") renderProjectPage();
    else renderProjectsPage();
    bindGlobal();
    suppressRenderMotion = false;
  }

  function renderQuiet() {
    suppressRenderMotion = true;
    render();
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
      const products = getCatalogProducts(counterparty);
      const expanded = route.counterparty === counterparty;
      const rows = products.map((product, index) => {
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
      }).join("");
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
              <thead><tr><th>Номер</th><th>Название</th><th>Габариты</th><th>Квадратура ДСП</th><th></th></tr></thead>
              <tbody>${rows || `<tr><td colspan="5"><div class="empty-state compact"><div class="empty-state-skeleton" aria-hidden="true"><span></span><span></span><span></span></div><div class="empty-state-icon" aria-hidden="true"><svg viewBox="0 0 48 48" focusable="false"><path d="M11 19.5 16.5 9h15L37 19.5v14A5.5 5.5 0 0 1 31.5 39h-15A5.5 5.5 0 0 1 11 33.5v-14Z"/><path d="M12 20h9l2.2 4h1.6L27 20h9"/></svg></div><h3>Изделий нет</h3><p>Добавьте шаблон изделия для этого контрагента.</p><button type="button" data-action="add-catalog-product" data-counterparty="${esc(counterparty)}">Добавить изделие</button></div></td></tr>`}</tbody>
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
      </div>
      ${sections || `<section class="empty">${emptyStateHtml({ title: "Контрагентов пока нет", text: "Создайте контрагента при добавлении проекта, чтобы вести каталог изделий.", action: "new-project", actionText: "Создать проект" })}</section>`}
    `);
  }

  function renderMaterialsPage() {
    const groups = getMaterialTypes();
    const sections = groups.map((type) => {
      const materials = state.materials[type] || [];
      const rows = materials.map((material, index) => `
        <tr>
          <td>${index + 1}</td>
          <td>${esc(material.name)}</td>
          <td>${esc(units.find((unit) => unit[0] === material.unit)?.[1] || material.unit || "шт.")}</td>
          <td class="nowrap">${money(material.cost)}</td>
          <td class="nowrap">${material.sheetLength && material.sheetWidth ? `${fmt(material.sheetLength, 0)} × ${fmt(material.sheetWidth, 0)} мм` : "—"}</td>
          <td>
            <div class="row-actions glass-button-group material-tools">
              <button class="ghost icon-btn" data-action="rename-material" data-type="${esc(type)}" data-id="${material.id}" data-tooltip="Изменить материал">✎</button>
              <button class="ghost danger icon-btn" data-action="remove-material" data-type="${esc(type)}" data-id="${material.id}" data-tooltip="Удалить материал">×</button>
            </div>
          </td>
        </tr>
      `).join("");
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
          <section class="table-wrap">
            <table>
              <thead><tr><th>Номер</th><th>Материал</th><th>Ед.</th><th>Себестоимость</th><th>Лист</th><th></th></tr></thead>
              <tbody>${rows || `<tr><td colspan="6"><div class="empty-state compact"><div class="empty-state-skeleton" aria-hidden="true"><span></span><span></span><span></span></div><div class="empty-state-icon" aria-hidden="true"><svg viewBox="0 0 48 48" focusable="false"><path d="M11 19.5 16.5 9h15L37 19.5v14A5.5 5.5 0 0 1 31.5 39h-15A5.5 5.5 0 0 1 11 33.5v-14Z"/><path d="M12 20h9l2.2 4h1.6L27 20h9"/></svg></div><h3>Материалов нет</h3><p>Добавьте материал в этот тип, чтобы использовать его в деталях изделий.</p><button type="button" data-action="add-material" data-type="${esc(type)}">Добавить материал</button></div></td></tr>`}</tbody>
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
          <button class="ghost" data-action="go-projects">← Назад</button>
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
            <div class="stat"><span>Квадратура ДСП</span><strong>${fmt(totals.area)} м²</strong></div>
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
    const totals = productTotals(product);
    const detailRows = product.details.map((detail) => {
      const materialOptions = (state.materials[detail.type] || []).map((material) => ({ value: material.id, label: material.name }));
      const typeOptions = getMaterialTypes().map((type) => ({ value: type, label: type }));
      const unit = detailCalc(product, detail);
      return `
        <tr data-detail-id="${detail.id}">
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
          <td><div class="formula-wrap"><textarea class="formula-input" rows="1" data-field="detail-length" data-axis="length" placeholder="240 или =">${esc(detail.lengthExpr)}</textarea></div></td>
          <td><div class="formula-wrap"><textarea class="formula-input" rows="1" data-field="detail-width" data-axis="width" placeholder="240 или =">${esc(detail.widthExpr)}</textarea></div></td>
          <td class="nowrap" data-calc-cell>${fmt(unit.area)} м²<br><span class="muted">${fmt(unit.length, 0)} × ${fmt(unit.width, 0)}</span></td>
          <td><button class="ghost danger icon-btn" data-action="remove-detail" data-id="${detail.id}" data-tooltip="Удалить деталь">×</button></td>
        </tr>
      `;
    }).join("");

    app.innerHTML = shell(`
      <div class="toolbar">
        <div>
          <button class="ghost" data-action="${catalogMode ? "back-catalog" : "back-project"}" data-tooltip="${catalogMode ? "Вернуться в каталог" : "Вернуться в проект"}">← ${catalogMode ? "К каталогу" : "К проекту"}</button>
          <h2>${route.productId ? "Карточка изделия" : "Новое изделие"}</h2>
          ${catalogMode ? `<div class="muted">${esc(route.counterparty || "")}</div>` : ""}
        </div>
        <div class="toolbar-actions glass-button-group">
          <button data-action="save-product">${route.productId ? "Сохранить" : "Создать"}</button>
        </div>
      </div>
      <section class="product-layout">
        <aside class="card">
          <div class="image-box ${product.image ? "has-image" : ""}">${product.image ? `<img src="${product.image}" alt="">` : "Изображение изделия"}</div>
          <div class="row-actions glass-button-group" style="margin-top:12px">
            <button class="ghost" data-action="attach-image" data-tooltip="Добавить изображение изделия">Прикрепить</button>
            <button class="ghost danger" data-action="remove-image" data-tooltip="Удалить изображение">Удалить</button>
            <input id="image-input" type="file" accept="image/*" hidden>
          </div>
          <div class="stats product-stats" style="grid-template-columns:1fr; margin:14px 0 0">
            <div class="stat"><span>Квадратура ДСП</span><strong data-live-product-area>${fmt(totals.area)} м²</strong></div>
            <div class="stat"><span>Грани стекла</span><strong data-live-glass-edge>${fmt(totals.glassEdge, 0)} мм</strong></div>
            <div class="stat"><span>Себестоимость</span><strong>${money(totals.cost)}</strong></div>
          </div>
        </aside>
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
      </section>
      <div class="toolbar" style="margin-top:18px">
        <h2>Детали</h2>
      </div>
      <section class="table-wrap detail-table-wrap">
        <table class="detail-table">
          <thead><tr><th>Название</th><th>Кол-во</th><th>Тип</th><th>Материал</th><th>Длина</th><th>Ширина</th><th>Расчет</th><th></th></tr></thead>
          <tbody>${detailRows || `<tr><td colspan="8">${emptyStateHtml({ title: "Деталей пока нет", text: "Создайте первую деталь изделия и укажите материал, размеры и количество.", action: "add-detail", actionText: "Добавить деталь", compact: true })}</td></tr>`}</tbody>
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
    app.innerHTML = shell(`
      <div class="toolbar">
        <div>
          <button class="ghost" data-action="back-project">← К проекту</button>
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
          <p class="muted">Детали можно перетаскивать мышкой внутри листов. Раскрой группируется по материалам, в названии которых есть ДСП.</p>
        </aside>
        <div class="canvas-wrap"><canvas id="cut-canvas" width="1200" height="800"></canvas></div>
      </section>
    `);
    bindGlobal();
    drawCutCanvas();
  }

  function renderDetailsPage() {
    const project = getProject();
    const tabs = project.products.map((product, index) => `<button class="${route.productId === product.id || (!route.productId && index === 0) ? "active" : "ghost"}" data-action="details-product" data-id="${product.id}">${esc(product.name)}</button>`).join("");
    const product = getProduct(project, route.productId) || project.products[0];
    const rows = product ? product.details.map((detail) => {
      const calc = detailCalc(product, detail);
      return `<tr><td>${esc(detail.name)}</td><td>${calc.qty}</td><td>${esc(calc.material?.name || "")}</td><td>${fmt(calc.length, 0)} × ${fmt(calc.width, 0)}</td><td>${fmt(calc.area)} м²</td></tr>`;
    }).join("") : "";
    app.innerHTML = shell(`
      <div class="toolbar">
        <div>
          <button class="ghost" data-action="back-project">← К проекту</button>
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
      project.draft = { id: uid(), name: "", image: "", length: 600, depth: 500, height: 720, hasLegs: false, legHeight: 100, details: [] };
    }
    return project.draft;
  }

  function createCatalogDraft() {
    state.catalog ||= {};
    const counterparty = route.counterparty || state.counterparties[0] || "Контрагент";
    state.catalog[counterparty] ||= [];
    state.catalogDrafts ||= {};
    if (!state.catalogDrafts[counterparty]) {
      state.catalogDrafts[counterparty] = { id: uid(), name: "", image: "", qty: 1, length: 600, depth: 500, height: 720, hasLegs: false, legHeight: 100, details: [] };
    }
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
      cell.innerHTML = `${fmt(calc.area)} м²<br><span class="muted">${fmt(calc.length, 0)} × ${fmt(calc.width, 0)}</span>`;
    });
    const totals = productTotals(product);
    const area = document.querySelector("[data-live-product-area]");
    const glassEdge = document.querySelector("[data-live-glass-edge]");
    if (area) area.textContent = `${fmt(totals.area)} м²`;
    if (glassEdge) glassEdge.textContent = `${fmt(totals.glassEdge, 0)} мм`;
    requestAnimationFrame(updateDetailTableOverflow);
  }

  function showFormulaCommand(input, product) {
    document.querySelector(".formula-command")?.remove();
    activeFormulaInput = input;
    if (!input.value.trim().startsWith("=")) return;
    const row = input.closest("[data-detail-id]");
    const refs = formulaReferences(product, row?.dataset.detailId);
    const rect = input.getBoundingClientRect();
    const panel = document.createElement("div");
    panel.className = "formula-command";
    panel.style.left = `${rect.left}px`;
    panel.style.top = `${rect.bottom + 8}px`;
    panel.style.width = `${Math.max(320, rect.width)}px`;
    panel.innerHTML = `
      <div class="formula-command-search">⌕ Выберите ссылку для формулы</div>
      <div class="formula-command-title">Suggestions</div>
      ${refs.map((ref) => `<button type="button" data-action="formula-ref" data-expr="${esc(ref.expr)}">${esc(ref.label)}<span>${fmt(ref.value, 0)} мм</span></button>`).join("")}
    `;
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
    active.value = `= ${button.dataset.expr}`;
    const product = currentEditableProduct();
    const row = active.closest("[data-detail-id]");
    if (row) updateDetailFromRow(row, product);
    saveState();
    refreshProductCalculations(product);
    document.querySelector(".formula-command")?.remove();
    active.focus();
  }

  function modalHtml() {
    const modalClass = `modal${suppressRenderMotion ? " no-animate" : ""}`;
    const backdropClass = `modal-backdrop${suppressRenderMotion ? " no-animate" : ""}`;
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
    const items = contextMenu.items.map((item) => {
      if (item.separator) return `<div class="context-separator" role="separator"></div>`;
      return `<button class="${item.danger ? "danger-item" : ""}" data-action="${item.action}" ${item.id ? `data-id="${item.id}"` : ""}>${item.label}</button>`;
    }).join("");
    return `
      <div class="context-layer" data-action="close-context">
        <div class="context-menu" role="menu" style="left:${contextMenu.x}px; top:${contextMenu.y}px">
          ${items}
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

  function openSettingsMenu(button) {
    const rect = button.getBoundingClientRect();
    const width = 240;
    const items = [
      { label: theme === "dark" ? "Светлая тема" : "Тёмная тема", action: "toggle-theme" },
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
      items
    };
    render();
  }

  function bindGlobal() {
    app.onclick = (event) => {
      const button = event.target.closest("[data-action]");
      if (!button) return;
      const action = button.dataset.action;
      const id = button.dataset.id;
      if (action !== "close-context") contextMenu = null;
      if (action === "close-context") { contextMenu = null; render(); return; }
      if (action === "noop") return;
      if (action === "toggle-select") { preserveProjectModalDraft(); openDropdown = openDropdown === button.dataset.key ? null : button.dataset.key; renderQuiet(); return; }
      if (action === "select-value") { handleSelectValue(button); return; }
      if (action === "select-add") { handleSelectAdd(button); return; }
      if (action === "select-delete") { handleSelectDelete(button); return; }
      if (action === "select-rename") { handleSelectRename(button); return; }
      if (action === "formula-ref") { insertFormulaReference(button); return; }
      if (action === "go-projects") { route = { name: "projects" }; modal = null; cutState = null; render(); }
      if (action === "crumb-project") { route = { name: "project", projectId: route.projectId }; modal = null; cutState = null; render(); }
      if (action === "open-materials") { route = { name: "materials" }; modal = null; cutState = null; render(); }
      if (action === "open-catalog") { route = { name: "catalog" }; modal = null; cutState = null; render(); }
      if (action === "toggle-catalog-counterparty") {
        const counterparty = button.dataset.counterparty;
        route = { name: "catalog", counterparty: route.counterparty === counterparty ? "" : counterparty };
        render();
      }
      if (action === "back-catalog") { route = { name: "catalog" }; cutState = null; render(); }
      if (action === "add-catalog-product") { route = { name: "catalog-product", counterparty: button.dataset.counterparty || state.counterparties[0] || "Контрагент" }; modal = null; render(); }
      if (action === "edit-catalog-product") { route = { name: "catalog-product", counterparty: button.dataset.counterparty, productId: id }; render(); }
      if (action === "delete-catalog-product" && confirm("Удалить изделие из каталога?")) {
        const products = getCatalogProducts(button.dataset.counterparty);
        state.catalog[button.dataset.counterparty] = products.filter((product) => product.id !== id);
        saveState();
        notify("Изделие удалено из каталога", "", "danger");
      }
      if (action === "open-settings") { openSettingsMenu(button); return; }
      if (action === "toggle-theme") toggleTheme();
      if (action === "new-project") { modal = { type: "project" }; render(); }
      if (action === "close-modal") { modal = null; render(); }
      if (action === "open-project") { route = { name: "project", projectId: id }; render(); }
      if (action === "delete-project" && confirm("Удалить проект?")) { state.projects = state.projects.filter((p) => p.id !== id); saveState(); notify("Проект удалён", "", "danger"); }
      if (action === "add-product") { modal = { type: "productPicker" }; render(); }
      if (action === "create-product") { getProject().draft = null; route = { name: "product", projectId: route.projectId }; modal = null; render(); }
      if (action === "duplicate-product") duplicateProduct(id, button.dataset.sourceProject, button.dataset.sourceCatalog);
      if (action === "edit-product") { route = { name: "product", projectId: route.projectId, productId: id }; render(); }
      if (action === "delete-product" && confirm("Удалить изделие?")) { const p = getProject(); p.products = p.products.filter((x) => x.id !== id); saveState(); notify("Изделие удалено", "", "danger"); }
      if (action === "product-qty") return;
      if (action === "back-project") { route = { name: "project", projectId: route.projectId }; cutState = null; render(); }
      if (action === "add-detail") addDetail();
      if (action === "remove-detail") removeDetail(id);
      if (action === "save-product") saveProduct();
      if (action === "attach-image") document.getElementById("image-input")?.click();
      if (action === "remove-image") { const p = currentEditableProduct(); p.image = ""; saveState(); notify("Изображение удалено"); }
      if (action === "add-material-group") addMaterialGroup();
      if (action === "rename-material-group") renameMaterialGroup(button.dataset.type);
      if (action === "delete-material-group") deleteMaterialGroup(button.dataset.type);
      if (action === "add-material") addMaterial(button.dataset.type);
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
        updateProductBasics(product);
        const detailRow = event.target.closest("[data-detail-id]");
        if (detailRow) updateDetailFromRow(detailRow, product);
        else updateProductFromForm(product);
        saveState();
        refreshProductCalculations(product);
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
        updateProductFromForm(currentEditableProduct());
        saveState();
        render();
      }
    };

    app.onfocusin = (event) => {
      if (event.target.matches(".formula-input")) {
        event.target.classList.add("is-expanded");
        showFormulaCommand(event.target, currentEditableProduct());
      }
    };

    app.onfocusout = (event) => {
      if (event.target.matches(".formula-input")) {
        event.target.classList.remove("is-expanded");
        setTimeout(() => document.querySelector(".formula-command")?.remove(), 120);
      }
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
    notify("Проект создан", project.name);
  }

  function handleSelectValue(button) {
    const { kind, value, type, detailId } = button.dataset;
    openDropdown = null;
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
      notify(kind === "detail-type" ? "Тип детали изменён" : "Материал выбран");
    }
  }

  function handleSelectAdd(button) {
    const { kind, type, detailId } = button.dataset;
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
    updateProductFromForm(product);
    const firstType = getMaterialTypes()[0] || "ЛДСП";
    state.materials[firstType] ||= [];
    product.details.push({ id: uid(), name: "", qty: 1, type: firstType, materialId: state.materials[firstType][0]?.id || "", lengthExpr: "длина изделия", widthExpr: "глубина изделия" });
    saveState();
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
    notify("Порядок деталей изменён");
  }

  function removeDetail(id) {
    const product = currentEditableProduct();
    updateProductFromForm(product);
    product.details = product.details.filter((detail) => detail.id !== id);
    saveState();
    notify("Деталь удалена", "", "danger");
  }

  function saveProduct() {
    if (isCatalogRoute()) {
      const product = currentEditableProduct();
      updateProductFromForm(product);
      product.qty = 1;
      const products = getCatalogProducts(route.counterparty);
      if (!route.productId) {
        products.push(product);
        if (state.catalogDrafts) delete state.catalogDrafts[route.counterparty];
      }
      saveState();
      route = { name: "catalog" };
      notify("Изделие каталога сохранено", product.name);
      return;
    }
    const project = getProject();
    const product = currentEditableProduct();
    updateProductFromForm(product);
    if (!route.productId) {
      project.products.push(product);
      project.draft = null;
    }
    delete project.draft;
    saveState();
    route = { name: "project", projectId: project.id };
    notify("Изделие сохранено", product.name);
  }

  function duplicateProduct(id, sourceProjectId = route.projectId, sourceCatalog = "") {
    const project = getProject();
    const sourceProject = state.projects.find((item) => item.id === sourceProjectId) || project;
    const source = sourceCatalog ? getCatalogProduct(sourceCatalog, id) : getProduct(sourceProject, id);
    if (!source) return;
    const copy = JSON.parse(JSON.stringify(source));
    copy.id = uid();
    copy.qty = 1;
    copy.details.forEach((detail) => detail.id = uid());
    project.products.push(copy);
    saveState();
    modal = null;
    notify("Изделие скопировано", copy.name);
  }

  function handleImage(file) {
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      const product = currentEditableProduct();
      product.image = reader.result;
      saveState();
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
    const name = prompt("Название материала");
    if (!name) return null;
    const cost = Number(prompt("Себестоимость за единицу", "0")) || 0;
    const isSheetMaterial = `${type} ${name}`.toLowerCase().includes("дсп");
    const unit = prompt("Единица: m2, lm или pc", isSheetMaterial ? "m2" : "pc") || "pc";
    const material = { id: uid(), name, cost, unit };
    if (isSheetMaterial) {
      material.sheetLength = Number(prompt("Длина листа, мм", "2750")) || 2750;
      material.sheetWidth = Number(prompt("Ширина листа, мм", "1830")) || 1830;
    }
    state.materials[type].push(material);
    if (isProductEditorRoute()) {
      const product = currentEditableProduct();
      updateProductFromForm(product);
      product.details.forEach((detail) => { if (detail.type === type && !detail.materialId) detail.materialId = material.id; });
    }
    saveState();
    if (!options.silentRender) notify("Материал добавлен", material.name);
    return material;
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
    saveState();
    notify("Материал удалён", "", "danger");
  }

  function buildCutState(project) {
    const next = { gap: cutState?.gap ?? 6, trim: cutState?.trim ?? 10, groups: [], scale: 0.25, drag: null };
    const groupsByMaterial = new Map();
    project.products.forEach((product) => {
      product.details.forEach((detail) => {
        const calc = detailCalc(product, detail);
        const material = calc.material;
        if (!material || !isDspMaterial(detail, material)) return;
        if (!groupsByMaterial.has(material.id)) {
          groupsByMaterial.set(material.id, { material, sheets: [], parts: [] });
        }
        for (let i = 0; i < calc.qty * productQty(product); i += 1) {
          groupsByMaterial.get(material.id).parts.push({ id: uid(), name: `${product.name}: ${detail.name}`, w: calc.length, h: calc.width, x: 0, y: 0, sheet: 0 });
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
    const gap = document.getElementById("cut-gap");
    const trim = document.getElementById("cut-trim");
    if (gap) gap.value = cutState.gap;
    if (trim) trim.value = cutState.trim;
  }

  function layoutCut(model) {
    model.groups.forEach((group) => {
      const sheetW = group.material.sheetLength || 2750;
      const sheetH = group.material.sheetWidth || 1830;
      group.sheets = [{ x: 0, y: 0, w: sheetW, h: sheetH }];
      let sheet = 0;
      const rows = [{ sheet: 0, x: model.trim, y: model.trim, h: 0 }];
      group.parts
        .sort((a, b) => Math.max(b.w, b.h) - Math.max(a.w, a.h) || (b.w * b.h) - (a.w * a.h))
        .forEach((part) => {
          if (part.w > part.h && part.w > sheetW - model.trim * 2 && part.h <= sheetW - model.trim * 2) {
            [part.w, part.h] = [part.h, part.w];
          }
          let placed = false;
          for (const row of rows) {
            if (row.x + part.w + model.trim <= sheetW && row.y + part.h + model.trim <= sheetH) {
              part.sheet = row.sheet;
              part.x = row.x;
              part.y = row.y;
              row.x += part.w + model.gap;
              row.h = Math.max(row.h, part.h);
              placed = true;
              break;
            }
          }
          if (!placed) {
            const lastRow = [...rows].reverse().find((row) => row.sheet === sheet);
            const nextY = lastRow ? lastRow.y + lastRow.h + model.gap : model.trim;
            if (!lastRow || nextY + part.h + model.trim > sheetH) {
              sheet += 1;
              group.sheets[sheet] = { x: 0, y: 0, w: sheetW, h: sheetH };
              rows.push({ sheet, x: model.trim + part.w + model.gap, y: model.trim, h: part.h });
              part.sheet = sheet;
              part.x = model.trim;
              part.y = model.trim;
            } else {
              rows.push({ sheet, x: model.trim + part.w + model.gap, y: nextY, h: part.h });
              part.sheet = sheet;
              part.x = model.trim;
              part.y = nextY;
            }
          }
        });
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
      height += 38 + group.sheets.length * ((group.material.sheetWidth || 1830) * scale + 54);
    });
    canvas.width = 1040;
    canvas.height = Math.max(700, height + pad);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.fillStyle = palette.bg;
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    let yCursor = pad;
    cutState.hit = [];
    cutState.groups.forEach((group, groupIndex) => {
      ctx.fillStyle = palette.title;
      ctx.font = "700 18px Segoe UI";
      ctx.fillText(group.material.name, pad, yCursor);
      yCursor += 24;
      group.sheets.forEach((sheet, sheetIndex) => {
        const sx = pad;
        const sy = yCursor;
        const sw = sheet.w * scale;
        const sh = sheet.h * scale;
        ctx.fillStyle = palette.sheet;
        ctx.strokeStyle = palette.sheetBorder;
        ctx.lineWidth = 2;
        ctx.fillRect(sx, sy, sw, sh);
        ctx.strokeRect(sx, sy, sw, sh);
        ctx.fillStyle = palette.note;
        ctx.font = "12px Segoe UI";
        ctx.fillText(`Лист ${sheetIndex + 1}: ${sheet.w} × ${sheet.h} мм`, sx + 8, sy + 18);
        const sheetParts = group.parts.filter((part) => part.sheet === sheetIndex);
        if (printMode) drawCutLines(ctx, sheetParts, sx, sy, sw, sh, scale, palette.cut);
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
          ctx.fillStyle = palette.text;
          ctx.font = "11px Segoe UI";
          ctx.fillText(part.name.slice(0, 38), px + 4, py + 14);
          ctx.fillText(`${Math.round(part.w)}×${Math.round(part.h)}`, px + 4, py + 28);
          cutState.hit.push({ groupIndex, sheetIndex, part, rect: [px, py, pw, ph], sheetRect: [sx, sy, sw, sh] });
        });
        yCursor += sh + 54;
      });
    });
  }

  function drawCutLines(ctx, parts, sx, sy, sw, sh, scale, color) {
    const xLines = new Set();
    const yLines = new Set();
    parts.forEach((part) => {
      xLines.add(Math.round(part.x));
      xLines.add(Math.round(part.x + part.w));
      yLines.add(Math.round(part.y));
      yLines.add(Math.round(part.y + part.h));
    });
    ctx.save();
    ctx.strokeStyle = color;
    ctx.lineWidth = 1;
    ctx.setLineDash([7, 5]);
    xLines.forEach((x) => {
      const px = sx + x * scale;
      ctx.beginPath();
      ctx.moveTo(px, sy);
      ctx.lineTo(px, sy + sh);
      ctx.stroke();
    });
    yLines.forEach((y) => {
      const py = sy + y * scale;
      ctx.beginPath();
      ctx.moveTo(sx, py);
      ctx.lineTo(sx + sw, py);
      ctx.stroke();
    });
    ctx.restore();
  }

  function bindCutCanvas() {
    const canvas = document.getElementById("cut-canvas");
    if (!canvas || canvas.dataset.bound) return;
    canvas.dataset.bound = "1";
    canvas.onmousedown = (event) => {
      const point = canvasPoint(canvas, event);
      const hit = [...(cutState.hit || [])].reverse().find((item) => point.x >= item.rect[0] && point.x <= item.rect[0] + item.rect[2] && point.y >= item.rect[1] && point.y <= item.rect[1] + item.rect[3]);
      if (!hit) return;
      cutState.drag = { hit, dx: point.x - hit.rect[0], dy: point.y - hit.rect[1] };
    };
    canvas.onmousemove = (event) => {
      if (!cutState.drag) return;
      const point = canvasPoint(canvas, event);
      const targetSheet = (cutState.hit || []).find((item) => point.x >= item.sheetRect[0] && point.x <= item.sheetRect[0] + item.sheetRect[2] && point.y >= item.sheetRect[1] && point.y <= item.sheetRect[1] + item.sheetRect[3] && item.groupIndex === cutState.drag.hit.groupIndex);
      const sheetRect = targetSheet?.sheetRect || cutState.drag.hit.sheetRect;
      const part = cutState.drag.hit.part;
      part.sheet = targetSheet?.sheetIndex ?? part.sheet;
      const sheet = cutState.groups[cutState.drag.hit.groupIndex].sheets[part.sheet];
      part.x = Math.max(0, Math.min(sheet.w - part.w, (point.x - sheetRect[0] - cutState.drag.dx) / cutState.scale));
      part.y = Math.max(0, Math.min(sheet.h - part.h, (point.y - sheetRect[1] - cutState.drag.dy) / cutState.scale));
      drawCutCanvas();
    };
    window.onmouseup = () => cutState && (cutState.drag = null);
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
    const win = window.open("", "_blank");
    win.document.write(`<html><head><title>Карта кроя</title><style>@page{size:A4 landscape;margin:10mm}body{font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Arial,sans-serif;color:#111}h1{font-size:14px;margin:0 0 4px}.meta{font-size:10px;margin-bottom:8px;display:flex;gap:14px;flex-wrap:wrap}img{max-width:100%}</style></head><body><h1>Карта кроя</h1><div class="meta"><span>Контрагент: ${esc(project.counterparty)}</span><span>Адрес: ${esc(project.address || "-")}</span><span>ДСП: ${fmt(totals.area)} м²</span></div><img src="${canvas.toDataURL("image/png")}"></body></html>`);
    win.document.close();
    win.focus();
    win.print();
    drawCutCanvas(false);
    notify("Карта кроя отправлена на печать");
  }

  function exportDetails() {
    const project = getProject();
    if (!project.products.length) return;
    const sections = project.products.flatMap((product) => {
      const copies = [];
      for (let copy = 0; copy < productQty(product); copy += 1) {
        const rows = product.details.map((detail) => {
          const calc = detailCalc(product, detail);
          return `<tr><td>${esc(detail.name)}</td><td>${calc.qty}</td><td>${esc(calc.material?.name || "")}</td><td>${Math.round(calc.length)}</td><td>${Math.round(calc.width)}</td><td>${fmt(calc.area)}</td></tr>`;
        }).join("");
        copies.push(`
          <h2>${esc(product.name)}${productQty(product) > 1 ? ` · ${copy + 1}` : ""}</h2>
          ${product.image ? `<img src="${product.image}" width="120" height="120">` : ""}
          <table><tr><th>Деталь</th><th>Кол-во</th><th>Материал</th><th>Длина</th><th>Ширина</th><th>м² ДСП</th></tr>${rows}</table>
        `);
      }
      return copies;
    }).join("<br>");
    const html = `<html><head><meta charset="utf-8"><style>@page{size:A4 portrait}body{font-family:Arial,sans-serif}h1{font-size:18px}h2{font-size:15px;margin:18px 0 6px}td,th{border:1px solid #999;padding:6px}table{border-collapse:collapse;margin-bottom:12px}img{object-fit:cover;border:1px solid #999;margin:4px 0 8px}</style></head><body><h1>Деталировка · ${esc(project.name)}</h1>${sections}</body></html>`;
    downloadBlob(`${project.name || "detalization"}.xls`, html, "application/vnd.ms-excel;charset=utf-8");
    notify("Деталировка выгружена", "Excel-файл создан");
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
  render();
})();
