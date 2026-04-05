import express from 'express';
import { existsSync } from 'node:fs';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import qrcode from 'qrcode-terminal';
import whatsappWeb from 'whatsapp-web.js';

const { Client, LocalAuth } = whatsappWeb;

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const configPath = path.join(scriptDirectory, 'channel.config.json');
const exampleConfigPath = path.join(scriptDirectory, 'channel.config.example.json');

async function main() {
  const command = process.argv[2] ?? 'help';
  const args = parseArgs(process.argv.slice(3));

  switch (command) {
    case 'serve':
      await serveCommand();
      break;
    case 'doctor':
      await doctorCommand();
      break;
    case 'health':
      await bridgeGetCommand('/health');
      break;
    case 'list-unread':
      await listUnreadCommand(args);
      break;
    case 'list-recent':
      await listRecentCommand(args);
      break;
    case 'wait-next-message':
      await waitNextMessageCommand(args);
      break;
    case 'send':
      await sendCommand(args);
      break;
    case 'help':
    case '--help':
    case '-h':
      printHelp();
      break;
    default:
      throw new Error(`Unknown command '${command}'.`);
  }
}

async function serveCommand() {
  const config = await loadConfig();
  const browserExecutablePath = resolveBrowserExecutablePath(config);
  const sessionDataPath = resolveSessionDataPath(config);
  const state = createBridgeState(config);
  let client = createWhatsAppClient(config, browserExecutablePath, state);

  const app = express();
  app.use(express.json({ limit: '64kb' }));

  app.get('/health', async (_request, response) => {
    response.json(await buildHealthPayload(client, state, config));
  });

  app.get('/messages/unread', asyncRoute(async (request, response) => {
    ensureReady(state);
    const limit = parseLimit(request.query.limit, 10);
    const messages = await getUnreadMessages(client, config, limit);
    response.json({ ok: true, count: messages.length, messages });
  }));

  app.get('/messages/recent', asyncRoute(async (request, response) => {
    ensureReady(state);
    const limit = parseLimit(request.query.limit, 10);
    const phoneNumber = resolveTargetPhone(config, request.query.phone);
    const messages = await getRecentMessages(client, phoneNumber, limit);
    response.json({ ok: true, phoneNumber, count: messages.length, messages });
  }));

  app.get('/events/next', asyncRoute(async (request, response) => {
    ensureReady(state);
    const timeoutSeconds = parseTimeoutSeconds(request.query.timeoutSeconds, 300);
    const message = await waitForNextIncomingMessage(state, timeoutSeconds);
    response.json({ ok: true, timedOut: message === null, message });
  }));

  app.post('/messages/send', asyncRoute(async (request, response) => {
    ensureReady(state);
    const phoneNumber = resolveTargetPhone(config, request.body?.phoneNumber);
    const text = String(request.body?.text ?? '').trim();

    if (!text) {
      throw createStatusError(400, 'The message text is required.');
    }

    const chatId = await resolveChatId(client, phoneNumber);
    const sentMessage = await client.sendMessage(chatId, text);

    if (config.markSeenAfterSend) {
      await client.sendSeen(chatId);
    }

    response.json({
      ok: true,
      phoneNumber,
      message: serializeMessage(sentMessage, phoneNumber),
    });
  }));

  app.use((error, _request, response, _next) => {
    const statusCode = Number.isInteger(error?.statusCode) ? error.statusCode : 500;
    response.status(statusCode).json({ ok: false, error: error?.message ?? 'Unexpected bridge failure.' });
  });

  const server = app.listen(config.listenPort, config.listenHost, () => {
    console.log(`WhatsApp personal channel bridge listening on http://${config.listenHost}:${config.listenPort}`);
    console.log(`Allowlisted phone numbers: ${config.allowedPhoneNumbers.join(', ')}`);
    console.log(`Session data directory: ${sessionDataPath}`);
    console.log('Scan the QR code below with your personal WhatsApp account if this is the first run.');
  });

  const shutdown = async (signal) => {
    console.log(`Received ${signal}. Shutting down bridge.`);
    rejectPendingIncomingMessageWaiters(state, createStatusError(503, 'The bridge is shutting down.'));

    await new Promise((resolve) => {
      server.close(() => resolve());
    });

    try {
      await client.destroy();
    }
    catch {
    }

    process.exit(0);
  };

  process.on('SIGINT', () => {
    void shutdown('SIGINT');
  });

  process.on('SIGTERM', () => {
    void shutdown('SIGTERM');
  });

  await client.initialize();
}

async function doctorCommand() {
  const config = await loadConfig();
  const browserExecutablePath = resolveBrowserExecutablePath(config);
  const sessionDirectory = resolveSessionDataPath(config);

  printJson({
    ok: true,
    configPath,
    sessionDirectory,
    config: {
      listenHost: config.listenHost,
      listenPort: config.listenPort,
      sessionName: config.sessionName,
      sessionDataPath: sessionDirectory,
      browserExecutablePath,
      headless: config.headless,
      markSeenAfterSend: config.markSeenAfterSend,
      allowedPhoneNumbers: config.allowedPhoneNumbers,
    },
    nextStep: `Run 'node ${path.join('tools', 'whatsapp-personal-channel', 'channel.mjs')} serve' in a separate terminal to start the bridge.`,
  });
}

async function listUnreadCommand(args) {
  const query = new URLSearchParams();
  query.set('limit', String(parseLimit(args.limit, 10)));
  await bridgeGetCommand(`/messages/unread?${query.toString()}`);
}

async function listRecentCommand(args) {
  const config = await loadConfig();
  const phoneNumber = resolveTargetPhone(config, args.phone);
  const query = new URLSearchParams();
  query.set('limit', String(parseLimit(args.limit, 10)));
  query.set('phone', phoneNumber);
  await bridgeGetCommand(`/messages/recent?${query.toString()}`);
}

async function waitNextMessageCommand(args) {
  const query = new URLSearchParams();
  query.set('timeoutSeconds', String(parseTimeoutSeconds(args['timeout-seconds'], 300)));
  await bridgeGetCommand(`/events/next?${query.toString()}`);
}

async function sendCommand(args) {
  const config = await loadConfig();
  const phoneNumber = resolveTargetPhone(config, args.phone);
  const text = String(args.text ?? '').trim();

  if (!text) {
    throw new Error('The --text argument is required for send.');
  }

  const payload = await callBridge(config, 'POST', '/messages/send', { phoneNumber, text });
  printJson(payload);
}

async function bridgeGetCommand(route) {
  const config = await loadConfig();
  const payload = await callBridge(config, 'GET', route);
  printJson(payload);
}

async function callBridge(config, method, route, body) {
  const url = new URL(`http://${config.listenHost}:${config.listenPort}${route}`);
  const headers = {};
  let requestBody;

  if (body !== undefined) {
    headers['content-type'] = 'application/json';
    requestBody = JSON.stringify(body);
  }

  let response;

  try {
    response = await fetch(url, {
      method,
      headers,
      body: requestBody,
    });
  }
  catch (error) {
    throw new Error(`Could not reach the WhatsApp personal channel bridge at ${url.origin}. Start or restart it with 'pwsh ./scripts/start-whatsapp-personal-channel.ps1'.`);
  }

  const rawText = await response.text();
  const payload = parseBridgePayload(rawText, response, route, url.origin);

  if (!response.ok || payload.ok === false) {
    throw new Error(payload.error ?? `Bridge request failed with HTTP ${response.status}.`);
  }

  return payload;
}

function parseBridgePayload(rawText, response, route, bridgeOrigin) {
  if (rawText.length === 0) {
    return {};
  }

  try {
    return JSON.parse(rawText);
  }
  catch {
    const trimmed = rawText.trim();

    if (looksLikeHtmlDocument(trimmed)) {
      const restartInstruction = `Restart the bridge with 'pwsh ./scripts/start-whatsapp-personal-channel.ps1'.`;

      if (route.startsWith('/events/next')) {
        throw new Error(`The running WhatsApp bridge returned HTML with HTTP ${response.status} for ${route}. This usually means the active bridge process is older than the listener client or another service is using ${bridgeOrigin}. ${restartInstruction}`);
      }

      throw new Error(`The WhatsApp bridge returned HTML with HTTP ${response.status} for ${route} instead of JSON. Ensure the bridge is the process listening on ${bridgeOrigin}. ${restartInstruction}`);
    }

    throw new Error(`The WhatsApp bridge returned invalid JSON for ${route}: ${trimmed.slice(0, 120)}`);
  }
}

function looksLikeHtmlDocument(value) {
  const normalized = String(value ?? '').trimStart().toLowerCase();
  return normalized.startsWith('<!doctype html') || normalized.startsWith('<html');
}

async function buildHealthPayload(client, state, config) {
  let clientState = state.lastState;

  if (state.ready) {
    try {
      clientState = await client.getState();
    }
    catch {
    }
  }

  return {
    ok: true,
    ready: state.ready,
    authenticated: state.authenticated,
    state: clientState,
    accountName: state.accountName,
    lastError: state.lastError,
    allowedPhoneNumbers: config.allowedPhoneNumbers,
    sessionName: config.sessionName,
  };
}

function createWhatsAppClient(config, browserExecutablePath, state) {
  const sessionDataPath = resolveSessionDataPath(config);
  const client = new Client({
    authStrategy: new LocalAuth({
      clientId: config.sessionName,
      dataPath: sessionDataPath,
    }),
    authTimeoutMs: 120_000,
    takeoverOnConflict: true,
    webVersionCache: {
      type: 'remote',
      remotePath: 'https://raw.githubusercontent.com/AlexxIT/WebVersion/main/webVersion.json',
    },
    puppeteer: {
      executablePath: browserExecutablePath,
      headless: config.headless,
      protocolTimeout: 120_000,
    },
  });

  hardenClientInjection(client, state);
  wireClientEvents(client, state, config);
  return client;
}

function hardenClientInjection(client, state) {
  if (typeof client.inject !== 'function') {
    return;
  }

  const originalInject = client.inject.bind(client);

  client.inject = async (...args) => {
    const maxAttempts = 5;

    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
      try {
        return await originalInject(...args);
      }
      catch (error) {
        const message = error?.message ?? String(error);

        if (!isRecoverableNavigationError(message) || attempt === maxAttempts) {
          throw error;
        }

        state.ready = false;
        state.lastState = 'recovering_after_navigation';
        state.lastError = message;
        console.warn(`WhatsApp Web navigated during startup. Retrying initialization step (${attempt}/${maxAttempts})...`);
        await delay(Math.min(attempt * 1_000, 5_000));
      }
    }
  };
}

function createBridgeState(config) {
  return {
    ready: false,
    authenticated: false,
    lastState: 'starting',
    lastError: null,
    accountName: null,
    sessionName: config.sessionName,
    incomingMessageQueue: [],
    pendingIncomingMessageWaiters: [],
    recentIncomingMessageIds: [],
    recentIncomingMessageIdSet: new Set(),
  };
}

function wireClientEvents(client, state, config) {
  client.on('qr', (qr) => {
    state.ready = false;
    state.lastState = 'qr';
    state.lastError = null;
    rejectPendingIncomingMessageWaiters(state, createStatusError(503, 'The bridge is waiting for QR authentication.'));
    qrcode.generate(qr, { small: true });
  });

  client.on('authenticated', () => {
    state.authenticated = true;
    state.lastError = null;
    state.lastState = 'authenticated';
    console.log('WhatsApp personal channel authenticated.');
  });

  client.on('ready', () => {
    state.ready = true;
    state.lastState = 'ready';
    state.accountName = client.info?.pushname ?? null;
    console.log(`WhatsApp personal channel ready as ${state.accountName ?? 'unknown account'}.`);
  });

  client.on('auth_failure', (message) => {
    state.ready = false;
    state.authenticated = false;
    state.lastState = 'auth_failure';
    state.lastError = message;
    rejectPendingIncomingMessageWaiters(state, createStatusError(503, `Authentication failed: ${message}`));
    console.error(`Authentication failed: ${message}`);
  });

  client.on('change_state', (nextState) => {
    state.lastState = nextState;
    console.log(`WhatsApp state changed: ${nextState}`);
  });

  client.on('disconnected', (reason) => {
    state.ready = false;
    state.lastState = 'disconnected';
    state.lastError = String(reason ?? 'Disconnected');
    rejectPendingIncomingMessageWaiters(state, createStatusError(503, `WhatsApp disconnected: ${reason ?? 'Disconnected'}`));
    console.error(`WhatsApp disconnected: ${reason}`);
  });

  client.on('message', (message) => {
    try {
      queueIncomingMessage(state, config, message);
    }
    catch (error) {
      console.error(`Failed to queue an incoming WhatsApp message: ${error?.message ?? String(error)}`);
    }
  });
}

function queueIncomingMessage(state, config, message) {
  const serializedMessage = serializeIncomingMessage(config, message);

  if (!serializedMessage || rememberIncomingMessageId(state, serializedMessage.id)) {
    return;
  }

  const waiter = state.pendingIncomingMessageWaiters.shift();

  if (waiter) {
    waiter.resolve(serializedMessage);
    return;
  }

  state.incomingMessageQueue.push(serializedMessage);

  if (state.incomingMessageQueue.length > 100) {
    state.incomingMessageQueue.shift();
  }
}

function serializeIncomingMessage(config, message) {
  if (!message || message.fromMe) {
    return null;
  }

  const phoneNumber = extractPhoneNumber(message.from);

  if (!phoneNumber || !config.allowedPhoneNumbers.includes(phoneNumber)) {
    return null;
  }

  return serializeMessage(message, phoneNumber);
}

function rememberIncomingMessageId(state, messageId) {
  if (!messageId) {
    return false;
  }

  if (state.recentIncomingMessageIdSet.has(messageId)) {
    return true;
  }

  state.recentIncomingMessageIdSet.add(messageId);
  state.recentIncomingMessageIds.push(messageId);

  while (state.recentIncomingMessageIds.length > 256) {
    const removed = state.recentIncomingMessageIds.shift();

    if (removed) {
      state.recentIncomingMessageIdSet.delete(removed);
    }
  }

  return false;
}

async function waitForNextIncomingMessage(state, timeoutSeconds) {
  if (state.incomingMessageQueue.length > 0) {
    return state.incomingMessageQueue.shift();
  }

  return await new Promise((resolve, reject) => {
    const waiter = {
      timeoutHandle: null,
      resolve(message) {
        if (waiter.timeoutHandle !== null) {
          clearTimeout(waiter.timeoutHandle);
        }

        removePendingIncomingMessageWaiter(state, waiter);
        resolve(message);
      },
      reject(error) {
        if (waiter.timeoutHandle !== null) {
          clearTimeout(waiter.timeoutHandle);
        }

        removePendingIncomingMessageWaiter(state, waiter);
        reject(error);
      },
    };

    waiter.timeoutHandle = setTimeout(() => {
      waiter.resolve(null);
    }, timeoutSeconds * 1000);

    state.pendingIncomingMessageWaiters.push(waiter);
  });
}

function removePendingIncomingMessageWaiter(state, waiter) {
  const index = state.pendingIncomingMessageWaiters.indexOf(waiter);

  if (index >= 0) {
    state.pendingIncomingMessageWaiters.splice(index, 1);
  }
}

function rejectPendingIncomingMessageWaiters(state, error) {
  const waiters = state.pendingIncomingMessageWaiters.splice(0);

  for (const waiter of waiters) {
    waiter.reject(error);
  }
}

async function getUnreadMessages(client, config, limit) {
  const collected = [];

  for (const phoneNumber of config.allowedPhoneNumbers) {
    const chat = await tryGetChat(client, phoneNumber);

    if (!chat || !Number.isInteger(chat.unreadCount) || chat.unreadCount < 1) {
      continue;
    }

    const fetchLimit = Math.min(Math.max(limit * 4, chat.unreadCount * 3, 20), 200);
    const messages = await chat.fetchMessages({ limit: fetchLimit });
    const unreadMessages = messages
      .filter((message) => !message.fromMe)
      .slice(-chat.unreadCount)
      .map((message) => serializeMessage(message, phoneNumber));

    collected.push(...unreadMessages);
  }

  return collected
    .sort((left, right) => (left.timestamp ?? 0) - (right.timestamp ?? 0))
    .slice(-limit);
}

async function getRecentMessages(client, phoneNumber, limit) {
  const chat = await tryGetChat(client, phoneNumber);

  if (!chat) {
    return [];
  }

  const messages = await chat.fetchMessages({ limit });
  return messages.map((message) => serializeMessage(message, phoneNumber));
}

async function tryGetChat(client, phoneNumber) {
  let chatId;

  try {
    chatId = await resolveChatId(client, phoneNumber);
  }
  catch {
    return null;
  }

  try {
    return await client.getChatById(chatId);
  }
  catch {
    return null;
  }
}

async function resolveChatId(client, phoneNumber) {
  const numberId = await client.getNumberId(phoneNumber);

  if (!numberId?._serialized) {
    throw createStatusError(404, `The phone number '${phoneNumber}' is not available on WhatsApp.`);
  }

  return numberId._serialized;
}

function serializeMessage(message, fallbackPhoneNumber) {
  const rawPhoneNumber = extractPhoneNumber(message.fromMe ? message.to : message.from) ?? fallbackPhoneNumber;

  return {
    id: serializeMessageId(message.id),
    phoneNumber: rawPhoneNumber,
    chatId: typeof message.from === 'string' ? message.from : null,
    fromMe: Boolean(message.fromMe),
    body: message.body ?? '',
    timestamp: typeof message.timestamp === 'number' ? message.timestamp : null,
    type: message.type ?? null,
    hasMedia: Boolean(message.hasMedia),
  };
}

function serializeMessageId(id) {
  if (!id) {
    return null;
  }

  if (typeof id._serialized === 'string' && id._serialized.length > 0) {
    return id._serialized;
  }

  if (typeof id.id === 'string' && id.id.length > 0) {
    return id.id;
  }

  return null;
}

async function loadConfig() {
  let rawText;

  try {
    rawText = await fs.readFile(configPath, 'utf8');
  }
  catch (error) {
    if (error && error.code === 'ENOENT') {
      throw new Error(`Missing ${path.basename(configPath)}. Copy ${path.basename(exampleConfigPath)} to ${path.basename(configPath)} and set the phone number allowlist.`);
    }

    throw error;
  }

  const parsed = JSON.parse(rawText);
  const allowedPhoneNumbers = Array.isArray(parsed.allowedPhoneNumbers)
    ? parsed.allowedPhoneNumbers.map((value) => normalizePhoneNumber(value))
    : [];

  if (allowedPhoneNumbers.length === 0) {
    throw new Error('channel.config.json must contain at least one allowedPhoneNumbers entry.');
  }

  const sessionName = String(parsed.sessionName ?? 'oneshotprompt-personal').trim();
  if (!/^[-_\w]+$/i.test(sessionName)) {
    throw new Error('sessionName may contain only letters, digits, underscores, and hyphens.');
  }

  const listenHost = String(parsed.listenHost ?? '127.0.0.1').trim() || '127.0.0.1';
  const listenPort = Number(parsed.listenPort ?? 3111);

  if (!Number.isInteger(listenPort) || listenPort < 1 || listenPort > 65535) {
    throw new Error('listenPort must be an integer between 1 and 65535.');
  }

  const browserExecutablePath = String(parsed.browserExecutablePath ?? '').trim();
  const sessionDataPath = String(parsed.sessionDataPath ?? '').trim();

  return {
    listenHost,
    listenPort,
    sessionName,
    sessionDataPath,
    browserExecutablePath,
    headless: Boolean(parsed.headless ?? true),
    markSeenAfterSend: Boolean(parsed.markSeenAfterSend ?? true),
    allowedPhoneNumbers: [...new Set(allowedPhoneNumbers)],
  };
}

function resolveSessionDataPath(config) {
  if (config.sessionDataPath) {
    if (!path.isAbsolute(config.sessionDataPath)) {
      throw new Error('sessionDataPath must be an absolute path when it is provided.');
    }

    return config.sessionDataPath;
  }

  if (process.platform === 'win32') {
    const localAppData = process.env.LOCALAPPDATA ?? process.env.LocalAppData;

    if (localAppData) {
      return path.join(localAppData, 'OneShotPrompt', 'whatsapp-personal-channel', '.wwebjs_auth');
    }
  }

  if (process.platform === 'darwin') {
    return path.join(os.homedir(), 'Library', 'Application Support', 'OneShotPrompt', 'whatsapp-personal-channel', '.wwebjs_auth');
  }

  const xdgDataHome = process.env.XDG_DATA_HOME;

  if (xdgDataHome) {
    return path.join(xdgDataHome, 'OneShotPrompt', 'whatsapp-personal-channel', '.wwebjs_auth');
  }

  return path.join(os.homedir(), '.local', 'share', 'OneShotPrompt', 'whatsapp-personal-channel', '.wwebjs_auth');
}

function resolveBrowserExecutablePath(config) {
  if (config.browserExecutablePath) {
    if (!path.isAbsolute(config.browserExecutablePath)) {
      throw new Error('browserExecutablePath must be an absolute path when it is provided.');
    }

    if (!fileExists(config.browserExecutablePath)) {
      throw new Error(`browserExecutablePath does not exist: ${config.browserExecutablePath}`);
    }

    return config.browserExecutablePath;
  }

  const candidatePaths = [
    path.join(process.env.ProgramFiles ?? '', 'Google', 'Chrome', 'Application', 'chrome.exe'),
    path.join(process.env['ProgramFiles(x86)'] ?? '', 'Google', 'Chrome', 'Application', 'chrome.exe'),
    path.join(process.env.LocalAppData ?? '', 'Google', 'Chrome', 'Application', 'chrome.exe'),
    path.join(process.env.ProgramFiles ?? '', 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
    path.join(process.env['ProgramFiles(x86)'] ?? '', 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
    path.join(process.env.LocalAppData ?? '', 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
  ];

  const match = candidatePaths.find(fileExists);

  if (match) {
    return match;
  }

  throw new Error('No local Chrome or Edge executable was found. Set browserExecutablePath in channel.config.json.');
}

function fileExists(filePath) {
  return filePath.length > 0 && existsSync(filePath);
}

function resolveTargetPhone(config, requestedPhoneNumber) {
  if (requestedPhoneNumber !== undefined) {
    const normalized = normalizePhoneNumber(requestedPhoneNumber);

    if (!config.allowedPhoneNumbers.includes(normalized)) {
      throw new Error(`Phone number '${normalized}' is not in the allowedPhoneNumbers list.`);
    }

    return normalized;
  }

  if (config.allowedPhoneNumbers.length === 1) {
    return config.allowedPhoneNumbers[0];
  }

  throw new Error('Multiple phone numbers are allowlisted. Pass --phone <number>.');
}

function normalizePhoneNumber(value) {
  const digits = String(value ?? '').replace(/\D/g, '');

  if (!digits) {
    throw new Error(`Invalid phone number '${value}'. Use the full international number, digits only.`);
  }

  return digits;
}

function isRecoverableNavigationError(message) {
  const normalized = String(message ?? '').toLowerCase();

  return normalized.includes('execution context was destroyed')
    || normalized.includes('cannot find context with specified id');
}

function delay(milliseconds) {
  return new Promise((resolve) => {
    setTimeout(resolve, milliseconds);
  });
}

function extractPhoneNumber(chatId) {
  if (typeof chatId !== 'string') {
    return null;
  }

  return chatId.endsWith('@c.us') ? chatId.slice(0, -5) : null;
}

function parseArgs(args) {
  const parsed = {};

  for (let index = 0; index < args.length; index++) {
    const token = args[index];

    if (!token.startsWith('--')) {
      throw new Error(`Unexpected argument '${token}'. Options must use the --name value format.`);
    }

    const key = token.slice(2);
    const nextValue = args[index + 1];

    if (nextValue === undefined || nextValue.startsWith('--')) {
      throw new Error(`Missing value for --${key}.`);
    }

    parsed[key] = nextValue;
    index++;
  }

  return parsed;
}

function parseLimit(value, fallback) {
  if (value === undefined) {
    return fallback;
  }

  const parsed = Number(value);

  if (!Number.isInteger(parsed) || parsed < 1 || parsed > 100) {
    throw new Error('The limit must be an integer between 1 and 100.');
  }

  return parsed;
}

function parseTimeoutSeconds(value, fallback) {
  if (value === undefined) {
    return fallback;
  }

  const parsed = Number(value);

  if (!Number.isInteger(parsed) || parsed < 1 || parsed > 600) {
    throw new Error('timeoutSeconds must be an integer between 1 and 600.');
  }

  return parsed;
}

function ensureReady(state) {
  if (!state.ready) {
    throw createStatusError(503, 'The bridge is not ready. Start the server, scan the QR code if needed, and wait for the ready state.');
  }
}

function asyncRoute(handler) {
  return async (request, response, next) => {
    try {
      await handler(request, response);
    }
    catch (error) {
      next(error);
    }
  };
}

function createStatusError(statusCode, message) {
  const error = new Error(message);
  error.statusCode = statusCode;
  return error;
}

function printHelp() {
  console.log(`WhatsApp Personal Channel\n\nCommands:\n  serve\n  doctor\n  health\n  list-unread [--limit <1-100>]\n  list-recent [--phone <digits>] [--limit <1-100>]\n  wait-next-message [--timeout-seconds <1-600>]\n  send [--phone <digits>] --text <message>\n\nNotes:\n  - The bridge only allows numbers listed in tools/whatsapp-personal-channel/channel.config.json.\n  - When exactly one phone number is allowlisted, --phone can be omitted for list-recent and send.\n  - Start the bridge in a separate terminal with 'node tools/whatsapp-personal-channel/channel.mjs serve'.`);
}

function printJson(value) {
  console.log(JSON.stringify(value, null, 2));
}

main().catch((error) => {
  console.error(error?.message ?? String(error));
  process.exitCode = 1;
});