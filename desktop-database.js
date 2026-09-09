const fs = require("fs");
const path = require("path");

function createDatabaseStore(baseDirectory) {
  const filePath = path.join(baseDirectory, "data", "database.json");

  return {
    filePath,
    load() {
      try {
        const raw = fs.readFileSync(filePath, "utf8");
        JSON.parse(raw);
        return raw;
      } catch (error) {
        if (error.code !== "ENOENT") console.error("Database read failed:", error);
        return "";
      }
    },
    save(raw) {
      try {
        const value = typeof raw === "string" ? raw : JSON.stringify(raw);
        JSON.parse(value);
        fs.mkdirSync(path.dirname(filePath), { recursive: true });
        const temporaryPath = `${filePath}.tmp`;
        fs.writeFileSync(temporaryPath, value, "utf8");
        fs.renameSync(temporaryPath, filePath);
        return { ok: true, path: filePath };
      } catch (error) {
        console.error("Database write failed:", error);
        return { ok: false, error: error.message };
      }
    }
  };
}

module.exports = { createDatabaseStore };
