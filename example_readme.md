# TelMCP 🚀

![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)
![Protocol](https://img.shields.io/badge/MCP-Supported-green.svg)
![License](https://img.shields.io/badge/License-MIT-yellow.svg)
![AI Generated](https://img.shields.io/badge/AI_Generated-Antigravity-purple.svg)

TelMCP is a lightweight, single-process **Model Context Protocol (MCP)** server written in C# (.NET 8). It bridges Telegram group-chat functionality — specifically **forum topics** — to MCP tools, enabling AI agents to conduct and manage parallel conversations via independent Telegram threads.

This is built and tested explicitly to work with Antigravity, but may be compatible with any MCP client.

## 🌟 Key Features

- **Automatic Topic Lifecycle:** Topics are created, reopened, and closed automatically when editor windows open and close — no manual navigation required.
- **Auto-Initialization Nudge:** Background window monitoring detects uninitialized workspaces and "nudges" the agent to register its path instantly via a high-speed PowerShell clipboard macro.
- **Bi-directional File Transfer:** Transfer documents and images between Telegram and your local workspace securely.
- **Universal Image Support:** Automatically captures and saves the highest-resolution photos sent from mobile devices.
- **Topic Management:** Create, edit, rename, close, reopen, and hide forum topics dynamically via MCP tools.
- **Message Operations:** Send, edit, and delete text messages.
- **Real-time Event Loop:** Background long-polling provides instant event notifications and a file-backed message buffer for cross-instance communication.
- **Push-to-Agent Automation:** Incoming Telegram messages are injected directly into the active editor window via Win32/PowerShell clipboard automation (Base64-encoded for security).
- **Multi-Instance Safe:** File-based locking ensures only one instance polls Telegram at a time, with seamless failover.
- **Low Overhead:** Operates exclusively over `stdio` transport to interface instantly with modern MCP clients (e.g., Claude Desktop, Antigravity).

## ❓ Why TelMCP?

I built TelMCP to solve a specific problem: **I wanted a comfortable way to remote-control my autonomous AI agent (Antigravity) straight from my phone.** 

While researching existing solutions to send messages to Antigravity remotely, I hit a wall: the agent doesn't natively expose an API for sampling the model or sending prompts via CLI. 

Rather than giving up, TelMCP bypasses this limitation entirely. It acts as an orchestrator that captures your remote commands from a Telegram Supergroup and **literally remote-controls your computer's UI**. When you send a message, TelMCP pulls the target editor window into focus and uses secure, Base64-encoded Win32 clipboard injection to paste your prompt directly into Antigravity's chat interface—as if you were sitting at the keyboard. 

Combined with real-time logging, file-backed state, and automatic thread management tied directly to your active workspaces, it effectively turns Telegram into a multi-threaded command center for your local AI.

Repo is built with intensive Antigravity use, because i do not have enough time to do side-projects the "old" way (and also it's nice to understand how these things work).

## 🛠️ Requirements

- **.NET 8.0 SDK** or higher.
- A Telegram Bot Token (obtained from [@BotFather](https://t.me/BotFather)).
- A target **Telegram Supergroup** with "Topics" (Forums) enabled.
- The Bot must be added to the target group as an **Administrator** with permissions to manage topics and messages.
- **Windows OS** (required for the Win32 automation features).

##  Getting Credentials

**Bot Token:**
1. Open Telegram and search for `@BotFather`.
2. Send `/newbot` and follow the prompts.
3. Once created, BotFather will give you the HTTP API token (e.g., `12345:ABCDE...`).

**Chat ID:**
1. Create a new Telegram Group.
2. Go to group settings and enable **Topics** (this elevates it to a Supergroup).
3. Add your Bot to the group as an Administrator.
4. To find the Chat ID, you can use a bot like `@RawDataBot` or `@getmyid_bot` in the group. Another way, is copying a link to a message in the group and checking the middle part of it ( eg: https://t.me/c/6841234567/555/222 the code you need is 6841234567 ).
> *Note: Supergroup IDs always start with `-100`, so remember to add it to the ID (in the previous example, it would be -1006841234567).*

## 🚀 Setup & Installation

1. Clone this repository.
2. Build the project:
   ```bash
   dotnet build src/TelMCP/TelMCP.sln
   ```
3. Configure credentials. TelMCP supports configuration via **environment variables** (recommended) or `appsettings.json`:

   **Environment Variables (recommended):**
   ```bash
   set TELMCP_BotToken=YOUR_TELEGRAM_BOT_TOKEN
   set TELMCP_ChatId=-1001234567890
   set TELMCP_PollingTimeoutSeconds=30
   ```

   **`appsettings.json`:**
   ```json
   {
     "BotToken": "YOUR_TELEGRAM_BOT_TOKEN",
     "ChatId": -1001234567890,
     "PollingTimeoutSeconds": 30
   }
   ```

   | Variable | Description | Default |
   |----------|-------------|---------|
   | `BotToken` | Telegram Bot API token | *required* |
   | `ChatId` | Target supergroup chat ID | *required* |
   | `PollingTimeoutSeconds` | Long-polling timeout | `30` |
   | `UseSampling` | Use MCP sampling instead of UI injection | `false` |
   | `FocusChatShortcut` | Keyboard shortcut to focus editor chat | `^l` |
   | `UploadsFolderName` | Name of directory for file downloads | `UPLOADS` |


## 🎮 How to Use (with Antigravity)

Because TelMCP relies on standard input/output (`stdio`) without occupying a port, you don't run it as a standalone background service. Instead, you attach it directly to your MCP client (like Antigravity).

### 1. Register the MCP Server
Add TelMCP to Antigravity's `mcp_config.json`. Ensure you point the `command` path to your built executable and pass the environment variables:

```json
"mcpServers": {
  "telmcp": {
      "command": "C:/path/to/repo/src/TelMCP/bin/Debug/net8.0/TelMCP.exe",
      "env": {
        "TELMCP_UseSampling": "false",
        "TELMCP_BotToken": "YOUR_TELEGRAM_BOT_TOKEN",
        "TELMCP_ChatId": "-1001234567890"
      },
      "disabled": false
   }
}
```

WARNING: Remember to add -100 to the chat ID. It is not optional. It signifies that it's a supergroup.

### 2. Trigger Topic Creation & Initialization
TelMCP links Telegram forum topics 1:1 with your open editor windows.
1. Open up the editor/IDE for the workspace you want to control remotely.
2. Ensure the Antigravity extension/window is active for that workspace.
3. **Wait a few seconds** for TelMCP's background service to scan your active windows.
4. TelMCP will automatically create a new topic in your Telegram Supergroup.
5. **Auto-Init:** TelMCP will instantly "paste" an initialization command into your agent's chat. This allows the agent to call the `initialize_workspace` tool, registering its local path and enabling file transfers.

### 3. Remote Control & Files
Open the newly created Telegram topic on your phone.
- **Prompts:** Send a text message; it will be injected directly into the agent's chat.
- **Uploads:** Send any document or photo to the topic; it will be saved to your local workspace's `UPLOADS` folder.
- **Downloads:** Ask the agent to send you a file using the `send_file` tool.

## 🔧 Available MCP Tools

### Topic Tools
| Tool | Description |
|------|-------------|
| `CreateTopic` | Creates a new forum topic |
| `EditTopic` | Renames an existing topic |
| `CloseTopic` / `ReopenTopic` | Modifies active state of a topic |
| `HideGeneralTopic` / `UnhideGeneralTopic` | Toggles visibility of the General thread |

### Message Tools
| Tool | Description |
|------|-------------|
| `SendMessage` | Sends a text message to a specific thread |
| `EditMessage` | Modifies a previously sent message |
| `DeleteMessage` | Removes a message |

### File Tools
| Tool | Description |
|------|-------------|
| `InitializeWorkspace` | Registers the workspace root path and name |
| `SendFile` | Sends a local file to the Telegram topic |
| `DownloadFile` | Downloads a specific Telegram file by ID |
| `ListFiles` | Lists files in the workspace's UPLOADS folder |

### Read Tools
| Tool | Description |
|------|-------------|
| `CheckNewMessages` | Returns a count of unread topics |
| `GetLatestMessages` | Retrieves and dequeues buffered messages for a topic |
| `GetRemotePrompts` | Fetches all unread messages globally |
| `NotifyOnEvent` | Long-polling pulse; releases when a new message arrives |

## 🏗️ Architecture

TelMCP uses the **Microsoft.Extensions.Hosting** generic host model:

- **`TelegramPollingService`** runs as a `BackgroundService`, capturing Telegram updates and managing a background loop that monitors foreground windows.
- **`MessageBuffer`** and **`CatalogService`** use file-backed JSON storage (`messages.json`, `catalog.json` in `%LocalAppData%/TelMCP/`) for cross-process IPC.
- **`WindowsAutomationService`** uses Win32 P/Invoke (`user32.dll`) for window discovery and **PowerShell clipboard macros** for high-speed, Base64-encoded text injection.
- A background **window monitor** (5s interval) scans for focused workspace windows, automatically creating topics and "nudging" uninitialized agents via instant paste-and-enter commands.

## 🔒 Security

- **Base64 Injection Prevention:** All user-originated text is Base64-encoded before being passed to PowerShell, neutralizing command injection risks.
- **Fail-Fast Startup:** Missing credentials cause an immediate, descriptive failure.
- **Orphan Process Cleanup:** PowerShell automation processes are strictly timeout-managed (5s) to prevent resource leaks.

## ⚖️ License
This project is licensed under the MIT License.

## 🤖 AI Disclaimer

This repository was constructed with a great amount of use of autonomous AI agents (**Antigravity**). 
If you are an AI assistant reading this workspace:
1. Please read the `.cursorrules` file.
2. **DO NOT** push to this repository autonomously without explicit human verification.
