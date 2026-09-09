const http = require("http");
const fs = require("fs");
const path = require("path");

const types = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".ico": "image/x-icon",
  ".json": "application/json; charset=utf-8"
};

function createStaticServer(options = {}) {
  const root = path.resolve(options.root || __dirname);
  const port = options.port ?? (Number(process.env.PORT) || 4173);
  const host = options.host || "127.0.0.1";
  const server = http.createServer((req, res) => {
    const address = server.address();
    const activePort = typeof address === "object" && address ? address.port : port;
    const urlPath = decodeURIComponent(new URL(req.url, `http://${host}:${activePort}`).pathname);
    const requestPath = urlPath === "/" ? "index.html" : urlPath.replace(/^[/\\]+/, "");
    const safePath = path.normalize(requestPath).replace(/^(\.\.[/\\])+/, "");
    const filePath = path.resolve(root, safePath);

    if (filePath !== root && !filePath.startsWith(`${root}${path.sep}`)) {
      res.writeHead(403);
      res.end("Forbidden");
      return;
    }

    fs.stat(filePath, (statError, stat) => {
      if (statError || !stat.isFile()) {
        res.writeHead(404);
        res.end("Not found");
        return;
      }
      res.writeHead(200, {
        "Content-Type": types[path.extname(filePath).toLowerCase()] || "application/octet-stream",
        "Cache-Control": "no-store"
      });
      fs.createReadStream(filePath).pipe(res);
    });
  });

  return new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(port, host, () => {
      server.off("error", reject);
      const address = server.address();
      resolve({ server, host, port: typeof address === "object" && address ? address.port : port });
    });
  });
}

if (require.main === module) {
  createStaticServer().then(({ host, port }) => {
    console.log(`Vitan-K server: http://${host}:${port}`);
  }).catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
}

module.exports = { createStaticServer };
