const { app, BrowserWindow, dialog, ipcMain, shell } = require("electron");
const path = require("path");
const { createStaticServer } = require("./server");
const { createDatabaseStore } = require("./desktop-database");

const APP_ID = "ru.mikeysnow.vitancut";
let mainWindow = null;
let localServer = null;

function databaseBaseDirectory() {
  return app.isPackaged ? path.dirname(app.getPath("exe")) : __dirname;
}

function databaseStore() {
  return createDatabaseStore(databaseBaseDirectory());
}

function installIpc() {
  ipcMain.on("database:load", (event) => {
    event.returnValue = databaseStore().load();
  });
  ipcMain.on("database:save", (event, raw) => {
    event.returnValue = databaseStore().save(raw);
  });
  ipcMain.handle("database:path", () => databaseStore().filePath);
}

async function createWindow() {
  const icon = path.join(__dirname, "assets", "app-icon.png");
  const started = await createStaticServer({ root: __dirname, port: 0 });
  localServer = started.server;

  mainWindow = new BrowserWindow({
    title: "Vitan-Cut",
    width: 1440,
    height: 900,
    minWidth: 1040,
    minHeight: 700,
    backgroundColor: "#d9ecff",
    icon,
    autoHideMenuBar: true,
    show: false,
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true
    }
  });

  mainWindow.removeMenu();
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    if (url === "about:blank") {
      return {
        action: "allow",
        overrideBrowserWindowOptions: { autoHideMenuBar: true, parent: mainWindow }
      };
    }
    if (/^https?:/i.test(url) && !url.startsWith(`http://${started.host}:${started.port}`)) {
      shell.openExternal(url);
    }
    return { action: "deny" };
  });
  mainWindow.once("ready-to-show", () => mainWindow.show());
  mainWindow.on("closed", () => {
    mainWindow = null;
  });

  await mainWindow.loadURL(`http://${started.host}:${started.port}`);
}

const hasLock = app.requestSingleInstanceLock();
if (!hasLock) {
  app.quit();
} else {
  app.setAppUserModelId(APP_ID);
  app.on("second-instance", () => {
    if (!mainWindow) return;
    if (mainWindow.isMinimized()) mainWindow.restore();
    mainWindow.focus();
  });
  app.whenReady().then(() => {
    installIpc();
    return createWindow();
  }).catch((error) => {
    dialog.showErrorBox("Ошибка запуска", `Не удалось запустить Vitan-Cut:\n${error.message}`);
    app.quit();
  });
  app.on("window-all-closed", () => app.quit());
  app.on("before-quit", () => {
    if (localServer) localServer.close();
  });
}
