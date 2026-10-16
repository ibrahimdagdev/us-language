const esbuild = require("esbuild");
const path = require("path");
const fs = require("fs");

const outDir = path.join(__dirname, "dist");
fs.mkdirSync(outDir, { recursive: true });

esbuild
  .build({
    entryPoints: [path.join(__dirname, "src", "extension.js")],
    bundle: true,
    outfile: path.join(outDir, "extension.js"),
    platform: "node",
    target: "node18",
    format: "cjs",
    external: ["vscode"],
    sourcemap: false,
    minify: false,
    logLevel: "info",
  })
  .catch(() => process.exit(1));
