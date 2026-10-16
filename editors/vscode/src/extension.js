const vscode = require("vscode");
const path = require("path");
const fs = require("fs");
const { LanguageClient, TransportKind } = require("vscode-languageclient/node");

/** @type {import("vscode-languageclient/node").LanguageClient | undefined} */
let client;

/**
 * Resolve US CLI executable for Language Server.
 * Priority: us.executablePath → %LOCALAPPDATA%\US\bin\us.exe → PATH `us`.
 * @returns {{ command: string, args: string[] }}
 */
function resolveServerCommand() {
  const config = vscode.workspace.getConfiguration("us");
  const configured = (config.get("executablePath") || "").toString().trim();
  const args = ["dil-sunucusu"];

  if (configured) {
    if (!fs.existsSync(configured)) {
      throw new Error(
        `us.executablePath bulunamadı: ${configured}. US CLI yolunu kontrol edin veya ayarı boş bırakın.`
      );
    }
    return { command: configured, args };
  }

  const localAppData = process.env.LOCALAPPDATA;
  if (localAppData) {
    const installed = path.join(localAppData, "US", "bin", "us.exe");
    if (fs.existsSync(installed)) {
      return { command: installed, args };
    }
  }

  return {
    command: process.platform === "win32" ? "us" : "us",
    args,
  };
}

async function startClient() {
  const server = resolveServerCommand();
  const serverOptions = {
    run: { command: server.command, args: server.args, transport: TransportKind.stdio },
    debug: { command: server.command, args: server.args, transport: TransportKind.stdio },
  };
  const clientOptions = {
    documentSelector: [{ scheme: "file", language: "us" }],
  };
  client = new LanguageClient("us", "US Language Server", serverOptions, clientOptions);
  await client.start();
}

/**
 * @param {vscode.ExtensionContext} context
 */
async function activate(context) {
  context.subscriptions.push(
    vscode.commands.registerCommand("us.restartLanguageServer", async () => {
      if (client) {
        await client.stop();
        client = undefined;
      }
      await startClient();
    }),
    vscode.commands.registerCommand("us.formatDocument", async () => {
      await vscode.commands.executeCommand("editor.action.formatDocument");
    })
  );

  try {
    await startClient();
  } catch (error) {
    const message = error && error.message ? error.message : String(error);
    vscode.window.showErrorMessage(
      `US language server başlatılamadı: ${message}. TextMate vurgulama çalışır; LSP özellikleri için US CLI kurulumunu kontrol edin.`
    );
  }
}

async function deactivate() {
  if (client) {
    await client.stop();
    client = undefined;
  }
}

module.exports = {
  activate,
  deactivate,
  resolveServerCommand,
};
