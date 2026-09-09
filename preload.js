const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("vitanDesktop", Object.freeze({
  isDesktop: true,
  loadDatabase() {
    return ipcRenderer.sendSync("database:load");
  },
  saveDatabase(raw) {
    return ipcRenderer.sendSync("database:save", raw);
  },
  databasePath() {
    return ipcRenderer.invoke("database:path");
  }
}));
